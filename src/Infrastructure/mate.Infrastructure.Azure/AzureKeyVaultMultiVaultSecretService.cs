// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Collections.Concurrent;
using System.Security.Claims;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using mate.Data;
using mate.Domain.Contracts;
using mate.Domain.Contracts.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace mate.Infrastructure.Azure;

/// <summary>
/// Enterprise secret service that resolves the target Key Vault dynamically per role and tenant.
/// Falls back to single-vault behavior when tenant/user context is not available.
/// </summary>
public sealed class AzureKeyVaultMultiVaultSecretService : ISecretService
{
    private readonly AzureInfrastructureOptions _options;
    private readonly ITenantAuthorizationService _tenantAuthorization;
    private readonly ITenantContext? _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TokenCredential _credential;
    private readonly ILogger<AzureKeyVaultMultiVaultSecretService> _logger;
    private readonly ConcurrentDictionary<string, SecretClient> _clientCache = new(StringComparer.OrdinalIgnoreCase);

    public AzureKeyVaultMultiVaultSecretService(
        IOptions<AzureInfrastructureOptions> options,
        ITenantAuthorizationService tenantAuthorization,
        IHttpContextAccessor httpContextAccessor,
        TokenCredential credential,
        ILogger<AzureKeyVaultMultiVaultSecretService> logger,
        ITenantContext? tenantContext = null)
    {
        _options = options.Value;
        _tenantAuthorization = tenantAuthorization;
        _httpContextAccessor = httpContextAccessor;
        _credential = credential;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task<string> GetSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        var client = await ResolveSecretClientAsync(requireWrite: false, ct);

        try
        {
            var secret = await client.GetSecretAsync(secretRef, cancellationToken: ct);
            return secret.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var envValue = Environment.GetEnvironmentVariable(secretRef);
            if (!string.IsNullOrEmpty(envValue))
                return envValue;

            throw new InvalidOperationException($"Secret '{secretRef}' is not configured in resolved vault or environment.", ex);
        }
    }

    public async Task SetSecretAsync(string secretRef, string secretValue, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        var client = await ResolveSecretClientAsync(requireWrite: true, ct);
        await client.SetSecretAsync(secretRef, secretValue, ct);
    }

    public async Task DeleteSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        var client = await ResolveSecretClientAsync(requireWrite: true, ct);

        try
        {
            await client.StartDeleteSecretAsync(secretRef, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Secret reference '{SecretRef}' not present in resolved vault. Nothing to delete.", secretRef);
        }
    }

    private async Task<SecretClient> ResolveSecretClientAsync(bool requireWrite, CancellationToken ct)
    {
        // Explicit platform-vault fallback for deployments that only configured a single vault.
        if (!string.IsNullOrWhiteSpace(_options.KeyVaultUri))
            return GetOrCreateClient(_options.KeyVaultUri);

        if (string.IsNullOrWhiteSpace(_options.PlatformVaultUri) || string.IsNullOrWhiteSpace(_options.TenantVaultUriTemplate))
            throw new InvalidOperationException("Multi-vault mode requires AzureInfrastructure:PlatformVaultUri and TenantVaultUriTemplate.");

        var user = _httpContextAccessor.HttpContext?.User;
        var userId = user?.FindFirstValue("sub")
                     ?? user?.FindFirstValue("oid")
                     ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);

        var tenantId = _tenantContext?.TenantId ?? mateDbContext.PlatformTenantId;

        if (string.IsNullOrWhiteSpace(userId))
        {
            // Background/CLI path: use platform vault unless tenant-specific context is explicitly set.
            if (tenantId == mateDbContext.PlatformTenantId)
                return GetOrCreateClient(_options.PlatformVaultUri!);

            return GetOrCreateClient(ResolveTenantVaultUri(tenantId));
        }

        var isSuperAdmin = await _tenantAuthorization.IsAuthorizedAsync(
            userId,
            mateDbContext.PlatformTenantId,
            "SuperAdmin",
            ct);

        if (isSuperAdmin)
            return GetOrCreateClient(_options.PlatformVaultUri!);

        var roles = await _tenantAuthorization.GetRolesAsync(userId, tenantId, ct);
        if (roles.Count == 0)
            throw new UnauthorizedAccessException($"User '{userId}' has no active role assignment for tenant '{tenantId}'.");

        if (requireWrite && roles.Any(r => string.Equals(r, "Tester", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("Tester role is read-only for tenant vault secrets.");

        return GetOrCreateClient(ResolveTenantVaultUri(tenantId));
    }

    private string ResolveTenantVaultUri(Guid tenantId)
        => _options.TenantVaultUriTemplate!.Replace("{tenantId}", tenantId.ToString("D"), StringComparison.OrdinalIgnoreCase);

    private SecretClient GetOrCreateClient(string vaultUri)
        => _clientCache.GetOrAdd(vaultUri, uri => new SecretClient(new Uri(uri), _credential));
}
