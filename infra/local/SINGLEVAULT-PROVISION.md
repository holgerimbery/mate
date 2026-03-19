# Single-Vault Provisioning Guide (Core Mode)

## Overview

This guide describes how to set up single-vault Azure Key Vault infrastructure for mate core Docker deployments. The provisioning script automates:

1. **Vault Creation**: One shared Key Vault for all users
2. **Secrets Migration**: Moves secrets from Docker environment to the vault
3. **Configuration Generation**: Outputs environment variables for Docker Compose
4. **Safety Validation**: Ensures subscription/tenant context is correct before making changes

## Architecture

### Vault Topology (Core Single-Tenant)

```
┌───────────────────────────────────┐
│  Single-Vault Architecture        │
├───────────────────────────────────┤
│                                   │
│  Shared Vault (mate-dev-kv)       │
│  └─ All users access this vault   │
│  └─ All secrets shared            │
│  └─ Access via Azure RBAC         │
│                                   │
└───────────────────────────────────┘
```

### Access Control

| Role | Vault | Permissions |
|------|-------|-------------|
| SuperAdmin | Shared | Read/Write/Delete |
| TenantAdmin | Shared | Read/Write/Delete |
| Tester | Shared | Read-only |

All roles access the **same vault**; access control is managed via Azure RBAC.

## Prerequisites

1. **Azure CLI** installed and authenticated
   ```powershell
   az login
   az account set --subscription <subscription-id>
   ```

2. **Permissions** in target subscription:
   - `Owner` or `Key Vault Administrator` role on resource group
   - Ability to create Azure Key Vaults

3. **Environment Details**:
   - Subscription ID (GUID)
   - Tenant ID (GUID)
   - Resource Group name (will be created if missing)
   - Environment name (dev|test|prod)

## Usage

### Basic Provisioning

```powershell
cd infra/local

.\provision-singlevault.ps1 `
  -SubscriptionId "<subscription-id>" `
  -TenantId "<tenant-id>" `
  -ResourceGroupName "mate-dev" `
  -Environment "dev"
```

### Dry-Run (Preview)

```powershell
.\provision-singlevault.ps1 `
  -SubscriptionId "<subscription-id>" `
  -TenantId "<tenant-id>" `
  -ResourceGroupName "mate-dev" `
  -Environment "dev" `
  -DryRun
```

### Custom Location (Non-eastus)

```powershell
.\provision-singlevault.ps1 `
  -SubscriptionId "<subscription-id>" `
  -TenantId "<tenant-id>" `
  -ResourceGroupName "mate-dev" `
  -Environment "dev" `
  -Location "westeurope"
```

## Vault Naming Convention

The script uses the following naming pattern:

### Shared Vault
```
mate-{environment}-kv
```

Examples:
- `mate-dev-kv`
- `mate-test-kv`
- `mate-prod-kv`

### Vault URI
```
https://mate-{environment}-kv.vault.azure.net/
```

## Configuration Output

After successful provisioning, add the following to `infra/local/.env`:

```bash
# Enable Key Vault for secrets (instead of database)
AzureInfrastructure__UseKeyVaultForSecrets=true

# Single-vault mode: all users access the same vault
AzureInfrastructure__UseMultiVaultForSecrets=false

# Shared vault URI (replace with actual URI from provisioning output)
AzureInfrastructure__KeyVaultUri=https://mate-dev-kv.vault.azure.net/
```

## Docker Compose Integration (Core Mode)

### Step 1: Run Provisioning Script
```powershell
cd infra/local
.\provision-singlevault.ps1 `
  -SubscriptionId "<subscription-id>" `
  -TenantId "<tenant-id>" `
  -ResourceGroupName "mate-dev" `
  -Environment "dev"
```

### Step 2: Update `.env`
Copy the configuration block from script output into `infra/local/.env` file.

### Step 3: Rebuild Containers (Core Mode)
```powershell
cd ../..  # Back to repo root
./debug-container.ps1 -Stop
./debug-container.ps1 -Source build -Rebuild
```

### Step 4: Verify
1. Open `http://localhost:5000` (Core WebUI)
2. Login with an admin account
3. Navigate to **Help** → **Runtime Environment**
4. Confirm **Secrets Mode** shows `Key Vault` (green badge)
5. Check app logs for Key Vault secret retrieval

## Secrets Migration

The provisioning script migrates the following secrets by default:

| Secret Name | Purpose |
|-------------|---------|
| `AzureAd--TenantId` | Entra ID tenant ID |
| `AzureAd--ClientId` | Entra ID app registration client ID |
| `AzureAd--ClientSecret` | Entra ID app registration secret |
| `AzureAd--Instance` | Microsoft login endpoint |

**Custom Secrets**: To add additional secrets to migration, edit the `$SecretsToMigrate` array in the provisioning script.

## Single-Vault vs. Multi-Vault

This guide covers **single-vault mode** for core deployments.

For **enterprise deployments** with multi-vault mode, see: [`/enterprise/mate-enterprise/infra/docs/MULTIVAULT-PROVISION.md`](../../enterprise/mate-enterprise/infra/docs/MULTIVAULT-PROVISION.md)

| Feature | Single-Vault (Core) | Multi-Vault (Enterprise) |
|---------|-------------------|------------------------|
| **Vaults** | 1 shared vault | Platform + tenant vaults |
| **Access Control** | Azure RBAC on vault | Role-based vault routing |
| **Scope** | Core deployments | Enterprise multi-tenant |
| **Configuration** | `UseMultiVaultForSecrets=false` | `UseMultiVaultForSecrets=true` |
| **Isolation** | Shared secrets only | Per-tenant isolation |

## Troubleshooting

### "You do not have permission to perform action 'Microsoft.KeyVault/vaults/write'"

**Cause**: Azure account lacks Key Vault administrator role

**Solution**: 
```powershell
# Verify role assignment
az role assignment list --assignee <your-user-id> --scope /subscriptions/<subscription-id>

# Request Key Vault Administrator role for resource group
```

### "Unable to switch to target tenant"

**Cause**: Subscription is not linked to target tenant

**Solution**:
1. Verify subscription and tenant IDs are correct
2. Confirm you have access to both subscription and tenant
3. Try manual tenant switch: `az login --tenant <tenant-id>`

### Vault Created but Secrets Not Accessible

**Cause**: RBAC assignments not yet propagated, or DefaultAzureCredential not finding credentials

**Solution**:
1. Wait 2-3 minutes for RBAC to propagate
2. Verify logged-in user is added to vault RBAC (or use managed identity in production)
3. Check docker compose logs: `docker logs <container-name>`

## Security Notes

1. **No Hardcoded Credentials**: All subscription/tenant IDs are runtime parameters
2. **RBAC-Based**: Vaults use Azure RBAC (not access policies) for modern access control
3. **Managed Identity Ready**: In production, use managed identity instead of user login
4. **CI/CD Integration**: For GitHub Actions, use Azure CLI authentication via service principal

## Related Documentation

- [Enterprise Multi-Vault Provisioning](../../enterprise/mate-enterprise/infra/docs/MULTIVAULT-PROVISION.md)
- [Key Vault Single-Vault Service](../docs/KEYVAULT-SERVICE.md)
- [Docker Compose Setup (Core)](./README.md)
- [EntraID Authentication Setup](../../docs/concepts/azure-entra-id-authentication-setup.md)
