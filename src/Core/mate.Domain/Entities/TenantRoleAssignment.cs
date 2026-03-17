namespace mate.Domain.Entities;

/// <summary>
/// Represents a role assignment for a user within a tenant.
/// Used exclusively in RedmondMode (enterprise multi-tenant) deployments.
/// 
/// Roles:
/// - SuperAdmin: Platform-scoped, manages all tenant vaults and platform configuration
/// - TenantAdmin: Tenant-scoped, manages resources within an assigned tenant
/// - Tester: Tenant-scoped read-only, can read secrets from assigned tenant vault
/// </summary>
public sealed class TenantRoleAssignment
{
    /// <summary>
    /// Unique identifier for the role assignment.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The ID of the user being assigned (from EntraId or identity provider).
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The tenant this assignment is scoped to.
    /// For SuperAdmin, this is the platform tenant.
    /// For TenantAdmin/Tester, this is the target tenant the user administers/tests.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The role assigned: SuperAdmin, TenantAdmin, or Tester.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Whether this assignment is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When the assignment was first created.
    /// </summary>
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The user ID of who created this assignment (audit trail).
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// When this record was last modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}
