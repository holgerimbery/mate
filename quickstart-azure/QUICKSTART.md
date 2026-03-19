# Deploy to Azure — Quick Reference

This directory contains quick deployment documentation for mate on Microsoft Azure.

The quickstart is repository-coupled and uses canonical scripts from `infra/azure/scripts` in the same repository checkout.

Release zip note: `mate-quickstart-azure-<version>.zip` is docs-only. Run scripts from a full repository checkout.

## Prerequisites Checklist

- [ ] Azure subscription with admin permissions
- [ ] Azure CLI installed and authenticated (`az login`)
- [ ] PowerShell 7+ installed
- [ ] Bicep CLI (included with Azure CLI)

## Quick Deploy (3 minutes)

From the repository root, simply run:

```powershell
# 1. Validate prerequisites
pwsh ./infra/azure/scripts/check-prerequisites.ps1

# 2. Configure environment (prompts for tenant ID, subscription, location, etc.)
pwsh ./infra/azure/scripts/setup-env.ps1

# 3. Preview what will be created
pwsh ./infra/azure/scripts/deploy-whatif.ps1

# 4. Deploy to Azure (creates all infrastructure)
pwsh ./infra/azure/scripts/deploy.ps1

# 5. Configure secrets and RBAC
pwsh ./infra/azure/scripts/setup-keyvault-secrets.ps1
```

## What Gets Deployed

✅ **Azure Container Apps** — WebUI (public) + Worker (internal)  
✅ **PostgreSQL Flexible Server** — v17, auto-configured  
✅ **Azure Blob Storage** — Document storage  
✅ **Azure Key Vault** — Secrets management  
✅ **Application Insights** — Monitoring & logs  
✅ **Service Bus** — Queue trigger (optional)  

## Deployment Options

| Option | Time | Automation | Manual Steps |
|--------|------|-----------|--------------|
| **Local Scripts** (recommended) | 5 min | Full | None |
| **GitHub Actions** | 5 min | Full | Submit workflow |
| **Portal UI** | 10+ min | Partial | Many |

## Scripts Details

- **infra/azure/scripts/check-prerequisites.ps1** — Validates Azure CLI, Bicep, PowerShell
- **infra/azure/scripts/setup-env.ps1** — Interactive: Collects Tenant ID, Subscription, Location, etc.
- **infra/azure/scripts/deploy-whatif.ps1** — Dry-run preview (recommended before deploy)
- **infra/azure/scripts/deploy.ps1** — Actual deployment + automatic secret/firewall config
- **infra/azure/scripts/setup-keyvault-secrets.ps1** — Post-deploy: Configures Key Vault and RBAC
- **infra/azure/scripts/update-container-images.ps1** — Update container images to new version (faster than full deploy)
- **infra/azure/scripts/cleanup-rg.ps1** — Deletes all resources (keeps resource group)

## Costs

Typical monthly costs by profile:

| Profile | Instance Type | Monthly Cost |
|---------|---------------|--------------|
| `xs` | Tiny (dev/test sandbox) | ~$15 |
| `s` | Small (personal dev) | ~$30 |
| `m` | Medium (team staging) | ~$80 |
| `l` | Large (production) | ~$200+ |

Costs depend on your region and usage patterns. **Always run `deploy-whatif.ps1` first to estimate costs.**

## After Deployment

Your app will be at:  
**`https://mate-dev-webui.{uniqueId}.{region}.azurecontainerapps.io`**

Sample data is available after first login. To test:

1. Navigate to Test Suites → Create New Suite
2. Add test cases targeting any integrated agent
3. Run the suite and view results

## Updating Container Images (New Release)

When a new version is released, quickly update without redeploying infrastructure:

```powershell
# Update to latest (recommended)
pwsh ./infra/azure/scripts/update-container-images.ps1

# Update to specific version
pwsh ./infra/azure/scripts/update-container-images.ps1 -ImageTag '<version>'  # e.g. '0.9.0-rc.1'

# Preview changes first
pwsh ./infra/azure/scripts/update-container-images.ps1 -ImageTag '<version>' -WhatIf
```

> **💡 Note:** Script waits for completion (typically 5–10 minutes). Runtime secret wiring is managed by Bicep + Key Vault references. Monitor (optional): `az deployment group show --name main --resource-group <rg> --query "{State:properties.provisioningState}" -o table`

## Troubleshooting

### "PostgreSQL connection timeout"
→ Wait 2–3 minutes and refresh. The deployment script auto-creates firewall rules.

### "Access denied to Key Vault"  
→ Run `setup-keyvault-secrets.ps1` to configure RBAC.

### "Container App revision unhealthy"  
→ Check logs: `az containerapp logs show --name mate-dev-webui --type console --tail 50`

### "Invalid blob connection string"  
→ Run `deploy.ps1` again — it will update secrets automatically.

See [README.md](./README.md) for full troubleshooting guide.

## Cleanup

```powershell
# Delete all resources (keeps resource group for re-deployment)
pwsh ./infra/azure/scripts/cleanup-rg.ps1

# Delete resource group entirely
az group delete --name rg-mate-dev
```

## Full Documentation

- **Deployment Guide**: [README.md](./README.md)
- **Architecture Details**: [docs/wiki/Developer-Architecture.md](https://github.com/holgerimbery/mate/blob/main/docs/wiki/Developer-Architecture.md)
- **Troubleshooting**: See README.md → Troubleshooting section

---

**Next:** After deployment succeeds, read [docs/wiki/User-Getting-Started.md](https://github.com/holgerimbery/mate/blob/main/docs/wiki/User-Getting-Started.md) to set up your first test suite.
