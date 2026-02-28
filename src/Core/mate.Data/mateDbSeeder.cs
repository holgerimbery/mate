using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace mate.Data;

/// <summary>
/// Seeds essential platform-level data on first startup.
/// All seed data is idempotent — running this multiple times is safe.
/// </summary>
public sealed class mateDbSeeder
{
    private readonly mateDbContext _db;
    private readonly ILogger<mateDbSeeder> _logger;

    public mateDbSeeder(mateDbContext db, ILogger<mateDbSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Fixed platform tenant — must be non-zero so EF Core's ValueGeneratedOnAdd sentinel
    // (Guid.Empty) does not replace it with a generated Guid before the INSERT.
    private static readonly Guid PlatformTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Deterministic dev tenant — mirrors DevelopmentAuthHandler.DevTenantId
    private static readonly Guid DevTenantId = Guid.Parse("00000000-0000-0000-0000-000000000099");

    /// <summary>Run all seed operations in order.</summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting database seed...");
        await SeedPlatformTenantAsync(ct);
        await SeedDevTenantAsync(ct);
        await SeedDefaultJudgeSettingAsync(ct);
        _logger.LogInformation("Database seed completed.");
    }

    /// <summary>
    /// Ensures the platform pseudo-tenant (Id = Guid.Empty) exists.
    /// Platform-level records (e.g. default JudgeSetting) reference this tenant to satisfy the FK.
    /// </summary>
    private async Task SeedPlatformTenantAsync(CancellationToken ct)
    {
        var exists = await _db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.ExternalTenantId == "platform", ct);

        if (exists) return;

        _db.Tenants.Add(new Tenant
        {
            Id               = PlatformTenantId,
            ExternalTenantId = "platform",
            DisplayName      = "Platform",
            Plan             = "enterprise",
            IsActive         = true,
            CreatedAt        = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded platform tenant.");
    }

    /// <summary>
    /// Ensures the deterministic local-development tenant exists.
    /// Mirrors the DevTenantId used by the Generic (dev) auth handler.
    /// </summary>
    private async Task SeedDevTenantAsync(CancellationToken ct)
    {
        var exists = await _db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.ExternalTenantId == DevTenantId.ToString(), ct);

        if (exists) return;

        _db.Tenants.Add(new Tenant
        {
            Id               = DevTenantId,
            ExternalTenantId = DevTenantId.ToString(),
            DisplayName      = "Development",
            Plan             = "enterprise",
            IsActive         = true,
            CreatedAt        = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded development tenant.");
    }

    /// <summary>
    /// Ensures there is a platform-level "Default ModelAsJudge" judge setting
    /// that tenant-created suites/agents can reference by convention (TenantId = Guid.Empty).
    /// </summary>
    private async Task SeedDefaultJudgeSettingAsync(CancellationToken ct)
    {
        const string defaultName = "Default ModelAsJudge";

        var exists = await _db.JudgeSettings
            .IgnoreQueryFilters()
            .AnyAsync(j => j.Name == defaultName && j.TenantId == PlatformTenantId, ct);

        if (exists)
            return;

        _db.JudgeSettings.Add(new JudgeSetting
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            TenantId = PlatformTenantId, // Platform-level — accessible by all tenants
            Name = defaultName,
            ProviderType = "ModelAsJudge",
            TaskSuccessWeight = 0.30,
            IntentMatchWeight = 0.20,
            FactualityWeight  = 0.20,
            HelpfulnessWeight = 0.15,
            SafetyWeight      = 0.15,
            PassThreshold = 0.70,
            Temperature = 0.2,
            TopP = 0.9,
            MaxOutputTokens = 800,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded default JudgeSetting '{Name}'.", defaultName);
    }
}
