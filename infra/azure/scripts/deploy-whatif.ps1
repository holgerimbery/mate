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
    [string]$AadClientId
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

# Prompt for values needed when deployPostgres=true
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
    'deployPostgres'        = $true
}

Write-Host "Preparing what-if deployment..." -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Setting subscription context..." -ForegroundColor Magenta
az account set --subscription $SubscriptionId --only-show-errors
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to set subscription context. Run 'az login --tenant $TenantId' first."
}

Write-Host "2. Ensuring resource group exists..." -ForegroundColor Magenta
$rgExists = az group exists --name $ResourceGroupName --only-show-errors
if ($rgExists -ne 'true') {
    az group create --name $ResourceGroupName --location $Location --only-show-errors | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create resource group '$ResourceGroupName'."
    }
}

Write-Host "3. Running what-if..." -ForegroundColor Magenta
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
    Write-Error "What-if failed. Review the errors above."
}

Write-Host ""
Write-Host "What-if completed successfully." -ForegroundColor Green
Write-Host "Next: run .\deploy.ps1" -ForegroundColor Green
Write-Host ""
