// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Contracts;

/// <summary>
/// Enforces subscription-based limits per tenant.
/// Throws <see cref="QuotaExceededException"/> (maps to HTTP 429) when a limit is breached.
/// </summary>
public interface IQuotaService
{
    /// <param name="currentCount">Number of agents already registered for the tenant.</param>
    Task EnforceAgentLimitAsync(Guid tenantId, int currentCount, CancellationToken ct = default);

    Task EnforceTestSuiteLimitAsync(Guid tenantId, int currentCount, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the tenant has remaining run quota for the current billing period.
    /// </summary>
    Task EnforceRunQuotaAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments the run counter on <see cref="Domain.Entities.TenantSubscription"/>.
    /// Call immediately after a run is launched.
    /// </summary>
    Task IncrementRunUsageAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Thrown when a tenant exceeds a quota limit.
/// Should be caught by the API layer and translated to HTTP 429 Too Many Requests.
/// </summary>
public sealed class QuotaExceededException : Exception
{
    public string QuotaName { get; }
    public long Limit { get; }
    public long Current { get; }

    public QuotaExceededException(string quotaName, long limit, long current)
        : base($"Quota '{quotaName}' exceeded: {current}/{limit}.")
    {
        QuotaName = quotaName;
        Limit = limit;
        Current = current;
    }

    public QuotaExceededException(string message) : base(message)
    {
        QuotaName = "unknown";
    }
}
