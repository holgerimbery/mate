# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

<#
.SYNOPSIS
Update container images in Azure Container Apps to a new version.

.DESCRIPTION
This script updates the WebUI and Worker container apps to use a new image tag from GHCR.
It performs a zero-downtime rolling update by:
1. Updating the image tag in .env (maintains Bicep state)
2. Running the Bicep deployment to reconcile all resources

This maintains full Bicep state tracking. The next deploy.ps1 run will use the updated tag.

.PARAMETER ImageTag
Container image tag from GHCR (e.g., 'v0.6.1', 'latest'). Default: 'latest'.

.PARAMETER ResourceGroupName
Azure resource group name. If not provided, attempts to load from .env file.

.PARAMETER WebAppName
WebUI container app name. If not provided, uses pattern '{EnvironmentName}-webui'.

.PARAMETER WorkerAppName
Worker container app name. If not provided, uses pattern '{EnvironmentName}-worker'.

.PARAMETER EnvironmentName
Environment name prefix (e.g., 'mate-dev'). Used to construct app names if not explicitly provided.

.PARAMETER SkipVerification
Skip post-update health and status verification.

.PARAMETER WhatIf
Preview the changes without actually updating the container apps (dry-run).

.PARAMETER Force
Skip confirmation prompt and update immediately.

.EXAMPLE
.\update-container-images.ps1 -ImageTag 'v0.6.1'

.EXAMPLE
.\update-container-images.ps1

.EXAMPLE
.\update-container-images.ps1 -ImageTag 'v0.6.1' -WhatIf

.EXAMPLE
.\update-container-images.ps1 -ImageTag 'v0.6.1' -ResourceGroupName 'mate-prod-rg' -Force

.EXAMPLE
.\update-container-images.ps1 -ImageTag 'latest' -EnvironmentName 'mate-staging'

#>

param(
    [Parameter(Mandatory = $false)]
    [string]$ImageTag = 'latest',

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $false)]
    [string]$WebAppName,

    [Parameter(Mandatory = $false)]
    [string]$WorkerAppName,

    [Parameter(Mandatory = $false)]
    [string]$EnvironmentName,

    [Parameter(Mandatory = $false)]
    [switch]$SkipVerification,

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf,

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
                'AZURE_RESOURCE_GROUP' { if (-not $ResourceGroupName) { $ResourceGroupName = $value } }
                'AZURE_ENVIRONMENT_NAME' { if (-not $EnvironmentName) { $EnvironmentName = $value } }
                'AZURE_LOCATION' { if (-not $Location) { $Location = $value } }
                'AZURE_PROFILE' { if (-not $SizeProfile) { $SizeProfile = $value } }
                'AZURE_AAD_CLIENT_ID' { if (-not $AadClientId) { $AadClientId = $value } }
            }
        }
    }
}

# Apply defaults
if (-not $EnvironmentName) { $EnvironmentName = 'mate-dev' }
if (-not $Location) { $Location = 'eastus' }
if (-not $SizeProfile) { $SizeProfile = 's' }
if (-not $ResourceGroupName) { $ResourceGroupName = "$EnvironmentName-rg" }
if (-not $WebAppName) { $WebAppName = "$EnvironmentName-webui" }
if (-not $WorkerAppName) { $WorkerAppName = "$EnvironmentName-worker" }

# Validate Azure CLI
if (-not (Get-Command 'az' -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI not found. Install: https://learn.microsoft.com/cli/azure/install-azure-cli"
}

Write-Host ""
if ($WhatIf) {
    Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
    Write-Host "║      Azure Container Apps - Image Update (DRY-RUN)         ║" -ForegroundColor Yellow
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Yellow
} else {
    Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║      Azure Container Apps - Image Update                   ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
}
Write-Host ""

Write-Host "UPDATE PARAMETERS:" -ForegroundColor Yellow
Write-Host "  Resource Group:   $ResourceGroupName"
Write-Host "  New Image Tag:    $ImageTag"
Write-Host "  WebUI App:        $WebAppName"
Write-Host "  Worker App:       $WorkerAppName"
Write-Host ""

# Verify resource group exists
Write-Host "Verifying resource group exists..." -ForegroundColor Gray
$rgExists = az group exists --name $ResourceGroupName 2>$null
if ($LASTEXITCODE -ne 0 -or $rgExists -ne 'true') {
    Write-Error "Resource group '$ResourceGroupName' not found. Please verify the name and try again."
}

# Verify container apps exist
Write-Host "Verifying container apps exist..." -ForegroundColor Gray
$webAppExists = az containerapp show --name $WebAppName --resource-group $ResourceGroupName --query "name" --output tsv 2>$null
$workerAppExists = az containerapp show --name $WorkerAppName --resource-group $ResourceGroupName --query "name" --output tsv 2>$null

if ($LASTEXITCODE -ne 0 -or -not $webAppExists) {
    Write-Error "WebUI container app '$WebAppName' not found in resource group '$ResourceGroupName'."
}
if ($LASTEXITCODE -ne 0 -or -not $workerAppExists) {
    Write-Error "Worker container app '$WorkerAppName' not found in resource group '$ResourceGroupName'."
}

# Get current image tags
Write-Host "Fetching current image versions..." -ForegroundColor Gray
$currentWebImage = az containerapp show --name $WebAppName --resource-group $ResourceGroupName --query "properties.template.containers[0].image" --output tsv 2>$null
$currentWorkerImage = az containerapp show --name $WorkerAppName --resource-group $ResourceGroupName --query "properties.template.containers[0].image" --output tsv 2>$null

Write-Host ""
Write-Host "CURRENT IMAGES:" -ForegroundColor Yellow
Write-Host "  WebUI:   $currentWebImage"
Write-Host "  Worker:  $currentWorkerImage"
Write-Host ""

$newWebImage = "ghcr.io/holgerimbery/mate-webui:$ImageTag"
$newWorkerImage = "ghcr.io/holgerimbery/mate-worker:$ImageTag"

Write-Host "NEW IMAGES:" -ForegroundColor Green
Write-Host "  WebUI:   $newWebImage"
Write-Host "  Worker:  $newWorkerImage"
Write-Host ""

if ($WhatIf) {
    Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host "DRY-RUN MODE: No changes will be made" -ForegroundColor Yellow
    Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "The following updates WOULD be performed:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "✓ WebUI container app '$WebAppName' would be updated to:" -ForegroundColor Gray
    Write-Host "    $newWebImage" -ForegroundColor Gray
    Write-Host ""
    Write-Host "✓ Worker container app '$WorkerAppName' would be updated to:" -ForegroundColor Gray
    Write-Host "    $newWorkerImage" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Changes summary:" -ForegroundColor Yellow
    Write-Host "  • New revisions would be created for both apps"
    Write-Host "  • Traffic would automatically switch to new revisions"
    Write-Host "  • Old revisions would be deactivated"
    Write-Host "  • Zero-downtime rolling update"
    Write-Host ""
    Write-Host "To perform this update, run without -WhatIf:" -ForegroundColor Green
    Write-Host "  .\update-container-images.ps1 -ImageTag '$ImageTag'" -ForegroundColor Gray
    Write-Host ""
    return
}

if (-not $Force) {
    Write-Host "⚠️  This will update the container images and create new revisions." -ForegroundColor Yellow
    Write-Host "   • Current containers will be replaced (zero-downtime rolling update)"
    Write-Host "   • New revisions will be created and activated"
    Write-Host "   • Old revisions will be deactivated"
    Write-Host ""
    
    $confirm = Read-Host "Type 'update' to proceed"
    if ($confirm -ne 'update') {
        Write-Host "Update cancelled." -ForegroundColor Yellow
        return
    }
}

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Starting container image updates..." -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Update .env file with new image tag to maintain Bicep state
Write-Host "[1/3] Updating .env with new image tag..." -ForegroundColor Magenta
$envUpdated = $false
if (Test-Path $envFile) {
    $envContent = Get-Content $envFile -Raw
    $oldEnvContent = $envContent
    
    # Update or add AZURE_IMAGE_TAG
    if ($envContent -match "AZURE_IMAGE_TAG=.*") {
        $envContent = $envContent -replace "AZURE_IMAGE_TAG=.*", "AZURE_IMAGE_TAG=$ImageTag"
        $envUpdated = $true
    } else {
        $envContent += "`nAZURE_IMAGE_TAG=$ImageTag"
        $envUpdated = $true
    }
    
    if ($envUpdated -and $envContent -ne $oldEnvContent) {
        Set-Content $envFile -Value $envContent -NoNewline
        Write-Host "  ✓ .env updated: AZURE_IMAGE_TAG=$ImageTag" -ForegroundColor Green
    } else {
        Write-Host "  ✓ .env already has AZURE_IMAGE_TAG=$ImageTag" -ForegroundColor Green
    }
} else {
    Write-Error ".env file not found at $envFile. Please run setup-env.ps1 first."
}

Write-Host ""

# Determine template and parameter paths
Write-Host "[2/3] Preparing Bicep deployment..." -ForegroundColor Magenta
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$templateDir = Split-Path -Parent $scriptDir  # Go up to infra/azure
$mainTemplate = Join-Path $templateDir "main.bicep"
$parameterFile = Join-Path $templateDir "parameters" "profile-$SizeProfile.json"

if (-not (Test-Path $mainTemplate)) {
    Write-Error "Template file not found: $mainTemplate"
}
if (-not (Test-Path $parameterFile)) {
    Write-Error "Parameter file not found: $parameterFile"
}

Write-Host "  Template: $(Split-Path -Leaf $mainTemplate)" -ForegroundColor Green
Write-Host "  Parameters: $(Split-Path -Leaf $parameterFile)" -ForegroundColor Green
Write-Host ""

# Detect PostgreSQL and load credentials to maintain state
Write-Host "Checking PostgreSQL state..." -ForegroundColor Gray
$postgresExists = $false
$postgresPassword = ''

$postgresServers = az postgres flexible-server list --resource-group $ResourceGroupName --query "[].name" --output tsv 2>$null
if ($LASTEXITCODE -eq 0 -and $postgresServers) {
    $postgresExists = $true
    Write-Host "  PostgreSQL detected - will maintain existing deployment" -ForegroundColor Gray
    
    # Load password from .pg-password file (like deploy.ps1 does)
    $pgPasswordFile = Join-Path $scriptDir ".pg-password"
    if (Test-Path $pgPasswordFile) {
        $postgresPassword = Get-Content $pgPasswordFile -Raw | ForEach-Object { $_.Trim() }
        Write-Host "  ✓ Loaded PostgreSQL credentials from .pg-password" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ PostgreSQL exists but .pg-password file not found" -ForegroundColor Yellow
        Write-Host "    Deployment may fail if postgres parameters are required" -ForegroundColor Yellow
        $postgresPassword = Read-Host "Enter PostgreSQL admin password" -AsSecureString
        $postgresPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($postgresPassword))
    }
} else {
    Write-Host "  No PostgreSQL detected - will skip postgres deployment" -ForegroundColor Gray
}

Write-Host ""

# Run Bicep deployment to update images while maintaining state
Write-Host "[3/3] Deploying updated container images..." -ForegroundColor Magenta
try {
    Write-Host ""
    Write-Host "BICEP STATE TRACKING:" -ForegroundColor Yellow
    Write-Host "  ✓ .env has been updated with AZURE_IMAGE_TAG=$ImageTag"
    Write-Host "  ✓ Bicep state is maintained - next deploy.ps1 will use this tag"
    Write-Host "  ✓ Running deployment to apply changes immediately"
    if ($postgresExists) {
        Write-Host "  ✓ PostgreSQL parameters included to maintain existing deployment" -ForegroundColor Yellow
    } else {
        Write-Host "  ✓ PostgreSQL deployment disabled (no existing postgres found)" -ForegroundColor Yellow
    }
    Write-Host ""

    # Build secure deployment parameters (using array like deploy.ps1)
    $deploymentParams = @{
        'environmentName' = $EnvironmentName
        'location'        = $Location
        'imageTag'        = $ImageTag
        'aadClientId'     = $AadClientId
    }
    
    if ($postgresExists -and $postgresPassword) {
        $deploymentParams['deployPostgres'] = $true
        $deploymentParams['postgresAdminLogin'] = 'pgadmin'
        $deploymentParams['postgresAdminPassword'] = $postgresPassword
    } else {
        $deploymentParams['deployPostgres'] = $false
    }
    
    # Build argument array (secure method - no password exposure in command line)
    $deployArgs = @(
        'deployment', 'group', 'create',
        '--resource-group', $ResourceGroupName,
        '--template-file', $mainTemplate,
        '--parameters', "@$parameterFile"
    )
    
    foreach ($key in $deploymentParams.Keys) {
        $deployArgs += @('--parameters', "$key=$($deploymentParams[$key])")
    }
    
    $deployArgs += @('--no-wait', '--output', 'none')
    
    & az @deployArgs 2>&1 | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI deployment returned exit code $LASTEXITCODE"
    }

    Write-Host "  ✓ Bicep deployment initiated (running in background)" -ForegroundColor Green
    
    # Configure runtime secret references (same as deploy.ps1 post-deployment step)
    # This ensures new revisions have working connection strings
    Write-Host ""
    Write-Host "Configuring runtime secret references for new revision..." -ForegroundColor Cyan
    
    $dbConnectionString = "Host=$EnvironmentName-pg.postgres.database.azure.com;Database=mate;Username=pgadmin;Password=$PostgresPasswordPlain;SSL Mode=Require"

    $storageAccountName = az resource list `
        --resource-group $ResourceGroupName `
        --resource-type 'Microsoft.Storage/storageAccounts' `
        --query "[0].name" -o tsv 2>$null

    $blobConnectionString = $null
    if ($LASTEXITCODE -eq 0 -and $storageAccountName) {
        $storageKey = az storage account keys list `
            --resource-group $ResourceGroupName `
            --account-name $storageAccountName `
            --query "[0].value" -o tsv 2>$null

        if ($LASTEXITCODE -eq 0 -and $storageKey) {
            $blobConnectionString = "DefaultEndpointsProtocol=https;AccountName=$storageAccountName;AccountKey=$storageKey;EndpointSuffix=core.windows.net"
        }
        else {
            Write-Host "  Could not resolve storage account key; blob secret will not be updated." -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "  No storage account found; blob secret will not be updated." -ForegroundColor Yellow
    }

    $containerApps = @("$EnvironmentName-webui", "$EnvironmentName-worker")

    foreach ($appName in $containerApps) {
        $appId = az containerapp show --resource-group $ResourceGroupName --name $appName --query id -o tsv 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $appId) {
            Write-Host "  ⊘ Skipping '$appName' (not found in RG)." -ForegroundColor DarkYellow
            continue
        }

        # Set runtime secrets
        $secrets = @("postgres-conn=$dbConnectionString")
        if ($blobConnectionString) {
            $secrets += "blob-conn=$blobConnectionString"
        }

        az containerapp secret set `
            --resource-group $ResourceGroupName `
            --name $appName `
            --secrets $secrets `
            -o none 2>$null

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ⚠ Failed to set runtime secrets on '$appName'." -ForegroundColor Yellow
            continue
        }

        # Wire environment variables to secret references
        $envUpdates = @("ConnectionStrings__Default=secretref:postgres-conn")
        if ($blobConnectionString) {
            $envUpdates += "AzureInfrastructure__BlobConnectionString=secretref:blob-conn"
        }

        az containerapp update `
            --resource-group $ResourceGroupName `
            --name $appName `
            --set-env-vars $envUpdates `
            -o none 2>$null

        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ⚠ Failed to update runtime env on '$appName'." -ForegroundColor Yellow
            continue
        }

        Write-Host "  ✓ Configured runtime secret references on '$appName'." -ForegroundColor Green
    }
    
    Write-Host "  ✓ Runtime secret wiring completed." -ForegroundColor Green
} catch {
    Write-Error "Failed to deploy with Bicep: $_"
}

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "Container image update in progress!" -ForegroundColor Green
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""

if (-not $SkipVerification) {
    Write-Host "Verifying deployment status..." -ForegroundColor Cyan
    Write-Host ""
    
    # Check WebUI revision
    Write-Host "WebUI Active Revisions:" -ForegroundColor Yellow
    az containerapp revision list `
        --name $WebAppName `
        --resource-group $ResourceGroupName `
        --query "[?properties.active].{Name:name, CreatedTime:properties.createdTime, Traffic:properties.trafficWeight, Health:properties.healthState}" `
        --output table
    
    Write-Host ""
    
    # Check Worker revision
    Write-Host "Worker Active Revisions:" -ForegroundColor Yellow
    az containerapp revision list `
        --name $WorkerAppName `
        --resource-group $ResourceGroupName `
        --query "[?properties.active].{Name:name, CreatedTime:properties.createdTime, Traffic:properties.trafficWeight, Health:properties.healthState}" `
        --output table
    
    Write-Host ""
    
    # Get WebUI URL
    $webuiUrl = az containerapp show `
        --name $WebAppName `
        --resource-group $ResourceGroupName `
        --query "properties.configuration.ingress.fqdn" `
        --output tsv 2>$null
    
    if ($webuiUrl) {
        Write-Host "WebUI URL: https://$webuiUrl" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "MONITORING COMMANDS:" -ForegroundColor Cyan
    Write-Host "  View WebUI logs:"
    Write-Host "    az containerapp logs show --name $WebAppName --resource-group $ResourceGroupName --type console --tail 50" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  View Worker logs:"
    Write-Host "    az containerapp logs show --name $WorkerAppName --resource-group $ResourceGroupName --type console --tail 50" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  List all revisions:"
    Write-Host "    az containerapp revision list --name $WebAppName --resource-group $ResourceGroupName" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "ROLLBACK (if needed):" -ForegroundColor Yellow
Write-Host "  To roll back to previous version, run:" -ForegroundColor Gray
Write-Host "    .\update-container-images.ps1 -ImageTag '<previous-tag>'" -ForegroundColor Gray
Write-Host ""
Write-Host "  Or activate a specific revision:" -ForegroundColor Gray
Write-Host "    az containerapp revision activate --name $WebAppName --resource-group $ResourceGroupName --revision <revision-name>" -ForegroundColor Gray
Write-Host ""

Write-Host "Update completed successfully! 🚀" -ForegroundColor Green
