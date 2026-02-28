using System.Text.Json;
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace mate.Data.Configuration;

internal sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Status).HasMaxLength(30).IsRequired();
        b.Property(e => e.RequestedBy).HasMaxLength(300);
        b.HasMany(e => e.Results)
            .WithOne(r => r.Run)
            .HasForeignKey(r => r.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ResultConfiguration : IEntityTypeConfiguration<Result>
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Result> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Verdict).HasMaxLength(20).IsRequired();
        b.Property(e => e.HumanVerdict).HasMaxLength(20);

        b.Property(e => e.Citations)
            .HasConversion(
                v => JsonSerializer.Serialize(v, _json),
                v => JsonSerializer.Deserialize<string[]>(v, _json) ?? Array.Empty<string>())
            .HasColumnName("CitationsJson");

        // Nav prop is Transcript (ICollection<TranscriptMessage>)
        b.HasMany(e => e.Transcript)
            .WithOne(tm => tm.Result)
            .HasForeignKey(tm => tm.ResultId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TranscriptMessageConfiguration : IEntityTypeConfiguration<TranscriptMessage>
{
    public void Configure(EntityTypeBuilder<TranscriptMessage> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Role).HasMaxLength(20).IsRequired();
        b.HasIndex(e => new { e.ResultId, e.TurnIndex });
    }
}
