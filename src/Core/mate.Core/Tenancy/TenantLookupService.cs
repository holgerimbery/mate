using mate.Data;
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace mate.Core.Tenancy;

/// <summary>
/// Looks up the internal TenantId from the external identity-provider tenant identifier.
/// Results are cached for 5 minutes to avoid per-request database calls.
/// </summary>
public sealed class TenantLookupService
{
    private readonly mateDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantLookupService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "tenant:ext:";

    public TenantLookupService(mateDbContext db, IMemoryCache cache, ILogger<TenantLookupService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Returns the internal Guid for the <paramref name="externalTenantId"/>.
    /// Returns null if the tenant is not registered or is inactive.
    /// </summary>
    public async Task<Guid?> LookupByExternalIdAsync(string externalTenantId, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyPrefix + externalTenantId;

        if (_cache.TryGetValue(cacheKey, out Guid cached))
            return cached;

        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.ExternalTenantId == externalTenantId && t.IsActive)
            .Select(t => new { t.Id })
            .FirstOrDefaultAsync(ct);

        if (tenant is null)
        {
            _logger.LogWarning("Tenant lookup failed for external tenant ID '{ExternalTenantId}'.", externalTenantId);
            return null;
        }

        _cache.Set(cacheKey, tenant.Id, CacheDuration);
        return tenant.Id;
    }

    /// <summary>Loads the full tenant record. Bypasses the query filter (used for admin operations).</summary>
    public async Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
    }
}
