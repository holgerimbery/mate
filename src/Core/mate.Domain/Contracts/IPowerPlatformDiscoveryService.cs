// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Contracts;

// ─── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Represents a Power Platform environment returned by discovery.</summary>
public record PowerPlatformEnvironment(
    string EnvironmentId,
    string DisplayName,
    string Region,
    string State,
    string EnvironmentType   // Default | Sandbox | Production | Trial | Developer
);

/// <summary>A Copilot Studio agent (bot) found in a given environment.</summary>
public record DiscoveredAgent(
    string BotId,
    string DisplayName,
    string SchemaName,
    string EnvironmentId,
    bool IsPublished,
    DateTimeOffset? LastModifiedAt
);

/// <summary>Connection parameters required to talk to a Copilot Studio agent via Direct Line.</summary>
public record CopilotStudioConnectionInfo(
    string DirectLineEndpoint,
    string BotIdentifier,
    string TokenEndpoint
);

// ─── Service contract ─────────────────────────────────────────────────────────

/// <summary>
/// Discovers environments and agents via the Power Platform REST API.
/// Used by the CopilotStudio connector module and the UI "import agent" wizard.
/// Requires a valid bearer token obtained through the auth module.
/// </summary>
public interface IPowerPlatformDiscoveryService
{
    /// <summary>Lists all accessible Power Platform environments for the authenticated user.</summary>
    Task<IReadOnlyList<PowerPlatformEnvironment>> GetEnvironmentsAsync(
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>Lists all Copilot Studio bots in the specified environment.</summary>
    Task<IReadOnlyList<DiscoveredAgent>> GetAgentsAsync(
        string environmentId,
        string bearerToken,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the Direct Line connection endpoint and token endpoint for a specific bot.
    /// </summary>
    Task<CopilotStudioConnectionInfo> GetConnectionInfoAsync(
        string environmentId,
        string botId,
        string bearerToken,
        CancellationToken ct = default);
}
