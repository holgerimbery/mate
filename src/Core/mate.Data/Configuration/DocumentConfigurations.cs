using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace mate.Data.Configuration;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.FileName).HasMaxLength(500).IsRequired();
        b.Property(e => e.ContentType).HasMaxLength(200).IsRequired();
        b.Property(e => e.BlobContainer).HasMaxLength(200).IsRequired();
        b.Property(e => e.BlobName).HasMaxLength(1000).IsRequired();
        b.Property(e => e.ContentHash).HasMaxLength(100);
        b.HasMany(e => e.Chunks)
            .WithOne(c => c.Document)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChunkConfiguration : IEntityTypeConfiguration<Chunk>
{
    public void Configure(EntityTypeBuilder<Chunk> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Category).HasMaxLength(200);
        // StartChapter/EndChapter are stored as double (section position)
        // Embedding stored as binary blob; null until a vector module populates it
        b.HasIndex(e => new { e.DocumentId, e.ChunkIndex });
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Action).HasMaxLength(100).IsRequired();
        b.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
        b.Property(e => e.UserId).HasMaxLength(200);
        b.HasIndex(e => new { e.TenantId, e.OccurredAt });
        // AuditLogs are immutable — prevent EF from generating update migrations
        b.ToTable("AuditLogs");
    }
}

internal sealed class GlobalQuestionGenerationSettingConfiguration
    : IEntityTypeConfiguration<GlobalQuestionGenerationSetting>
{
    public void Configure(EntityTypeBuilder<GlobalQuestionGenerationSetting> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Model).HasMaxLength(200).IsRequired();
        b.Property(e => e.Endpoint).HasMaxLength(300);
        b.Property(e => e.ApiKeyRef).HasMaxLength(300);
        b.HasIndex(e => e.TenantId).IsUnique(); // one setting per tenant
    }
}
