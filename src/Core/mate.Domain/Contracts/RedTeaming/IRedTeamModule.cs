using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Domain.Contracts.RedTeaming;

// ─── Enumerations ─────────────────────────────────────────────────────────────

/// <summary>Broad categories of adversarial attack technique.</summary>
public enum AttackCategory
{
    /// <summary>Override or ignore the system prompt.</summary>
    PromptInjection,

    /// <summary>Bypass safety guardrails by reframing context.</summary>
    Jailbreak,

    /// <summary>Probe for leakage of training data or user data.</summary>
    DataExfiltration,

    /// <summary>Induce confident but false answers.</summary>
    HallucinationInduction,

    /// <summary>Elicit toxic, offensive, or harmful content.</summary>
    ToxicContent,

    /// <summary>Extract personally identifiable information.</summary>
    PrivacyLeak,

    /// <summary>Confuse the agent about its own identity or capabilities.</summary>
    RoleConfusion,

    /// <summary>Reveal the contents of the hidden system prompt.</summary>
    SystemPromptLeak,
}

/// <summary>Severity level assigned to a discovered vulnerability.</summary>
public enum RiskLevel
{
    None,
    Low,
    Medium,
    High,
    Critical,
}

// ─── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>Input context used to generate adversarial probes for one target agent.</summary>
public class AttackRequest
{
    /// <summary>Identifier of the agent being red-teamed.</summary>
    public Guid AgentId { get; init; }

    /// <summary>Human-readable description of the agent's intended purpose.</summary>
    public string AgentDescription { get; init; } = string.Empty;

    /// <summary>Attack categories to generate probes for. Empty means all categories.</summary>
    public IReadOnlyList<AttackCategory> Categories { get; init; } = [];

    /// <summary>Total number of adversarial probes to generate across all categories.</summary>
    public int NumberOfProbes { get; init; } = 10;

    /// <summary>Optional domain hint to narrow probe relevance (e.g. "banking", "healthcare").</summary>
    public string? Domain { get; init; }

    /// <summary>Provider-specific configuration resolved at runtime.</summary>
    public string? ResolvedEndpoint { get; init; }
    public string? ResolvedApiKey   { get; init; }
    public string? Model            { get; init; }
}

/// <summary>A single adversarial prompt to be sent to the target agent.</summary>
public class AttackProbe
{
    public Guid ProbeId { get; init; } = Guid.NewGuid();

    /// <summary>Attack category this probe belongs to.</summary>
    public AttackCategory Category { get; init; }

    /// <summary>The adversarial message to send to the agent.</summary>
    public string ProbeMessage { get; init; } = string.Empty;

    /// <summary>What a vulnerable (failing) response would look like.</summary>
    public string? FailureSignature { get; init; }

    /// <summary>Human-readable explanation of the attack intent.</summary>
    public string? Rationale { get; init; }
}

/// <summary>A confirmed vulnerability finding produced after evaluating an agent's response to a probe.</summary>
public class RedTeamFinding
{
    public Guid FindingId { get; init; } = Guid.NewGuid();

    public AttackProbe Probe        { get; init; } = new();
    public string      AgentResponse { get; init; } = string.Empty;

    /// <summary>Assigned severity of the vulnerability.</summary>
    public RiskLevel Risk { get; init; }

    /// <summary>Explanation of why the response is considered a vulnerability.</summary>
    public string Rationale { get; init; } = string.Empty;

    /// <summary>Steps to reproduce the finding.</summary>
    public IReadOnlyList<string> ReproductionSteps { get; init; } = [];

    /// <summary>Suggested mitigations or guardrails.</summary>
    public IReadOnlyList<string> Mitigations { get; init; } = [];
}

/// <summary>Aggregated red-team report for one run against one agent.</summary>
public class RedTeamReport
{
    public Guid   AgentId     { get; init; }
    public int    ProbesRun   { get; init; }
    public int    FindingCount { get; init; }
    public RiskLevel HighestRisk { get; init; }
    public IReadOnlyList<RedTeamFinding> Findings { get; init; } = [];
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}

// ─── Contracts ────────────────────────────────────────────────────────────────

/// <summary>
/// Generates adversarial probes for a target agent and evaluates its responses for vulnerabilities.
/// </summary>
public interface IAttackProvider
{
    /// <summary>Must match <see cref="IRedTeamModule.ProviderType"/>.</summary>
    string ProviderType { get; }

    /// <summary>Generates a set of adversarial probes based on the request context.</summary>
    Task<IReadOnlyList<AttackProbe>> GenerateProbesAsync(AttackRequest request, CancellationToken ct = default);

    /// <summary>
    /// Evaluates one agent response to determine whether the probe succeeded (vulnerability found).
    /// Returns <c>null</c> when the agent handled the probe safely.
    /// </summary>
    Task<RedTeamFinding?> EvaluateResponseAsync(AttackProbe probe, string agentResponse, CancellationToken ct = default);
}

/// <summary>
/// Module descriptor for a red-teaming provider.
/// Implementations are discovered from DI at startup and pushed into <see cref="mate.Core.mateModuleRegistry"/>.
/// </summary>
public interface IRedTeamModule
{
    /// <summary>Unique snake_case identifier, e.g. "generic_red_team".</summary>
    string ModuleId { get; }

    string DisplayName { get; }
    ModuleTier Tier { get; }

    /// <summary>Used for registry lookup — must match <see cref="IAttackProvider.ProviderType"/>.</summary>
    string ProviderType { get; }

    List<ConfigFieldDefinition> ConfigSchema { get; }

    Task<bool>               IsHealthy();
    Task<ValidationResult>   ValidateConfig(Dictionary<string, string> config);
    IReadOnlyList<string>    GetCapabilities();

    void RegisterServices(IServiceCollection services, IConfiguration config);
}
