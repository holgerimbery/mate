// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Data;
using mate.Domain.Contracts;
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace mate.Core.Tenancy;

/// <summary>
/// Enforces subscription quotas for a tenant.
/// All limit values are read from the tenant's active <see cref="TenantSubscription"/>.
/// Throws <see cref="QuotaExceededException"/> (HTTP 429) when a limit is breached.
/// </summary>
public sealed class QuotaService : IQuotaService
{
    private readonly mateDbContext _db;
    private readonly ILogger<QuotaService> _logger;

    public QuotaService(mateDbContext db, ILogger<QuotaService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnforceAgentLimitAsync(Guid tenantId, int currentCount, CancellationToken ct = default)
    {
        var sub = await GetSubscriptionAsync(tenantId, ct);
        if (sub is null) return;

        if (currentCount >= sub.MaxAgents)
        {
            _logger.LogWarning("Tenant {TenantId} exceeded agent limit {Limit}.", tenantId, sub.MaxAgents);
            throw new QuotaExceededException("MaxAgents", sub.MaxAgents, currentCount);
        }
    }

    /// <inheritdoc />
    public async Task EnforceTestSuiteLimitAsync(Guid tenantId, int currentCount, CancellationToken ct = default)
    {
        var sub = await GetSubscriptionAsync(tenantId, ct);
        if (sub is null) return;

        if (currentCount >= sub.MaxTestSuites)
        {
            _logger.LogWarning("Tenant {TenantId} exceeded test suite limit {Limit}.", tenantId, sub.MaxTestSuites);
            throw new QuotaExceededException("MaxTestSuites", sub.MaxTestSuites, currentCount);
        }
    }

    /// <inheritdoc />
    public async Task EnforceRunQuotaAsync(Guid tenantId, CancellationToken ct = default)
    {
        var sub = await GetSubscriptionAsync(tenantId, ct);
        if (sub is null) return;

        if (sub.MonthlyRunsUsed >= sub.MaxMonthlyRuns)
        {
            _logger.LogWarning(
                "Tenant {TenantId} exceeded monthly run quota {Limit}/{Used}.",
                tenantId, sub.MaxMonthlyRuns, sub.MonthlyRunsUsed);
            throw new QuotaExceededException("MaxMonthlyRuns", sub.MaxMonthlyRuns, sub.MonthlyRunsUsed);
        }
    }

    /// <inheritdoc />
    public async Task IncrementRunUsageAsync(Guid tenantId, CancellationToken ct = default)
    {
        var sub = await GetSubscriptionAsync(tenantId, ct);
        if (sub is null) return;

        sub.MonthlyRunsUsed++;
        await _db.SaveChangesAsync(ct);
        _logger.LogDebug("Run usage incremented for tenant {TenantId}: {Used}/{Max}.",
            tenantId, sub.MonthlyRunsUsed, sub.MaxMonthlyRuns);
    }

    private async Task<TenantSubscription?> GetSubscriptionAsync(Guid tenantId, CancellationToken ct)
    {
        return await _db.TenantSubscriptions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);
    }
}
