// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using Azure;
using Azure.Security.KeyVault.Secrets;
using mate.Domain.Contracts.Infrastructure;
using Microsoft.Extensions.Logging;

namespace mate.Infrastructure.Azure;

/// <summary>
/// Secret service backed by Azure Key Vault.
/// Falls back to environment variables for compatibility with existing local/development flows.
/// </summary>
public sealed class AzureKeyVaultSecretService : ISecretService
{
    private readonly SecretClient _secretClient;
    private readonly ILogger<AzureKeyVaultSecretService> _logger;

    public AzureKeyVaultSecretService(
        SecretClient secretClient,
        ILogger<AzureKeyVaultSecretService> logger)
    {
        _secretClient = secretClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        var kvName = NormalizeSecretName(secretRef);
        try
        {
            var secret = await _secretClient.GetSecretAsync(kvName, cancellationToken: ct);
            _logger.LogDebug("Resolved secret reference '{SecretRef}' (vault name: '{KvName}') from Azure Key Vault.", secretRef, kvName);
            return secret.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var envValue = Environment.GetEnvironmentVariable(secretRef);
            if (!string.IsNullOrEmpty(envValue))
            {
                _logger.LogDebug("Resolved secret reference '{SecretRef}' from environment variable fallback.", secretRef);
                return envValue;
            }

            _logger.LogWarning("Secret reference '{SecretRef}' (vault name: '{KvName}') not found in Azure Key Vault or environment.", secretRef, kvName);
            throw new InvalidOperationException(
                $"Secret '{secretRef}' (vault name: '{kvName}') is not configured in Azure Key Vault and no fallback environment variable exists.",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(string secretRef, string secretValue, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        var kvName = NormalizeSecretName(secretRef);
        await _secretClient.SetSecretAsync(kvName, secretValue, ct);
        _logger.LogInformation("Stored secret reference '{SecretRef}' (vault name: '{KvName}') in Azure Key Vault.", secretRef, kvName);
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        var kvName = NormalizeSecretName(secretRef);
        try
        {
            await _secretClient.StartDeleteSecretAsync(kvName, ct);
            _logger.LogInformation("Deleted secret reference '{SecretRef}' (vault name: '{KvName}') from Azure Key Vault.", secretRef, kvName);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Secret reference '{SecretRef}' (vault name: '{KvName}') not present in Azure Key Vault. Nothing to delete.", secretRef, kvName);
        }
    }

    /// <summary>
    /// Converts a DB-style secret reference (underscores, special chars) to a valid
    /// Azure Key Vault secret name (alphanumeric + hyphens only).
    /// Matches the normalization logic in <see cref="AzureKeyVaultMultiVaultSecretService"/>.
    /// </summary>
    private static string NormalizeSecretName(string secretRef)
    {
        var normalized = new string(secretRef
            .Trim()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-')
            .ToArray());

        while (normalized.Contains("--", StringComparison.Ordinal))
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);

        normalized = normalized.Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Secret reference resolves to an empty Key Vault secret name.", nameof(secretRef));

        return normalized;
    }
}
