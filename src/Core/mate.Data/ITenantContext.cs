// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Data;

/// <summary>
/// Provides the current tenant identity to the DbContext and services.
/// Implementations:
///   - <see cref="HttpTenantContext"/>  — resolved from JWT claim in web requests
///   - <see cref="StaticTenantContext"/> — injected by background worker / CLI
///   - null / not registered           — platform-admin / migration path (no filter)
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
}

/// <summary>A fixed tenant context for background workers and CLI tools.</summary>
public sealed class StaticTenantContext : ITenantContext
{
    private Guid _tenantId;

    public StaticTenantContext(Guid tenantId) => _tenantId = tenantId;

    public Guid TenantId => _tenantId;

    /// <summary>Mutates the tenant ID for the current scope (worker use only).</summary>
    public void SetTenantId(Guid tenantId) => _tenantId = tenantId;
}
