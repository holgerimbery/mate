// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Contracts;

/// <summary>
/// Resolves platform-level role assignments for a user within a specific tenant.
/// In standard deployments the underlying table is empty and all methods return safe defaults.
/// </summary>
public interface ITenantAuthorizationService
{
    /// <summary>
    /// Returns true when the <paramref name="userId"/> holds the <paramref name="requiredRole"/>
    /// (or a higher-privilege role) within <paramref name="tenantId"/> and the assignment is active.
    /// Returns false when no matching active assignment exists.
    /// </summary>
    Task<bool> IsAuthorizedAsync(string userId, Guid tenantId, string requiredRole, CancellationToken ct = default);

    /// <summary>
    /// Returns all active roles assigned to <paramref name="userId"/> within <paramref name="tenantId"/>.
    /// Returns an empty list when no active assignments exist.
    /// </summary>
    Task<IReadOnlyList<string>> GetRolesAsync(string userId, Guid tenantId, CancellationToken ct = default);
}
