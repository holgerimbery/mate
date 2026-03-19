# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
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

.PARAMETER Mode
Optional. Controls provisioning vs RBAC behavior:
- EnsureRbac (default): full provisioning flow + RBAC verification/assignment
- VerifyOnly: validate context, vault, and RBAC only (no writes)
- GrantOnly: only ensure RBAC assignment on the target vault

.PARAMETER PrincipalObjectId
Optional. Object ID of the identity that should get vault role assignment.

.PARAMETER AppId
Optional. Application (client) ID of the service principal. Resolved to object ID automatically.

.PARAMETER RoleName
Optional. RBAC role to assign at vault scope. Default: Key Vault Secrets Officer.

.PARAMETER WaitForPropagationSeconds
Optional. Wait time after role assignment before re-check. Default: 20.

.EXAMPLE
# Provision dev environment vault for mate core
.\provision-singlevault.ps1 `
    -SubscriptionId "<subscription-id>" `
    -TenantId "<tenant-id>" `
    -ResourceGroupName "<resource-group>" `
    -Environment "dev"

# Dry-run to see what would happen
.\provision-singlevault.ps1 `
    -SubscriptionId "<subscription-id>" `
    -TenantId "<tenant-id>" `
    -ResourceGroupName "<resource-group>" `
  -Environment "dev" `
  -DryRun

#>

param(
    [Parameter(Mandatory = $false, HelpMessage = "Azure subscription ID (GUID format)")]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $false, HelpMessage = "Azure tenant ID (GUID format)")]
    [string]$TenantId,

    [Parameter(Mandatory = $false, HelpMessage = "Azure resource group name")]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $false, HelpMessage = "Environment (dev|test|prod)")]
    [string]$Environment,

    [Parameter(Mandatory = $false, HelpMessage = "Azure location for vault creation")]
    [string]$Location = "eastus",

    [Parameter(HelpMessage = "Show what would be done without making changes")]
    [switch]$DryRun,

    [Parameter(Mandatory = $false, HelpMessage = "Provisioning mode: EnsureRbac | VerifyOnly | GrantOnly")]
    [ValidateSet('EnsureRbac', 'VerifyOnly', 'GrantOnly')]
    [string]$Mode = 'EnsureRbac',

    [Parameter(Mandatory = $false, HelpMessage = "Object ID of principal to grant vault role")]
    [string]$PrincipalObjectId,

    [Parameter(Mandatory = $false, HelpMessage = "Application (client) ID of principal to grant vault role")]
    [string]$AppId,

    [Parameter(Mandatory = $false, HelpMessage = "Vault RBAC role name")]
    [string]$RoleName = 'Key Vault Secrets Officer',

    [Parameter(Mandatory = $false, HelpMessage = "Seconds to wait for RBAC propagation")]
    [int]$WaitForPropagationSeconds = 20
)

# ─────────────────────────────────────────────────────────────────────────────
# Configuration & Constants
# ─────────────────────────────────────────────────────────────────────────────

$ErrorActionPreference = "Stop"
$VerbosePreference = "Continue"

# Shared naming helper
. "$PSScriptRoot\keyvault-naming-helper.ps1"

function Import-DotEnvVariables {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        return
    }

    Get-Content -Path $Path | ForEach-Object {
        $line = $_.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) {
            return
        }

        $idx = $line.IndexOf('=')
        if ($idx -lt 1) {
            return
        }

        $name = $line.Substring(0, $idx).Trim()
        $value = $line.Substring($idx + 1).Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        # Explicit process environment wins over .env file values.
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            [Environment]::SetEnvironmentVariable($name, $value)
        }
    }
}

# Load local .env values so secrets can be resolved without manual export.
Import-DotEnvVariables -Path (Join-Path $PSScriptRoot '.env')

# Resolve Azure context from parameters first, then from .env context fields.
if ([string]::IsNullOrWhiteSpace($SubscriptionId)) { $SubscriptionId = $env:AzureContext__SubscriptionId }
if ([string]::IsNullOrWhiteSpace($TenantId)) { $TenantId = $env:AzureContext__TenantId }
if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) { $ResourceGroupName = $env:AzureContext__ResourceGroupName }
if ([string]::IsNullOrWhiteSpace($Environment)) { $Environment = $env:AzureContext__Environment }
if ([string]::IsNullOrWhiteSpace($TenantId)) { $TenantId = $env:AzureAd__TenantId }

if ([string]::IsNullOrWhiteSpace($SubscriptionId)) { throw "Missing SubscriptionId. Provide -SubscriptionId or AzureContext__SubscriptionId in .env." }
if ([string]::IsNullOrWhiteSpace($TenantId)) { throw "Missing TenantId. Provide -TenantId or AzureContext__TenantId in .env." }
if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) { throw "Missing ResourceGroupName. Provide -ResourceGroupName or AzureContext__ResourceGroupName in .env." }
if ([string]::IsNullOrWhiteSpace($Environment)) { throw "Missing Environment. Provide -Environment or AzureContext__Environment in .env." }

if ($SubscriptionId -notmatch '^[a-f0-9\-]{36}$') { throw "Invalid subscription ID format: $SubscriptionId" }
if ($TenantId -notmatch '^[a-f0-9\-]{36}$') { throw "Invalid tenant ID format: $TenantId" }
if ($Environment -notin @('dev', 'test', 'prod')) { throw "Invalid environment '$Environment'. Allowed: dev, test, prod." }

# Vault selection:
# 1) If AzureInfrastructure__KeyVaultUri is configured in .env, use that vault name.
# 2) Otherwise derive the canonical core vault name from environment.
$configuredVaultUri = $env:AzureInfrastructure__KeyVaultUri
$configuredVaultName = $null
if (-not [string]::IsNullOrWhiteSpace($configuredVaultUri)) {
    try {
        $configuredVaultName = ([Uri]$configuredVaultUri).Host.Split('.')[0]
    }
    catch {
        throw "Invalid AzureInfrastructure__KeyVaultUri in .env: '$configuredVaultUri'"
    }
}

# Vault naming convention (max 24 chars, alphanumeric + hyphen only)
$VaultName = if (-not [string]::IsNullOrWhiteSpace($configuredVaultName)) {
    $configuredVaultName
} else {
    Get-CoreVaultName -Environment $Environment
}

function Test-VaultNameAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $available = az keyvault check-name --name $Name --query nameAvailable --output tsv
    return ($available -eq "true")
}

function Try-CreateKeyVault {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $output = az keyvault create `
        --name $Name `
        --resource-group $ResourceGroupName `
        --location $Location `
        --enable-rbac-authorization `
        --output none 2>&1
    
    $exitCode = $LASTEXITCODE
    $outputStr = (($output | Out-String).Trim())
    
    # Success only if exit code is 0 AND no error patterns in output
    $success = $exitCode -eq 0 -and -not ($outputStr -match 'Error|error|failed|Failed')

    return @{
        Success = $success
        Output = $outputStr
        ExitCode = $exitCode
    }
}

function Resolve-PrincipalObjectId {
    param(
        [string]$ExplicitObjectId,
        [string]$ExplicitAppId
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitObjectId)) {
        return $ExplicitObjectId
    }

    $candidateAppId = $ExplicitAppId
    if ([string]::IsNullOrWhiteSpace($candidateAppId)) {
        $candidateAppId = $env:AZURE_CLIENT_ID
    }

    if (-not [string]::IsNullOrWhiteSpace($candidateAppId)) {
        $spOid = az ad sp show --id $candidateAppId --query id --output tsv 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($spOid)) {
            return $spOid
        }
        throw "Could not resolve service principal object ID for AppId '$candidateAppId'."
    }

    $signedInUserOid = az ad signed-in-user show --query id --output tsv 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($signedInUserOid)) {
        return $signedInUserOid
    }

    throw "Could not resolve principal object ID. Provide -PrincipalObjectId or -AppId (or AZURE_CLIENT_ID)."
}

function Get-VaultScope {
    param(
        [Parameter(Mandatory = $true)][string]$Subscription,
        [Parameter(Mandatory = $true)][string]$ResourceGroup,
        [Parameter(Mandatory = $true)][string]$Vault
    )

    return "/subscriptions/$Subscription/resourceGroups/$ResourceGroup/providers/Microsoft.KeyVault/vaults/$Vault"
}

function Test-RoleAssignmentExists {
    param(
        [Parameter(Mandatory = $true)][string]$Scope,
        [Parameter(Mandatory = $true)][string]$AssigneeObjectId,
        [Parameter(Mandatory = $true)][string]$Role
    )

    $count = az role assignment list `
        --scope $Scope `
        --assignee-object-id $AssigneeObjectId `
        --query "[?roleDefinitionName=='$Role'] | length(@)" `
        --output tsv 2>$null

    return ($LASTEXITCODE -eq 0 -and $count -as [int] -gt 0)
}

function Ensure-RoleAssignment {
    param(
        [Parameter(Mandatory = $true)][string]$Scope,
        [Parameter(Mandatory = $true)][string]$AssigneeObjectId,
        [Parameter(Mandatory = $true)][string]$Role,
        [switch]$PlanOnly,
        [int]$PropagationWaitSeconds = 20
    )

    if (Test-RoleAssignmentExists -Scope $Scope -AssigneeObjectId $AssigneeObjectId -Role $Role) {
        Write-Host "  ✓ RBAC already present: '$Role' for principal '$AssigneeObjectId'" -ForegroundColor Green
        return
    }

    if ($PlanOnly) {
        Write-Host "  [DRY-RUN] Would grant '$Role' to principal '$AssigneeObjectId' on scope '$Scope'"
        return
    }

    Write-Host "  Granting '$Role' to principal '$AssigneeObjectId'..." -ForegroundColor Gray
    az role assignment create `
        --role $Role `
        --assignee-object-id $AssigneeObjectId `
        --scope $Scope `
        --output none 2>$null | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create role assignment for principal '$AssigneeObjectId' on '$Scope'."
    }

    if ($PropagationWaitSeconds -gt 0) {
        Write-Host "  Waiting $PropagationWaitSeconds second(s) for RBAC propagation..." -ForegroundColor Gray
        Start-Sleep -Seconds $PropagationWaitSeconds
    }

    if (-not (Test-RoleAssignmentExists -Scope $Scope -AssigneeObjectId $AssigneeObjectId -Role $Role)) {
        throw "Role assignment was created but not yet visible after propagation wait. Re-run VerifyOnly shortly."
    }

    Write-Host "  ✓ RBAC assignment ensured." -ForegroundColor Green
}

Write-Host "═══════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Single-Vault Provisioning for mate Core (Environment: $Environment)" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Mode: $Mode"
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
    if ($Mode -in @('VerifyOnly', 'GrantOnly')) {
        throw "Resource group '$ResourceGroupName' does not exist. Mode '$Mode' does not create resources."
    }

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
    if ($Mode -in @('VerifyOnly', 'GrantOnly')) {
        throw "Key Vault '$VaultName' does not exist in resource group '$ResourceGroupName'. Mode '$Mode' does not create vaults."
    }

    if ($DryRun) {
        Write-Host "  [DRY-RUN] Would create vault: $VaultName"
    } else {
        $created = $false
        $selectedVaultName = $VaultName
        for ($i = 0; $i -lt 10; $i++) {
            if ($i -gt 0) {
                $suffix = New-RandomVaultSuffix
                $selectedVaultName = Get-CoreVaultName -Environment $Environment -Suffix $suffix
                Write-Host "  Retrying with random suffix: $selectedVaultName" -ForegroundColor Yellow
            }

            Write-Host "  Creating Key Vault..."
            $result = Try-CreateKeyVault -Name $selectedVaultName
            if ($result.Success) {
                $VaultName = $selectedVaultName
                Write-Host "  ✓ Key Vault created: $VaultName" -ForegroundColor Green
                $created = $true
                break
            }

            if ($result.Output -match 'VaultAlreadyExists|already in use') {
                continue
            }

            throw "Failed to create Key Vault '$selectedVaultName': $($result.Output)"
        }

        if (-not $created) {
            throw "Unable to create a Key Vault after multiple random-name attempts."
        }
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
# Phase 3.5: Grant RBAC Role to Current User
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "Phase 3.5: Verifying/ensuring vault RBAC..." -ForegroundColor Yellow

if ($DryRun) {
    Write-Host "  [DRY-RUN] RBAC principal resolution and role ensure would run in mode '$Mode'."
} else {
    $resolvedPrincipalObjectId = Resolve-PrincipalObjectId -ExplicitObjectId $PrincipalObjectId -ExplicitAppId $AppId
    $vaultScope = Get-VaultScope -Subscription $SubscriptionId -ResourceGroup $ResourceGroupName -Vault $VaultName

    Write-Host "  Principal Object ID: $resolvedPrincipalObjectId"
    Write-Host "  Role: $RoleName"
    Write-Host "  Scope: $vaultScope"

    Ensure-RoleAssignment `
        -Scope $vaultScope `
        -AssigneeObjectId $resolvedPrincipalObjectId `
        -Role $RoleName `
        -PropagationWaitSeconds $WaitForPropagationSeconds
}
Write-Host ""

if ($Mode -eq 'VerifyOnly') {
    Write-Host "VerifyOnly completed. No provisioning or secret migration executed." -ForegroundColor Green
    return
}

if ($Mode -eq 'GrantOnly') {
    Write-Host "GrantOnly completed. RBAC ensured; provisioning and secret migration skipped." -ForegroundColor Green
    return
}

# ─────────────────────────────────────────────────────────────────────────────
# Phase 4: Secrets Migration
# ─────────────────────────────────────────────────────────────────────────────

Write-Host "Phase 4: Planning secrets migration..." -ForegroundColor Yellow

# Secrets are resolved from runtime inputs/environment (no hardcoded IDs/secrets)
$azureAdClientId = $env:AzureAd__ClientId
$azureAdClientSecret = $env:AzureAd__ClientSecret
$azureAdInstance = if ([string]::IsNullOrWhiteSpace($env:AzureAd__Instance)) { "https://login.microsoftonline.com/" } else { $env:AzureAd__Instance }

$SecretsToMigrate = @(
    @{ Name = "AzureAd--TenantId"; Value = $TenantId; Description = "Entra ID tenant ID"; Required = $true; Source = "-TenantId parameter" },
    @{ Name = "AzureAd--ClientId"; Value = $azureAdClientId; Description = "Entra ID app client ID"; Required = $true; Source = "AzureAd__ClientId environment variable" },
    @{ Name = "AzureAd--ClientSecret"; Value = $azureAdClientSecret; Description = "Entra ID app secret"; Required = $true; Source = "AzureAd__ClientSecret environment variable" },
    @{ Name = "AzureAd--Instance"; Value = $azureAdInstance; Description = "Microsoft login endpoint"; Required = $false; Source = "AzureAd__Instance environment variable (optional)" }
)

$ResolvedSecrets = @()
foreach ($secret in $SecretsToMigrate) {
    if ([string]::IsNullOrWhiteSpace($secret.Value)) {
        if ($secret.Required) {
            throw "Required value for '$($secret.Name)' is missing. Provide it via $($secret.Source)."
        }
        continue
    }
    $ResolvedSecrets += $secret
}

Write-Host "  Secrets to migrate:"
foreach ($secret in $ResolvedSecrets) {
    Write-Host "    - $($secret.Name)" -ForegroundColor Gray
}

if (-not $DryRun) {
    Write-Host "  Migrating secrets..."
    foreach ($secret in $ResolvedSecrets) {
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
