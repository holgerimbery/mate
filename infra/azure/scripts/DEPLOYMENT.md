# Azure Deployment Workflow

This directory contains PowerShell helper scripts to deploy the Mate infrastructure to Azure.

## Overview

The deployment process is split into two phases:

1. **What-If (Dry-Run)**: Preview resources without creating them
2. **Deploy (Live)**: Create resources in Azure

## Prerequisites

**Required Tools:**
- Azure CLI (version 2.50+) — install via `winget install Microsoft.AzureCLI`
- Bicep CLI (included with Azure CLI)
- PowerShell 5.1+ (PowerShell 7+ recommended)

**Required Access:**
- Azure tenant with admin consent rights
- Ability to create service principals and assign roles
- Subscription with remaining resource quota

**Information You'll Need:**
- Tenant ID (Azure Portal → Azure AD → Properties)
- Subscription ID (Azure Portal → Subscriptions)
- Resource Group name (will be created if it doesn't exist)
- PostgreSQL admin password (prompted interactively, never stored)

## Quick Start

### 1. Setup Environment Variables

Store your Azure tenant, subscription, and resource group information locally (never in git):

```powershell
.\setup-env.ps1
```

This interactive wizard creates a `.env` file with your settings:
- **Save location:** `infra/azure/.env` (automatically git-ignored)
- **What gets stored:** Tenant ID, Subscription ID, Resource Group, Location, etc.
- **Security:** `.env` is never committed to git

See `.env.template` for all available configuration options.

### 2. Check Prerequisites

```powershell
.\check-prerequisites.ps1
```

Validates that Azure CLI, Bicep, and PowerShell are installed.

### 3. Preview Deployment (What-If)

```powershell
.\deploy-whatif.ps1
```

**This will:**
- Use values from `.env` automatically (no parameters needed)
- NOT create any Azure resources
- NOT modify your Azure account
- Show exactly what WOULD be created
- Display resource names, locations, and estimated costs

**To override .env values**, pass parameters:

```powershell
.\deploy-whatif.ps1 -Location 'westeurope' -Profile 'm'
```

**Output:** Review carefully for:
- Resource names and locations
- Capacity and scaling settings
- Any errors or missing parameters

### 4. Deploy to Azure (Live)

```powershell
.\deploy.ps1
```

**This will:**
- Use values from `.env` automatically (no parameters needed)
- Prompt for PostgreSQL admin password (never stored, never logged)
- Create resource group (if needed)
- Deploy all Azure resources
- Display post-deployment tasks

**To override .env values**, pass parameters:

```powershell
.\deploy.ps1 -Location 'westeurope' -Profile 'm'
```

**Warning:** This creates real Azure resources and incurs costs. Always run `deploy-whatif.ps1` first.

## Deployment Profiles

| Profile | Web Min | Web Max | Worker Max | CPU | Memory | Use Case |
|---------|---------|---------|------------|-----|--------|----------|
| `xs`    | 0       | 1       | 2          | 0.25 | 0.5 GB | Testing, lowest cost |
| `s`     | 1       | 3       | 5          | 0.5 | 1 GB   | **Default for dev** |
| `m`     | 2       | 6       | 10         | 1.0 | 2 GB   | Growth production |
| `l`     | 3       | 12      | 20         | 2.0 | 4 GB   | High throughput |

**Development Policy:** Internal engineering always uses `dev` environment with `s` profile.

## Services Deployed

- **Azure Container Apps**: Runs WebUI (external ingress on port 8080) and Worker (internal, queue-driven)
- **Azure Service Bus**: Message queue `test-runs` with dead-lettering
- **PostgreSQL Flexible Server**: Relational database, v17, 32GB Burstable tier
- **Azure Blob Storage**: Document store with HTTPS-only, no public access
- **Azure Key Vault**: Credential and secret management
- **Application Insights & Log Analytics**: Telemetry and logging

## Post-Deployment Tasks

After `deploy.ps1` completes, you must:

1. **Create managed identity role assignments**
   ```powershell
   # Assign WebUI managed identity to Key Vault (read secrets)
   az role assignment create \
     --assignee <webui-principal-id> \
     --role "Key Vault Secrets User" \
     --scope /subscriptions/<subscription-id>/resourceGroups/<rg-name>/providers/Microsoft.KeyVault/vaults/<vault-name>
   
   # Assign Worker managed identity to Service Bus and Blob Storage
   ```

2. **Store secrets in Key Vault**
   ```powershell
   az keyvault secret set \
     --vault-name <vault-name> \
     --name 'postgres-connection-string' \
     --value 'Server=<postgres-server>.postgres.database.azure.com;Database=mate;Port=5432;User Id=admin;Password=<password>'
   ```

3. **Update container environment variables**
   - Edit Container Apps to bind Key Vault secret references
   - Example: `@Microsoft.KeyVault(VaultName=<vault-name>;SecretName=postgres-connection-string)`

4. **Run database migration**
   ```powershell
   # Create a job container to run entity framework migrations
   az container create \
     --resource-group <rg-name> \
     --name mate-migration \
     --image ghcr.io/holgerimbery/mate-webui:latest \
     --command-line "dotnet ef database update" \
     --environment-variables ASPNETCORE_ENVIRONMENT=Production Infrastructure=Azure
   ```

5. **Validate health endpoints**
   - WebUI: `curl https://<webapp-fqdn>/health/live`
   - Worker: Check Container Insights dashboard for queue consumption

## Troubleshooting

### Azure CLI not found
- Install from: `winget install Microsoft.AzureCLI` or https://learn.microsoft.com/cli/azure/install-azure-cli-windows
- Restart PowerShell after installation

### Bicep not found
- Run: `az bicep install`

### Authentication fails
- Clear cached credentials: `az account clear`
- Re-authenticate: `az login --tenant <TENANT-ID>`
- Verify subscription: `az account show`

### Deployment fails
- Check resource group doesn't already exist in wrong location
- Verify PostgreSQL admin password meets complexity requirements (12+ chars, mixed case, numbers)
- Review Azure Portal → Resource Groups → Deployments for error details

## Environment Variables & Configuration

**WebUI Container Environment:**
- `ASPNETCORE_ENVIRONMENT`: Always `Production`
- `Authentication`: `EntraId` (Azure Entra ID)
- `Monitoring`: `ApplicationInsights`
- `Infrastructure`: `Azure`
- Key Vault secrets (passed as references, not plain text)

**Worker Container Environment:**
- `Infrastructure`: `Azure`
- Service Bus connection string (Key Vault reference)
- Blob Storage connection string (Key Vault reference)
- PostgreSQL connection string (Key Vault reference)

## Cleanup

To remove all resources and stop incurring costs:

```powershell
az group delete --name mate-dev-rg --yes --no-wait
```

This will delete all Azure resources in the resource group (cannot be undone).

## References

- [Azure Container Apps Documentation](https://learn.microsoft.com/azure/container-apps/)
- [Bicep Language Documentation](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)
- [Azure CLI Reference](https://learn.microsoft.com/cli/azure/)
- [Azure Key Vault Documentation](https://learn.microsoft.com/azure/key-vault/)
