// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Data;
using mate.Domain.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace mate.Core.Tenancy;

/// <summary>
/// Resolves platform-level role assignments stored in <c>TenantRoleAssignments</c>.
/// Constructs <see cref="mateDbContext"/> directly with a null tenant context to bypass the
/// global query filter — the same pattern used by <see cref="TenantLookupService"/>.
/// In standard deployments the table is empty and all methods return safe defaults,
/// making this service a no-op outside of enterprise mode.
/// </summary>
public sealed class TenantAuthorizationService : ITenantAuthorizationService
{
    private readonly DbContextOptions<mateDbContext> _dbOptions;
    private readonly ILogger<TenantAuthorizationService> _logger;

    public TenantAuthorizationService(
        DbContextOptions<mateDbContext> dbOptions,
        ILogger<TenantAuthorizationService> logger)
    {
        _dbOptions = dbOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsAuthorizedAsync(
        string userId,
        Guid tenantId,
        string requiredRole,
        CancellationToken ct = default)
    {
        var roles = await GetRolesAsync(userId, tenantId, ct);
        return roles.Contains(requiredRole, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRolesAsync(
        string userId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogDebug("GetRolesAsync called with empty userId — returning empty list.");
            return [];
        }

        // Bypass the global tenant query filter so we can query across tenants
        // (platform-level lookup). Matches the null-tenantContext pattern in TenantLookupService.
        using var db = new mateDbContext(_dbOptions, tenantContext: null);

        var roles = await db.TenantRoleAssignments
            .IgnoreQueryFilters()
            .Where(r => r.UserId == userId && r.TenantId == tenantId && r.IsActive)
            .Select(r => r.Role)
            .ToListAsync(ct);

        if (roles.Count > 0)
            _logger.LogDebug(
                "User {UserId} has {Count} active role(s) in tenant {TenantId}.",
                userId, roles.Count, tenantId);

        return roles;
    }
}
