// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Data;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace mate.Core.Tenancy;

/// <summary>
/// Resolves the current tenant from the authenticated HTTP request.
/// Reads the 'tid' claim injected by Entra ID and maps it to the internal TenantId
/// stored in the database by looking up ExternalTenantId on the Tenant entity.
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    private readonly Guid _tenantId;

    /// <param name="tenantId">Resolved internal TenantId Guid.</param>
    public HttpTenantContext(Guid tenantId)
    {
        _tenantId = tenantId;
    }

    public Guid TenantId => _tenantId;
}

/// <summary>
/// Middleware-style service that resolves the tenant from the HTTP context
/// and registers an <see cref="HttpTenantContext"/> as a scoped service.
/// Register this as a background resolution service in the DI pipeline.
/// </summary>
public sealed class TenantResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TenantLookupService _lookup;

    public TenantResolver(IHttpContextAccessor httpContextAccessor, TenantLookupService lookup)
    {
        _httpContextAccessor = httpContextAccessor;
        _lookup = lookup;
    }

    /// <summary>
    /// Resolves the current tenant from the active HTTP context.
    /// Returns null when called outside an HTTP request (e.g. background worker).
    /// </summary>
    public async Task<Guid?> ResolveAsync(CancellationToken ct = default)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null || !user.Identity?.IsAuthenticated == true)
            return null;

        // 'tid' claim is the external tenant identifier in Entra ID tokens
        var externalTenantId = user.FindFirstValue("tid")
                               ?? user.FindFirstValue(ClaimTypes.GroupSid);

        if (string.IsNullOrWhiteSpace(externalTenantId))
            return null;

        return await _lookup.LookupByExternalIdAsync(externalTenantId, ct);
    }
}
