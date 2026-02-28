using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace mate.Data.Configuration;

internal sealed class JudgeSettingConfiguration : IEntityTypeConfiguration<JudgeSetting>
{
    public void Configure(EntityTypeBuilder<JudgeSetting> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.Property(e => e.ProviderType).HasMaxLength(100).IsRequired();
        b.Property(e => e.Model).HasMaxLength(200);
        b.Property(e => e.EndpointRef).HasMaxLength(300);
        b.Property(e => e.ApiKeyRef).HasMaxLength(300);
        b.HasMany(e => e.RubricSets)
            .WithOne(rs => rs.JudgeSetting)
            .HasForeignKey(rs => rs.JudgeSettingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RubricSetConfiguration : IEntityTypeConfiguration<RubricSet>
{
    public void Configure(EntityTypeBuilder<RubricSet> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.HasMany(e => e.Criteria)
            .WithOne(c => c.RubricSet)
            .HasForeignKey(c => c.RubricSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RubricCriteriaConfiguration : IEntityTypeConfiguration<RubricCriteria>
{
    public void Configure(EntityTypeBuilder<RubricCriteria> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.Property(e => e.EvaluationType).HasMaxLength(50).IsRequired();
        b.Property(e => e.Pattern).HasMaxLength(2000);
    }
}
