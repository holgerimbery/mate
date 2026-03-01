using mate.Domain.Contracts.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Infrastructure.Local;

/// <summary>
/// DI registration for Phase 1 local infrastructure services.
/// Call <c>services.AddmateLocalInfrastructure(config)</c> from the host's Program.cs.
/// </summary>
public static class LocalInfrastructureServiceExtensions
{
    /// <summary>
    /// Registers Phase 1 local-filesystem infrastructure:
    /// - <see cref="IBlobStorageService"/> → <see cref="LocalBlobStorageService"/>
    /// - <see cref="ISecretService"/>      → <see cref="EnvironmentVariableSecretService"/>
    /// - <see cref="IMessageQueue"/>       → <see cref="InProcessMessageQueue"/> (singleton)
    /// - <see cref="IBackupService"/>      → <see cref="SqliteBackupService"/>
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
        services.AddScoped<IBackupService, SqliteBackupService>();

        return services;
    }
}
