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
using Microsoft.EntityFrameworkCore;
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
    private readonly mateDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TokenCredential _credential;
    private readonly ILogger<AzureKeyVaultMultiVaultSecretService> _logger;
    private readonly ConcurrentDictionary<string, SecretClient> _clientCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, string> _tenantTokenCache = new();

    public AzureKeyVaultMultiVaultSecretService(
        IOptions<AzureInfrastructureOptions> options,
        ITenantAuthorizationService tenantAuthorization,
        IHttpContextAccessor httpContextAccessor,
        TokenCredential credential,
        ILogger<AzureKeyVaultMultiVaultSecretService> logger,
        mateDbContext db,
        ITenantContext? tenantContext = null)
    {
        _options = options.Value;
        _tenantAuthorization = tenantAuthorization;
        _httpContextAccessor = httpContextAccessor;
        _credential = credential;
        _logger = logger;
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<string> GetSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);
        var kvSecretName = NormalizeSecretName(secretRef);

        var client = await ResolveSecretClientAsync(requireWrite: false, ct);

        try
        {
            var secret = await client.GetSecretAsync(kvSecretName, cancellationToken: ct);
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
        var kvSecretName = NormalizeSecretName(secretRef);

        var client = await ResolveSecretClientAsync(requireWrite: true, ct);
        await client.SetSecretAsync(kvSecretName, secretValue, ct);
    }

    public async Task DeleteSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);
        var kvSecretName = NormalizeSecretName(secretRef);

        var client = await ResolveSecretClientAsync(requireWrite: true, ct);

        try
        {
            await client.StartDeleteSecretAsync(kvSecretName, ct);
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
        var claimRoles = GetUserRoles(user);

        var tenantId = _tenantContext?.TenantId ?? mateDbContext.PlatformTenantId;

        if (tenantId == Guid.Empty)
            throw new InvalidOperationException(
                "Tenant context is empty for multi-vault secret operation. " +
                "Authenticate with a tenant-mapped identity before storing or resolving customer secrets.");

        if (string.IsNullOrWhiteSpace(userId))
        {
            // Background/CLI path: use platform vault unless tenant-specific context is explicitly set.
            if (tenantId == mateDbContext.PlatformTenantId)
                return GetOrCreateClient(_options.PlatformVaultUri!);

            return GetOrCreateClient(await ResolveTenantVaultUriAsync(tenantId, ct));
        }

        if (tenantId == mateDbContext.PlatformTenantId &&
            claimRoles.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase))
            return GetOrCreateClient(_options.PlatformVaultUri!);

        var isSuperAdmin = await _tenantAuthorization.IsAuthorizedAsync(
            userId,
            mateDbContext.PlatformTenantId,
            "SuperAdmin",
            ct);

        if (tenantId == mateDbContext.PlatformTenantId && isSuperAdmin)
            return GetOrCreateClient(_options.PlatformVaultUri!);

        var roles = await _tenantAuthorization.GetRolesAsync(userId, tenantId, ct);
        if (roles.Count == 0 && claimRoles.Count > 0)
            roles = claimRoles;

        if (roles.Count == 0)
            throw new UnauthorizedAccessException($"User '{userId}' has no active role assignment for tenant '{tenantId}'.");

        var isTesterOnly = roles.Any(r => string.Equals(r, "Tester", StringComparison.OrdinalIgnoreCase))
                           && !roles.Any(r => string.Equals(r, "TenantAdmin", StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(r, "SuperAdmin", StringComparison.OrdinalIgnoreCase));

        if (requireWrite && isTesterOnly)
            throw new UnauthorizedAccessException("Tester role is read-only for tenant vault secrets.");

        if (tenantId == mateDbContext.PlatformTenantId)
            throw new InvalidOperationException(
                "Tenant context is platform tenant for a tenant-scoped secret operation. " +
                "Select a concrete tenant context before storing customer secrets in multi-vault mode.");

        return GetOrCreateClient(await ResolveTenantVaultUriAsync(tenantId, ct));
    }

    private async Task<string> ResolveTenantVaultUriAsync(Guid tenantId, CancellationToken ct)
    {
        var tenantToken = await ResolveTenantTokenAsync(tenantId, ct);
        return _options.TenantVaultUriTemplate!.Replace("{tenantId}", tenantToken, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveTenantTokenAsync(Guid tenantId, CancellationToken ct)
    {
        if (_tenantTokenCache.TryGetValue(tenantId, out var cached))
            return cached;

        var externalTenantId = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == tenantId && t.IsActive)
            .Select(t => t.ExternalTenantId)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(externalTenantId))
            throw new InvalidOperationException($"No active tenant mapping found for tenant '{tenantId}'.");

        string token;
        if (Guid.TryParse(externalTenantId, out var externalGuid))
        {
            token = externalGuid.ToString("N")[..8];
        }
        else
        {
            var normalized = new string(externalTenantId
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());

            if (string.IsNullOrWhiteSpace(normalized))
                throw new InvalidOperationException($"External tenant id '{externalTenantId}' cannot be converted into a valid vault token.");

            token = normalized[..Math.Min(8, normalized.Length)];
        }

        _tenantTokenCache[tenantId] = token;
        return token;
    }

    private SecretClient GetOrCreateClient(string vaultUri)
        => _clientCache.GetOrAdd(vaultUri, uri => new SecretClient(new Uri(uri), _credential));

    private static string NormalizeSecretName(string secretRef)
    {
        var normalized = new string(secretRef
            .Trim()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-')
            .ToArray());

        while (normalized.Contains("--", StringComparison.Ordinal))
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);

        normalized = normalized.Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Secret reference resolves to an empty Key Vault secret name.", nameof(secretRef));

        return normalized;
    }

    private static IReadOnlyList<string> GetUserRoles(ClaimsPrincipal? user)
    {
        if (user is null)
            return [];

        var roles = user.FindAll("roles").Select(c => c.Value)
            .Concat(user.FindAll("mate:role").Select(c => c.Value))
            .Concat(user.FindAll(ClaimTypes.Role).Select(c => c.Value))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return roles;
    }
}
