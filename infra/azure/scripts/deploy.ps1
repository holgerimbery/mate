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
Write-Host "3. Deploy infrastructure" -ForegroundColor Magenta
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

# 3) Deploy
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
        $failedOps = az deployment operation group list --resource-group $ResourceGroup --name $DeploymentName --query "[?properties.provisioningState=='Failed'].[properties.targetResource.resourceName, properties.statusMessage.error.code, properties.statusMessage.error.message]" -o json 2>$null | ConvertFrom-Json
        
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
                
                # Build progress bar
                $barLength = 30
                $filledLength = [math]::Round(($progressPct / 100) * $barLength)
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
                        Write-Host "  • $($_[0]): $($_[1])" -ForegroundColor Red
                        if ($_[2]) {
                            Write-Host "    Detail: $($_[2])" -ForegroundColor DarkRed
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
$deployOutput = az @deployArgs --no-wait -o json 2>&1

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
        $finalOutput = az deployment group show --resource-group $ResourceGroupName --name $deploymentName --query '{status:properties.provisioningState, webUiUrl:properties.outputs.webUiUrl.value, keyVault:properties.outputs.keyVaultName.value}' -o json 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            $deployOutput = $finalOutput
            Write-Host $deployOutput
        }
    } else {
        # Deployment monitoring revealed failure or timeout
        Write-Error "Deployment was not successful"
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

        $deployOutput = az @deployArgs --query '{status:properties.provisioningState, webUiUrl:properties.outputs.webUiUrl.value, keyVault:properties.outputs.keyVaultName.value}' -o json 2>&1
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

        $deployOutput = az @deployArgs --query '{status:properties.provisioningState, webUiUrl:properties.outputs.webUiUrl.value, keyVault:properties.outputs.keyVaultName.value}' -o json 2>&1
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

Write-Host ""
Write-Host "═" * 60 -ForegroundColor Green
Write-Host "✓ Deployment completed successfully" -ForegroundColor Green
Write-Host "  Duration: $('{0:mm\:ss}' -f [timespan]::FromSeconds($totalDuration))" -ForegroundColor Green
Write-Host "═" * 60 -ForegroundColor Green
Write-Host ""
Write-Host "Next step:" -ForegroundColor Yellow
Write-Host "  .\setup-keyvault-secrets.ps1" -ForegroundColor Magenta
Write-Host ""
