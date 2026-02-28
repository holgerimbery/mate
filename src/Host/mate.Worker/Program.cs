using mate.Core;
using mate.Core.Tenancy;
using mate.Data;
using mate.Domain.Contracts.Infrastructure;
using mate.Domain.Contracts.Monitoring;
using mate.Infrastructure.Local;
using mate.Modules.AgentConnector.CopilotStudio;
using mate.Modules.AgentConnector.Generic;
using mate.Modules.Monitoring.Generic;
using mate.Modules.Testing.Generic;
using mate.Modules.Testing.ModelAsJudge;
using mate.Modules.Testing.RubricsJudge;
using mate.Modules.Testing.CopilotStudioJudge;
using mate.Modules.Testing.HybridJudge;
using mate.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

// ── Bootstrap Serilog early so startup errors are captured ───────────────────

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog((ctx, services, cfg) =>
        {
            cfg.ReadFrom.Configuration(ctx.Configuration)
               .ReadFrom.Services(services)
               .Enrich.FromLogContext()
               .WriteTo.Console()
               .WriteTo.File(
                   path: "logs/worker-.log",
                   rollingInterval: RollingInterval.Day,
                   retainedFileCountLimit: 14);
        })
        .ConfigureServices((ctx, services) =>
        {
            var config = ctx.Configuration;

            // ── Data ──────────────────────────────────────────────────────────
            services.AddSingleton<mate.Data.ITenantContext>(
                new mate.Data.StaticTenantContext(Guid.Empty));
            services.AddmateSqlite(config);

            // ── Infrastructure ────────────────────────────────────────────────
            services.AddmateLocalInfrastructure(config);

            // ── Core ──────────────────────────────────────────────────────────
            services.AddmateCore();

            // ── Agent connector modules ───────────────────────────────────────
            services.AddmateCopilotStudioModule(config);
            services.AddmateGenericAgentConnector();

            // ── Judge / testing modules ───────────────────────────────────────
            services.AddmateGenericTestingModule();
            services.AddmateModelAsJudgeModule(config);
            services.AddmateRubricsJudgeModule();
            services.AddmateHybridJudgeModule(config);
            services.AddmateCopilotStudioJudgeModule(config);

            // ── Monitoring ────────────────────────────────────────────────────
            services.AddmateGenericMonitoring();

            // ── Background worker ─────────────────────────────────────────────
            services.AddHostedService<TestRunWorker>();
        })
        .Build();

    // Apply EF migrations + seed default data on startup
    await host.Services.ApplyMigrationsAsync(seed: true);

    await host.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "mate.Worker terminated unexpectedly.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
