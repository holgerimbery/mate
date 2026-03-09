# Deploy to Azure — Quick Reference

This directory contains a complete deployment guide for mate on Microsoft Azure. Below are the quick deployment steps.

## Prerequisites Checklist

- [ ] Azure subscription with admin permissions
- [ ] Azure CLI installed and authenticated (`az login`)
- [ ] PowerShell 7+ installed
- [ ] Bicep CLI (included with Azure CLI)

## Quick Deploy (3 minutes)

All scripts are included in this package. Simply run:

```powershell
# 1. Validate prerequisites
pwsh ./check-prerequisites.ps1

# 2. Configure environment (prompts for tenant ID, subscription, location, etc.)
pwsh ./setup-env.ps1

# 3. Preview what will be created
pwsh ./deploy-whatif.ps1

# 4. Deploy to Azure (creates all infrastructure)
pwsh ./deploy.ps1

# 5. Configure secrets and RBAC
pwsh ./setup-keyvault-secrets.ps1
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

- **check-prerequisites.ps1** — Validates Azure CLI, Bicep, PowerShell
- **setup-env.ps1** — Interactive: Collects Tenant ID, Subscription, Location, etc.
- **deploy-whatif.ps1** — Dry-run preview (recommended before deploy)
- **deploy.ps1** — Actual deployment + automatic secret/firewall config
- **setup-keyvault-secrets.ps1** — Post-deploy: Configures Key Vault and RBAC
- **update-container-images.ps1** — Update container images to new version (faster than full deploy)
- **cleanup-rg.ps1** — Deletes all resources (keeps resource group)

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
# Update to specific version
pwsh ./update-container-images.ps1 -ImageTag 'v0.6.1'

# Update to latest
pwsh ./update-container-images.ps1

# Preview changes first
pwsh ./update-container-images.ps1 -ImageTag 'v0.6.1' -WhatIf
```

**Time:** 1–2 minutes (zero-downtime rolling update)

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
pwsh ./cleanup-rg.ps1

# Delete resource group entirely
az group delete --name rg-mate-dev
```

## Full Documentation

- **Deployment Guide**: [README.md](./README.md)
- **Architecture Details**: [../../docs/concepts/SaaS-Architecture-v2.md](../../docs/concepts/SaaS-Architecture-v2.md)
- **Troubleshooting**: See README.md → Troubleshooting section

---

**Next:** After deployment succeeds, read [docs/wiki/User-Getting-Started.md](https://github.com/holgerimbery/mate/blob/main/docs/wiki/User-Getting-Started.md) to set up your first test suite.
