# Azure Deployment Workflow

This directory contains deployment workflow documentation for Azure.

Deployment is repository-coupled and uses canonical scripts from `infra/azure/scripts` in the same repository checkout.

Release zip note: `mate-quickstart-azure-<version>.zip` is docs-only. Run scripts from a full repository checkout.

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
.\infra\azure\scripts\setup-env.ps1
```

This interactive wizard creates a `.env` file with your settings:
- **Save location:** `infra/azure/scripts/.env` (automatically git-ignored)
- **What gets stored:** Tenant ID, Subscription ID, Resource Group, Location, etc.
- **Security:** `.env` is never committed to git

See `.env.template` for all available configuration options.

### 2. Check Prerequisites

```powershell
.\infra\azure\scripts\check-prerequisites.ps1
```

Validates that Azure CLI, Bicep, and PowerShell are installed.

### 3. Preview Deployment (What-If)

```powershell
.\infra\azure\scripts\deploy-whatif.ps1
```

**This will:**
- Use values from `.env` automatically (no parameters needed)
- NOT create any Azure resources
- NOT modify your Azure account
- Show exactly what WOULD be created
- Display resource names, locations, and estimated costs

**To override .env values**, pass parameters:

```powershell
.\infra\azure\scripts\deploy-whatif.ps1 -Location 'westeurope' -Profile 'm'
```

**Output:** Review carefully for:
- Resource names and locations
- Capacity and scaling settings
- Any errors or missing parameters

### 4. Deploy to Azure (Live)

```powershell
.\infra\azure\scripts\deploy.ps1
```

**This will:**
- Use values from `.env` automatically (no parameters needed)
- Prompt for PostgreSQL admin password (never stored, never logged)
- Create resource group (if needed)
- Deploy all Azure resources
- Automatically recover known first-run Key Vault secret bootstrap failures (when `.credentials` is available)
- Ensure PostgreSQL connectivity prerequisites for Azure-hosted apps when public network mode is used
- Configure WebUI/Worker runtime DB secret references (`ConnectionStrings__Default` -> `secretref:postgres-conn`)
- Configure WebUI/Worker runtime Blob secret references (`AzureInfrastructure__BlobConnectionString` -> `secretref:blob-conn`)

**To override .env values**, pass parameters:

```powershell
.\infra\azure\scripts\deploy.ps1 -Location 'westeurope' -Profile 'm'
```

---

## 5. Update Container Images (After New Release)

After a new version is released, update the container images without redeploying the entire infrastructure:

```powershell
.\infra\azure\scripts\update-container-images.ps1  # Defaults to 'latest'
```

Or specify a specific version tag:

```powershell
.\infra\azure\scripts\update-container-images.ps1 -ImageTag '<version>'  # e.g. '0.9.0-rc.1'
```

**This will:**
- Update `.env` with the new image tag (maintains Bicep state)
- Run a focused Bicep deployment for container images only
- Create new revisions for WebUI and Worker (zero-downtime rolling update)
- Automatically switch traffic to new revisions
- Maintain full Bicep state tracking for future deployments

> **💡 Deployment Note:** The script waits for deployment completion before returning. Typical runtime is **5–10 minutes**.
> Runtime secret wiring is managed by Bicep + Key Vault references.
> Monitor progress (if needed) with:
> ```powershell
> az deployment group show --name main --resource-group <your-rg> --query "{State:properties.provisioningState, Duration:properties.duration}" -o table
> ```

**Preview changes first:**

```powershell
.\infra\azure\scripts\update-container-images.ps1 -ImageTag '<version>' -WhatIf  # e.g. '0.9.0-rc.1'
```

**Update to latest without specifying tag:**

```powershell
.\infra\azure\scripts\update-container-images.ps1  # Defaults to 'latest'
```

### When to Use Each Script

| Scenario | Script |
|----------|--------|
| Initial deployment | `deploy.ps1` |
| New release available | `update-container-images.ps1` |
| Scale up/down (Profile change) | `deploy.ps1` |
| Hotfix or rollback | `update-container-images.ps1` |
| PostgreSQL or storage changes | `deploy.ps1` |

---

**Warning:** This creates real Azure resources and incurs costs. Always run `deploy-whatif.ps1` first.

## Deployment Profiles

| Profile | Web Min | Web Max | Worker Max | CPU | Memory | Use Case |
|---------|---------|---------|------------|-----|--------|----------|
| `xs`    | 1       | 1       | 2          | 0.25 | 0.5 GB | Testing, lowest cost |
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

After `deploy.ps1` completes successfully, infrastructure and secret wiring are expected to be ready.

### Manual steps required for correct Entra ID login flow

1. **Get the WebUI URL from deployment output**
   - You need the exact FQDN for redirect URI registration.

2. **Register redirect URI in Entra app registration**
   - Required redirect URI format:
     https://<webui-fqdn>/signin-oidc
   - Also set front-channel logout URL:
     https://<webui-fqdn>/signout-callback-oidc

3. **Enable ID token issuance on the app registration**
   - In Authentication for the app registration, enable ID tokens for web sign-in.

4. **Validate login in private/incognito browser**
   - Open the WebUI URL.
   - Confirm redirect to Microsoft sign-in and return to app after authentication.

### Optional verification commands (recommended)

Verify redirect URIs:
az ad app show --id <aad-client-id> --query "web.redirectUris"

Verify web container auth-related env:
az containerapp show --resource-group <rg-name> --name <env>-webui --query "properties.template.containers[0].env[?name=='AzureAd__ClientId' || name=='AzureAd__TenantId' || name=='AzureAd__CallbackPath']"

Verify Key Vault secret exists:
az keyvault secret list --vault-name <env>-kv --query "[?name=='azuread-client-secret'].name" -o tsv

Verify DB secret reference in WebUI:
az containerapp show --resource-group <rg-name> --name <env>-webui --query "properties.template.containers[0].env[?name=='ConnectionStrings__Default']"

Verify PostgreSQL firewall allows Azure services (public mode):
az postgres flexible-server firewall-rule list --resource-group <rg-name> --name <env>-pg -o table

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

### WebUI URL does not respond (startup failures)
- Check active revision health:
   az containerapp revision list --resource-group <rg-name> --name <env>-webui -o table
- Check application logs:
   az containerapp logs show --resource-group <rg-name> --name <env>-webui --type console --tail 120
- Most common cause: database connectivity during startup migration.
   - Verify `ConnectionStrings__Default` uses `secretRef` (`postgres-conn`).
   - Verify PostgreSQL firewall/network access if using public mode.

### Container app crashes after `update-container-images.ps1` (HTTP 404)

**Symptoms:** New revision unhealthy (Degraded), app returns 404 "Container App stopped" after image update.

**Most common causes:**
- Incorrect image tag (image pull failure)
- Missing Key Vault RBAC for managed identity
- Missing/incorrect PostgreSQL password input for deployment parameters

**Recommended recovery:**
1. Re-run image update with the expected tag and explicit password input:
   ```powershell
   .\infra\azure\scripts\update-container-images.ps1 -ImageTag '<expected-tag>'
   ```
2. Check active revision health and logs:
   ```powershell
   az containerapp revision list --resource-group <rg-name> --name <env>-webui -o table
   az containerapp logs show --resource-group <rg-name> --name <env>-webui --type console --tail 120
   ```
3. Verify Key Vault access and secret references are present on the container app revision.

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

To clean everything inside a resource group without deleting the resource group itself:

```powershell
.\infra\azure\scripts\cleanup-rg.ps1 -ResourceGroupName <rg-name>
```

This script deletes live resources, removes deployment records, purges RG-scoped soft-deleted Key Vault entries, and verifies the RG is empty at the end.

## References

- [Azure Container Apps Documentation](https://learn.microsoft.com/azure/container-apps/)
- [Bicep Language Documentation](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)
- [Azure CLI Reference](https://learn.microsoft.com/cli/azure/)
- [Azure Key Vault Documentation](https://learn.microsoft.com/azure/key-vault/)
