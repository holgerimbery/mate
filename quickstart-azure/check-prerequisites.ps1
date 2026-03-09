# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Commercial use of this file, in whole or in part, is prohibited without prior written permission.

<#
.SYNOPSIS
Validate and display Azure deployment prerequisites.

.DESCRIPTION
Checks that required tools are installed and provides setup instructions
for prerequisites needed to deploy the Mate infrastructure to Azure.

#>

$ErrorActionPreference = 'Continue'

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║        Azure Deployment Prerequisites Check                ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Check Azure CLI
Write-Host "Checking prerequisites..." -ForegroundColor Yellow
Write-Host ""

$azCliFound = Get-Command 'az' -ErrorAction SilentlyContinue
if ($azCliFound) {
    $azVersion = az version --only-show-errors 2>$null | ConvertFrom-Json
    Write-Host "✓ Azure CLI installed: $($azVersion.'azure-cli')" -ForegroundColor Green
}
else {
    Write-Host "✗ Azure CLI NOT found" -ForegroundColor Red
    Write-Host "  Install: winget install Microsoft.AzureCLI" -ForegroundColor Yellow
    Write-Host "  Or:      https://learn.microsoft.com/cli/azure/install-azure-cli-windows" -ForegroundColor Yellow
}

# Check Bicep
if ($azCliFound) {
    $bicepVersion = az bicep version 2>$null
    if ($?) {
        Write-Host "✓ Bicep CLI installed: $bicepVersion" -ForegroundColor Green
    }
    else {
        Write-Host "✗ Bicep CLI NOT found" -ForegroundColor Red
        Write-Host "  Install: az bicep install" -ForegroundColor Yellow
    }
}

# Check PowerShell version
$psVersion = $PSVersionTable.PSVersion
if ($psVersion.Major -ge 7) {
    Write-Host "✓ PowerShell 7 or later: $psVersion" -ForegroundColor Green
}
else {
    Write-Host "⚠ PowerShell 5.1 detected (works, but PowerShell 7+ recommended)" -ForegroundColor Yellow
    Write-Host "  Install: https://github.com/PowerShell/PowerShell/releases" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "After prerequisites are installed:" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Get your Azure tenant and subscription IDs:"
Write-Host "   - Tenant ID:       Azure portal → Azure AD → Properties → Tenant ID" -ForegroundColor Gray
Write-Host "   - Subscription ID: Azure portal → Subscriptions → Subscription ID" -ForegroundColor Gray
Write-Host ""

Write-Host "2. Ensure you have admin consent for service principal creation:"
Write-Host "   - Role needed: Owner or User Access Administrator (Contributor)" -ForegroundColor Gray
Write-Host ""

Write-Host "3. Authenticate with admin:" -ForegroundColor Cyan
Write-Host "   az account clear" -ForegroundColor Magenta
Write-Host "   az login --tenant '<TENANT-ID>'" -ForegroundColor Magenta
Write-Host ""

Write-Host "4. Run a what-if deployment first:"
Write-Host ""
Write-Host "   .\deploy-whatif.ps1 \" -ForegroundColor Magenta
Write-Host "     -TenantId '<TENANT-ID>' \" -ForegroundColor Magenta
Write-Host "     -SubscriptionId '<SUBSCRIPTION-ID>' \" -ForegroundColor Magenta
Write-Host "     -Location 'eastus' \" -ForegroundColor Magenta
Write-Host "     -EnvironmentName 'mate-dev' \" -ForegroundColor Magenta
Write-Host "     -Profile 's'" -ForegroundColor Magenta
Write-Host ""

Write-Host "5. Review the what-if output, then deploy:"
Write-Host ""
Write-Host "   .\deploy.ps1 \" -ForegroundColor Magenta
Write-Host "     -TenantId '<TENANT-ID>' \" -ForegroundColor Magenta
Write-Host "     -SubscriptionId '<SUBSCRIPTION-ID>' \" -ForegroundColor Magenta
Write-Host "     -Location 'eastus' \" -ForegroundColor Magenta
Write-Host "     -EnvironmentName 'mate-dev' \" -ForegroundColor Magenta
Write-Host "     -Profile 's'" -ForegroundColor Magenta
Write-Host ""
