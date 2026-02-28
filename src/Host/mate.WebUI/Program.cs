using System.Security.Claims;
using System.Security.Cryptography;
using mate.Core;
using mate.Core.DocumentProcessing;
using mate.Core.Execution;
using mate.Core.Tenancy;
using mate.Data;
using mate.Domain.Contracts.Infrastructure;
using mate.Domain.Contracts.Modules;
using mate.Domain.Contracts.Monitoring;
using mate.Domain.Entities;
using mate.Infrastructure.Local;
using mate.Modules.AgentConnector.CopilotStudio;
using mate.Modules.AgentConnector.Generic;
using mate.Modules.Auth.EntraId;
using mate.Modules.Auth.Generic;
using mate.Modules.Monitoring.ApplicationInsights;
using mate.Modules.Monitoring.Generic;
using mate.Modules.Testing.Generic;
using mate.Modules.Testing.CopilotStudioJudge;
using mate.Modules.Testing.HybridJudge;
using mate.Modules.Testing.ModelAsJudge;
using mate.Modules.Testing.RubricsJudge;
using mate.WebUI.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;
using Serilog;

// ── Bootstrap Serilog early ──────────────────────────────────────────────────

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .WriteTo.Console()
           .WriteTo.File(
               path: "logs/webui-.log",
               rollingInterval: RollingInterval.Day,
               retainedFileCountLimit: 30);
    });

    var config = builder.Configuration;

    // ── Data (EF Core + migrations) ──────────────────────────────────────────
    builder.Services.AddmateSqlite(config);
    builder.Services.AddMemoryCache();

    // ── Tenant resolution ───────────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    // HttpTenantContext requires a resolved Guid — use a factory so DI can construct it.
    // The factory reads the 'mate:externalTenantId' claim (Generic/dev auth) or 'tid' (Entra ID).
    // Falls back to StaticTenantContext(Guid.Empty) when no HTTP context is present (seeder, startup).
    builder.Services.AddScoped<ITenantContext>(sp =>
    {
        var user = sp.GetService<IHttpContextAccessor>()?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var externalId = user.FindFirstValue("mate:externalTenantId")
                          ?? user.FindFirstValue("tid");
            if (Guid.TryParse(externalId, out var tenantId))
                return new HttpTenantContext(tenantId);
        }
        return new StaticTenantContext(Guid.Empty);
    });

    // ── Infrastructure ───────────────────────────────────────────────────────
    builder.Services.AddmateLocalInfrastructure(config);

    // ── Core services ────────────────────────────────────────────────────────
    builder.Services.AddmateCore();

    // ── Agent connector modules ──────────────────────────────────────────────
    builder.Services.AddmateCopilotStudioModule(config);
    builder.Services.AddmateGenericAgentConnector();

    // ── Judge / testing modules ──────────────────────────────────────────────
    builder.Services.AddmateGenericTestingModule();
    builder.Services.AddmateModelAsJudgeModule(config);
    builder.Services.AddmateRubricsJudgeModule();
    builder.Services.AddmateHybridJudgeModule(config);
    builder.Services.AddmateCopilotStudioJudgeModule(config);

    // ── Authentication ───────────────────────────────────────────────────────
    var authScheme = config["Authentication:Scheme"] ?? "EntraId";
    var authBuilder = builder.Services.AddAuthentication(authScheme);

    IAuthModule authModule = authScheme switch
    {
        "Generic" => new mate.Modules.Auth.Generic.GenericAuthModule(),
        "EntraId" or _ => new mate.Modules.Auth.EntraId.EntraIdAuthModule(),
    };

    authModule.ConfigureAuthentication(authBuilder, config);

    builder.Services.AddAuthorization(options =>
        authModule.ConfigureAuthorization(options, config));

    builder.Services.AddSingleton<IAuthModule>(authModule);

    // ── Monitoring ───────────────────────────────────────────────────────────
    var monitoringProvider = config["Monitoring:Provider"] ?? "Generic";
    if (monitoringProvider == "ApplicationInsights")
        builder.Services.AddmateApplicationInsightsMonitoring(config);
    else
        builder.Services.AddmateGenericMonitoring();

    // ── Blazor Server ────────────────────────────────────────────────────────
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddCascadingAuthenticationState();

    // ── HTTP client defaults ─────────────────────────────────────────────────
    builder.Services.AddHttpClient();

    // ── OpenAPI ──────────────────────────────────────────────────────────────
    builder.Services.AddOpenApi();

    // ── Health checks ────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ── Build app ────────────────────────────────────────────────────────────
    var app = builder.Build();

    // Apply EF migrations + seed on startup (non-blocking, runs before first request)
    await app.Services.ApplyMigrationsAsync(seed: true);

    // ── Populate module registry from DI ─────────────────────────────────────
    // Modules self-register into DI via their Add*Module() extensions.
    // Here we resolve all implementations and push them into the singleton
    // mateModuleRegistry so that Blazor pages and the execution engine can
    // enumerate available modules at runtime.
    {
        using var scope = app.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<mateModuleRegistry>();
        foreach (var m in scope.ServiceProvider.GetServices<IAgentConnectorModule>())
            registry.RegisterConnector(m);
        foreach (var m in scope.ServiceProvider.GetServices<ITestingModule>())
            registry.RegisterTestingModule(m);
        foreach (var m in scope.ServiceProvider.GetServices<IJudgeProvider>())
            registry.RegisterJudgeProvider(m);
        foreach (var m in scope.ServiceProvider.GetServices<IMonitoringModule>())
            registry.RegisterMonitoring(m);
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    // ── API key authentication middleware ────────────────────────────────────
    // Runs after standard auth so bearer/cookie sessions continue to work unchanged.
    app.Use(async (ctx, next) =>
    {
        if (!(ctx.User.Identity?.IsAuthenticated ?? false))
        {
            if (ctx.Request.Headers.TryGetValue("X-Api-Key", out var rawKey))
            {
                var hash = Convert.ToHexString(
                    SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey.ToString())))
                    .ToLowerInvariant();
                using var scope = ctx.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<mateDbContext>();
                var apiKey = await db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive);
                if (apiKey is not null)
                {
                    apiKey.LastUsedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.Name, apiKey.Name),
                        new Claim(ClaimTypes.Role, apiKey.Role),
                        new Claim("api_key_id", apiKey.Id.ToString())
                    };
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
                }
            }
        }
        await next();
    });

    app.UseAntiforgery();

    // ── Platform endpoints ───────────────────────────────────────────────────
    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options =>
    {
        options.Title = "mate — AI Agent Quality Testing Platform";
        options.Theme = ScalarTheme.Default;
    }).AllowAnonymous();

    // ── REST API ─────────────────────────────────────────────────────────────
    var api = app.MapGroup("/api");

    // Agents
    api.MapGet("/agents", async (mateDbContext db) =>
    {
        var agents = await db.Agents
            .Include(a => a.ConnectorConfigs)
            .Include(a => a.JudgeSetting)
            .OrderBy(a => a.Name)
            .Select(a => new
            {
                a.Id, a.Name, a.Description, a.Environment, a.Tags, a.IsActive,
                a.JudgeSettingId, a.CreatedAt, a.UpdatedAt,
                ConnectorConfigs = a.ConnectorConfigs.Select(c => new
                {
                    c.Id, c.ConnectorType, c.IsActive
                })
            })
            .ToListAsync();
        return Results.Ok(agents);
    }).WithName("ListAgents").WithTags("Agents");

    api.MapGet("/agents/{id:guid}", async (Guid id, mateDbContext db) =>
    {
        var agent = await db.Agents
            .Include(a => a.ConnectorConfigs)
            .Include(a => a.JudgeSetting)
            .FirstOrDefaultAsync(a => a.Id == id);
        return agent is null ? Results.NotFound() : Results.Ok(agent);
    }).WithName("GetAgent").WithTags("Agents");

    api.MapPost("/agents", async (CreateAgentRequest req, mateDbContext db, ITenantContext tenant, ClaimsPrincipal user) =>
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = req.Name,
            Description = req.Description,
            Environment = req.Environment,
            Tags = req.Tags,
            JudgeSettingId = req.JudgeSettingId,
            CreatedBy = user.Identity?.Name ?? "api",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        if (!string.IsNullOrWhiteSpace(req.ConnectorType))
        {
            agent.ConnectorConfigs.Add(new AgentConnectorConfig
            {
                Id = Guid.NewGuid(),
                AgentId = agent.Id,
                TenantId = tenant.TenantId,
                ConnectorType = req.ConnectorType,
                ConfigJson = req.ConfigJson,
                IsActive = true
            });
        }
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return Results.Created($"/api/agents/{agent.Id}", new { agent.Id });
    }).WithName("CreateAgent").WithTags("Agents");

    api.MapPut("/agents/{id:guid}", async (Guid id, UpdateAgentRequest req, mateDbContext db) =>
    {
        var agent = await db.Agents.FindAsync(id);
        if (agent is null) return Results.NotFound();
        agent.Name = req.Name;
        agent.Description = req.Description;
        agent.Environment = req.Environment;
        agent.Tags = req.Tags;
        agent.IsActive = req.IsActive;
        agent.JudgeSettingId = req.JudgeSettingId;
        agent.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new { agent.Id });
    }).WithName("UpdateAgent").WithTags("Agents");

    api.MapDelete("/agents/{id:guid}", async (Guid id, mateDbContext db) =>
    {
        var agent = await db.Agents.FindAsync(id);
        if (agent is null) return Results.NotFound();
        db.Agents.Remove(agent);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }).WithName("DeleteAgent").WithTags("Agents");

    api.MapPut("/agents/{agentId:guid}/connectors/{connectorId:guid}", async (
        Guid agentId, Guid connectorId, UpdateConnectorConfigRequest req, mateDbContext db) =>
    {
        var cfg = await db.AgentConnectorConfigs.FindAsync(connectorId);
        if (cfg is null || cfg.AgentId != agentId) return Results.NotFound();
        cfg.ConnectorType = req.ConnectorType;
        cfg.ConfigJson = req.ConfigJson;
        cfg.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return Results.Ok(new { cfg.Id });
    }).WithName("UpdateConnectorConfig").WithTags("Agents");

    // TestSuites
    api.MapGet("/testsuites", async (mateDbContext db) =>
    {
        var suites = await db.TestSuites
            .Include(s => s.TestCases)
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Id, s.Name, s.Description, s.PassThreshold, s.IsActive,
                s.JudgeSettingId, s.CreatedAt, s.UpdatedAt,
                TestCaseCount = s.TestCases.Count
            })
            .ToListAsync();
        return Results.Ok(suites);
    }).WithName("ListTestSuites").WithTags("Test Suites");

    api.MapGet("/testsuites/{id:guid}", async (Guid id, mateDbContext db) =>
    {
        var suite = await db.TestSuites
            .Include(s => s.TestCases.OrderBy(tc => tc.SortOrder))
            .FirstOrDefaultAsync(s => s.Id == id);
        return suite is null ? Results.NotFound() : Results.Ok(suite);
    }).WithName("GetTestSuite").WithTags("Test Suites");

    api.MapPost("/testsuites", async (CreateTestSuiteRequest req, mateDbContext db, ITenantContext tenant, ClaimsPrincipal user) =>
    {
        var suite = new TestSuite
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = req.Name,
            Description = req.Description,
            PassThreshold = req.PassThreshold,
            JudgeSettingId = req.JudgeSettingId,
            CreatedBy = user.Identity?.Name ?? "api",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.TestSuites.Add(suite);
        await db.SaveChangesAsync();
        return Results.Created($"/api/testsuites/{suite.Id}", new { suite.Id });
    }).WithName("CreateTestSuite").WithTags("Test Suites");

    api.MapPut("/testsuites/{id:guid}", async (Guid id, UpdateTestSuiteRequest req, mateDbContext db) =>
    {
        var suite = await db.TestSuites.FindAsync(id);
        if (suite is null) return Results.NotFound();
        suite.Name = req.Name;
        suite.Description = req.Description;
        suite.PassThreshold = req.PassThreshold;
        suite.IsActive = req.IsActive;
        suite.JudgeSettingId = req.JudgeSettingId;
        suite.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new { suite.Id });
    }).WithName("UpdateTestSuite").WithTags("Test Suites");

    api.MapDelete("/testsuites/{id:guid}", async (Guid id, mateDbContext db) =>
    {
        var suite = await db.TestSuites.FindAsync(id);
        if (suite is null) return Results.NotFound();
        db.TestSuites.Remove(suite);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }).WithName("DeleteTestSuite").WithTags("Test Suites");

    // TestCases
    api.MapGet("/testsuites/{id:guid}/testcases", async (Guid id, mateDbContext db) =>
    {
        var exists = await db.TestSuites.AnyAsync(s => s.Id == id);
        if (!exists) return Results.NotFound();
        var cases = await db.TestCases
            .Where(tc => tc.SuiteId == id)
            .OrderBy(tc => tc.SortOrder)
            .ToListAsync();
        return Results.Ok(cases);
    }).WithName("ListTestCases").WithTags("Test Cases");

    api.MapPost("/testsuites/{id:guid}/testcases", async (Guid id, CreateTestCaseRequest req, mateDbContext db, ITenantContext tenant) =>
    {
        var suite = await db.TestSuites.FindAsync(id);
        if (suite is null) return Results.NotFound();
        var tc = new TestCase
        {
            Id = Guid.NewGuid(),
            SuiteId = id,
            TenantId = tenant.TenantId,
            Name = req.Name,
            Description = req.Description,
            UserInput = req.UserInput,
            AcceptanceCriteria = req.AcceptanceCriteria,
            ExpectedIntent = req.ExpectedIntent,
            ExpectedEntities = req.ExpectedEntities,
            ReferenceAnswer = req.ReferenceAnswer,
            SortOrder = req.SortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.TestCases.Add(tc);
        await db.SaveChangesAsync();
        return Results.Created($"/api/testcases/{tc.Id}", new { tc.Id });
    }).WithName("CreateTestCase").WithTags("Test Cases");

    api.MapPut("/testcases/{id:guid}", async (Guid id, UpdateTestCaseRequest req, mateDbContext db) =>
    {
        var tc = await db.TestCases.FindAsync(id);
        if (tc is null) return Results.NotFound();
        tc.Name = req.Name;
        tc.Description = req.Description;
        tc.UserInput = req.UserInput;
        tc.AcceptanceCriteria = req.AcceptanceCriteria;
        tc.ExpectedIntent = req.ExpectedIntent;
        tc.ExpectedEntities = req.ExpectedEntities;
        tc.ReferenceAnswer = req.ReferenceAnswer;
        tc.IsActive = req.IsActive;
        tc.SortOrder = req.SortOrder;
        tc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new { tc.Id });
    }).WithName("UpdateTestCase").WithTags("Test Cases");

    api.MapDelete("/testcases/{id:guid}", async (Guid id, mateDbContext db) =>
    {
        var tc = await db.TestCases.FindAsync(id);
        if (tc is null) return Results.NotFound();
        db.TestCases.Remove(tc);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }).WithName("DeleteTestCase").WithTags("Test Cases");

    // Runs
    api.MapGet("/runs", async (mateDbContext db) =>
    {
        var runs = await db.Runs
            .Include(r => r.Suite)
            .Include(r => r.Agent)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new
            {
                r.Id, r.Status, r.StartedAt, r.CompletedAt,
                r.TotalTestCases, r.PassedCount, r.FailedCount, r.SkippedCount,
                r.AverageLatencyMs, r.RequestedBy,
                SuiteName = r.Suite != null ? r.Suite.Name : null,
                AgentName = r.Agent != null ? r.Agent.Name : null
            })
            .ToListAsync();
        return Results.Ok(runs);
    }).WithName("ListRuns").WithTags("Runs");

    api.MapGet("/runs/{id:guid}", async (Guid id, mateDbContext db) =>
    {
        var run = await db.Runs
            .Include(r => r.Suite)
            .Include(r => r.Agent)
            .FirstOrDefaultAsync(r => r.Id == id);
        return run is null ? Results.NotFound() : Results.Ok(run);
    }).WithName("GetRun").WithTags("Runs");

    api.MapPost("/runs", async (StartRunRequest req, mateDbContext db, ITenantContext tenant,
        IMessageQueue queue, ClaimsPrincipal user) =>
    {
        var suite = await db.TestSuites.Include(s => s.TestCases).FirstOrDefaultAsync(s => s.Id == req.SuiteId);
        if (suite is null) return Results.NotFound("Suite not found.");
        var agent = await db.Agents.Include(a => a.ConnectorConfigs).FirstOrDefaultAsync(a => a.Id == req.AgentId);
        if (agent is null) return Results.NotFound("Agent not found.");

        var run = new Run
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            SuiteId = req.SuiteId,
            AgentId = req.AgentId,
            Status = "pending",
            TotalTestCases = suite.TestCases.Count(tc => tc.IsActive),
            RequestedBy = req.RequestedBy.Length > 0 ? req.RequestedBy : (user.Identity?.Name ?? "api"),
            StartedAt = DateTime.UtcNow
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();

        var job = new TestRunJob(
            JobId: Guid.NewGuid(),
            TenantId: tenant.TenantId,
            RunId: run.Id,
            SuiteId: req.SuiteId,
            AgentId: req.AgentId,
            RequestedBy: run.RequestedBy,
            TestCaseIds: null);
        await queue.EnqueueAsync("test-runs", job);

        return Results.Created($"/api/runs/{run.Id}", new { run.Id });
    }).WithName("StartRun").WithTags("Runs");

    api.MapGet("/runs/{id:guid}/results", async (Guid id, mateDbContext db) =>
    {
        var results = await db.Results
            .Include(r => r.TestCase)
            .Where(r => r.RunId == id)
            .ToListAsync();
        return Results.Ok(results);
    }).WithName("GetRunResults").WithTags("Runs");

    api.MapDelete("/runs/{id:guid}", async (Guid id, mateDbContext db) =>
    {
        var run = await db.Runs.FindAsync(id);
        if (run is null) return Results.NotFound();
        db.Runs.Remove(run);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }).WithName("DeleteRun").WithTags("Runs");

    // Results
    api.MapGet("/results/{id:guid}/transcript", async (Guid id, mateDbContext db) =>
    {
        var messages = await db.TranscriptMessages
            .Where(m => m.ResultId == id)
            .OrderBy(m => m.TurnIndex)
            .ToListAsync();
        return Results.Ok(messages);
    }).WithName("GetResultTranscript").WithTags("Results");

    api.MapPost("/results/{id:guid}/human-verdict", async (Guid id, SetHumanVerdictRequest req, mateDbContext db, ClaimsPrincipal user) =>
    {
        if (req.Verdict is not ("pass" or "fail"))
            return Results.BadRequest("Verdict must be 'pass' or 'fail'.");
        var result = await db.Results.FindAsync(id);
        if (result is null) return Results.NotFound();
        result.HumanVerdict = req.Verdict;
        result.HumanVerdictNote = req.Note;
        result.HumanVerdictAt = DateTime.UtcNow;
        result.HumanVerdictBy = req.SetBy.Length > 0 ? req.SetBy : (user.Identity?.Name ?? "api");
        await db.SaveChangesAsync();
        return Results.Ok(new { result.Id, result.HumanVerdict });
    }).WithName("SetHumanVerdict").WithTags("Results");

    // Documents
    api.MapGet("/documents", async (mateDbContext db) =>
    {
        var docs = await db.Documents
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new
            {
                d.Id, d.FileName, d.ContentType, d.FileSizeBytes,
                d.PageCount, d.ChunkCount, d.UploadedAt, d.UploadedBy
            })
            .ToListAsync();
        return Results.Ok(docs);
    }).WithName("ListDocuments").WithTags("Documents");

    api.MapPost("/documents", async (HttpRequest req, mateDbContext db, DocumentIngestor ingestor,
        ITenantContext tenant, ClaimsPrincipal user) =>
    {
        if (!req.HasFormContentType)
            return Results.BadRequest("Expected multipart form data.");
        var form = await req.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return Results.BadRequest("No file uploaded.");
        using var stream = file.OpenReadStream();
        var doc = await ingestor.IngestAsync(file.FileName, stream, tenant.TenantId);
        return Results.Created($"/api/documents/{doc.Id}", new { doc.Id, doc.FileName });
    }).WithName("UploadDocument").WithTags("Documents").DisableAntiforgery();

    api.MapDelete("/documents/{id:guid}", async (Guid id, mateDbContext db, IBlobStorageService blobs) =>
    {
        var doc = await db.Documents.FindAsync(id);
        if (doc is null) return Results.NotFound();
        await blobs.DeleteAsync(doc.BlobContainer, doc.BlobName);
        db.Documents.Remove(doc);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }).WithName("DeleteDocument").WithTags("Documents");

    // Metrics
    api.MapGet("/metrics/summary", async (mateDbContext db) =>
    {
        var totalRuns = await db.Runs.CountAsync();
        var completedRuns = await db.Runs.Where(r => r.Status == "completed").ToListAsync();
        var passRate = completedRuns.Count > 0
            ? completedRuns.Average(r => r.TotalTestCases > 0 ? (double)r.PassedCount / r.TotalTestCases : 0)
            : 0.0;
        var avgLatency = completedRuns.Count > 0 ? completedRuns.Average(r => r.AverageLatencyMs) : 0.0;
        var totalSuites = await db.TestSuites.CountAsync();
        var totalAgents = await db.Agents.CountAsync(a => a.IsActive);
        return Results.Ok(new { totalRuns, totalSuites, totalAgents, passRate, avgLatency });
    }).WithName("GetMetricsSummary").WithTags("Metrics");

    // JudgeSettings
    api.MapGet("/judgesettings", async (mateDbContext db) =>
    {
        var settings = await db.JudgeSettings.OrderBy(j => j.Name).ToListAsync();
        return Results.Ok(settings);
    }).WithName("ListJudgeSettings").WithTags("Settings");

    api.MapPost("/judgesettings", async (CreateJudgeSettingRequest req, mateDbContext db, ITenantContext tenant) =>
    {
        if (req.IsDefault)
        {
            var existing = await db.JudgeSettings.Where(j => j.IsDefault).ToListAsync();
            foreach (var e in existing) e.IsDefault = false;
        }
        var setting = new JudgeSetting
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = req.Name,
            ProviderType = req.ProviderType,
            PromptTemplate = req.PromptTemplate,
            TaskSuccessWeight = req.TaskSuccessWeight,
            IntentMatchWeight = req.IntentMatchWeight,
            FactualityWeight = req.FactualityWeight,
            HelpfulnessWeight = req.HelpfulnessWeight,
            SafetyWeight = req.SafetyWeight,
            PassThreshold = req.PassThreshold,
            UseReferenceAnswer = req.UseReferenceAnswer,
            Model = req.Model,
            Temperature = req.Temperature,
            TopP = req.TopP,
            MaxOutputTokens = req.MaxOutputTokens,
            EndpointRef = req.EndpointRef,
            ApiKeyRef = req.ApiKeyRef,
            IsDefault = req.IsDefault,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.JudgeSettings.Add(setting);
        await db.SaveChangesAsync();
        return Results.Created($"/api/judgesettings/{setting.Id}", new { setting.Id });
    }).WithName("CreateJudgeSetting").WithTags("Settings");

    api.MapPut("/judgesettings/{id:guid}", async (Guid id, UpdateJudgeSettingRequest req, mateDbContext db) =>
    {
        var setting = await db.JudgeSettings.FindAsync(id);
        if (setting is null) return Results.NotFound();
        if (req.IsDefault && !setting.IsDefault)
        {
            var existing = await db.JudgeSettings.Where(j => j.IsDefault && j.Id != id).ToListAsync();
            foreach (var e in existing) e.IsDefault = false;
        }
        setting.Name = req.Name;
        setting.ProviderType = req.ProviderType;
        setting.PromptTemplate = req.PromptTemplate;
        setting.TaskSuccessWeight = req.TaskSuccessWeight;
        setting.IntentMatchWeight = req.IntentMatchWeight;
        setting.FactualityWeight = req.FactualityWeight;
        setting.HelpfulnessWeight = req.HelpfulnessWeight;
        setting.SafetyWeight = req.SafetyWeight;
        setting.PassThreshold = req.PassThreshold;
        setting.UseReferenceAnswer = req.UseReferenceAnswer;
        setting.Model = req.Model;
        setting.Temperature = req.Temperature;
        setting.TopP = req.TopP;
        setting.MaxOutputTokens = req.MaxOutputTokens;
        setting.EndpointRef = req.EndpointRef;
        setting.ApiKeyRef = req.ApiKeyRef;
        setting.IsDefault = req.IsDefault;
        setting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new { setting.Id });
    }).WithName("UpdateJudgeSetting").WithTags("Settings");

    api.MapDelete("/judgesettings/{id:guid}", async (Guid id, mateDbContext db) =>
    {
        var setting = await db.JudgeSettings.FindAsync(id);
        if (setting is null) return Results.NotFound();
        db.JudgeSettings.Remove(setting);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }).WithName("DeleteJudgeSetting").WithTags("Settings");

    // Admin: API Keys
    api.MapGet("/admin/api-keys", async (mateDbContext db) =>
    {
        var keys = await db.ApiKeys
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new
            {
                k.Id, k.Name, k.Prefix, k.Role, k.IsActive, k.CreatedAt, k.LastUsedAt, k.CreatedBy
            })
            .ToListAsync();
        return Results.Ok(keys);
    }).WithName("ListApiKeys").WithTags("Admin");

    api.MapPost("/admin/api-keys", async (CreateApiKeyRequest req, mateDbContext db, ITenantContext tenant, ClaimsPrincipal user) =>
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("Name is required.");
        var rawKey = $"mate_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_").TrimEnd('=')}";
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();
        var prefix = rawKey[..12];
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            Name = req.Name,
            Prefix = prefix,
            KeyHash = hash,
            Role = req.Role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = user.Identity?.Name ?? "admin"
        };
        db.ApiKeys.Add(apiKey);
        await db.SaveChangesAsync();
        return Results.Created($"/api/admin/api-keys/{apiKey.Id}", new { apiKey.Id, apiKey.Name, apiKey.Prefix, RawKey = rawKey });
    }).WithName("CreateApiKey").WithTags("Admin");

    api.MapDelete("/admin/api-keys/{id:guid}", async (Guid id, mateDbContext db) =>
    {
        var apiKey = await db.ApiKeys.FindAsync(id);
        if (apiKey is null) return Results.NotFound();
        apiKey.IsActive = false;
        await db.SaveChangesAsync();
        return Results.Ok(new { apiKey.Id, Revoked = true });
    }).WithName("RevokeApiKey").WithTags("Admin");

    // Test connection
    api.MapPost("/test-connection", async (Guid agentId, mateDbContext db) =>
    {
        var agent = await db.Agents.Include(a => a.ConnectorConfigs).FirstOrDefaultAsync(a => a.Id == agentId);
        if (agent is null) return Results.NotFound("Agent not found.");
        var cfg = agent.ConnectorConfigs.FirstOrDefault(c => c.IsActive);
        if (cfg is null) return Results.BadRequest("Agent has no active connector configuration.");
        return Results.Ok(new { Message = "Connector configuration found.", cfg.ConnectorType });
    }).WithName("TestConnection").WithTags("System");

    // Audit log
    api.MapGet("/admin/audit-log", async (mateDbContext db, int page = 1, int pageSize = 50) =>
    {
        var total = await db.AuditLogs.CountAsync();
        var logs = await db.AuditLogs
            .OrderByDescending(l => l.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return Results.Ok(new { total, page, pageSize, logs });
    }).WithName("GetAuditLog").WithTags("Admin");

    app.MapRazorComponents<mate.WebUI.Components.App>()
       .AddInteractiveServerRenderMode();

    app.Run();
    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "mate.WebUI terminated unexpectedly.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
