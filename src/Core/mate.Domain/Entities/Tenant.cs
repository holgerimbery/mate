namespace mate.Domain.Entities;

/// <summary>
/// A tenant represents one customer organisation using the platform.
/// Every data entity in the system carries TenantId — isolation is enforced
/// at the EF layer via global query filters so no query ever leaks across tenants.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }

    /// <summary>External identity-provider tenant identifier (e.g. the 'tid' JWT claim for Entra ID).</summary>
    public string ExternalTenantId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>trial | starter | professional | enterprise</summary>
    public string Plan { get; set; } = "trial";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public TenantSubscription? Subscription { get; set; }
    public ICollection<TenantUser> Users { get; set; } = [];
    public ICollection<Agent> Agents { get; set; } = [];
    public ICollection<TestSuite> TestSuites { get; set; } = [];
    public ICollection<JudgeSetting> JudgeSettings { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];
    public ICollection<ApiKey> ApiKeys { get; set; } = [];
}
