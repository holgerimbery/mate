// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace mate.Data.Configuration;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(e => e.ExternalTenantId).HasMaxLength(200).IsRequired();
        b.HasIndex(e => e.ExternalTenantId).IsUnique();
        b.Property(e => e.Plan).HasMaxLength(50).IsRequired();

        b.HasOne(e => e.Subscription).WithOne(s => s.Tenant)
            .HasForeignKey<TenantSubscription>(s => s.TenantId);
        // Remaining relationships use unidirectional navigation (dependent side has no back-nav)
        b.HasMany(e => e.Users).WithOne().HasForeignKey(u => u.TenantId);
        b.HasMany(e => e.Agents).WithOne().HasForeignKey(a => a.TenantId);
        b.HasMany(e => e.TestSuites).WithOne().HasForeignKey(ts => ts.TenantId);
        b.HasMany(e => e.JudgeSettings).WithOne().HasForeignKey(js => js.TenantId);
        b.HasMany(e => e.Documents).WithOne().HasForeignKey(d => d.TenantId);
        b.HasMany(e => e.ApiKeys).WithOne().HasForeignKey(k => k.TenantId);
    }
}

internal sealed class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Plan).HasMaxLength(50).IsRequired();
        b.HasIndex(e => e.TenantId).IsUnique();
    }
}

internal sealed class TenantUserConfiguration : IEntityTypeConfiguration<TenantUser>
{
    public void Configure(EntityTypeBuilder<TenantUser> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.ExternalUserId).HasMaxLength(200).IsRequired();
        b.Property(e => e.Email).HasMaxLength(320).IsRequired();
        b.Property(e => e.Role).HasMaxLength(50).IsRequired();
        b.HasIndex(e => new { e.TenantId, e.ExternalUserId }).IsUnique();
    }
}

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Prefix).HasMaxLength(12).IsRequired();
        b.Property(e => e.KeyHash).HasMaxLength(100).IsRequired();
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.Property(e => e.Role).HasMaxLength(50).IsRequired();
        b.HasIndex(e => e.Prefix);
    }
}
