using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace mate.Data.Configuration;

/// <summary>
/// EF Core configuration for TenantRoleAssignment entity.
/// 
/// This configuration is enterprise-only (RedmondMode=true).
/// In core mode (RedmondMode=false), the table will not be used,
/// but it is safe to leave in the schema as it has no impact on core operations.
/// </summary>
internal sealed class TenantRoleAssignmentConfiguration : IEntityTypeConfiguration<TenantRoleAssignment>
{
    public void Configure(EntityTypeBuilder<TenantRoleAssignment> b)
    {
        b.HasKey(e => e.Id);

        b.Property(e => e.UserId)
            .HasMaxLength(256)
            .IsRequired();

        b.Property(e => e.TenantId)
            .IsRequired();

        b.Property(e => e.Role)
            .HasMaxLength(50)
            .IsRequired();

        b.Property(e => e.IsActive)
            .HasDefaultValue(true);

        b.Property(e => e.AssignedAt)
            .IsRequired();

        b.Property(e => e.CreatedBy)
            .HasMaxLength(256)
            .IsRequired();

        b.Property(e => e.ModifiedAt)
            .IsRequired();

        // Composite index: (TenantId, UserId) for role lookup queries
        b.HasIndex(e => new { e.TenantId, e.UserId })
            .HasDatabaseName("IX_TenantRoleAssignments_TenantId_UserId");

        // Index on IsActive for filtering active assignments
        b.HasIndex(e => e.IsActive)
            .HasDatabaseName("IX_TenantRoleAssignments_IsActive");

        // Composite index: (TenantId, IsActive) for efficient active role lookups
        b.HasIndex(e => new { e.TenantId, e.IsActive })
            .HasDatabaseName("IX_TenantRoleAssignments_TenantId_IsActive");

        b.ToTable("TenantRoleAssignments");
    }
}
