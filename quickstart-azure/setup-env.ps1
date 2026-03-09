# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

<#
.SYNOPSIS
Interactive setup wizard for Azure deployment environment variables.

.DESCRIPTION
Guides you through setting up the .env file with your Azure tenant, subscription,
and resource group information. Stores values locally (never in git).

#>

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $scriptDir ".env"
$envTemplate = Join-Path $scriptDir ".env.template"

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Azure Deployment Environment Setup                       ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

if (Test-Path $envFile) {
    Write-Host ".env file already exists at: $envFile" -ForegroundColor Green
    $reuse = Read-Host "Use existing .env? (y/n)"
    if ($reuse -eq 'y') {
        Write-Host ""
        Write-Host "Contents:" -ForegroundColor Yellow
        Get-Content $envFile | Where-Object { -not $_.StartsWith('#') -and $_ -ne '' } | ForEach-Object {
            $key = ($_ -split '=')[0]
            if ($key -match 'PASSWORD|SECRET|KEY') {
                Write-Host "  $key=***REDACTED***" -ForegroundColor Gray
            }
            else {
                Write-Host "  $_" -ForegroundColor Gray
            }
        }
        Write-Host ""
        Write-Host "Ready to deploy. Run:" -ForegroundColor Green
        Write-Host ""
        Write-Host "  .\deploy-whatif.ps1" -ForegroundColor Magenta
        Write-Host ""
        return
    }
}

Write-Host "This wizard will create a .env file with your Azure deployment settings." -ForegroundColor Yellow
Write-Host "Values are stored locally (not in git). See .env.template for all options." -ForegroundColor Yellow
Write-Host ""

# Tenant ID
Write-Host "Azure Tenant ID:" -ForegroundColor Cyan
Write-Host "  Get from: Azure Portal → Azure AD → Properties → Tenant ID" -ForegroundColor Gray
$tenantId = Read-Host "Enter Tenant ID"
if (-not $tenantId) {
    Write-Error "Tenant ID is required"
}

Write-Host ""

# Subscription ID
Write-Host "Azure Subscription ID:" -ForegroundColor Cyan
Write-Host "  Get from: Azure Portal → Subscriptions → Subscription ID" -ForegroundColor Gray
$subscriptionId = Read-Host "Enter Subscription ID"
if (-not $subscriptionId) {
    Write-Error "Subscription ID is required"
}

Write-Host ""

# Resource Group
Write-Host "Azure Resource Group Name:" -ForegroundColor Cyan
Write-Host "  Will be created if it doesn't exist. Default: mate-dev-rg" -ForegroundColor Gray
$rg = Read-Host "Enter Resource Group Name (or press Enter for default)"
if (-not $rg) {
    $rg = "mate-dev-rg"
}

Write-Host ""

# Location
Write-Host "Azure Region/Location:" -ForegroundColor Cyan
Write-Host "  Examples: eastus, westeurope, australiaeast, uksouth" -ForegroundColor Gray
Write-Host "  Default: eastus" -ForegroundColor Gray
$location = Read-Host "Enter Location (or press Enter for default)"
if (-not $location) {
    $location = "eastus"
}

Write-Host ""

# Environment Name
Write-Host "Environment Name Prefix:" -ForegroundColor Cyan
Write-Host "  Used for resource naming (e.g., mate-dev-aca, mate-dev-postgres)" -ForegroundColor Gray
Write-Host "  Default: mate-dev" -ForegroundColor Gray
$envName = Read-Host "Enter Environment Name (or press Enter for default)"
if (-not $envName) {
    $envName = "mate-dev"
}

Write-Host ""

# Profile
Write-Host "Deployment Profile:" -ForegroundColor Cyan
Write-Host "  xs = testing (0.25 CPU, 0.5GB, 0-1 web replicas, 0-2 worker replicas)" -ForegroundColor Gray
Write-Host "  s  = development (0.5 CPU, 1GB, 1-3 web replicas, 0-5 worker replicas)" -ForegroundColor Gray
Write-Host "  m  = growth (1.0 CPU, 2GB, 2-6 web replicas, 0-10 worker replicas)" -ForegroundColor Gray
Write-Host "  l  = production (2.0 CPU, 4GB, 3-12 web replicas, 0-20 worker replicas)" -ForegroundColor Gray
Write-Host "  Default: s (development)" -ForegroundColor Gray
$profile = Read-Host "Enter Profile (xs/s/m/l, or press Enter for s)"
if (-not $profile) {
    $profile = "s"
}
if ($profile -notin @('xs', 's', 'm', 'l')) {
    Write-Error "Invalid profile. Must be xs, s, m, or l"
}

Write-Host ""

# Image Tag
Write-Host "Container Image Tag:" -ForegroundColor Cyan
Write-Host "  Version at ghcr.io (e.g., latest, v1.0.0, main)" -ForegroundColor Gray
Write-Host "  Default: latest" -ForegroundColor Gray
$imageTag = Read-Host "Enter Image Tag (or press Enter for latest)"
if (-not $imageTag) {
    $imageTag = "latest"
}

Write-Host ""

# Entra ID App Registration
Write-Host "Entra ID Application Registration for WebUI Authentication:" -ForegroundColor Cyan
Write-Host "  The WebUI requires an Entra ID app registration to enable secure authentication." -ForegroundColor Gray
Write-Host ""
Write-Host "Do you have an existing app registration? (y/n)" -ForegroundColor Yellow
$hasAppReg = Read-Host "Enter y (use existing) or n (create new)"

if ($hasAppReg -eq 'y') {
    Write-Host ""
    Write-Host "Using Existing App Registration" -ForegroundColor Cyan
    Write-Host "  Get Client ID from: Azure Portal → Entra ID → App Registrations → Your App → Overview" -ForegroundColor Gray
    Write-Host ""
    $aadClientId = Read-Host "Enter AAD Client ID"
    if (-not $aadClientId) {
        Write-Error "AAD Client ID is required for secure authentication"
    }
}
else {
    Write-Host ""
    Write-Host "Creating New App Registration..." -ForegroundColor Cyan
    Write-Host "  Display Name: mate-webui-$envName" -ForegroundColor Gray
    Write-Host "  Note: This requires 'Application Developer' or 'Global Administrator' role" -ForegroundColor Yellow
    Write-Host ""
    
    $confirm = Read-Host "Create app registration now? (y/n)"
    if ($confirm -eq 'y') {
        try {
            $appName = "mate-webui-$envName"
            Write-Host "  Creating app registration '$appName'..." -ForegroundColor Gray
            
            $result = az ad app create --display-name $appName --sign-in-audience AzureADMyOrg
            if ($LASTEXITCODE -ne 0) {
                Write-Host ""
                Write-Error "Failed to create app registration. Error: $result`n`nPlease create manually and re-run setup."
            }
            
            $appData = $result | ConvertFrom-Json
            $aadClientId = $appData.appId
            
            Write-Host ""
            Write-Host "✓ App Registration Created Successfully!" -ForegroundColor Green
            Write-Host "  Display Name:       $appName"
            Write-Host "  Application ID:     $aadClientId"
            Write-Host "  Tenant ID:          $tenantId"
            Write-Host ""
            Write-Host "IMPORTANT: After deployment, you must register the redirect URI." -ForegroundColor Yellow
            Write-Host "See docs/concepts/azure-entra-id-authentication-setup.md for details." -ForegroundColor Yellow
        }
        catch {
            Write-Host ""
            Write-Error "Failed to create app registration: $_`n`nPlease create manually in Azure Portal and re-run setup."
        }
    }
    else {
        Write-Host ""
        Write-Host "To create manually:" -ForegroundColor Yellow
        Write-Host "  1. Go to Azure Portal → Entra ID → App Registrations"
        Write-Host "  2. Click 'New registration'"
        Write-Host "  3. Name: mate-webui-$envName"
        Write-Host "  4. Supported accounts: Single tenant"
        Write-Host "  5. Copy the Application (client) ID"
        Write-Host ""
        Write-Error "App registration required. Please create and re-run setup with the Client ID."
    }
}

Write-Host ""

# Entra ID Client Secret
Write-Host "Entra ID Client Secret for WebUI:" -ForegroundColor Cyan
Write-Host "  The WebUI uses a confidential client that requires a client secret." -ForegroundColor Gray
Write-Host "  This secret will be stored securely in Azure Key Vault." -ForegroundColor Gray
Write-Host ""
Write-Host "What would you like to do?" -ForegroundColor Yellow
Write-Host "  1. Use existing client secret (you provide it)" -ForegroundColor White
Write-Host "  2. Create new client secret automatically" -ForegroundColor White
Write-Host ""
$secretOption = Read-Host "Enter 1 (existing) or 2 (create new), or press Enter for 1"
if (-not $secretOption) {
    $secretOption = '1'
}

if ($secretOption -eq '1') {
    Write-Host ""
    Write-Host "Provide Existing Client Secret:" -ForegroundColor Cyan
    Write-Host "  Get from: Azure Portal → Entra ID → App Registrations → Your App → Certificates & secrets" -ForegroundColor Gray
    Write-Host "  (Copy only when created - it won't be shown again)" -ForegroundColor Yellow
    Write-Host ""
    $aadClientSecret = Read-Host "Enter client secret"
    if (-not $aadClientSecret) {
        Write-Error "Client secret is required for authentication"
    }
}
elseif ($secretOption -eq '2') {
    Write-Host ""
    Write-Host "Creating New Client Secret..." -ForegroundColor Cyan
    Write-Host "  Note: This requires 'Application Developer' or app owner permissions" -ForegroundColor Yellow
    Write-Host ""
    
    try {
        $secretDisplayName = "mate-webui-$envName-$(Get-Date -Format 'yyyyMMdd')"
        Write-Host "  Creating client secret for app $aadClientId..." -ForegroundColor Gray
        Write-Host "  Secret description: $secretDisplayName" -ForegroundColor Gray
        
        $secretResult = az ad app credential reset --id $aadClientId --append --display-name $secretDisplayName --query password -o tsv
        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Error "Failed to create client secret. Error: $secretResult`n`nPlease create manually in Azure Portal and re-run setup."
        }
        
        $aadClientSecret = $secretResult
        
        Write-Host "✓ Client Secret Created Successfully!" -ForegroundColor Green
        Write-Host "  Secret: (stored, will be saved to Key Vault)" -ForegroundColor Gray
        Write-Host ""
    }
    catch {
        Write-Host ""
        Write-Error "Failed to create client secret: $_`n`nPlease create manually in Azure Portal and re-run setup."
    }
}
else {
    Write-Error "Invalid option. Enter 1 or 2"
}

Write-Host ""

# Confirm
Write-Host "Summary:" -ForegroundColor Yellow
Write-Host "  Tenant ID:           $tenantId"
Write-Host "  Subscription ID:     $subscriptionId"
Write-Host "  Resource Group:      $rg"
Write-Host "  Location:            $location"
Write-Host "  Environment Name:    $envName"
Write-Host "  Profile:             $profile"
Write-Host "  Image Tag:           $imageTag"
Write-Host "  AAD Client ID:       $aadClientId"
Write-Host "  AAD Client Secret:   ***REDACTED***"
Write-Host ""

$confirm = Read-Host "Save to .env? (y/n)"
if ($confirm -ne 'y') {
    Write-Host "Cancelled." -ForegroundColor Yellow
    return
}

# Create .env file (NO SECRET - stored in Key Vault instead)
@"
# Azure Deployment Environment Configuration
# Generated by setup-env.ps1
# IMPORTANT: .env is git-ignored and should NEVER be committed

AZURE_TENANT_ID=$tenantId
AZURE_SUBSCRIPTION_ID=$subscriptionId
AZURE_RESOURCE_GROUP=$rg
AZURE_LOCATION=$location
AZURE_ENVIRONMENT_NAME=$envName
AZURE_PROFILE=$profile
AZURE_IMAGE_TAG=$imageTag
AZURE_AAD_CLIENT_ID=$aadClientId
AZURE_POSTGRES_ADMIN_USER=pgadmin
AZURE_AAD_SECRET_CONFIGURED=true

# PostgreSQL password will be prompted interactively at deployment
# (safer than storing in .env)

# Entra ID Client Secret
# Securely stored in Azure Key Vault (not in .env for security)
# Key Vault Name: `$envName`-kv
# Secret Name: azuread-client-secret

# Entra ID Authentication Setup
# After deployment, register this redirect URI in your Entra ID app registration:
# https://your-webui-fqdn/signin-oidc
# The WebUI FQDN will be shown after successful deployment.
"@ | Out-File $envFile -Encoding UTF8

Write-Host ""
Write-Host "✓ .env file created at: $envFile" -ForegroundColor Green
Write-Host ""

# Store client secret in Key Vault (pre-deployment)
Write-Host "Setting Up Key Vault for Client Secret..." -ForegroundColor Cyan
Write-Host ""

try {
    # Note: Key Vault will be created during first deployment
    # We'll store the secret after infrastructure is ready
    # For now, save to a temporary variable for later use
    
    Write-Host "  Client secret will be stored in Key Vault after infrastructure deployment." -ForegroundColor Gray
    Write-Host "  Key Vault Name: $envName-kv" -ForegroundColor Gray
    Write-Host "  Secret Name: azuread-client-secret" -ForegroundColor Gray
    Write-Host ""
    
    # Create a temporary credentials file (git-ignored) that contains just the secret
    $tempCredFile = Join-Path $scriptDir ".credentials"
    
    # Store secret securely for post-deployment setup
    @{
        AAD_CLIENT_SECRET = $aadClientSecret
        AAD_CLIENT_ID = $aadClientId
        TENANT_ID = $tenantId
        SUBSCRIPTION_ID = $subscriptionId
        RESOURCE_GROUP = $rg
        ENVIRONMENT_NAME = $envName
    } | ConvertTo-Json | Out-File $tempCredFile -Encoding UTF8 -Force
    
    Write-Host "✓ Credentials stored temporarily (will be used after deployment)" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Error "Failed to prepare Key Vault setup: $_"
}

# Prompt for PostgreSQL password
Write-Host ""
Write-Host "PostgreSQL Admin Password:" -ForegroundColor Cyan
Write-Host "  Used to initialize the PostgreSQL database for Mate" -ForegroundColor Gray
Write-Host "  This will be stored securely and prompted at deployment time" -ForegroundColor Gray
Write-Host "  Requirements: min 8 chars, uppercase, lowercase, numbers, special chars" -ForegroundColor Gray
Write-Host ""
$postgresPassword = Read-Host "Enter PostgreSQL admin password (or press Enter to be prompted at deployment)"

if ($postgresPassword) {
    # Store temporarily for deployment
    $postgresPassword | Add-Content (Join-Path $scriptDir ".pg-password") -Force
    Write-Host "✓ PostgreSQL password stored temporarily" -ForegroundColor Green
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. First time? Login to Azure:"
Write-Host "   az account clear" -ForegroundColor Magenta
Write-Host "   az login --tenant $tenantId" -ForegroundColor Magenta
Write-Host ""

Write-Host "2. Preview resources (what-if dry-run):"
Write-Host "   .\deploy-whatif.ps1" -ForegroundColor Magenta
Write-Host ""

Write-Host "3. Deploy to Azure (creates real resources, approx 10-15 minutes):"
Write-Host "   .\deploy.ps1" -ForegroundColor Magenta
Write-Host ""

Write-Host "4. After deployment succeeds, setup Key Vault & authentication:"
Write-Host "   .\setup-keyvault-secrets.ps1" -ForegroundColor Magenta
Write-Host "   (This stores your client secret securely and configures RBAC)" -ForegroundColor Gray
Write-Host ""

Write-Host "5. Update Entra ID app registration with redirect URI:"
Write-Host "   See: docs/concepts/azure-entra-id-authentication-setup.md" -ForegroundColor Cyan
Write-Host ""
