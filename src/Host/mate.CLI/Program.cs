using System.CommandLine;
using mate.Core;
using mate.Data;
using mate.Domain;
using mate.Infrastructure.Local;
using mate.Modules.AgentConnector.CopilotStudio;
using mate.Modules.AgentConnector.Generic;
using mate.Modules.Testing.ModelAsJudge;
using mate.Modules.Testing.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

// ── Bootstrap Serilog ────────────────────────────────────────────────────────

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

// ── Build CLI command tree ────────────────────────────────────────────────────

var rootCommand = new RootCommand($"{BrandInfo.BrandName} CLI — {BrandInfo.BrandCliDescription}");

// ── run command ───────────────────────────────────────────────────────────────

var runCommand     = new Command("run", "Execute a test run");
var runIdOption    = new Option<Guid>("--run-id", "The Guid of a Run entity to execute") { IsRequired = true };
var tenantIdOption = new Option<Guid>("--tenant-id", "The tenant scope for this run")   { IsRequired = true };

runCommand.AddOption(runIdOption);
runCommand.AddOption(tenantIdOption);

runCommand.SetHandler(async (Guid runId, Guid tenantId) =>
{
    using var host = BuildHost(tenantId);
    await host.StartAsync();
    await host.Services.ApplyMigrationsAsync(seed: false);

    var executor = host.Services.GetRequiredService<mate.Core.Execution.TestExecutionService>();
    await executor.ExecuteAsync(runId);
    await host.StopAsync();
    Log.Information("Run {RunId} completed.", runId);
}, runIdOption, tenantIdOption);

// ── migrate command ───────────────────────────────────────────────────────────

var migrateCommand = new Command("migrate", "Apply pending database migrations and seed default data");
migrateCommand.SetHandler(async () =>
{
    using var host = BuildHost(Guid.Empty);
    await host.StartAsync();
    await host.Services.ApplyMigrationsAsync(seed: true);
    await host.StopAsync();
    Log.Information("Migrations applied.");
});

rootCommand.AddCommand(runCommand);
rootCommand.AddCommand(migrateCommand);

try
{
    return await rootCommand.InvokeAsync(args);
}
finally
{
    await Log.CloseAndFlushAsync();
}

// ── Host builder ──────────────────────────────────────────────────────────────

static IHost BuildHost(Guid tenantId)
{
    return Host.CreateDefaultBuilder()
        .UseSerilog((ctx, _, cfg) =>
        {
            cfg.WriteTo.Console()
               .Enrich.FromLogContext();
        })
        .ConfigureServices((ctx, services) =>
        {
            var config = ctx.Configuration;

            // Tenant context — static for CLI
            services.AddSingleton<ITenantContext>(new StaticTenantContext(tenantId));

            // Data
            services.AddmateSqlite(config);

            // Infrastructure
            services.AddmateLocalInfrastructure(config);

            // Core services
            services.AddmateCore();

            // Connector modules
            services.AddmateCopilotStudioModule(config);
            services.AddmateGenericAgentConnector();

            // Judge modules
            services.AddmateGenericTestingModule();
            services.AddmateModelAsJudgeModule(config);
        })
        .Build();
}
