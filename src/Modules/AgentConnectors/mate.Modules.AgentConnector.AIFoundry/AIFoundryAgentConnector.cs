using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Modules.AgentConnector.AIFoundry;

/// <summary>
/// Azure AI Foundry agent connector — placeholder implementation.
///
/// This connector will integrate with Azure AI Foundry (formerly Azure ML) agents
/// once the Azure AI Foundry SDK for .NET reaches GA and the agent endpoint contract
/// is finalised. Tracking: https://aka.ms/azure-ai-foundry-sdk
///
/// Until then, calling <see cref="SendMessageAsync"/> throws <see cref="NotImplementedException"/>.
/// </summary>
public sealed class AIFoundryAgentConnector : IAgentConnector
{
    public string ConnectorType => "AIFoundry";

    public Task<ConversationSession> StartConversationAsync(
        AgentConnectionConfig config,
        CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "AIFoundryAgentConnector.StartConversationAsync is not yet implemented. " +
            "Awaiting Azure AI Foundry .NET SDK GA.");
    }

    public Task<AgentResponse> SendMessageAsync(
        ConversationSession session,
        string userMessage,
        CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "AIFoundryAgentConnector.SendMessageAsync is not yet implemented.");
    }

    public Task EndConversationAsync(ConversationSession session, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>Azure AI Foundry connector module descriptor — placeholder.</summary>
public sealed class AIFoundryConnectorModule : IAgentConnectorModule
{
    public string ConnectorType => "AIFoundry";
    public string DisplayName   => "Azure AI Foundry (coming soon)";
    public ModuleTier Tier      => ModuleTier.Free;

    public IAgentConnector CreateConnector(AgentConnectionConfig config)
        => new AIFoundryAgentConnector();

    public ValidationResult ValidateConfig(string configJson)
        => ValidationResult.Fail("Azure AI Foundry connector is not yet implemented.");

    public IEnumerable<ConfigFieldDefinition> GetConfigDefinition()
    {
        yield return new ConfigFieldDefinition("SubscriptionId", "Azure Subscription ID",     "Azure subscription GUID",   "text",   true);
        yield return new ConfigFieldDefinition("ResourceGroup",  "Resource Group",             "Azure resource group name", "text",   true);
        yield return new ConfigFieldDefinition("WorkspaceName",  "AI Foundry Workspace Name",  "AI Foundry workspace name", "text",   true);
        yield return new ConfigFieldDefinition("AgentId",        "Agent ID",                   "AI Foundry agent identifier","text",  true);
        yield return new ConfigFieldDefinition("ApiKeyRef",      "API Key (secret ref)",       "Secret ref for the API key","secret", true);
    }

    public void RegisterServices(IServiceCollection services, IConfiguration config) { }
}

/// <summary>DI extension for Azure AI Foundry connector module.</summary>
public static class AIFoundryConnectorExtensions
{
    public static IServiceCollection AddmateAIFoundryConnector(this IServiceCollection services)
    {
        services.AddSingleton<IAgentConnectorModule, AIFoundryConnectorModule>();
        return services;
    }
}
