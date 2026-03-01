using mate.Data;
using mate.Domain.Contracts.Infrastructure;
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace mate.Infrastructure.Local;

/// <summary>
/// Phase 1 secret service that stores secrets in the application database.
/// Falls back to environment variables for backward compatibility with existing deployments
/// that use env-var references in ApiKeyRef fields.
///
/// Storage: <see cref="AppSecret"/> rows, scoped to the current tenant.
/// Security note: values are stored in plain text in the local SQLite/Postgres database.
/// Phase 2 deployments should use the Azure Key Vault implementation.
/// </summary>
public sealed class DatabaseSecretService : ISecretService
{
    private readonly mateDbContext _db;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<DatabaseSecretService> _logger;

    public DatabaseSecretService(
        mateDbContext db,
        ILogger<DatabaseSecretService> logger,
        ITenantContext? tenantContext = null)
    {
        _db            = db;
        _logger        = logger;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<string> GetSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        // 1) Check database (tenant-scoped + platform secrets visible via query filter)
        var secret = await _db.AppSecrets
            .FirstOrDefaultAsync(s => s.RefName == secretRef, ct);

        if (secret is not null)
        {
            _logger.LogDebug("Resolved secret reference '{SecretRef}' from database.", secretRef);
            return secret.Value;
        }

        // 2) Fall back to environment variable (backward compat for existing configs)
        var envValue = Environment.GetEnvironmentVariable(secretRef);
        if (!string.IsNullOrEmpty(envValue))
        {
            _logger.LogDebug("Resolved secret reference '{SecretRef}' from environment variable.", secretRef);
            return envValue;
        }

        _logger.LogWarning("Secret reference '{SecretRef}' not found in database or environment.", secretRef);
        throw new InvalidOperationException(
            $"Secret '{secretRef}' is not configured. " +
            $"Add it in Settings or set an environment variable with that name.");
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(string secretRef, string secretValue, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        var tenantId = _tenantContext?.TenantId ?? mateDbContext.PlatformTenantId;

        var existing = await _db.AppSecrets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.RefName == secretRef && s.TenantId == tenantId, ct);

        if (existing is not null)
        {
            existing.Value     = secretValue;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.AppSecrets.Add(new AppSecret
            {
                Id        = Guid.NewGuid(),
                TenantId  = tenantId,
                RefName   = secretRef,
                Value     = secretValue,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Stored secret reference '{SecretRef}' in database.", secretRef);
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        var tenantId = _tenantContext?.TenantId ?? mateDbContext.PlatformTenantId;

        var existing = await _db.AppSecrets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.RefName == secretRef && s.TenantId == tenantId, ct);

        if (existing is not null)
        {
            _db.AppSecrets.Remove(existing);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Deleted secret reference '{SecretRef}' from database.", secretRef);
        }
    }
}
