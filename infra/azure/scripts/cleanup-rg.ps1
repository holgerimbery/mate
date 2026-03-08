# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

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

    [ValidateRange(60, 7200)]
    [int]$WaitTimeoutSeconds = 900,

    [ValidateRange(2, 120)]
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

$rgLower = $ResourceGroupName.ToLower()

function Get-RgScopedDeletedKeyVaultNames {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupNameLower
    )

    $deletedKvsJson = az keyvault list-deleted -o json 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to list deleted Key Vaults.'
    }

    $deletedKvs = @()
    if ($deletedKvsJson) {
        $deletedKvs = $deletedKvsJson | ConvertFrom-Json
    }

    $names = @()
    foreach ($kv in $deletedKvs) {
        $id = [string]$kv.id
        if (-not [string]::IsNullOrWhiteSpace($id) -and $id.ToLower().Contains("/resourcegroups/$ResourceGroupNameLower/")) {
            $names += [string]$kv.name
        }
    }

    return @($names | Where-Object { $_ -and $_.Trim() } | Select-Object -Unique)
}

Write-Host 'Step 1/6: Cancel running deployments in RG...' -ForegroundColor Yellow
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

Write-Host 'Step 2/6: Delete deployment records in RG...' -ForegroundColor Yellow
$allDeployments = az deployment group list --resource-group $ResourceGroupName --query '[].name' -o tsv 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to list deployment records.'
}

if ($allDeployments) {
    $deployments = $allDeployments -split "`n" | Where-Object { $_.Trim() }
    Write-Host "  Deployment records found: $($deployments.Count)" -ForegroundColor Gray

    foreach ($name in $deployments) {
        $deploymentName = $name.Trim()
        Write-Host "  Deleting deployment record: $deploymentName" -ForegroundColor Gray
        az deployment group delete --resource-group $ResourceGroupName --name $deploymentName 2>$null | Out-Null
    }
}
else {
    Write-Host '  No deployment records found.' -ForegroundColor Gray
}

Write-Host 'Step 3/6: Delete all live resources in RG...' -ForegroundColor Yellow
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

Write-Host 'Step 4/6: Wait for resource deletions to complete...' -ForegroundColor Yellow
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

Write-Host 'Step 5/6: Purge RG-scoped soft-deleted Key Vaults...' -ForegroundColor Yellow
$rgScopedDeletedVaults = Get-RgScopedDeletedKeyVaultNames -ResourceGroupNameLower $rgLower

if ($rgScopedDeletedVaults.Count -gt 0) {
    foreach ($name in ($rgScopedDeletedVaults | Select-Object -Unique)) {
        Write-Host "  Purging deleted Key Vault: $name" -ForegroundColor Gray
        az keyvault purge --name $name 2>$null | Out-Null
    }

    $purgeStart = Get-Date
    while ($true) {
        $remainingDeletedVaults = Get-RgScopedDeletedKeyVaultNames -ResourceGroupNameLower $rgLower
        if ($remainingDeletedVaults.Count -eq 0) {
            Write-Host '  Soft-deleted RG-scoped Key Vault count: 0' -ForegroundColor Green
            break
        }

        $elapsedPurge = [int]((Get-Date) - $purgeStart).TotalSeconds
        Write-Host "  Waiting for Key Vault purge... remaining: $($remainingDeletedVaults.Count) (elapsed ${elapsedPurge}s)" -ForegroundColor Gray

        if ($elapsedPurge -ge $WaitTimeoutSeconds) {
            $remainingNames = $remainingDeletedVaults -join ', '
            throw "Timeout waiting for Key Vault purge. Remaining soft-deleted vaults: $remainingNames"
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }
}
else {
    Write-Host '  No RG-scoped deleted Key Vaults found.' -ForegroundColor Gray
}

Write-Host 'Step 6/6: Final verification...' -ForegroundColor Yellow
$finalLiveIds = az resource list --resource-group $ResourceGroupName --query '[].id' -o tsv 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Failed final resource check.'
}

$finalLiveCount = 0
if ($finalLiveIds) {
    $finalLiveCount = ($finalLiveIds -split "`n" | Where-Object { $_.Trim() }).Count
}

$finalDeploymentRecords = az deployment group list --resource-group $ResourceGroupName --query '[].name' -o tsv 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Failed final deployment records check.'
}

$finalDeploymentRecordCount = 0
if ($finalDeploymentRecords) {
    $finalDeploymentRecordCount = ($finalDeploymentRecords -split "`n" | Where-Object { $_.Trim() }).Count
}

$finalRgScopedDeletedKvCount = (Get-RgScopedDeletedKeyVaultNames -ResourceGroupNameLower $rgLower).Count

if ($finalLiveCount -ne 0 -or $finalDeploymentRecordCount -ne 0 -or $finalRgScopedDeletedKvCount -ne 0) {
    throw "Cleanup verification failed. LIVE_RESOURCE_COUNT=$finalLiveCount, DEPLOYMENT_RECORD_COUNT=$finalDeploymentRecordCount, RG_SCOPED_SOFT_DELETED_KV_COUNT=$finalRgScopedDeletedKvCount"
}

Write-Host ''
Write-Host '=== RG Cleanup Complete ===' -ForegroundColor Green
Write-Host "LIVE_RESOURCE_COUNT=$finalLiveCount" -ForegroundColor Cyan
Write-Host "DEPLOYMENT_RECORD_COUNT=$finalDeploymentRecordCount" -ForegroundColor Cyan
Write-Host "RG_SCOPED_SOFT_DELETED_KV_COUNT=$finalRgScopedDeletedKvCount" -ForegroundColor Cyan
Write-Host ''
