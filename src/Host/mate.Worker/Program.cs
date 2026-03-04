// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Core;
using mate.Core.Tenancy;
using mate.Data;
using mate.Domain.Contracts.Infrastructure;
using mate.Domain.Contracts.Modules;
using mate.Domain.Contracts.Monitoring;
using mate.Infrastructure.Azure;
using mate.Infrastructure.Local;
using mate.Modules.AgentConnector.CopilotStudio;
using mate.Modules.AgentConnector.Generic;
using mate.Modules.Monitoring.Generic;
using mate.Modules.Testing.Generic;
using mate.Modules.Testing.ModelAsJudge;
using mate.Modules.Testing.ModelQGen;
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
            // PostgreSQL is always used — Container mode targets local Docker PostgreSQL,
            // Azure mode targets Azure Database for PostgreSQL.
            services.AddmatePostgres(config);

            // ── Infrastructure ────────────────────────────────────────────────
            // AzureBlobStorageService targets Azurite in Container mode and Azure Blob Storage in Azure mode.
            services.AddmateAzureInfrastructure(config);

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
            // ── Question generation modules ─────────────────────────────────────────
            services.AddmateModelQGenModule(config);
            // ── Monitoring ────────────────────────────────────────────────────
            services.AddmateGenericMonitoring();

            // ── Background worker ─────────────────────────────────────────────
            services.AddHostedService<TestRunWorker>();
        })
        .Build();

    // Apply EF migrations + seed default data on startup
    await host.Services.ApplyMigrationsAsync(seed: true);

    // ── Populate module registry from DI ──────────────────────────────────────────
    {
        using var scope = host.Services.CreateScope();
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
