// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using Azure.Identity;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using mate.Domain.Contracts.Infrastructure;
using mate.Domain.Contracts;
using mate.Infrastructure.Local;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace mate.Infrastructure.Azure;

/// <summary>
/// DI registration for Phase 2 Azure / Container infrastructure services.
/// Call <c>services.AddmateAzureInfrastructure(config)</c> from the host's Program.cs
/// when <c>Infrastructure__Provider</c> is set to "Azure" or "Container".
///
/// Registered services:
/// - <see cref="IBlobStorageService"/> → <see cref="AzureBlobStorageService"/> (Azure Blob / Azurite)
/// - <see cref="ISecretService"/>      → <see cref="DatabaseSecretService"/> (same as Local; Key Vault is E1-08)
/// - <see cref="IMessageQueue"/>       → <see cref="InProcessMessageQueue"/> (same as Local; Service Bus is E1-13)
/// - <see cref="IBackupService"/>      → <see cref="NoOpBackupService"/> (no SQLite in this tier)
/// </summary>
public static class AzureInfrastructureServiceExtensions
{
    public static IServiceCollection AddmateAzureInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<AzureInfrastructureOptions>(
            config.GetSection(AzureInfrastructureOptions.Section));

        // Blob: Azure Blob Storage or Azurite (same code, different connection string)
        services.AddScoped<IBlobStorageService, AzureBlobStorageService>();

        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

        // Secrets: route to Azure Key Vault when explicitly enabled; otherwise DB-backed.
        services.AddScoped<DatabaseSecretService>();
        services.AddScoped<AzureKeyVaultSecretService>();
        services.AddScoped<AzureKeyVaultMultiVaultSecretService>();
        services.AddScoped<ISecretService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureInfrastructureOptions>>().Value;

            if (!options.UseKeyVaultForSecrets || string.IsNullOrWhiteSpace(options.KeyVaultUri))
            {
                if (options.UseMultiVaultForSecrets)
                    return sp.GetRequiredService<DatabaseSecretService>();

                return sp.GetRequiredService<DatabaseSecretService>();
            }

            if (options.UseMultiVaultForSecrets)
                return sp.GetRequiredService<AzureKeyVaultMultiVaultSecretService>();

            var credential = sp.GetRequiredService<TokenCredential>();
            var client = new SecretClient(new Uri(options.KeyVaultUri), credential);
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AzureKeyVaultSecretService>>();
            return new AzureKeyVaultSecretService(client, logger);
        });

        // Message queue: still in-process (Service Bus is backlog E1-13)
        services.AddSingleton<IMessageQueue, InProcessMessageQueue>();

        // Backup: no-op — PostgreSQL backup is handled at infrastructure level
        services.AddScoped<IBackupService, NoOpBackupService>();

        return services;
    }
}
