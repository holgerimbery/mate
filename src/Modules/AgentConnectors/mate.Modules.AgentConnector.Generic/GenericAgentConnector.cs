using mate.Domain.Contracts.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Modules.AgentConnector.Generic;

/// <summary>
/// Generic / placeholder agent connector.
/// Returns a "not implemented" response so that the system can run without a real connector.
/// Replace with a concrete implementation once the target agent platform is known.
/// </summary>
public sealed class GenericAgentConnector : IAgentConnector
{
    public string ConnectorType => "Generic";

    public async Task<ConversationSession> StartConversationAsync(
        AgentConnectionConfig config,
        CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return new ConversationSession
        {
            ConnectorType = "Generic",
            Metadata = { ["note"] = "Generic connector — no real conversation started." }
        };
    }

    public Task<AgentResponse> SendMessageAsync(
        ConversationSession session,
        string userMessage,
        CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "GenericAgentConnector does not implement SendMessageAsync. " +
            "Configure a real connector module (e.g. CopilotStudio, AIFoundry, Parloa).");
    }

    public Task EndConversationAsync(ConversationSession session, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>Generic agent connector module — placeholder.</summary>
public sealed class GenericAgentConnectorModule : IAgentConnectorModule
{
    public string ConnectorType => "Generic";
    public string DisplayName   => "Generic Connector (placeholder)";
    public ModuleTier Tier      => ModuleTier.Free;

    public IAgentConnector CreateConnector(AgentConnectionConfig config)
        => new GenericAgentConnector();

    public ValidationResult ValidateConfig(string configJson)
        => ValidationResult.Fail("Generic connector is a placeholder and cannot be used for real tests.");

    public IEnumerable<ConfigFieldDefinition> GetConfigDefinition()
        => [];

    public void RegisterServices(IServiceCollection services, IConfiguration config) { }
}

/// <summary>DI extension for the Generic agent connector module.</summary>
public static class GenericAgentConnectorExtensions
{
    public static IServiceCollection AddmateGenericAgentConnector(this IServiceCollection services)
    {
        services.AddSingleton<IAgentConnectorModule, GenericAgentConnectorModule>();
        return services;
    }
}
