namespace mate.Domain.Entities;

/// <summary>
/// A tenant-scoped named secret stored in the database.
/// Used by <c>DatabaseSecretService</c> as the primary secret store for local deployments.
/// Phase 2 deployments may replace or augment this with Azure Key Vault.
/// </summary>
public class AppSecret
{
    public Guid Id { get; set; }

    /// <summary>The tenant that owns this secret. Guid.Empty = platform-level (accessible by all tenants).</summary>
    public Guid TenantId { get; set; }

    /// <summary>The reference name used in entity config fields (e.g. "qgen_abc123_key").</summary>
    public string RefName { get; set; } = string.Empty;

    /// <summary>The secret value. In Phase 2 this would be encrypted at rest.</summary>
    public string Value { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
