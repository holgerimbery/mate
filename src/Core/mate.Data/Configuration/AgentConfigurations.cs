using System.Text.Json;
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace mate.Data.Configuration;

internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Agent> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasMaxLength(1000);
        b.Property(e => e.Environment).HasMaxLength(50);

        // Tags stored as JSON text column
        b.Property(e => e.Tags)
            .HasConversion(
                v => JsonSerializer.Serialize(v, _json),
                v => JsonSerializer.Deserialize<string[]>(v, _json) ?? Array.Empty<string>())
            .HasColumnName("TagsJson");

        b.HasMany(e => e.ConnectorConfigs)
            .WithOne(c => c.Agent)
            .HasForeignKey(c => c.AgentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AgentConnectorConfigConfiguration : IEntityTypeConfiguration<AgentConnectorConfig>
{
    public void Configure(EntityTypeBuilder<AgentConnectorConfig> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.ConnectorType).HasMaxLength(100).IsRequired();
        // ConfigJson is serialized JSON — no max length restriction because
        // values depend on the connector type and will vary widely.
        b.Property(e => e.ConfigJson).IsRequired();
    }
}
