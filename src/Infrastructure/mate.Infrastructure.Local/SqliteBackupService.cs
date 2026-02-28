using mate.Domain.Contracts.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace mate.Infrastructure.Local;

/// <summary>
/// Phase 1 database backup service for SQLite.
/// Copies the SQLite database file to a timestamped backup in the configured backup directory.
/// </summary>
public sealed class SqliteBackupService : IBackupService
{
    private readonly string _dbPath;
    private readonly string _backupDirectory;
    private readonly ILogger<SqliteBackupService> _logger;

    public SqliteBackupService(
        IOptions<LocalInfrastructureOptions> options,
        ILogger<SqliteBackupService> logger)
    {
        _dbPath          = options.Value.SqliteDatabasePath ?? "mate-local.db";
        _backupDirectory = options.Value.BackupPath ?? "backups";
        _logger          = logger;
    }

    /// <inheritdoc />
    public async Task<Stream> CreateBackupStreamAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_dbPath))
            throw new FileNotFoundException($"SQLite database not found at '{_dbPath}'.", _dbPath);

        Directory.CreateDirectory(_backupDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(_backupDirectory, $"mate-backup-{stamp}.db");

        // SQLite requires a file-level copy (not a stream copy) for a consistent snapshot.
        File.Copy(_dbPath, backupPath, overwrite: false);

        _logger.LogInformation("SQLite backup created: {BackupPath}", backupPath);

        return await Task.FromResult(new FileStream(
            backupPath, FileMode.Open, FileAccess.Read, FileShare.None,
            bufferSize: 81920, useAsync: true));
    }

    /// <inheritdoc />
    public async Task RestoreAsync(Stream backupStream, CancellationToken ct = default)
    {
        if (File.Exists(_dbPath))
        {
            var preRestoreBackup = _dbPath + ".pre-restore";
            File.Copy(_dbPath, preRestoreBackup, overwrite: true);
            _logger.LogWarning("Existing database backed up to '{PreRestoreBackup}' before restore.", preRestoreBackup);
        }

        await using var fileStream = new FileStream(
            _dbPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        await backupStream.CopyToAsync(fileStream, ct);
        _logger.LogInformation("Database restored from backup stream to '{DbPath}'.", _dbPath);
    }
}
