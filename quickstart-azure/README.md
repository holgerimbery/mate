# mate — Quickstart: Deploy to Azure

Deploy **mate** to Microsoft Azure with a managed infrastructure setup including Azure Container Apps, PostgreSQL Flexible Server, Blob Storage, Key Vault, and Application Insights.

## Prerequisites

**Before you start:**

1. **Azure account** — Valid Azure subscription with permissions to create resources
2. **Azure CLI** — [Install Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
3. **PowerShell 7+** — [Install PowerShell Core](https://learn.microsoft.com/powershell/scripting/install/installing-powershell)
4. **Bicep CLI** — Usually installed with Azure CLI; verify: `az bicep version`
5. **Authenticated session** — Run `az login` and authenticate with your Azure tenant

### Quick Install (Windows)

```powershell
# Install prerequisites using winget
winget install Microsoft.AzureCLI
winget install Microsoft.PowerShell

# After installing, verify
az --version
pwsh --version
az bicep version
```

## Deployment Steps

All scripts are included in this package. Follow these 5 steps:

### 1. Prepare Environment

```powershell
# Review prerequisites (validates Azure CLI, Bicep, PowerShell)
pwsh ./check-prerequisites.ps1
```

This validates that Azure CLI, Bicep, and PowerShell are installed.

### 2. Configure Environment Variables

```powershell
pwsh ./setup-env.ps1
```

This creates a `.env` file with your Azure credentials. You'll be prompted for:

- **Azure Tenant ID** — Your Azure AD tenant ID
- **Subscription ID** — Target Azure subscription
- **Location** — Azure region (e.g., `westeurope`, `eastus`)
- **Environment Name** — Prefix for resources (e.g., `mate-dev`, `mate-prod`)
- **Resource Group** — Name for the resource group (auto-generated if not provided)
- **AAD Client ID** — Entra ID application ID for authentication

**Security:** The `.env` file is **never committed to git** — it contains sensitive information and is protected by `.gitignore`.

### 3. Preview Changes (Recommended)

```powershell
pwsh ./deploy-whatif.ps1
```

This performs a **dry-run** showing exactly which Azure resources would be created. Review the output carefully before proceeding.

### 4. Deploy Infrastructure

```powershell
pwsh ./deploy.ps1
```

This command:

1. ✅ Deploys Bicep template to Azure
2. ✅ Creates Azure Container Apps (WebUI + Worker)
3. ✅ Provisions PostgreSQL Flexible Server
4. ✅ Sets up Blob Storage (documents)
5. ✅ Configures Key Vault for secrets
6. ✅ Creates Application Insights monitoring
7. ✅ **Automatically configures runtime secrets** (DB + Blob connection strings)
8. ✅ **Automatically creates PostgreSQL firewall rules**

**Typical deployment time:** 3–5 minutes

### 5. Post-Deployment: Key Vault & RBAC Setup

```powershell
pwsh ./setup-keyvault-secrets.ps1
```

This script:

1. Stores your Entra ID client secret in Key Vault
2. Configures managed identity RBAC permissions
3. Verifies the setup works
4. Provides next steps

## Access Your Deployment

After deployment succeeds, you'll see:

```
✓ Container App deployed: https://mate-dev-webui.orangebay-XXXXXXXX.westeurope.azurecontainerapps.io
```

Open that URL in your browser. You'll be redirected to Entra ID login if configured, or see the mate dashboard.

### First Login

If using **Entra ID authentication**:
- You'll be redirected to sign in with your Azure AD account
- After first login, you may need admin consent for the app registration

If using **no authentication** (dev mode):
- Dashboard appears immediately; no login required

## Deployment Profiles

Choose a size profile based on your use case:

| Profile | Replicas | CPUs | Memory | Cost | Use Case |
|---------|----------|------|--------|------|----------|
| `xs` | 0–1 | 0.25 | 0.5 GB | ~$15/mo | Local testing, dev sandbox |
| `s` | 1–2 | 0.5 | 1 GB | ~$30/mo | Development environment |
| `m` | 2–4 | 1 | 2 GB | ~$80/mo | Staging / light production |
| `l` | 4–8 | 2 | 4 GB | ~$200/mo | Production workload |

To use a different profile, pass `-Profile` when running `deploy.ps1`:

```powershell
pwsh ./deploy.ps1 -Profile m
```

Or set in `.env`:

```env
AZURE_PROFILE=m
```

## Troubleshooting

### Problem: "PostgreSQL connection timeout"

**Cause:** Firewall rule not applied or PostgreSQL still initializing.

**Solution:** The deployment script automatically creates the firewall rule. If the error persists:

1. Check PostgreSQL status in Azure Portal
2. Wait 2–3 minutes and manually refresh the Container Apps revisions
3. If still failing, check logs: `az containerapp logs show --name mate-dev-webui`

### Problem: "Access denied to Key Vault"

**Cause:** Managed identity RBAC permissions not configured.

**Solution:** Run `setup-keyvault-secrets.ps1` to configure RBAC, or manually grant the Container App managed identity the `Key Vault Secrets User` role.

### Problem: "Container App revision unhealthy"

**Cause:** Environment variables not set or secrets not injected.

**Solution:** 
1. Check logs: `az containerapp logs show --name mate-dev-webui --type console`
2. Verify secrets: `az containerapp secret list --name mate-dev-webui`
3. Verify env vars: `az containerapp show --name mate-dev-webui | jq '.properties.template.containers[0].env'`

### Problem: "Invalid blob connection string"

**Cause:** Storage account key not retrieved or secret not injected.

**Solution:** The deployment script automatically handles this. If the error persists:

1. Run `deploy.ps1` again (it will update secrets)
2. Or manually inject: see logs from the latest deployment output

## Cleanup

To **delete all Azure resources**:

```powershell
pwsh ./cleanup-rg.ps1
```

This removes all resources in the resource group **except the resource group itself** (allowing re-deployment to the same RG).

To also **delete the resource group**:

```powershell
az group delete --name <resource-group-name>
```

## Environment Details

### Deployed Resources

- **Azure Container Apps Environment** — Managed container orchestration
- **Container App: WebUI** (`mate-dev-webui`) — External HTTPS ingress on port 8080
- **Container App: Worker** (`mate-dev-worker`) — Internal queue processor
- **PostgreSQL Flexible Server** (`mate-dev-pg`) — v17, public network mode
- **Azure Blob Storage** (`matedevst`) — Document storage
- **Azure Key Vault** — Secrets management (connection strings, client secrets)
- **Application Insights** — Monitoring and logging

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ Azure Container App Environment (westeurope)                │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  WebUI (External Ingress)        Worker (Internal Queue)    │
│  ▲                               ▲                           │
│  │ HTTPS:8080                    │ Service Bus Trigger      │
│  │                               │ (scale 0–2)              │
│  └──────────────┬────────────────┘                           │
│                 │                                             │
│  ┌──────────────┴──────────────┐                            │
│  │ Shared Secrets (Key Vault)  │                            │
│  │ • postgres-conn             │                            │
│  │ • blob-conn                 │                            │
│  └──────────────┬──────────────┘                            │
│                 │                                             │
└─────────────────┼─────────────────────────────────────────┘
                  │
      ┌───────────┼────────────────┬──────────────┐
      │           │                │              │
      ▼           ▼                ▼              ▼
  PostgreSQL  Blob Storage   Service Bus    App Insights
  (Flexible   (matedevst)    (TODO)         (logs/metrics)
   Server)
```

### Security

- **Managed Identity** — Container Apps authenticate to Key Vault using system-assigned identities
- **Public PostgreSQL** — Access restricted to Azure services via firewall rule (`0.0.0.0`)
- **Private Secrets** — Connection strings stored in Key Vault, never in code
- **Entra ID Auth** — WebUI redirects to Azure AD login (configurable)

## Next Steps

### Monitor Your Deployment

```powershell
# Check Container App status
az containerapp revision list --resource-group rg-mate-dev --name mate-dev-webui

# View WebUI logs
az containerapp logs show --resource-group rg-mate-dev --name mate-dev-webui --type console --tail 50

# View Worker logs
az containerapp logs show --resource-group rg-mate-dev --name mate-dev-worker --type console --tail 50
```

### Configure Authentication

By default, mate uses **no authentication** in local/dev mode. To enable **Entra ID**:

1. **Register the app** in Azure AD (or use existing app registration)
2. Set in `.env`:
   ```env
   AUTHENTICATION__SCHEME=EntraId
   AZURE_AAD_CLIENT_ID=<your-app-id>
   ```
3. Get the **client secret** from Azure AD and store in Key Vault:
   ```powershell
   pwsh ./setup-keyvault-secrets.ps1
   ```

### Scale the Deployment

To change resource allocations (CPU, memory, replicas):

Edit `.env` and change `AZURE_PROFILE`, then run `deploy.ps1` again:

```powershell
$env:AZURE_PROFILE = 'm'  # Upgrade to medium
pwsh ./deploy.ps1
```

### Access Monitoring Data

Open **Application Insights** in Azure Portal to view:
- Request traces and performance
- Dependency calls (PostgreSQL, Blob Storage)
- Custom events and logs
- Error tracking

## Support

For issues or questions:

1. Check [Troubleshooting](#troubleshooting) above
2. Review logs using Azure CLI commands above
3. Open an issue on [GitHub](https://github.com/holgerimbery/mate/issues)
4. See [docs/wiki/Developer-Getting-Started.md](../../docs/wiki/Developer-Getting-Started.md) for more details

---

**Next:** Deploy to a staging or production environment by running the same scripts with different parameters (different resource group, location, or profile).
