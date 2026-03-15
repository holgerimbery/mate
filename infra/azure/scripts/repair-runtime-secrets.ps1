# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

<#
.SYNOPSIS
Repairs runtime secret wiring for Azure Container Apps.

.DESCRIPTION
Sets postgres and blob connection secrets on WebUI/Worker apps and rewires
ConnectionStrings__Default and AzureInfrastructure__BlobConnectionString to
secret references.

.PARAMETER ResourceGroupName
Azure resource group containing the container apps.

.PARAMETER EnvironmentName
Environment prefix used for app naming (e.g., mate-dev).

.PARAMETER PostgresPassword
Optional PostgreSQL admin password. If omitted, loads from .pg-password or prompts.

.EXAMPLE
.\repair-runtime-secrets.ps1 -ResourceGroupName 'rg-mate' -EnvironmentName 'mate-dev'
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $false)]
    [string]$EnvironmentName,

    [Parameter(Mandatory = $false)]
    [string]$PostgresPassword
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $scriptDir '.env'

if (Test-Path $envFile) {
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

if (-not $ResourceGroupName) { throw 'ResourceGroupName is required (or set AZURE_RESOURCE_GROUP in .env).' }
if (-not $EnvironmentName) { throw 'EnvironmentName is required (or set AZURE_ENVIRONMENT_NAME in .env).' }

if (-not $PostgresPassword) {
    $pgPasswordFile = Join-Path $scriptDir '.pg-password'
    if (Test-Path $pgPasswordFile) {
        $PostgresPassword = (Get-Content $pgPasswordFile -Raw).Trim()
    }
    else {
        $secure = Read-Host 'Enter PostgreSQL admin password' -AsSecureString
        $PostgresPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    }
}

if ([string]::IsNullOrWhiteSpace($PostgresPassword)) {
    throw 'PostgreSQL password is empty.'
}

Write-Host 'Configuring runtime secret references...' -ForegroundColor Cyan

$storageAccountName = az resource list `
    --resource-group $ResourceGroupName `
    --resource-type 'Microsoft.Storage/storageAccounts' `
    --query '[0].name' -o tsv 2>$null

if ($LASTEXITCODE -ne 0 -or -not $storageAccountName) {
    throw 'No storage account found; cannot configure blob connection secret.'
}

$storageKey = az storage account keys list `
    --resource-group $ResourceGroupName `
    --account-name $storageAccountName `
    --query '[0].value' -o tsv 2>$null

if ($LASTEXITCODE -ne 0 -or -not $storageKey) {
    throw "Could not resolve storage account key for '$storageAccountName'."
}

$dbConnectionString = "Host=$EnvironmentName-pg.postgres.database.azure.com;Database=mate;Username=pgadmin;Password=$PostgresPassword;SSL Mode=Require"
$blobConnectionString = "DefaultEndpointsProtocol=https;AccountName=$storageAccountName;AccountKey=$storageKey;EndpointSuffix=core.windows.net"

$containerApps = @("$EnvironmentName-webui", "$EnvironmentName-worker")

foreach ($appName in $containerApps) {
    $appId = az containerapp show --resource-group $ResourceGroupName --name $appName --query id -o tsv 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $appId) {
        throw "Container app '$appName' not found in resource group '$ResourceGroupName'."
    }

    az containerapp secret set `
        --resource-group $ResourceGroupName `
        --name $appName `
        --secrets "postgres-conn=$dbConnectionString" "blob-conn=$blobConnectionString" `
        -o none 2>$null

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set runtime secrets on '$appName'."
    }

    az containerapp update `
        --resource-group $ResourceGroupName `
        --name $appName `
        --set-env-vars "ConnectionStrings__Default=secretref:postgres-conn" "AzureInfrastructure__BlobConnectionString=secretref:blob-conn" "RedmondMode=false" `
        -o none 2>$null

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to update runtime env on '$appName'."
    }

    Write-Host "  ✓ Configured runtime secret references on '$appName'." -ForegroundColor Green
}

Write-Host 'Runtime secret wiring completed successfully.' -ForegroundColor Green
