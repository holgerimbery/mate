using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Modules.AgentConnector.Parloa;

/// <summary>
/// Parloa AI agent connector — placeholder implementation.
///
/// Parloa exposes its agents via a REST API. This stub will be completed once
/// the Parloa API credentials and endpoint contract are confirmed.
///
/// Required configuration fields are listed in <see cref="GetConfigDefinition"/>.
/// </summary>
public sealed class ParloaAgentConnector : IAgentConnector
{
    public string ConnectorType => "Parloa";

    public Task<ConversationSession> StartConversationAsync(
        AgentConnectionConfig config,
        CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "ParloaAgentConnector.StartConversationAsync is not yet implemented. " +
            "Configure API credentials and implement the Parloa REST protocol.");
    }

    public Task<AgentResponse> SendMessageAsync(
        ConversationSession session,
        string userMessage,
        CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "ParloaAgentConnector.SendMessageAsync is not yet implemented.");
    }

    public Task EndConversationAsync(ConversationSession session, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>Parloa connector module descriptor — placeholder.</summary>
public sealed class ParloaConnectorModule : IAgentConnectorModule
{
    public string ConnectorType => "Parloa";
    public string DisplayName   => "Parloa AI (coming soon)";

    public IAgentConnector CreateConnector(AgentConnectionConfig config)
        => new ParloaAgentConnector();

    public ValidationResult ValidateConfig(string configJson)
        => ValidationResult.Fail("Parloa connector is not yet implemented.");

    public IEnumerable<ConfigFieldDefinition> GetConfigDefinition()
    {
        yield return new ConfigFieldDefinition("ApiEndpoint",   "Parloa API Endpoint",  "Parloa REST endpoint URL",   "text",   true);
        yield return new ConfigFieldDefinition("ApiKeyRef",     "API Key (secret ref)", "Secret ref for the API key", "secret", true);
        yield return new ConfigFieldDefinition("AgentId",       "Agent / Bot ID",       "Parloa agent identifier",    "text",   true);
        yield return new ConfigFieldDefinition("TimeoutSeconds","Reply Timeout (s)",    "Max seconds to wait",        "number", false);
    }

    public void RegisterServices(IServiceCollection services, IConfiguration config) { }
}

/// <summary>DI extension for Parloa connector module.</summary>
public static class ParloaConnectorExtensions
{
    public static IServiceCollection AddmateParloaConnector(this IServiceCollection services)
    {
        services.AddSingleton<IAgentConnectorModule, ParloaConnectorModule>();
        return services;
    }
}
