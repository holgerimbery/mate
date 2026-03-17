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

        try
        {
            var secret = await _secretClient.GetSecretAsync(secretRef, cancellationToken: ct);
            _logger.LogDebug("Resolved secret reference '{SecretRef}' from Azure Key Vault.", secretRef);
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

            _logger.LogWarning("Secret reference '{SecretRef}' not found in Azure Key Vault or environment.", secretRef);
            throw new InvalidOperationException(
                $"Secret '{secretRef}' is not configured in Azure Key Vault and no fallback environment variable exists.",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(string secretRef, string secretValue, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        await _secretClient.SetSecretAsync(secretRef, secretValue, ct);
        _logger.LogInformation("Stored secret reference '{SecretRef}' in Azure Key Vault.", secretRef);
    }

    /// <inheritdoc />
    public async Task DeleteSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        try
        {
            await _secretClient.StartDeleteSecretAsync(secretRef, ct);
            _logger.LogInformation("Deleted secret reference '{SecretRef}' from Azure Key Vault.", secretRef);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Secret reference '{SecretRef}' not present in Azure Key Vault. Nothing to delete.", secretRef);
        }
    }
}
