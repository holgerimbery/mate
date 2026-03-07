<#
.SYNOPSIS
Post-deployment setup: Store client secret in Key Vault and configure RBAC.

.DESCRIPTION
After the infrastructure is deployed, this script:
1. Stores the Entra ID client secret in Azure Key Vault
2. Configures managed identity RBAC (Key Vault Secrets User role)
3. Verifies the setup works
4. Provides instructions for next steps

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
    $currentSubId = az account show --query id -o tsv 2>&1
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
try {
    $kvExists = az keyvault show --name $keyVaultName --resource-group $resourceGroup -o tsv 2>&1 | Measure-Object | Select-Object -ExpandProperty Count
    
    if ($kvExists -eq 0) {
        Write-Error "Key Vault '$keyVaultName' not found in resource group '$resourceGroup'. Verify deployment completed successfully."
    }
    
    Write-Host "✓ Key Vault found: $keyVaultName" -ForegroundColor Green
}
catch {
    Write-Error "Failed to verify Key Vault: $_"
}

Write-Host ""

# Step 3: Store client secret in Key Vault
Write-Host "Step 3: Storing client secret in Key Vault..." -ForegroundColor Cyan
try {
    Write-Host "  Storing secret 'azuread-client-secret' in $keyVaultName..." -ForegroundColor Gray
    
    az keyvault secret set `
        --vault-name $keyVaultName `
        --name "azuread-client-secret" `
        --value $aadClientSecret `
        2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to store secret in Key Vault"
    }
    
    Write-Host "✓ Client secret stored successfully" -ForegroundColor Green
}
catch {
    Write-Error "Failed to store secret: $_"
}

Write-Host ""

# Step 4: Configure managed identity RBAC
Write-Host "Step 4: Configuring managed identity permissions..." -ForegroundColor Cyan
try {
    $webMiName = "$environmentName-web-mi"
    
    Write-Host "  Getting managed identity '$webMiName'..." -ForegroundColor Gray
    $webMiPrincipalId = az identity show `
        --resource-group $resourceGroup `
        --name $webMiName `
        --query principalId -o tsv 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to get managed identity '$webMiName'"
    }
    
    Write-Host "  Principal ID: $webMiPrincipalId" -ForegroundColor Gray
    
    # Get Key Vault resource ID
    $kvId = az keyvault show `
        --name $keyVaultName `
        --resource-group $resourceGroup `
        --query id -o tsv 2>&1
    
    Write-Host "  Granting 'Key Vault Secrets User' role..." -ForegroundColor Gray
    
    az role assignment create `
        --assignee $webMiPrincipalId `
        --role "Key Vault Secrets User" `
        --scope $kvId `
        2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        # Role assignment already exists, that's OK
        Write-Host "  (Role may already be assigned)" -ForegroundColor Gray
    }
    else {
        Write-Host "  Role assignment created" -ForegroundColor Gray
    }
    
    Write-Host "✓ Managed identity configured" -ForegroundColor Green
}
catch {
    Write-Error "Failed to configure RBAC: $_"
}

Write-Host ""

# Step 5: Verify secret access
Write-Host "Step 5: Verifying secret access..." -ForegroundColor Cyan
try {
    $secret = az keyvault secret show `
        --vault-name $keyVaultName `
        --name "azuread-client-secret" `
        --query value -o tsv 2>&1
    
    if ($secret -and $secret -eq $aadClientSecret) {
        Write-Host "✓ Secret verified successfully" -ForegroundColor Green
    }
    else {
        Write-Error "Secret verification failed - values don't match"
    }
}
catch {
    Write-Error "Failed to verify secret: $_"
}

Write-Host ""

# Step 6: Optional - Store PostgreSQL password
Write-Host "Step 6: Storing PostgreSQL password (optional)..." -ForegroundColor Cyan
if (Test-Path $pgPassFile) {
    try {
        $pgPassword = Get-Content $pgPassFile -Raw
        
        Write-Host "  Storing 'postgres-admin-password'..." -ForegroundColor Gray
        az keyvault secret set `
            --vault-name $keyVaultName `
            --name "postgres-admin-password" `
            --value $pgPassword `
            2>&1 | Out-Null
        
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

# Step 7: Redeploy container apps
Write-Host "Step 7: Redeploying Container Apps..." -ForegroundColor Cyan
Write-Host ""
Write-Host "Now that the client secret is in Key Vault, the Container Apps can access it." -ForegroundColor Yellow
Write-Host "Run the deployment to create the WebUI and Worker containers:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  .\deploy.ps1" -ForegroundColor Magenta
Write-Host ""

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Setup Complete!" -ForegroundColor Green
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
