namespace mate.Domain.Entities;

/// <summary>
/// Quota limits and usage counters for a tenant.
/// Quota enforcement is checked atomically before each run is enqueued
/// (see IQuotaService) to prevent bypass via concurrent requests.
/// </summary>
public class TenantSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>trial | starter | professional | enterprise</summary>
    public string Plan { get; set; } = "trial";

    public int MaxAgents { get; set; } = 5;
    public int MaxTestSuites { get; set; } = 20;
    public int MaxTestCasesPerSuite { get; set; } = 100;
    public int MaxMonthlyRuns { get; set; } = 500;

    /// <summary>Counter reset at PeriodEnd. Incremented atomically by IQuotaService.</summary>
    public int MonthlyRunsUsed { get; set; }

    public DateTime PeriodStart { get; set; } = DateTime.UtcNow;
    public DateTime PeriodEnd { get; set; } = DateTime.UtcNow.AddMonths(1);

    /// <summary>Optional external subscription reference (Stripe / Azure Marketplace).</summary>
    public string? ExternalSubscriptionId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
