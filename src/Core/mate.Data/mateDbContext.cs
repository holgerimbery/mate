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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> classes in this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(mateDbContext).Assembly);

        // ── Global query filters (multi-tenant isolation) ─────────────────
        // The guard `_tenantContext != null` ensures filters are skipped during
        // migrations and platform-admin access where no tenant context is set.
        if (_tenantContext != null)
        {
            var tid = _tenantContext.TenantId;

            modelBuilder.Entity<Agent>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<AgentConnectorConfig>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<TestSuite>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<TestSuiteAgent>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<TestCase>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<Run>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<Result>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<TranscriptMessage>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<JudgeSetting>().HasQueryFilter(e => e.TenantId == tid || e.TenantId == PlatformTenantId);
            modelBuilder.Entity<RubricSet>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<Document>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<Chunk>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<ApiKey>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<TenantUser>().HasQueryFilter(e => e.TenantId == tid);
            modelBuilder.Entity<TenantSubscription>().HasQueryFilter(e => e.TenantId == tid);
        }
    }
}
