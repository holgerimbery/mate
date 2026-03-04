// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Domain.Contracts.Infrastructure;
using Microsoft.Extensions.Logging;

namespace mate.Infrastructure.Local;

/// <summary>
/// No-op backup service for PostgreSQL / Azure deployments where backup is handled
/// at the infrastructure level (Azure Database automated backups, pg_dump, etc.).
/// </summary>
public sealed class NoOpBackupService : IBackupService
{
    private readonly ILogger<NoOpBackupService> _logger;

    public NoOpBackupService(ILogger<NoOpBackupService> logger)
    {
        _logger = logger;
    }

    public Task<Stream> CreateBackupStreamAsync(CancellationToken ct = default)
    {
        _logger.LogInformation(
            "NoOpBackupService: backup is managed at infrastructure level (PostgreSQL / Azure). Skipping.");
        Stream empty = new MemoryStream();
        return Task.FromResult(empty);
    }

    public Task RestoreAsync(Stream backupStream, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "NoOpBackupService: restore is not supported in PostgreSQL / Azure mode. " +
            "Use Azure Database restore or pg_restore instead.");
        return Task.CompletedTask;
    }
}
