# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

<#
.SYNOPSIS
Update container images in Azure Container Apps to a new version.

.DESCRIPTION
This script updates the WebUI and Worker container apps to use a new image tag from GHCR.
It performs a zero-downtime rolling update by creating new revisions with the updated images.

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
            }
        }
    }
}

# Apply defaults
if (-not $EnvironmentName) { $EnvironmentName = 'mate-dev' }
if (-not $ResourceGroupName) { $ResourceGroupName = "$EnvironmentName-rg" }
if (-not $WebAppName) { $WebAppName = "$EnvironmentName-webui" }
if (-not $WorkerAppName) { $WorkerAppName = "$EnvironmentName-worker" }

# Validate Azure CLI
if (-not (Get-Command 'az' -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI not found. Install: https://learn.microsoft.com/cli/azure/install-azure-cli"
if ($WhatIf) {
    Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
    Write-Host "║      Azure Container Apps - Image Update (DRY-RUN)         ║" -ForegroundColor Yellow
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Yellow
} else {
    Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║      Azure Container Apps - Image Update                   ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
}
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║      Azure Container Apps - Image Update                   ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
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
$WhatIf) {
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

if (
Write-Host "NEW IMAGES:" -ForegroundColor Green
Write-Host "  WebUI:   $newWebImage"
Write-Host "  Worker:  $newWorkerImage"
Write-Host ""

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

# Update WebUI
Write-Host "[1/2] Updating WebUI container app..." -ForegroundColor Magenta
$webUpdateStart = Get-Date
try {
    az containerapp update `
        --name $WebAppName `
        --resource-group $ResourceGroupName `
        --image $newWebImage `
        --output none 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI returned exit code $LASTEXITCODE"
    }
    
    $webUpdateDuration = [math]::Round(((Get-Date) - $webUpdateStart).TotalSeconds, 1)
    Write-Host "  ✓ WebUI updated successfully ($webUpdateDuration s)" -ForegroundColor Green
} catch {
    Write-Error "Failed to update WebUI container app: $_"
}

Write-Host ""

# Update Worker
Write-Host "[2/2] Updating Worker container app..." -ForegroundColor Magenta
$workerUpdateStart = Get-Date
try {
    az containerapp update `
        --name $WorkerAppName `
        --resource-group $ResourceGroupName `
        --image $newWorkerImage `
        --output none 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI returned exit code $LASTEXITCODE"
    }
    
    $workerUpdateDuration = [math]::Round(((Get-Date) - $workerUpdateStart).TotalSeconds, 1)
    Write-Host "  ✓ Worker updated successfully ($workerUpdateDuration s)" -ForegroundColor Green
} catch {
    Write-Error "Failed to update Worker container app: $_"
}

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "Container image updates completed!" -ForegroundColor Green
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
