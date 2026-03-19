// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Core.DocumentProcessing;
using mate.Core.Execution;
using mate.Core.Tenancy;
using mate.Data;
using mate.Domain.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Core;

/// <summary>
/// DI registration extension for the mate.Core layer.
/// Call <c>services.AddmateCore()</c> from the host's Program.cs.
/// This does NOT register infrastructure (blob, secrets, queue) — those are in
/// <c>services.AddmateLocalInfrastructure()</c> or <c>services.AddmateAzureInfrastructure()</c>.
/// </summary>
public static class CoreServiceExtensions
{
    /// <summary>
    /// Registers all mate.Core services: module registry, tenancy, document processing,
    /// test execution, and quota enforcement.
    /// </summary>
    public static IServiceCollection AddmateCore(this IServiceCollection services)
    {
        // Module registry is a singleton — modules register themselves at startup
        services.AddSingleton<mateModuleRegistry>();

        // Tenancy
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddScoped<TenantLookupService>();
        services.AddScoped<TenantResolver>();
        services.AddScoped<IQuotaService, QuotaService>();
        services.AddScoped<ITenantAuthorizationService, TenantAuthorizationService>();

        // Document processing
        services.AddScoped<DocumentChunker>();
        services.AddScoped<DocumentIngestor>();

        // Test execution
        services.AddScoped<TestExecutionService>();

        // Seeder (transient — called once at startup)
        services.AddTransient<mateDbSeeder>();

        return services;
    }
}
