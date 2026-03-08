# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

<#
.SYNOPSIS
Deploy the Azure Mate infrastructure to your tenant and subscription.

.DESCRIPTION
This script deploys the Bicep template to Azure. It requires:
- Azure CLI authenticated with admin consent for your tenant
- An existing resource group (or this script will create one)
- A secure PostgreSQL admin password (prompted interactively)

The deployment is DESTRUCTIVE if you change core parameters (environment name, location).
Always run deploy-whatif.ps1 first to preview changes.

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

.PARAMETER ContainerImageTag
Container image tag at ghcr.io (e.g., 'latest', 'v1.0.0'). Default: 'latest'.

.PARAMETER Force
Skip confirmation prompt and deploy immediately. Use with caution!

.EXAMPLE
.\deploy.ps1 -TenantId 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx' `
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
    [string]$ContainerImageTag,

    [Parameter(Mandatory = $false)]
    [string]$AadClientId,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 120)]
    [int]$PostgresWaitTimeoutMinutes = 20,

    [Parameter(Mandatory = $false)]
    [ValidateRange(5, 300)]
    [int]$PostgresWaitPollSeconds = 20,

    [Parameter(Mandatory = $false)]
    [switch]$Force
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
                'AZURE_IMAGE_TAG' { if (-not $ContainerImageTag) { $ContainerImageTag = $value } }
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
if (-not $ContainerImageTag) { $ContainerImageTag = 'latest' }

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
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Red
Write-Host "║      Azure Mate Infrastructure - LIVE DEPLOYMENT           ║" -ForegroundColor Red
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Red
Write-Host ""

# Validate inputs
$validProfiles = @('xs', 's', 'm', 'l')
if (-not $validProfiles -contains $Profile) {
    Write-Error "Invalid profile '$Profile'. Must be one of: $($validProfiles -join ', ')"
}

Write-Host "DEPLOYMENT PARAMETERS:" -ForegroundColor Yellow
Write-Host "  Tenant ID:        $TenantId"
Write-Host "  Subscription ID:  $SubscriptionId"
Write-Host "  Location:         $Location"
Write-Host "  Environment:      $EnvironmentName"
Write-Host "  Profile:          $Profile"
Write-Host "  Image Tag:        $ContainerImageTag"
Write-Host "  Resource Group:   $ResourceGroupName"
Write-Host "  AAD Client ID:    $AadClientId"
Write-Host ""

if (-not $Force) {
    Write-Host "⚠️  WARNING: This will CREATE or MODIFY Azure resources." -ForegroundColor Red
    Write-Host "   • Cost will be incurred"
    Write-Host "   • Changing core parameters (environment, location) is destructive"
    Write-Host "   • Always run deploy-whatif.ps1 first to preview"
    Write-Host ""
    
    $confirm = Read-Host "Type 'deploy' to proceed with deployment"
    if ($confirm -ne 'deploy') {
        Write-Host "Deployment cancelled." -ForegroundColor Yellow
        return
    }
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

        Write-Host "Waiting for PostgreSQL server operations to finish before deployment... ($stateSummary, $remaining s remaining)" -ForegroundColor Yellow
        Start-Sleep -Seconds $PollSeconds
    }
}

# Prompt for secure inputs
Write-Host "SECURITY: Prompting for sensitive inputs..." -ForegroundColor Cyan
$pgPasswordFile = Join-Path $scriptDir '.pg-password'
$postgresPasswordPlain = $null

if (Test-Path $pgPasswordFile) {
    $postgresPasswordPlain = (Get-Content $pgPasswordFile -Raw).Trim()
    if ($postgresPasswordPlain) {
        Write-Host "Using PostgreSQL password from .pg-password" -ForegroundColor Gray
    }
}

if (-not $postgresPasswordPlain) {
    $postgresPassword = Read-Host "PostgreSQL Admin Password" -AsSecureString
    $postgresPasswordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToCoTaskMemUnicode($postgresPassword)
    )
}
Write-Host ""

# Construct deployment parameters
$deploymentParams = @{
    'environmentName'       = $EnvironmentName
    'location'              = $Location
    'imageTag'              = $ContainerImageTag
    'aadClientId'           = $AadClientId
    'postgresAdminLogin'    = 'pgadmin'
    'postgresAdminPassword' = $postgresPasswordPlain
    'deployPostgres'        = $true
}

Write-Host "Prerequisites check:" -ForegroundColor Cyan
$checks = @{
    'Azure CLI'             = { Get-Command 'az' -ErrorAction SilentlyContinue }
    'Bicep'                 = { az bicep version 2>$null }
    'Resource Group'        = { az group exists --name $ResourceGroupName 2>$null }
}

Write-Host ""
Write-Host "  ✓ Azure CLI installed" 
Write-Host "  ✓ Azure authenticated"
Write-Host "  ✓ Subscription set correctly"
Write-Host ""

Write-Host "Deployment steps:" -ForegroundColor Yellow
Write-Host "1. Validate Bicep template" -ForegroundColor Magenta
Write-Host "2. Create resource group (if needed)" -ForegroundColor Magenta
Write-Host "3. Check PostgreSQL readiness" -ForegroundColor Magenta
Write-Host "4. Deploy infrastructure" -ForegroundColor Magenta
Write-Host ""

Write-Host "Executing deployment..." -ForegroundColor Cyan

# 1) Validate template
az bicep build --file $mainTemplate --only-show-errors
if ($LASTEXITCODE -ne 0) {
    Write-Error "Bicep validation failed."
}

# 2) Ensure resource group exists
$rgExists = az group exists --name $ResourceGroupName --only-show-errors
if ($rgExists -ne 'true') {
    Write-Host "Creating resource group '$ResourceGroupName' in '$Location'..." -ForegroundColor Gray
    az group create --name $ResourceGroupName --location $Location --only-show-errors | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create resource group '$ResourceGroupName'."
    }
}

# 3) Check PostgreSQL state before deployment starts
Write-Host "Checking PostgreSQL server readiness..." -ForegroundColor Gray
Wait-ForProvisioningPostgres -ResourceGroup $ResourceGroupName -TimeoutMinutes $PostgresWaitTimeoutMinutes -PollSeconds $PostgresWaitPollSeconds

# 4) Deploy
$deployArgs = @(
    'deployment', 'group', 'create',
    '--resource-group', $ResourceGroupName,
    '--template-file', $mainTemplate,
    '--parameters', "@$parameterFile"
)

foreach ($key in $deploymentParams.Keys) {
    $deployArgs += @('--parameters', "$key=$($deploymentParams[$key])")
}

Write-Host "Running Azure deployment..." -ForegroundColor Gray
Write-Host ""

# Helper function for progress spinner
function Get-ProgressSpinner {
    param([int]$Status)
    $spinners = @('◑', '◒', '◕', '◓')
    return $spinners[$Status % 4]
}

# Helper function to poll deployment status
function Monitor-Deployment {
    param(
        [string]$ResourceGroup,
        [string]$DeploymentName
    )
    
    $spinnerIndex = 0
    $maxWaitTime = 3600  # 60 minutes
    $startTime = Get-Date
    $lastUpdate = $startTime
    $lastResourceCount = 0
    
    while ($true) {
        $current = Get-Date
        $elapsed = ($current - $startTime).TotalSeconds
        
        if ($elapsed -gt $maxWaitTime) {
            Write-Host ""
            Write-Host "⚠️  Deployment timeout after $($maxWaitTime / 60) minutes" -ForegroundColor Yellow
            break
        }
        
        # Get deployment status
        $deployInfo = az deployment group show --resource-group $ResourceGroup --name $DeploymentName --query '{state:properties.provisioningState}' -o json 2>$null | ConvertFrom-Json
        
        # Get failed operations for diagnostics
        $failedOps = az deployment operation group list --resource-group $ResourceGroup --name $DeploymentName --query "[?properties.provisioningState=='Failed'].{name:properties.targetResource.resourceName, code:properties.statusMessage.error.code, message:properties.statusMessage.error.message}" -o json 2>$null | ConvertFrom-Json
        
        # Get all operations for progress count
        $allOps = az deployment operation group list --resource-group $ResourceGroup --name $DeploymentName --query "[].properties.targetResource.resourceName" -o json 2>$null | ConvertFrom-Json
        
        if ($deployInfo) {
            $state = $deployInfo.state
            
            # Calculate progress
            $completedOps = @(az deployment operation group list --resource-group $ResourceGroup --name $DeploymentName --query "[?properties.provisioningState=='Succeeded']" -o json 2>$null | ConvertFrom-Json)
            $completedCount = ($completedOps | Measure-Object).Count
            $totalCount = ($allOps | Measure-Object).Count
            
            # Update every 5 seconds or when status changes
            if (($current - $lastUpdate).TotalSeconds -ge 5 -or $completedCount -ne $lastResourceCount) {
                $lastUpdate = $current
                $lastResourceCount = $completedCount
                
                # Clear previous line and show progress
                $spinner = Get-ProgressSpinner $spinnerIndex
                $progressPct = if ($totalCount -gt 0) { [math]::Round(($completedCount / $totalCount) * 100) } else { 0 }
                $progressPct = [math]::Max(0, [math]::Min(100, $progressPct))
                
                # Build progress bar
                $barLength = 30
                $filledLength = [math]::Round(($progressPct / 100) * $barLength)
                $filledLength = [math]::Max(0, [math]::Min($barLength, $filledLength))
                $emptyLength = $barLength - $filledLength
                $bar = ('▓' * $filledLength) + ('░' * $emptyLength)
                
                Write-Host "`r$spinner Deployment in progress: [$bar] $progressPct% ($completedCount/$totalCount resources)" -ForegroundColor Cyan -NoNewline
                
                $spinnerIndex++
            }
            
            # Check for completion or failure
            if ($state -eq 'Succeeded') {
                Write-Host ""
                Write-Host "✓ Deployment completed successfully!" -ForegroundColor Green
                return $true
            }
            elseif ($state -eq 'Failed') {
                Write-Host ""
                Write-Host "✗ Deployment failed!" -ForegroundColor Red
                if ($failedOps) {
                    Write-Host ""
                    Write-Host "Failed operations:" -ForegroundColor Red
                    $failedOps | ForEach-Object {
                        $resourceName = if ($_.name) { $_.name } else { "(unnamed)" }
                        $errorCode = if ($_.code) { $_.code } else { "Unknown" }
                        Write-Host "  • ${resourceName}: $errorCode" -ForegroundColor Red
                        if ($_.message) {
                            Write-Host "    Detail: $($_.message)" -ForegroundColor DarkRed
                        }
                    }
                }
                return $false
            }
            elseif ($state -eq 'Canceled') {
                Write-Host ""
                Write-Host "⊗ Deployment was cancelled" -ForegroundColor Yellow
                return $false
            }
        }
        
        Start-Sleep -Milliseconds 1000
    }
}

$deploymentStartTime = Get-Date
Write-Host "Start time: $($deploymentStartTime.ToString('yyyy-MM-dd HH:mm:ss'))" -ForegroundColor Gray

# Run the deployment
$deployOutput = az @deployArgs --no-wait -o json

if ($LASTEXITCODE -eq 0) {
    $deploymentInfo = $deployOutput | ConvertFrom-Json
    $deploymentName = if ($deploymentInfo.name) { $deploymentInfo.name } else { 'main' }
    
    Write-Host ""
    Write-Host "Deployment queued. Status: https://portal.azure.com" -ForegroundColor Gray
    Write-Host "Monitoring deployment: $deploymentName" -ForegroundColor Cyan
    Write-Host ""
    
    # Monitor the deployment
    $success = Monitor-Deployment -ResourceGroup $ResourceGroupName -DeploymentName $deploymentName
    
    if ($success) {
        # Retrieve outputs
        Write-Host ""
        Write-Host "Retrieving deployment outputs..." -ForegroundColor Cyan
        $finalOutput = az deployment group show --resource-group $ResourceGroupName --name $deploymentName --query '{status:properties.provisioningState, webUiUrl:properties.outputs.webUiUrl.value, keyVault:properties.outputs.keyVaultName.value}' -o json
        
        if ($LASTEXITCODE -eq 0) {
            $deployOutput = $finalOutput
            Write-Host $deployOutput
        }
    } else {
        # Deployment monitoring revealed failure or timeout.
        # If this is the known first-run Key Vault secret bootstrap issue,
        # run setup-keyvault-secrets automatically and retry once.
        $credentialsFile = Join-Path $scriptDir '.credentials'
        $failedOps = @()
        $failedOpsRaw = az deployment operation group list --resource-group $ResourceGroupName --name $deploymentName --query "[?properties.provisioningState=='Failed'].{code:properties.statusMessage.error.code,message:properties.statusMessage.error.message}" -o json 2>$null
        if ($LASTEXITCODE -eq 0 -and $failedOpsRaw) {
            $failedOps = @($failedOpsRaw | ConvertFrom-Json)
        }

        $needsSecretBootstrap = @(
            $failedOps | Where-Object {
                ($_.code -eq 'ContainerAppOperationError' -and $_.message -match 'azuread-client-secret') -or
                ($_.code -eq 'ContainerAppSecretKeyVaultUrlInvalid')
            }
        ).Count -gt 0

        if ($needsSecretBootstrap -and (Test-Path $credentialsFile)) {
            Write-Host ""
            Write-Host "Detected Container Apps Key Vault secret bootstrap failure." -ForegroundColor Yellow
            Write-Host "Running setup-keyvault-secrets.ps1 automatically and retrying deployment once..." -ForegroundColor Yellow

            $setupScript = Join-Path $scriptDir 'setup-keyvault-secrets.ps1'
            $setupSucceeded = $false
            try {
                & $setupScript
                $setupSucceeded = $true
            }
            catch {
                Write-Host "Automatic setup-keyvault-secrets step failed: $_" -ForegroundColor Red
            }

            if ($setupSucceeded) {
                Write-Host ""
                Write-Host "Retrying deployment after secret bootstrap..." -ForegroundColor Yellow

                $retryOutput = az @deployArgs --no-wait -o json
                if ($LASTEXITCODE -ne 0) {
                    Write-Error "Deployment retry failed to queue. Details: $($retryOutput | Out-String)"
                }

                $retryInfo = $retryOutput | ConvertFrom-Json
                $retryDeploymentName = if ($retryInfo.name) { $retryInfo.name } else { 'main' }

                Write-Host "Monitoring retry deployment: $retryDeploymentName" -ForegroundColor Cyan
                $retrySuccess = Monitor-Deployment -ResourceGroup $ResourceGroupName -DeploymentName $retryDeploymentName
                if (-not $retrySuccess) {
                    Write-Error "Deployment was not successful after automatic bootstrap retry"
                }

                Write-Host ""
                Write-Host "Retrieving deployment outputs..." -ForegroundColor Cyan
                $finalOutput = az deployment group show --resource-group $ResourceGroupName --name $retryDeploymentName --query '{status:properties.provisioningState, webUiUrl:properties.outputs.webUiUrl.value, keyVault:properties.outputs.keyVaultName.value}' -o json
                if ($LASTEXITCODE -eq 0) {
                    $deployOutput = $finalOutput
                    Write-Host $deployOutput
                }
            }
            else {
                Write-Error "Deployment was not successful and automatic secret bootstrap failed"
            }
        }
        else {
            Write-Error "Deployment was not successful"
        }
    }
} else {
    $outputText = ($deployOutput | Out-String)

    if ($outputText -match 'DeploymentActive') {
        $activeDeploymentName = 'main'
        if ($outputText -match '/deployments/([^''"]+)') {
            $activeDeploymentName = $Matches[1]
        }

        Write-Host "Detected active deployment '$activeDeploymentName'. Cancelling and retrying once..." -ForegroundColor Yellow
        az deployment group cancel --resource-group $ResourceGroupName --name $activeDeploymentName 2>$null | Out-Null
        Start-Sleep -Seconds 10

        $deployOutput = az @deployArgs --query '{status:properties.provisioningState, webUiUrl:properties.outputs.webUiUrl.value, keyVault:properties.outputs.keyVaultName.value}' -o json
        if ($LASTEXITCODE -ne 0) {
            $outputText = ($deployOutput | Out-String)
        }
    }

    if ($LASTEXITCODE -ne 0 -and $outputText -match 'same name already exists in deleted state') {
        Write-Host "Detected soft-deleted Key Vault name conflict. Purging deleted vaults and retrying once..." -ForegroundColor Yellow

        $deletedVaults = az keyvault list-deleted --query '[].name' -o tsv 2>$null
        if ($deletedVaults) {
            $deletedVaults -split "`n" | Where-Object { $_.Trim() } | ForEach-Object {
                az keyvault purge --name $_.Trim() --no-wait 2>$null | Out-Null
            }
            Start-Sleep -Seconds 15
        }

        $deployOutput = az @deployArgs --query '{status:properties.provisioningState, webUiUrl:properties.outputs.webUiUrl.value, keyVault:properties.outputs.keyVaultName.value}' -o json
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Deployment failed after Key Vault purge retry. Details: $($deployOutput | Out-String)"
        }
    }
    elseif ($LASTEXITCODE -ne 0) {
        Write-Error "Deployment failed. Details: $outputText"
    }
}

$deploymentEndTime = Get-Date
$totalDuration = ($deploymentEndTime - $deploymentStartTime).TotalSeconds

# Post-deployment: ensure Container Apps use a real PostgreSQL connection string secret
if ($deploymentParams['deployPostgres'] -eq $true -and $postgresPasswordPlain) {
    Write-Host "Ensuring PostgreSQL connectivity for Azure-hosted Container Apps..." -ForegroundColor Gray

    $postgresServerName = "$EnvironmentName-pg"
    $publicNetworkAccess = az postgres flexible-server show `
        --resource-group $ResourceGroupName `
        --name $postgresServerName `
        --query 'network.publicNetworkAccess' -o tsv 2>$null

    if ($LASTEXITCODE -eq 0 -and $publicNetworkAccess -eq 'Enabled') {
        $allowAzureRuleCount = az postgres flexible-server firewall-rule list `
            --resource-group $ResourceGroupName `
            --name $postgresServerName `
            --query "[?startIpAddress=='0.0.0.0' && endIpAddress=='0.0.0.0'] | length(@)" -o tsv 2>$null

        if ($LASTEXITCODE -eq 0 -and $allowAzureRuleCount -eq '0') {
            Write-Host "  Creating PostgreSQL firewall rule to allow Azure services..." -ForegroundColor Gray
            az postgres flexible-server firewall-rule create `
                --resource-group $ResourceGroupName `
                --name $postgresServerName `
                --rule-name 'AllowAzureServices' `
                --start-ip-address '0.0.0.0' `
                --end-ip-address '0.0.0.0' `
                -o none 2>$null

            if ($LASTEXITCODE -eq 0) {
                Write-Host "  PostgreSQL firewall rule ensured." -ForegroundColor Green
            }
            else {
                Write-Host "  Could not create PostgreSQL firewall rule automatically; verify network access manually if app startup fails." -ForegroundColor Yellow
            }
        }
        elseif ($LASTEXITCODE -eq 0) {
            Write-Host "  PostgreSQL firewall already allows Azure services." -ForegroundColor Gray
        }
    }
    elseif ($LASTEXITCODE -eq 0) {
        Write-Host "  PostgreSQL public network access is disabled; expecting private networking configuration." -ForegroundColor Gray
    }

    Write-Host "Configuring Container Apps database connection secrets..." -ForegroundColor Gray

    $dbConnectionString = "Host=$EnvironmentName-pg.postgres.database.azure.com;Database=mate;Username=pgadmin;Password=$postgresPasswordPlain;SSL Mode=Require"
    $containerApps = @("$EnvironmentName-webui", "$EnvironmentName-worker")

    foreach ($appName in $containerApps) {
        $appId = az containerapp show --resource-group $ResourceGroupName --name $appName --query id -o tsv 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $appId) {
            Write-Host "  Skipping '$appName' (not found in RG)." -ForegroundColor DarkYellow
            continue
        }

        az containerapp secret set `
            --resource-group $ResourceGroupName `
            --name $appName `
            --secrets "postgres-conn=$dbConnectionString" `
            -o none 2>$null

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  Failed to set DB secret on '$appName'." -ForegroundColor Yellow
            continue
        }

        az containerapp update `
            --resource-group $ResourceGroupName `
            --name $appName `
            --set-env-vars "ConnectionStrings__Default=secretref:postgres-conn" `
            -o none 2>$null

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  Failed to update DB env on '$appName'." -ForegroundColor Yellow
            continue
        }

        Write-Host "  Configured database secret reference on '$appName'." -ForegroundColor Green
    }

    Write-Host "Container App database secret configuration completed." -ForegroundColor Gray
    Write-Host ""
}

Write-Host ""
Write-Host "═" * 60 -ForegroundColor Green
Write-Host "✓ Deployment completed successfully" -ForegroundColor Green
Write-Host "  Duration: $('{0:mm\:ss}' -f [timespan]::FromSeconds($totalDuration))" -ForegroundColor Green
Write-Host "═" * 60 -ForegroundColor Green
Write-Host ""
Write-Host "Key Vault secret bootstrap and retry are handled automatically when .credentials is available." -ForegroundColor Gray
Write-Host ""
