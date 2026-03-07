<#
.SYNOPSIS
Cleans all resources from an Azure resource group without deleting the resource group itself.

.DESCRIPTION
- Deletes all live resources in the specified resource group.
- Cancels running deployments in that resource group.
- Purges soft-deleted Key Vaults that belong to that resource group only.

This script is intended to provide a clean starting point for re-deployment.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [int]$WaitTimeoutSeconds = 900,

    [int]$PollIntervalSeconds = 10
)

$ErrorActionPreference = 'Stop'

Write-Host ''
Write-Host '=== RG Cleanup Start ===' -ForegroundColor Cyan
Write-Host "Resource Group: $ResourceGroupName" -ForegroundColor Gray

# Validate Azure CLI and login context.
$azVersion = az version 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Azure CLI not available. Install Azure CLI and login first.'
}

$account = az account show --query id -o tsv 2>$null
if ($LASTEXITCODE -ne 0 -or -not $account) {
    throw 'Not logged into Azure. Run: az login'
}

$rgExists = az group exists --name $ResourceGroupName -o tsv 2>$null
if ($rgExists -ne 'true') {
    throw "Resource group '$ResourceGroupName' does not exist."
}

Write-Host 'Step 1/4: Cancel running deployments in RG...' -ForegroundColor Yellow
$runningDeployments = az deployment group list --resource-group $ResourceGroupName --query "[?properties.provisioningState=='Running'].name" -o tsv 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to list deployments.'
}

if ($runningDeployments) {
    $runningDeployments -split "`n" | Where-Object { $_.Trim() } | ForEach-Object {
        $name = $_.Trim()
        Write-Host "  Cancelling deployment: $name" -ForegroundColor Gray
        az deployment group cancel --resource-group $ResourceGroupName --name $name 2>$null | Out-Null
    }
}
else {
    Write-Host '  No running deployments found.' -ForegroundColor Gray
}

Write-Host 'Step 2/4: Delete all live resources in RG...' -ForegroundColor Yellow
$resourceIds = az resource list --resource-group $ResourceGroupName --query '[].id' -o tsv 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to list resources in resource group.'
}

if ($resourceIds) {
    $ids = $resourceIds -split "`n" | Where-Object { $_.Trim() }
    Write-Host "  Resources found: $($ids.Count)" -ForegroundColor Gray

    foreach ($id in $ids) {
        Write-Host "  Deleting: $id" -ForegroundColor Gray
        az resource delete --ids $id --no-wait 2>$null | Out-Null
    }
}
else {
    Write-Host '  No live resources found.' -ForegroundColor Gray
}

Write-Host 'Step 3/4: Wait for resource deletions to complete...' -ForegroundColor Yellow
$start = Get-Date
while ($true) {
    $remainingIds = az resource list --resource-group $ResourceGroupName --query '[].id' -o tsv 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed while checking remaining resources.'
    }

    if (-not $remainingIds) {
        Write-Host '  Live resource count: 0' -ForegroundColor Green
        break
    }

    $remainingCount = ($remainingIds -split "`n" | Where-Object { $_.Trim() }).Count
    $elapsed = [int]((Get-Date) - $start).TotalSeconds
    Write-Host "  Waiting... remaining resources: $remainingCount (elapsed ${elapsed}s)" -ForegroundColor Gray

    if ($elapsed -ge $WaitTimeoutSeconds) {
        throw "Timeout waiting for resource deletions. Remaining count: $remainingCount"
    }

    Start-Sleep -Seconds $PollIntervalSeconds
}

Write-Host 'Step 4/4: Purge RG-scoped soft-deleted Key Vaults...' -ForegroundColor Yellow
$deletedKvsJson = az keyvault list-deleted -o json 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to list deleted Key Vaults.'
}

$deletedKvs = @()
if ($deletedKvsJson) {
    $deletedKvs = $deletedKvsJson | ConvertFrom-Json
}

$rgLower = $ResourceGroupName.ToLower()
$rgScopedDeletedVaults = @()
foreach ($kv in $deletedKvs) {
    $id = [string]$kv.id
    if (-not [string]::IsNullOrWhiteSpace($id) -and $id.ToLower().Contains("/resourcegroups/$rgLower/")) {
        $rgScopedDeletedVaults += [string]$kv.name
    }
}

if ($rgScopedDeletedVaults.Count -gt 0) {
    foreach ($name in ($rgScopedDeletedVaults | Select-Object -Unique)) {
        Write-Host "  Purging deleted Key Vault: $name" -ForegroundColor Gray
        az keyvault purge --name $name 2>$null | Out-Null
    }
}
else {
    Write-Host '  No RG-scoped deleted Key Vaults found.' -ForegroundColor Gray
}

$finalLiveIds = az resource list --resource-group $ResourceGroupName --query '[].id' -o tsv 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Failed final resource check.'
}

$finalLiveCount = 0
if ($finalLiveIds) {
    $finalLiveCount = ($finalLiveIds -split "`n" | Where-Object { $_.Trim() }).Count
}

$finalDeletedKvJson = az keyvault list-deleted -o json 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Failed final deleted Key Vault check.'
}

$finalDeletedKv = @()
if ($finalDeletedKvJson) {
    $finalDeletedKv = $finalDeletedKvJson | ConvertFrom-Json
}

$finalRgScopedDeletedKvCount = 0
foreach ($kv in $finalDeletedKv) {
    $id = [string]$kv.id
    if (-not [string]::IsNullOrWhiteSpace($id) -and $id.ToLower().Contains("/resourcegroups/$rgLower/")) {
        $finalRgScopedDeletedKvCount++
    }
}

Write-Host ''
Write-Host '=== RG Cleanup Complete ===' -ForegroundColor Green
Write-Host "LIVE_RESOURCE_COUNT=$finalLiveCount" -ForegroundColor Cyan
Write-Host "RG_SCOPED_SOFT_DELETED_KV_COUNT=$finalRgScopedDeletedKvCount" -ForegroundColor Cyan
Write-Host ''
