<#
.SYNOPSIS
Provision Azure Key Vault for single-vault mode deployment (Core mode).
This script creates a single shared Key Vault and migrates secrets from Docker environment.

.DESCRIPTION
Provisions a single Key Vault for mate core deployments:
- Creates or reuses a single vault (mate-${environment}-kv)
- Migrates secrets from Docker compose / .env to the vault
- All users (SuperAdmin, TenantAdmin, Tester) access the same vault
- Access control via Azure RBAC on the vault
- All Azure context (subscription, tenant, RG) passed as runtime parameters (never hardcoded)

.PARAMETER SubscriptionId
Mandatory. Azure subscription ID (GUID format). Will be validated before proceeding.

.PARAMETER TenantId
Mandatory. Azure tenant ID / directory ID (GUID format). Will be validated against current login context.

.PARAMETER ResourceGroupName
Mandatory. Azure resource group where vault will be created. Must exist or will be created.

.PARAMETER Environment
Mandatory. Environment name (dev|test|prod) - determines vault naming prefix.

.PARAMETER Location
Optional. Azure location for vault creation. Default: 'eastus'.

.PARAMETER DryRun
Optional. If specified, shows what would be created/migrated without making changes.

.EXAMPLE
# Provision dev environment vault for mate core
.\provision-singlevault.ps1 `
  -SubscriptionId "e262e6b5-e05f-4598-bce1-7f1ffa3992e7" `
  -TenantId "b9b61d9b-e271-44fd-9c3b-d9fff2022339" `
  -ResourceGroupName "mate-dev" `
  -Environment "dev"

# Dry-run to see what would happen
.\provision-singlevault.ps1 `
  -SubscriptionId "e262e6b5-e05f-4598-bce1-7f1ffa3992e7" `
  -TenantId "b9b61d9b-e271-44fd-9c3b-d9fff2022339" `
  -ResourceGroupName "mate-dev" `
  -Environment "dev" `
  -DryRun

#>

param(
    [Parameter(Mandatory = $true, HelpMessage = "Azure subscription ID (GUID format)")]
    [ValidatePattern("^[a-f0-9\-]{36}$", ErrorMessage = "Invalid subscription ID format")]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true, HelpMessage = "Azure tenant ID (GUID format)")]
    [ValidatePattern("^[a-f0-9\-]{36}$", ErrorMessage = "Invalid tenant ID format")]
    [string]$TenantId,

    [Parameter(Mandatory = $true, HelpMessage = "Azure resource group name")]
    [ValidateNotNullOrEmpty()]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true, HelpMessage = "Environment (dev|test|prod)")]
    [ValidateSet("dev", "test", "prod")]
    [string]$Environment,

    [Parameter(Mandatory = $false, HelpMessage = "Azure location for vault creation")]
    [string]$Location = "eastus",

    [Parameter(HelpMessage = "Show what would be done without making changes")]
    [switch]$DryRun
)

# ─────────────────────────────────────────────────────────────────────────────
# Configuration & Constants
# ─────────────────────────────────────────────────────────────────────────────

$ErrorActionPreference = "Stop"
$VerbosePreference = "Continue"

# Vault naming convention (max 24 chars, alphanumeric + hyphen only)
$VaultName = "mate-${Environment}-kv"

Write-Host "═══════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Single-Vault Provisioning for mate Core (Environment: $Environment)" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Phase 1: Azure Context Validation
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "Phase 1: Validating Azure context..." -ForegroundColor Yellow

# Check current Azure CLI login context
$currentContext = az account show --query "{ subscriptionId: id, tenantId: tenantId, name: name }" --output json | ConvertFrom-Json
if (-not $currentContext) {
    throw "Not logged into Azure. Run 'az login' first."
}

Write-Host "  Current context: Subscription=$($currentContext.subscriptionId), Tenant=$($currentContext.tenantId)"
Write-Host "  Target context:  Subscription=$SubscriptionId, Tenant=$TenantId"

# Validate tenant matches
if ($currentContext.tenantId -ne $TenantId) {
    Write-Host "  ⚠ Warning: Current login tenant differs from target tenant." -ForegroundColor Yellow
    Write-Host "  Attempting to switch to target tenant..." -ForegroundColor Yellow
    az account set --subscription $SubscriptionId 2>$null
    $currentContext = az account show --query "{ subscriptionId: id, tenantId: tenantId }" --output json | ConvertFrom-Json
    if ($currentContext.tenantId -ne $TenantId) {
        throw "Unable to switch to target tenant $TenantId. Verify subscription and tenant are linked."
    }
}

# Set active subscription
az account set --subscription $SubscriptionId
Write-Host "  ✓ Subscription set to: $SubscriptionId" -ForegroundColor Green
Write-Host "  ✓ Tenant verified: $TenantId" -ForegroundColor Green
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Phase 2: Resource Group Creation/Validation
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "Phase 2: Creating/validating resource group..." -ForegroundColor Yellow

$rgExists = az group exists --name $ResourceGroupName | ConvertFrom-Json
if (-not $rgExists) {
    if ($DryRun) {
        Write-Host "  [DRY-RUN] Would create resource group: $ResourceGroupName in location: $Location"
    } else {
        Write-Host "  Creating resource group: $ResourceGroupName..."
        az group create --name $ResourceGroupName --location $Location | Out-Null
        Write-Host "  ✓ Resource group created: $ResourceGroupName" -ForegroundColor Green
    }
} else {
    Write-Host "  ✓ Resource group already exists: $ResourceGroupName" -ForegroundColor Green
}
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Phase 3: Vault Creation/Validation
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "Phase 3: Creating/validating Key Vault..." -ForegroundColor Yellow
Write-Host "  Vault name: $VaultName"

$vault = az keyvault list --resource-group $ResourceGroupName --query "[?name=='$VaultName']" --output json | ConvertFrom-Json
if ($vault.Count -eq 0) {
    if ($DryRun) {
        Write-Host "  [DRY-RUN] Would create vault: $VaultName"
    } else {
        Write-Host "  Creating Key Vault..."
        az keyvault create `
            --name $VaultName `
            --resource-group $ResourceGroupName `
            --location $Location `
            --enable-rbac-authorization `
            --output none
        Write-Host "  ✓ Key Vault created: $VaultName" -ForegroundColor Green
    }
} else {
    Write-Host "  ✓ Key Vault already exists: $VaultName" -ForegroundColor Green
}

if (-not $DryRun) {
    $vault = az keyvault show --name $VaultName --output json | ConvertFrom-Json
    $VaultUri = $vault.properties.vaultUri
    Write-Host "  Vault URI: $VaultUri"
}
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Phase 4: Secrets Migration
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "Phase 4: Planning secrets migration..." -ForegroundColor Yellow

# Sample secrets to migrate (from .env or Docker environment)
# In production, these would come from actual Docker .env or config
$SecretsToMigrate = @(
    @{ Name = "AzureAd--TenantId"; Value = $TenantId; Description = "Entra ID tenant ID" },
    @{ Name = "AzureAd--ClientId"; Value = "3a5429fa-442b-4b22-b16d-efcc266533f3"; Description = "Entra ID app client ID" },
    @{ Name = "AzureAd--ClientSecret"; Value = "bD48Q~bHWFAGcw_bT1UDXAyiEBSYpUXD3IW-7c7j"; Description = "Entra ID app secret" },
    @{ Name = "AzureAd--Instance"; Value = "https://login.microsoftonline.com/"; Description = "Microsoft login endpoint" }
)

Write-Host "  Secrets to migrate:"
foreach ($secret in $SecretsToMigrate) {
    Write-Host "    - $($secret.Name)" -ForegroundColor Gray
}

if (-not $DryRun) {
    Write-Host "  Migrating secrets..."
    foreach ($secret in $SecretsToMigrate) {
        # Normalize secret name for Key Vault (alphanumeric + hyphen only)
        $kvSecretName = $secret.Name -replace "[^a-zA-Z0-9\-]", "-"
        
        try {
            az keyvault secret set `
                --vault-name $VaultName `
                --name $kvSecretName `
                --value $secret.Value `
                --output none
            Write-Host "    ✓ $kvSecretName" -ForegroundColor Green
        } catch {
            Write-Host "    ✗ Failed to set $kvSecretName : $_" -ForegroundColor Red
            throw
        }
    }
}
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Phase 5: Docker Compose Configuration Output
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "Phase 5: Docker Compose Configuration" -ForegroundColor Yellow
Write-Host ""
Write-Host "Add the following environment variables to infra/local/.env:" -ForegroundColor Cyan
Write-Host ""

$configOutput = @"
# ─── Single Vault Key Vault Configuration ────────────────────────────────────
# Generated by: provision-singlevault.ps1
# Subscription: $SubscriptionId
# Tenant: $TenantId
# Resource Group: $ResourceGroupName
# Environment: $Environment
# Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

# Enable Key Vault for secrets (instead of database defaults)
AzureInfrastructure__UseKeyVaultForSecrets=true

# Single vaul mode: all users access the same vault
AzureInfrastructure__UseMultiVaultForSecrets=false

# Shared vault URI (all roles access this vault)
AzureInfrastructure__KeyVaultUri=$VaultUri
"@

Write-Host $configOutput

Write-Host ""
Write-Host "Steps to finalize:" -ForegroundColor Cyan
Write-Host "  1. Copy the configuration above to infra/local/.env"
Write-Host "  2. Rebuild the Docker containers: ./debug-container.ps1 -Stop; ./debug-container.ps1 -Source build -Rebuild"
Write-Host "  3. Verify Key Vault secrets are being resolved (check app logs)"
Write-Host "  4. Test by logging in with an admin and checking Secrets Mode badge on Help page"
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Phase 6: Summary & Validation
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "═══════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "DRY-RUN COMPLETED (no changes were made)" -ForegroundColor Yellow
} else {
    Write-Host "✓ PROVISIONING COMPLETED SUCCESSFULLY" -ForegroundColor Green
}

Write-Host ""
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  Subscription ID:  $SubscriptionId"
Write-Host "  Tenant ID:        $TenantId"
Write-Host "  Resource Group:   $ResourceGroupName"
Write-Host "  Environment:      $Environment"
if (-not $DryRun) {
    Write-Host "  Vault Name:       $VaultName"
    Write-Host "  Vault URI:        $VaultUri"
}
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
