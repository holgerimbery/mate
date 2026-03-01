using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
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
using mate.Modules.Testing.ModelQGen;
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

    // ── Forwarded Headers (nginx / reverse proxy) ────────────────────────────
    // ASPNETCORE_FORWARDEDHEADERS_ENABLED=true in the container environment
    // activates the built-in ForwardedHeaders middleware automatically (same
    // approach as MaaJforMCS). No manual Configure<ForwardedHeadersOptions>
    // needed — the built-in handler clears KnownNetworks/KnownProxies so it
    // trusts X-Forwarded-Proto from any upstream, which is correct inside Docker
    // where the proxy IP varies. The explicit custom config below is kept only
    // as a fallback for environments that do NOT set the env var.

    // NOTE: No custom DataProtection config — matching MaaJforMCS which uses
    // the default in-memory keys. PersistKeysToFileSystem + SetApplicationName
    // were removed because stale keys in the volume caused cookie decrypt failures.

    // ── Data (EF Core + migrations) ──────────────────────────────────────────
    builder.Services.AddmateSqlite(config);
    builder.Services.AddMemoryCache();

    // ── Tenant resolution ───────────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    // TenantLookupService maps ExternalTenantId (from auth claim) → internal Tenant.Id (from DB).
    // This ensures continuity when switching auth schemes (Generic ↔ EntraId):
    // as long as the Tenants table has a row with the correct ExternalTenantId, data is accessible.
    builder.Services.AddScoped<ITenantContext>(sp =>
    {
        // Primary: HttpContext (works for REST API requests and SSR pre-render)
        var user = sp.GetService<IHttpContextAccessor>()?.HttpContext?.User;

        // Fallback: AuthenticationStateProvider (required for Blazor Server interactive circuits
        // where IHttpContextAccessor.HttpContext is null per ASP.NET Core Blazor docs).
        // ServerAuthenticationStateProvider returns a pre-completed Task backed by the user
        // captured during the initial HTTP connection, so GetAwaiter().GetResult() is safe.
        if (user?.Identity?.IsAuthenticated != true)
        {
            var asp = sp.GetService<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>();
            if (asp is not null)
            {
                try
                {
                    var authState = asp.GetAuthenticationStateAsync().GetAwaiter().GetResult();
                    user = authState.User;
                }
                catch { /* not in a Blazor context — ignore */ }
            }
        }

        if (user?.Identity?.IsAuthenticated == true)
        {
            var externalId = user.FindFirstValue("mate:externalTenantId")
                          ?? user.FindFirstValue("tid");
            if (!string.IsNullOrEmpty(externalId))
            {
                var lookup = sp.GetService<TenantLookupService>();
                if (lookup is not null)
                {
                    // Task.Run(): no captured SynchronizationContext → avoids deadlock
                    // when called under Blazor's RendererSynchronizationContext.
                    // TenantLookupService constructs mateDbContext directly with null
                    // ITenantContext, breaking the circular DI chain entirely.
                    var resolved = Task.Run(() => lookup.LookupByExternalIdAsync(externalId))
                        .GetAwaiter().GetResult();
                    if (resolved.HasValue)
                        return new HttpTenantContext(resolved.Value);
                }
                // Fallback: try to use claim directly as GUID (Generic auth dev flow)
                if (Guid.TryParse(externalId, out var tenantId))
                    return new HttpTenantContext(tenantId);
            }
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

    // ── Question generation modules ──────────────────────────────────────────
    builder.Services.AddmateModelQGenModule(config);

    // ── Authentication ───────────────────────────────────────────────────────
    var authScheme = config["Authentication:Scheme"] ?? "EntraId";

    IAuthModule authModule = authScheme switch
    {
        "None" or "Generic" => new mate.Modules.Auth.Generic.GenericAuthModule(),
        "EntraId" or _ => new mate.Modules.Auth.EntraId.EntraIdAuthModule(),
    };

    // For EntraId: use OpenIdConnectDefaults.AuthenticationScheme as the outer
    // scheme name — exactly as MaaJforMCS does. AddMicrosoftIdentityWebApp then
    // internally wires DefaultScheme=Cookies, DefaultChallengeScheme=OpenIdConnect.
    // Overriding those options explicitly (as we did before) can conflict with
    // what the library configures in its own IConfigureOptions pass.
    AuthenticationBuilder authBuilder = authScheme is "EntraId"
        ? builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        : builder.Services.AddAuthentication(authScheme);

    authModule.ConfigureAuthentication(authBuilder, config);

    // EntraId browser sign-in: register /MicrosoftIdentity/Account/* controller routes
    if (authScheme is "EntraId")
        mate.Modules.Auth.EntraId.EntraIdAuthModule.AddMicrosoftIdentityUI(builder.Services);

    // No extra AddScheme — matches MaaJforMCS. API key is handled by inline middleware only.

    builder.Services.AddAuthorization(options =>
    {
        authModule.ConfigureAuthorization(options, config);
    });

    builder.Services.AddSingleton<IAuthModule>(authModule);

    // Register ClaimsTransformation using the SAME instance registered as IAuthModule
    // so that IClaimsTransformation and IAuthModule are the same object (no duplicate
    // claim sets, no second construction with different lifetime).
    if (authModule is IClaimsTransformation ct)
        builder.Services.AddSingleton<IClaimsTransformation>(_ => ct);

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
        foreach (var m in scope.ServiceProvider.GetServices<IQuestionGenerationProvider>())
            registry.RegisterQuestionProvider(m);
        foreach (var m in scope.ServiceProvider.GetServices<IMonitoringModule>())
            registry.RegisterMonitoring(m);
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    // ── Forwarded Headers ────────────────────────────────────────────────────
    // ASPNETCORE_FORWARDEDHEADERS_ENABLED=true already activates the built-in
    // ForwardedHeaders middleware automatically before any other middleware runs.
    // The explicit call below is kept as a no-op safety net for environments
    // that run without the env var (e.g. plain dotnet run without Docker).
    app.UseForwardedHeaders();

    // ── Diagnostic: log request host/scheme after ForwardedHeaders processing ──
    // Helps debug proxy/redirect_uri mismatches. Remove once auth is stable.
    app.Use(async (ctx, next) =>
    {
        Log.Information("DIAG REQ {Method} {Scheme}://{Host}{Path} (IsHttps={IsHttps})",
            ctx.Request.Method, ctx.Request.Scheme, ctx.Request.Host, ctx.Request.Path, ctx.Request.IsHttps);
        await next();
        // Log where /signin-oidc redirects the browser after sign-in
        if (ctx.Request.Path.StartsWithSegments("/signin-oidc") && ctx.Response.StatusCode is 302 or 301)
            Log.Information("DIAG POST-SIGNIN redirect → {Location}", ctx.Response.Headers.Location.ToString());
    });

    // Only redirect to HTTPS when NOT using Entra ID.
    // With EntraId, Azure AD uses response_mode=form_post — it POSTs to /signin-oidc
    // with state+code in the form body. UseHttpsRedirection converts that POST to a
    // GET (HTTP 301), stripping the body → state is null → "message.State is null or empty".
    // For EntraId: nginx terminates TLS (prod) or the container runs plain HTTP (dev/Docker);
    // in both cases no HTTPS redirect is needed or safe.
    if (authScheme is not "EntraId")
    {
        app.UseHttpsRedirection();
    }

    app.UseStaticFiles();
    // NOTE: No explicit app.UseRouting() here — matches MaaJforMCS exactly.
    // In .NET 9, when UseRouting() is called explicitly BEFORE UseAuthentication(),
    // the endpoint is matched before auth runs. The cookie handler then sees the
    // pre-matched /_blazor SignalR endpoint context and issues a raw 401 redirect
    // instead of the normal cookie challenge for WebSocket upgrade requests.
    // Without explicit UseRouting(), ASP.NET Core inserts it automatically at the
    // right place (after endpoint middleware runs auth/authz), so all requests pass
    // through UseAuthentication() before any endpoint-specific behaviour fires.
    app.UseAuthentication();

    // API key authentication middleware — mirrors MaaJforMCS inline approach.
    if (authScheme is not "Generic" and not "None")
    {
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
                            new Claim("api_key_id", apiKey.Id.ToString()),
                            new Claim("mate:externalTenantId", apiKey.TenantId.ToString()),
                        };
                        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
                    }
                }
            }
            await next();
        });
    }

    app.UseAuthorization();

    app.UseAntiforgery();

    // EntraId: map /MicrosoftIdentity/Account/{SignIn,SignOut,AccessDenied} controller routes
    // Registered AFTER UseAntiforgery — matches MaaJforMCS pipeline order.
    if (authScheme is "EntraId")
        app.MapControllers();

    // ── Platform endpoints ───────────────────────────────────────────────────
    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options =>
    {
        options.Title = "mate — AI Agent Quality Testing Platform";
        options.Theme = ScalarTheme.Default;
    }).AllowAnonymous();

    // ── REST API ─────────────────────────────────────────────────────────────
    // When a real auth scheme is active, require authentication on all /api endpoints.
    // Generic/None (dev) allows unauthenticated access so local dev works without a token.
    var api = authScheme is not "Generic" and not "None"
        ? app.MapGroup("/api").RequireAuthorization("AnyAuthenticated")
        : app.MapGroup("/api");

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

    // Admin: Backup
    api.MapGet("/admin/backup", async (mate.Domain.Contracts.Infrastructure.IBackupService backup, CancellationToken ct) =>
    {
        var stream = await backup.CreateBackupStreamAsync(ct);
        var stamp  = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Results.Stream(stream, "application/octet-stream", $"mate-backup-{stamp}.db");
    }).WithName("DownloadBackup").WithTags("Admin").AllowAnonymous();

    // Admin: Restore
    api.MapPost("/admin/restore", async (
        HttpContext ctx,
        mate.Domain.Contracts.Infrastructure.IBackupService backup,
        CancellationToken ct) =>
    {
        if (!ctx.Request.HasFormContentType)
            return Results.BadRequest("Expected multipart/form-data with a 'file' field.");
        var form = await ctx.Request.ReadFormAsync(ct);
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
            return Results.BadRequest("No file provided.");
        await using var stream = file.OpenReadStream();
        await backup.RestoreAsync(stream, ct);
        return Results.Ok(new { Message = "Restore successful. Please reload the application." });
    }).WithName("RestoreBackup").WithTags("Admin").AllowAnonymous();

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

// ── API Key Authentication Handler ────────────────────────────────────────────
internal sealed class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public ApiKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var rawKey))
            return AuthenticateResult.NoResult();

        var hash = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey.ToString())))
            .ToLowerInvariant();

        using var scope = Context.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<mateDbContext>();
        var apiKey = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive);

        if (apiKey is null)
            return AuthenticateResult.Fail("Invalid or inactive API key.");

        apiKey.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, apiKey.Name),
            new Claim(ClaimTypes.Role, apiKey.Role),
            new Claim("api_key_id", apiKey.Id.ToString()),
            new Claim("mate:externalTenantId", apiKey.TenantId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
