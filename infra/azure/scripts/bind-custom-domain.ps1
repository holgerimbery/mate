# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

<#
.SYNOPSIS
Bind a custom domain to the WebUI Azure Container App.

.DESCRIPTION
This script binds a hostname (for example nttdata.mate365.app) to the WebUI container app.
It uses values from infra/azure/scripts/.env when available.

If no certificate is provided, Azure CLI will look for or create a managed certificate
when running `az containerapp hostname bind`.

.PARAMETER DomainName
Custom domain to bind. If omitted, the script prompts for it interactively.

.PARAMETER ResourceGroupName
Azure resource group. If omitted, uses AZURE_RESOURCE_GROUP from .env.

.PARAMETER WebAppName
WebUI container app name. If omitted, uses {AZURE_ENVIRONMENT_NAME}-webui from .env.

.PARAMETER EnvironmentName
Environment name prefix used to infer WebAppName. If omitted, uses AZURE_ENVIRONMENT_NAME.

.PARAMETER WhatIf
Preview actions without making changes.

.PARAMETER Force
Skip confirmation prompt.

.EXAMPLE
.\bind-custom-domain.ps1

.EXAMPLE
.\bind-custom-domain.ps1 -DomainName 'app.example.com'

.EXAMPLE
.\bind-custom-domain.ps1 -DomainName 'app.example.com' -WhatIf
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$DomainName,

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $false)]
    [string]$WebAppName,

    [Parameter(Mandatory = $false)]
    [string]$EnvironmentName,

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf,

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Load .env defaults when present
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $scriptDir '.env'

if (Test-Path $envFile) {
    Write-Host 'Loading configuration from .env...' -ForegroundColor Gray
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

if (-not $EnvironmentName) { $EnvironmentName = 'mate-dev' }
if (-not $ResourceGroupName) { $ResourceGroupName = "$EnvironmentName-rg" }
if (-not $WebAppName) { $WebAppName = "$EnvironmentName-webui" }

if (-not $DomainName) {
    $DomainName = Read-Host 'Custom domain to bind (for example app.example.com)'
}

if ([string]::IsNullOrWhiteSpace($DomainName)) {
    throw 'DomainName is required.'
}

if (-not (Get-Command 'az' -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI not found. Install Azure CLI first.'
}

Write-Host ''
if ($WhatIf) {
    Write-Host '=== Custom Domain Bind (DRY-RUN) ===' -ForegroundColor Yellow
} else {
    Write-Host '=== Custom Domain Bind ===' -ForegroundColor Cyan
}
Write-Host "Resource Group: $ResourceGroupName"
Write-Host "WebUI App:      $WebAppName"
Write-Host "Domain:         $DomainName"
Write-Host ''

# Validate container app exists and capture default fqdn
$defaultFqdn = az containerapp show `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --query 'properties.configuration.ingress.fqdn' `
    --output tsv 2>$null

if ($LASTEXITCODE -ne 0 -or -not $defaultFqdn) {
    throw "Container app '$WebAppName' not found in resource group '$ResourceGroupName', or ingress FQDN is unavailable."
}

Write-Host "Default FQDN:   $defaultFqdn" -ForegroundColor Gray

# Detect the managed environment (required by hostname bind when cert is auto-managed)
$managedEnvId = az containerapp show `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --query 'properties.managedEnvironmentId' `
    --output tsv 2>$null

if ($LASTEXITCODE -ne 0 -or -not $managedEnvId) {
    throw "Unable to resolve managed environment for container app '$WebAppName'."
}

Write-Host "Managed Env:    $managedEnvId" -ForegroundColor Gray

# Best-effort DNS check
try {
    $dnsOutput = nslookup $DomainName | Out-String
    if ($dnsOutput -notmatch [Regex]::Escape($defaultFqdn)) {
        Write-Host ''
        Write-Host 'Warning: DNS lookup does not currently show the Container App FQDN.' -ForegroundColor Yellow
        Write-Host "Expected CNAME target: $defaultFqdn" -ForegroundColor Yellow
        Write-Host 'Proceeding anyway. Certificate issuance may fail until DNS is correct.' -ForegroundColor Yellow
    } else {
        Write-Host 'DNS check: domain resolves to the expected Container App endpoint.' -ForegroundColor Green
    }
}
catch {
    Write-Host 'DNS check skipped (nslookup failed).' -ForegroundColor Yellow
}

# Show existing hostnames
$existing = az containerapp hostname list `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --output json 2>$null

Write-Host ''
Write-Host 'Current bound hostnames:' -ForegroundColor Gray
if ($existing -and $existing -ne '[]') {
    az containerapp hostname list --resource-group $ResourceGroupName --name $WebAppName --output table
}
else {
    Write-Host '  (none)'
}

if ($WhatIf) {
    Write-Host ''
    Write-Host 'DRY-RUN: No changes made.' -ForegroundColor Yellow
    Write-Host 'Would run:' -ForegroundColor Gray
    Write-Host "  az containerapp hostname bind --resource-group $ResourceGroupName --name $WebAppName --hostname $DomainName --environment $managedEnvId" -ForegroundColor Gray
    exit 0
}

if (-not $Force) {
    Write-Host ''
    $confirm = Read-Host "Type 'bind' to bind $DomainName"
    if ($confirm -ne 'bind') {
        Write-Host 'Cancelled.' -ForegroundColor Yellow
        exit 0
    }
}

Write-Host ''
Write-Host 'Binding custom domain...' -ForegroundColor Cyan
$bindOutput = az containerapp hostname bind `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --hostname $DomainName `
    --environment $managedEnvId `
    --output none

if ($LASTEXITCODE -ne 0) {
    $message = ($bindOutput | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($message)) {
        throw 'Custom domain bind failed.'
    }
    throw "Custom domain bind failed: $message"
}

Write-Host 'Custom domain bind completed.' -ForegroundColor Green

Write-Host ''
Write-Host 'Updated hostnames:' -ForegroundColor Cyan
az containerapp hostname list --resource-group $ResourceGroupName --name $WebAppName --output table

Write-Host ''
Write-Host "Validation URL: https://$DomainName" -ForegroundColor Green
