// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Domain.Contracts.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace mate.Infrastructure.Local;

/// <summary>
/// Phase 1 secret service that reads secrets from environment variables.
/// Secret reference names are looked up directly as environment variable names.
///
/// Security contract: this service never logs secret values, only reference names.
/// For production deployments use the Azure Key Vault implementation.
/// </summary>
public sealed class EnvironmentVariableSecretService : ISecretService
{
    private readonly ILogger<EnvironmentVariableSecretService> _logger;

    public EnvironmentVariableSecretService(ILogger<EnvironmentVariableSecretService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">When the environment variable is not set.</exception>
    public Task<string> GetSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);

        var value = Environment.GetEnvironmentVariable(secretRef);
        if (string.IsNullOrEmpty(value))
        {
            _logger.LogWarning("Secret reference '{SecretRef}' is not set as an environment variable.", secretRef);
            throw new InvalidOperationException(
                $"Secret '{secretRef}' is not configured. Set an environment variable with that name.");
        }

        _logger.LogDebug("Resolved secret reference '{SecretRef}' from environment.", secretRef);
        return Task.FromResult(value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Setting environment variables at runtime only affects the current process.
    /// This is primarily useful for integration tests and local development.
    /// </remarks>
    public Task SetSecretAsync(string secretRef, string secretValue, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);
        // secretValue intentionally not null-checked — empty secrets are valid
        Environment.SetEnvironmentVariable(secretRef, secretValue);
        _logger.LogInformation("Set in-process environment variable for secret '{SecretRef}'.", secretRef);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteSecretAsync(string secretRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretRef);
        Environment.SetEnvironmentVariable(secretRef, null);
        _logger.LogInformation("Removed in-process environment variable for secret '{SecretRef}'.", secretRef);
        return Task.CompletedTask;
    }
}
