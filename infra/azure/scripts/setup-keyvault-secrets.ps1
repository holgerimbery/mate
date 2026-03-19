# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

<#
.SYNOPSIS
Repair/recovery script: re-seed Key Vault secrets for an existing mate deployment.

.DESCRIPTION
Use this script when secrets need to be re-stored after rotation or after a failed
initial deployment. During a normal first-time deployment, deploy.ps1 (Phase 0)
hands this responsibility automatically.

This script:
1. Verifies the Key Vault exists and the caller has the required role
2. Stores/refreshes the Entra ID client secret (azuread-client-secret)
3. Stores/refreshes the PostgreSQL admin password (postgres-admin-password)

Note: Blob, postgres connection string, and servicebus connection string secrets
are managed inline by the Bicep deployment. Managed identity RBAC (Key Vault
Secrets User) is also assigned inline by Bicep.

.EXAMPLE
.\setup-keyvault-secrets.ps1
#>

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $scriptDir ".env"
$credsFile = Join-Path $scriptDir ".credentials"
$pgPassFile = Join-Path $scriptDir ".pg-password"

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Post-Deployment: Key Vault & Authentication Setup        ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Load environment variables
if (-not (Test-Path $envFile)) {
    Write-Error ".env file not found at $envFile`n`nRun setup-env.ps1 first"
}

$env = @{}
Get-Content $envFile | Where-Object { -not $_.StartsWith('#') -and $_ -ne '' } | ForEach-Object {
    $parts = $_ -split '=', 2
    if ($parts.Length -eq 2) {
        $env[$parts[0].Trim()] = $parts[1].Trim()
    }
}

$tenantId = $env['AZURE_TENANT_ID']
$subscriptionId = $env['AZURE_SUBSCRIPTION_ID']
$resourceGroup = $env['AZURE_RESOURCE_GROUP']
$environmentName = $env['AZURE_ENVIRONMENT_NAME']
$aadClientId = $env['AZURE_AAD_CLIENT_ID']

if (-not $credsFile -or -not (Test-Path $credsFile)) {
    Write-Error "Credentials file not found. Run setup-env.ps1 first to prepare credentials."
}

$creds = Get-Content $credsFile | ConvertFrom-Json
$aadClientSecret = $creds.AAD_CLIENT_SECRET

function Ensure-CallerCanManageKeyVaultSecrets {
    param(
        [Parameter(Mandatory = $true)]
        [string]$KeyVaultId
    )

    $caller = az account show --query "{name:user.name,type:user.type}" -o json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $caller -or -not $caller.name) {
        Write-Error "Unable to determine current Azure caller from az account show"
    }

    $existingAssignmentCount = az role assignment list `
        --assignee $caller.name `
        --scope $KeyVaultId `
        --query "[?roleDefinitionName=='Key Vault Secrets Officer'] | length(@)" -o tsv

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not verify existing Key Vault Secrets Officer role assignment; attempting assignment"
        $existingAssignmentCount = '0'
    }

    if ($existingAssignmentCount -eq '0') {
        Write-Host "  Granting caller '$($caller.name)' role 'Key Vault Secrets Officer' on Key Vault..." -ForegroundColor Gray
        $assignOutput = az role assignment create `
            --assignee $caller.name `
            --role "Key Vault Secrets Officer" `
            --scope $KeyVaultId `
            -o json 2>&1

        if ($LASTEXITCODE -ne 0) {
            $assignText = ($assignOutput | Out-String).Trim()
            Write-Error "Failed to grant Key Vault Secrets Officer to caller '$($caller.name)'. Error: $assignText"
        }

        Write-Host "  Caller role assignment created" -ForegroundColor Gray
    }
    else {
        Write-Host "  Caller already has Key Vault Secrets Officer role" -ForegroundColor Gray
    }
}

Write-Host "Configuration Summary:" -ForegroundColor Yellow
Write-Host "  Tenant ID:           $tenantId"
Write-Host "  Subscription ID:     $subscriptionId"
Write-Host "  Resource Group:      $resourceGroup"
Write-Host "  Environment Name:    $environmentName"
Write-Host "  AAD Client ID:       $aadClientId"
Write-Host "  Key Vault Name:      $environmentName-kv"
Write-Host ""

# Step 1: Verify subscription context
Write-Host "Step 1: Verifying Azure subscription context..." -ForegroundColor Cyan
try {
    $currentSubId = az account show --query id -o tsv
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Not logged in. Run: az login --tenant $tenantId"
    }
    
    if ($currentSubId -ne $subscriptionId) {
        Write-Host "Setting subscription context to $subscriptionId..." -ForegroundColor Gray
        az account set --subscription $subscriptionId 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to set subscription context"
        }
    }
    Write-Host "✓ Subscription verified" -ForegroundColor Green
}
catch {
    Write-Error "Failed to verify subscription: $_"
}

Write-Host ""

# Step 2: Verify Key Vault exists
Write-Host "Step 2: Verifying Key Vault..." -ForegroundColor Cyan
$keyVaultName = "$environmentName-kv"
$kvId = $null
try {
    $keyVault = az keyvault show --name $keyVaultName --resource-group $resourceGroup -o json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $keyVault) {
        Write-Error "Key Vault '$keyVaultName' not found in resource group '$resourceGroup'. Verify deployment completed successfully."
    }

    $kvId = $keyVault.id
    
    Write-Host "✓ Key Vault found: $keyVaultName" -ForegroundColor Green
}
catch {
    Write-Error "Failed to verify Key Vault: $_"
}

Write-Host ""

# Step 3: Store client secret in Key Vault
Write-Host "Step 3: Storing client secret in Key Vault..." -ForegroundColor Cyan
try {
    Ensure-CallerCanManageKeyVaultSecrets -KeyVaultId $kvId

    Write-Host "  Storing secret 'azuread-client-secret' in $keyVaultName..." -ForegroundColor Gray
    
    $setSecretOutput = az keyvault secret set `
        --vault-name $keyVaultName `
        --name "azuread-client-secret" `
        --value $aadClientSecret `
        --query id -o tsv 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        $setSecretText = ($setSecretOutput | Out-String).Trim()
        Write-Error "Failed to store secret in Key Vault. Error: $setSecretText"
    }
    
    Write-Host "✓ Client secret stored successfully" -ForegroundColor Green
}
catch {
    Write-Error "Failed to store secret: $_"
}

Write-Host ""

# Step 4 is handled inline by Bicep (managed identity RBAC + blob/postgres/servicebus connection strings).
# This script only manages the manually-bootstrapped secrets.

# Step 4: Optional — Store/refresh PostgreSQL password
Write-Host "Step 4: Storing PostgreSQL password (optional refresh)..." -ForegroundColor Cyan
if (Test-Path $pgPassFile) {
    try {
        $pgPassword = Get-Content $pgPassFile -Raw
        
        Write-Host "  Storing 'postgres-admin-password'..." -ForegroundColor Gray
        $setPgOutput = az keyvault secret set `
            --vault-name $keyVaultName `
            --name "postgres-admin-password" `
            --value $pgPassword `
            --query id -o tsv 2>&1

        if ($LASTEXITCODE -ne 0) {
            $setPgText = ($setPgOutput | Out-String).Trim()
            Write-Error "Failed to store PostgreSQL password in Key Vault. Error: $setPgText"
        }
        
        Write-Host "✓ PostgreSQL password stored" -ForegroundColor Green
        
        # Clean up temporary password file
        Remove-Item $pgPassFile -Force
        Write-Host "  (Temporary password file cleaned up)" -ForegroundColor Gray
    }
    catch {
        Write-Warning "Failed to store PostgreSQL password: $_"
    }
}
else {
    Write-Host "  No PostgreSQL password stored (will be prompted at deployment)" -ForegroundColor Gray
}

Write-Host ""

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Repair Complete!" -ForegroundColor Green
Write-Host "  azuread-client-secret refreshed in Key Vault: $keyVaultName" -ForegroundColor Green
Write-Host "  Blob/postgres/servicebus connection strings are managed by Bicep." -ForegroundColor Gray
Write-Host "  Managed identity RBAC (Key Vault Secrets User) is managed by Bicep." -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  1. Run .\deploy.ps1 to create Container Apps" -ForegroundColor White
Write-Host "  2. Get WebUI FQDN from deployment output" -ForegroundColor White
Write-Host "  3. Register redirect URI in Entra ID app registration:" -ForegroundColor White
Write-Host "     https://{WebUI_FQDN}/signin-oidc" -ForegroundColor Gray
Write-Host "  4. See: docs/concepts/azure-entra-id-authentication-setup.md" -ForegroundColor White
Write-Host ""

# Clean up credentials file
Write-Host "Cleaning up temporary credentials file..." -ForegroundColor Gray
Remove-Item $credsFile -Force
Write-Host "✓ Cleanup complete" -ForegroundColor Green
Write-Host ""
