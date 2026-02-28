using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Domain.Contracts.Modules;

// ─── Shared value objects ────────────────────────────────────────────────────

/// <summary>Live context of one connector session (one conversation with the agent).</summary>
public class ConversationSession
{
    public Guid SessionId { get; init; } = Guid.NewGuid();

    /// <summary>Connector-native conversation identifier (e.g. Direct Line conversationId).</summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>Which connector owns this session.</summary>
    public string ConnectorType { get; init; } = string.Empty;

    /// <summary>
    /// Connector-specific state bag. Used to carry watermarks, thread IDs, session tokens,
    /// etc. between StartConversation / SendMessage / End calls.
    /// Values must be serialisable to string.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>Response received from the agent for one user message.</summary>
public class AgentResponse
{
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<string> Attachments { get; init; } = [];

    /// <summary>Full platform-native JSON for the activity. Stored in TranscriptMessage.RawActivityJson.</summary>
    public string? RawActivityJson { get; init; }

    public long LatencyMs { get; init; }
}

/// <summary>Metadata about one configuration field exposed by a module (drives the dynamic UI form).</summary>
public record ConfigFieldDefinition(
    string Key,
    string Label,
    string Description,
    string FieldType,          // "text" | "password" | "number" | "boolean" | "select"
    bool IsRequired,
    string? DefaultValue = null,
    IReadOnlyList<string>? SelectOptions = null
);

/// <summary>Result returned by a module's config-validation method.</summary>
public record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Ok() => new(true, []);
    public static ValidationResult Fail(params string[] errors) => new(false, errors);
}

// ─── Agent Connector contract ─────────────────────────────────────────────────

/// <summary>
/// Implements the protocol for talking to one type of conversational AI agent.
/// Register via Add{CodePrefix}ConnectorModule&lt;T&gt;() at startup.
/// </summary>
public interface IAgentConnector
{
    /// <summary>Must match AgentConnectorConfig.ConnectorType (e.g. "CopilotStudio").</summary>
    string ConnectorType { get; }

    Task<ConversationSession> StartConversationAsync(
        AgentConnectionConfig config,
        CancellationToken ct = default);

    Task<AgentResponse> SendMessageAsync(
        ConversationSession session,
        string message,
        CancellationToken ct = default);

    Task EndConversationAsync(
        ConversationSession session,
        CancellationToken ct = default);
}

/// <summary>
/// Module descriptor that registers its IAgentConnector into DI
/// and exposes metadata used by the admin UI.
/// </summary>
public interface IAgentConnectorModule
{
    string ConnectorType { get; }
    string DisplayName { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    IEnumerable<ConfigFieldDefinition> GetConfigDefinition();
    ValidationResult ValidateConfig(string configJson);

    /// <summary>Creates a live connector instance wired to the provided connection config.</summary>
    IAgentConnector CreateConnector(AgentConnectionConfig config);
}

/// <summary>Typed config passed from AgentConnectorConfig.ConfigJson to IAgentConnector.</summary>
public class AgentConnectionConfig
{
    public string ConnectorType { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";

    /// <summary>
    /// Resolved secret values — populated at runtime by Core from ISecretService.
    /// Never stored to the database.
    /// </summary>
    public Dictionary<string, string> ResolvedSecrets { get; init; } = [];
}
