// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace mate.Data;

/// <summary>
/// Application DbContext with multi-tenant row-level isolation enforced via
/// EF Core Global Query Filters.  Every "data" entity carries a TenantId;
/// the active filter value is injected through <see cref="ITenantContext"/>.
/// Pass <c>null</c> for migrations, platform-admin tooling, and CLI seeding.
/// </summary>
public sealed class mateDbContext : DbContext
{
    // Platform-level records use this fixed TenantId. Must be non-zero so EF Core's
    // ValueGeneratedOnAdd sentinel (Guid.Empty) does not replace it during seeding.
    public static readonly Guid PlatformTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private readonly ITenantContext? _tenantContext;

    public mateDbContext(DbContextOptions<mateDbContext> options, ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // ── Tenant / identity ────────────────────────────────────────────────────
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    // ── Agent configuration ──────────────────────────────────────────────────
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentConnectorConfig> AgentConnectorConfigs => Set<AgentConnectorConfig>();

    // ── Test definition ──────────────────────────────────────────────────────
    public DbSet<TestSuite> TestSuites => Set<TestSuite>();
    public DbSet<TestSuiteAgent> TestSuiteAgents => Set<TestSuiteAgent>();
    public DbSet<TestCase> TestCases => Set<TestCase>();

    // ── Execution ────────────────────────────────────────────────────────────
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<Result> Results => Set<Result>();
    public DbSet<TranscriptMessage> TranscriptMessages => Set<TranscriptMessage>();

    // ── Evaluation ───────────────────────────────────────────────────────────
    public DbSet<JudgeSetting> JudgeSettings => Set<JudgeSetting>();
    public DbSet<RubricSet> RubricSets => Set<RubricSet>();
    public DbSet<RubricCriteria> RubricCriterias => Set<RubricCriteria>();

    // ── Document processing ──────────────────────────────────────────────────
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Chunk> Chunks => Set<Chunk>();

    // ── Platform ─────────────────────────────────────────────────────────────
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<GlobalQuestionGenerationSetting> GlobalQuestionGenerationSettings => Set<GlobalQuestionGenerationSetting>();
    public DbSet<AppSecret> AppSecrets => Set<AppSecret>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> classes in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(mateDbContext).Assembly);

        // ── Global query filters (multi-tenant isolation) ─────────────────
        // Reference _tenantContext as a field (not a captured local variable) so EF Core
        // evaluates it per-instance at runtime. Capturing `var tid = _tenantContext.TenantId`
        // would bake in the value from the first DbContext instance (usually Guid.Empty from
        // the migration/seeder scope) across all subsequent instances since EF Core caches
        // the compiled model.
        modelBuilder.Entity<Agent>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<AgentConnectorConfig>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<TestSuite>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<TestSuiteAgent>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<TestCase>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Run>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Result>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<TranscriptMessage>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<JudgeSetting>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId || e.TenantId == PlatformTenantId);
        modelBuilder.Entity<RubricSet>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId || e.TenantId == PlatformTenantId);
        modelBuilder.Entity<AppSecret>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId || e.TenantId == PlatformTenantId);
        modelBuilder.Entity<Document>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<Chunk>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<ApiKey>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<TenantUser>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        modelBuilder.Entity<TenantSubscription>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
    }
}
