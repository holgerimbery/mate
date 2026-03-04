// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Domain.Contracts.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Infrastructure.Local;

/// <summary>
/// DI registration for local infrastructure services (blob storage on local filesystem).
/// Call <c>services.AddmateLocalInfrastructure(config)</c> from the host's Program.cs.
/// </summary>
public static class LocalInfrastructureServiceExtensions
{
    /// <summary>
    /// Registers local-filesystem infrastructure:
    /// - <see cref="IBlobStorageService"/> → <see cref="LocalBlobStorageService"/>
    /// - <see cref="ISecretService"/>      → <see cref="EnvironmentVariableSecretService"/>
    /// - <see cref="IMessageQueue"/>       → <see cref="InProcessMessageQueue"/> (singleton)
    /// - <see cref="IBackupService"/>      → <see cref="NoOpBackupService"/>
    /// </summary>
    public static IServiceCollection AddmateLocalInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<LocalInfrastructureOptions>(
            config.GetSection(LocalInfrastructureOptions.Section));

        services.AddScoped<IBlobStorageService, LocalBlobStorageService>();
        services.AddScoped<ISecretService, DatabaseSecretService>();
        services.AddSingleton<IMessageQueue, InProcessMessageQueue>();
        services.AddScoped<IBackupService, NoOpBackupService>();

        return services;
    }
}
