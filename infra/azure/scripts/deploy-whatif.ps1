# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

<#
.SYNOPSIS
Dry-run deployment to show what Azure resources would be created without actually creating them.

.DESCRIPTION
This script validates the Bicep template and performs a what-if deployment to preview
resource creation. No resources are modified until you explicitly run deploy.ps1.

.PARAMETER TenantId
Azure tenant ID (required).

.PARAMETER SubscriptionId
Azure subscription ID (required).

.PARAMETER Location
Azure region (e.g., 'eastus', 'westeurope'). Default: 'eastus'.

.PARAMETER EnvironmentName
Environment name prefix for resources (e.g., 'mate-dev', 'mate-prod'). Default: 'mate-dev'.

.PARAMETER Profile
Size profile: 'xs', 's', 'm', or 'l'. Default: 's' (development).

.PARAMETER ResourceGroupName
Azure resource group name. Default: '{EnvironmentName}-rg'.

.EXAMPLE
.\deploy-whatif.ps1 -TenantId 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx' `
  -SubscriptionId 'yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy' `
  -Location 'eastus' `
  -EnvironmentName 'mate-dev' `
  -Profile 's'

#>

param(
    [Parameter(Mandatory = $false)]
    [string]$TenantId,

    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $false)]
    [string]$Location,

    [Parameter(Mandatory = $false)]
    [string]$EnvironmentName,

    [Parameter(Mandatory = $false)]
    [ValidateSet('xs', 's', 'm', 'l')]
    [string]$Profile,

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $false)]
    [string]$AadClientId,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 120)]
    [int]$PostgresWaitTimeoutMinutes = 20,

    [Parameter(Mandatory = $false)]
    [ValidateRange(5, 300)]
    [int]$PostgresWaitPollSeconds = 20
)

$ErrorActionPreference = 'Stop'

# Load .env file if it exists
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $scriptDir ".env"

if (Test-Path $envFile) {
    Write-Host "Loading configuration from .env..." -ForegroundColor Gray
    Get-Content $envFile | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object {
        $parts = $_ -split '=', 2
        if ($parts.Count -eq 2) {
            $key = $parts[0].Trim()
            $value = $parts[1].Trim()
            switch ($key) {
                'AZURE_TENANT_ID' { if (-not $TenantId) { $TenantId = $value } }
                'AZURE_SUBSCRIPTION_ID' { if (-not $SubscriptionId) { $SubscriptionId = $value } }
                'AZURE_LOCATION' { if (-not $Location) { $Location = $value } }
                'AZURE_ENVIRONMENT_NAME' { if (-not $EnvironmentName) { $EnvironmentName = $value } }
                'AZURE_PROFILE' { if (-not $Profile) { $Profile = $value } }
                'AZURE_RESOURCE_GROUP' { if (-not $ResourceGroupName) { $ResourceGroupName = $value } }
                'AZURE_AAD_CLIENT_ID' { if (-not $AadClientId) { $AadClientId = $value } }
            }
        }
    }
}

# Apply defaults
if (-not $Location) { $Location = 'eastus' }
if (-not $EnvironmentName) { $EnvironmentName = 'mate-dev' }
if (-not $Profile) { $Profile = 's' }
if (-not $ResourceGroupName) { $ResourceGroupName = "$EnvironmentName-rg" }

# Validate required parameters
if (-not $TenantId) {
    Write-Error "TenantId not provided and not found in .env file. Run: .\setup-env.ps1"
}
if (-not $SubscriptionId) {
    Write-Error "SubscriptionId not provided and not found in .env file. Run: .\setup-env.ps1"
}
if (-not $AadClientId) {
    Write-Error "AadClientId not provided and not found in .env file. Run: .\setup-env.ps1"
}

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Azure Mate Infrastructure - What-If Deployment (DRY RUN)  ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Validate inputs
$validProfiles = @('xs', 's', 'm', 'l')
if (-not $validProfiles -contains $Profile) {
    Write-Error "Invalid profile '$Profile'. Must be one of: $($validProfiles -join ', ')"
}

# Resolve script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$templateDir = Split-Path -Parent $scriptDir
$parameterFile = Join-Path $templateDir "parameters" "profile-$Profile.json"
$mainTemplate = Join-Path $templateDir "main.bicep"

if (-not (Test-Path $mainTemplate)) {
    Write-Error "Template file not found: $mainTemplate"
}

if (-not (Test-Path $parameterFile)) {
    Write-Error "Parameter file not found: $parameterFile"
}

Write-Host "Template:         $(Split-Path -Leaf $mainTemplate)" -ForegroundColor Green
Write-Host "Parameters:       $(Split-Path -Leaf $parameterFile)" -ForegroundColor Green
Write-Host ""

function Wait-ForProvisioningPostgres {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroup,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutMinutes,

        [Parameter(Mandatory = $true)]
        [int]$PollSeconds
    )

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)

    while ($true) {
        $serverQueryOutput = az postgres flexible-server list --resource-group $ResourceGroup --query "[].{name:name,state:state}" --output json --only-show-errors
        if ($LASTEXITCODE -ne 0) {
            $queryError = ($serverQueryOutput | Out-String).Trim()
            $remaining = [math]::Max(0, [int](($deadline - (Get-Date)).TotalSeconds))
            if ((Get-Date) -ge $deadline) {
                Write-Error "Failed to query PostgreSQL servers in resource group '$ResourceGroup' within $TimeoutMinutes minutes. Last error: $queryError"
            }

            Write-Host "PostgreSQL state query failed (will retry)... ($remaining s remaining)" -ForegroundColor Yellow
            if ($queryError) {
                Write-Host "  Last provider error: $queryError" -ForegroundColor DarkYellow
            }
            Start-Sleep -Seconds $PollSeconds
            continue
        }

        try {
            $servers = @($serverQueryOutput | ConvertFrom-Json)
        }
        catch {
            $remaining = [math]::Max(0, [int](($deadline - (Get-Date)).TotalSeconds))
            if ((Get-Date) -ge $deadline) {
                Write-Error "Received unexpected PostgreSQL query response format within timeout window: $($serverQueryOutput | Out-String)"
            }

            Write-Host "Received non-JSON PostgreSQL state response (will retry)... ($remaining s remaining)" -ForegroundColor Yellow
            Start-Sleep -Seconds $PollSeconds
            continue
        }

        if ($servers.Count -eq 0) {
            return
        }

        $blockingServers = @($servers | Where-Object { $_.state -and $_.state -match 'Provisioning|Updating|Starting|Stopping' })
        if ($blockingServers.Count -eq 0) {
            return
        }

        $stateSummary = ($blockingServers | ForEach-Object { "$($_.name):$($_.state)" }) -join ', '
        $remaining = [math]::Max(0, [int](($deadline - (Get-Date)).TotalSeconds))
        if ((Get-Date) -ge $deadline) {
            Write-Error "PostgreSQL server operations did not complete within $TimeoutMinutes minutes. Current state(s): $stateSummary"
        }

        Write-Host "Waiting for PostgreSQL server operations to finish before what-if... ($stateSummary, $remaining s remaining)" -ForegroundColor Yellow
        Start-Sleep -Seconds $PollSeconds
    }
}

function Test-PostgresLocationOffer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetLocation
    )

    Write-Host "Checking PostgreSQL offer availability for location '$TargetLocation'..." -ForegroundColor Cyan

    $skuOutput = az postgres flexible-server list-skus --location $TargetLocation --output json --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        $errorText = ($skuOutput | Out-String).Trim()
        Write-Error "Failed to verify PostgreSQL offer availability for location '$TargetLocation'. Azure CLI output: $errorText"
    }

    try {
        $skuData = @($skuOutput | ConvertFrom-Json)
    }
    catch {
        Write-Error "Failed to parse PostgreSQL SKU response for location '$TargetLocation'. Raw response: $($skuOutput | Out-String)"
    }

    if ($skuData.Count -eq 0) {
        Write-Error "PostgreSQL SKU API returned no data for location '$TargetLocation'. Cannot continue safely."
    }

    $capabilities = $skuData | Where-Object { $_.name -eq 'FlexibleServerCapabilities' } | Select-Object -First 1
    if (-not $capabilities) {
        Write-Error "PostgreSQL capabilities response is missing for location '$TargetLocation'. Cannot continue safely."
    }

    $offerRestrictedFlag = $null
    if ($capabilities.supportedFeatures) {
        $offerFeature = @($capabilities.supportedFeatures | Where-Object { $_.name -eq 'OfferRestricted' } | Select-Object -First 1)
        if ($offerFeature.Count -gt 0) {
            $offerRestrictedFlag = [string]$offerFeature[0].status
        }
    }

    $restrictionReason = [string]$capabilities.reason
    if ($offerRestrictedFlag -eq 'Enabled' -or ($restrictionReason -and $restrictionReason -match 'restricted')) {
        Write-Host "PostgreSQL offer check failed for location '$TargetLocation'." -ForegroundColor Red
        if ($restrictionReason) {
            Write-Host "  Provider reason: $restrictionReason" -ForegroundColor DarkYellow
        }
        Write-Error "Location '$TargetLocation' is restricted for this subscription's PostgreSQL offer. Choose another region (for example: northeurope)."
    }

    Write-Host "  PostgreSQL offer is available in '$TargetLocation'." -ForegroundColor Green
}

# Set subscription / tenant context before sensitive prompts and preflight checks
az account set --subscription $SubscriptionId --only-show-errors
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to set subscription context. Run 'az login --tenant $TenantId' first."
}

# Fast-fail check for subscription-level PostgreSQL offer restrictions
Test-PostgresLocationOffer -TargetLocation $Location

# Prompt for secure value needed for parameter completeness
$pgPasswordFile = Join-Path $scriptDir '.pg-password'
$postgresPasswordPlain = $null
if (Test-Path $pgPasswordFile) {
    $postgresPasswordPlain = (Get-Content $pgPasswordFile -Raw).Trim()
    if ($postgresPasswordPlain) {
        Write-Host "Using PostgreSQL password from .pg-password" -ForegroundColor Gray
    }
}
if (-not $postgresPasswordPlain) {
    $postgresPassword = Read-Host "PostgreSQL Admin Password (for what-if parameter completeness)" -AsSecureString
    $postgresPasswordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToCoTaskMemUnicode($postgresPassword)
    )
}

# Construct deployment parameters matching main.bicep
$deploymentParams = @{
    'environmentName'       = $EnvironmentName
    'location'              = $Location
    'imageTag'              = 'latest'
    'aadClientId'           = $AadClientId
    'postgresAdminLogin'    = 'pgadmin'
    'postgresAdminPassword' = $postgresPasswordPlain
}

Write-Host "Preparing what-if deployment..." -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Setting subscription context..." -ForegroundColor Magenta
Write-Host "  Already validated." -ForegroundColor Gray

Write-Host "2. Ensuring resource group exists..." -ForegroundColor Magenta
$rgExists = az group exists --name $ResourceGroupName --only-show-errors
if ($rgExists -ne 'true') {
    az group create --name $ResourceGroupName --location $Location --only-show-errors | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create resource group '$ResourceGroupName'."
    }
}

Write-Host "3. Checking PostgreSQL server readiness..." -ForegroundColor Magenta
Wait-ForProvisioningPostgres -ResourceGroup $ResourceGroupName -TimeoutMinutes $PostgresWaitTimeoutMinutes -PollSeconds $PostgresWaitPollSeconds

Write-Host "4. Running what-if..." -ForegroundColor Magenta
$whatIfArgs = @(
    'deployment', 'group', 'what-if',
    '--resource-group', $ResourceGroupName,
    '--template-file', $mainTemplate,
    '--parameters', "@$parameterFile"
)

foreach ($key in $deploymentParams.Keys) {
    $whatIfArgs += @('--parameters', "$key=$($deploymentParams[$key])")
}

az @whatIfArgs --result-format FullResourcePayloads
if ($LASTEXITCODE -ne 0) {
    Write-Host "What-if failed on first attempt. Re-checking PostgreSQL state and retrying once..." -ForegroundColor Yellow
    Wait-ForProvisioningPostgres -ResourceGroup $ResourceGroupName -TimeoutMinutes $PostgresWaitTimeoutMinutes -PollSeconds $PostgresWaitPollSeconds
    az @whatIfArgs --result-format FullResourcePayloads
    if ($LASTEXITCODE -ne 0) {
        Write-Error "What-if failed after retry. Review the errors above."
    }
}

Write-Host ""
Write-Host "What-if completed successfully." -ForegroundColor Green
Write-Host "Next: run .\deploy.ps1" -ForegroundColor Green
Write-Host ""
