// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Text.Json;
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace mate.Data.Configuration;

internal sealed class TestSuiteConfiguration : IEntityTypeConfiguration<TestSuite>
{
    public void Configure(EntityTypeBuilder<TestSuite> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(200).IsRequired();
        b.Property(e => e.Description).HasMaxLength(1000);
        b.HasMany(e => e.TestSuiteAgents)
            .WithOne(sa => sa.TestSuite)
            .HasForeignKey(sa => sa.TestSuiteId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasMany(e => e.TestCases)
            .WithOne(tc => tc.Suite)
            .HasForeignKey(tc => tc.SuiteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TestSuiteAgentConfiguration : IEntityTypeConfiguration<TestSuiteAgent>
{
    public void Configure(EntityTypeBuilder<TestSuiteAgent> b)
    {
        b.HasKey(e => new { e.TestSuiteId, e.AgentId });
    }
}

internal sealed class TestCaseConfiguration : IEntityTypeConfiguration<TestCase>
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<TestCase> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(400).IsRequired();
        b.Property(e => e.AcceptanceCriteria).IsRequired();

        // string[] stored as JSON text column for multi-turn support
        b.Property(e => e.UserInput)
            .HasConversion(
                v => JsonSerializer.Serialize(v, _json),
                v => JsonSerializer.Deserialize<string[]>(v, _json) ?? Array.Empty<string>())
            .HasColumnName("UserInputJson")
            .IsRequired();

        b.Property(e => e.ExpectedEntities)
            .HasConversion(
                v => JsonSerializer.Serialize(v, _json),
                v => JsonSerializer.Deserialize<string[]>(v, _json) ?? Array.Empty<string>())
            .HasColumnName("ExpectedEntitiesJson");

        b.Property(e => e.Tags)
            .HasConversion(
                v => JsonSerializer.Serialize(v, _json),
                v => JsonSerializer.Deserialize<string[]>(v, _json) ?? Array.Empty<string>())
            .HasColumnName("TagsJson")
            .HasDefaultValue(Array.Empty<string>());
    }
}
