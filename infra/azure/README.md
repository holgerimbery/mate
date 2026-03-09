# Azure Deployment Scaffold

This folder contains the first implementation scaffold for `E1-13` Azure deployment.

## Contents

- `main.bicep`: orchestrates all modules
- `modules/`: modular Azure resources
- `parameters/`: size/profile parameter files
- `scripts/`: PowerShell deployment helpers (`what-if`, deploy)

## Prerequisites

- Azure CLI
- Bicep CLI (bundled with recent Azure CLI)
- Rights to create resources in the target subscription/tenant

## Quick Start (dev + S)

```powershell
pwsh ./infra/azure/scripts/deploy-azure.ps1 \
  -TenantId "<tenant-guid>" \
  -SubscriptionId "<subscription-guid>" \
  -Location "westeurope" \
  -EnvironmentName "dev" \
  -SizeProfile "s" \
  -ResourceGroupName "rg-mate-dev" \
  -DeploymentName "mate-dev-s"
```

## What-if

```powershell
pwsh ./infra/azure/scripts/whatif-azure.ps1 \
  -TenantId "<tenant-guid>" \
  -SubscriptionId "<subscription-guid>" \
  -Location "westeurope" \
  -EnvironmentName "dev" \
  -SizeProfile "s" \
  -ResourceGroupName "rg-mate-dev"
```

## Notes

- Internal engineering default remains `dev` + `s`.
- Parameter files for `xs`, `s`, `m`, `l` are included.
- The installer prompt flow (`E1-13l`) will later select scope/profile and emit deterministic parameters.
