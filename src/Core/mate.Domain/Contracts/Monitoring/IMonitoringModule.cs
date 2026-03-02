using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Domain.Contracts.Monitoring;

// ─── Monitoring DTOs ──────────────────────────────────────────────────────────

/// <summary>Properties emitted when a run starts.</summary>
public record RunStartedEvent(
    Guid RunId,
    Guid TenantId,
    Guid SuiteId,
    Guid AgentId,
    int TotalTestCases,
    DateTimeOffset StartedAt
);

/// <summary>Properties emitted when a single test case is evaluated.</summary>
public record TestCaseResultEvent(
    Guid ResultId,
    Guid RunId,
    Guid TenantId,
    string Verdict,
    double OverallScore,
    long LatencyMs,
    string? ErrorType = null
);

/// <summary>Properties emitted when a run finishes.</summary>
public record RunCompletedEvent(
    Guid RunId,
    Guid TenantId,
    string FinalStatus,
    int PassCount,
    int FailCount,
    int ErrorCount,
    double PassRate,
    double AverageScore,
    long P50LatencyMs,
    long P95LatencyMs,
    TimeSpan Duration
);

/// <summary>LLM token consumption event for cost tracking.</summary>
public record LlmUsageEvent(
    Guid RunId,
    Guid TenantId,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    string Purpose  // "judge" | "question_generation"
);

// ─── Monitoring contracts ─────────────────────────────────────────────────────

/// <summary>
/// Abstraction for emitting structured observability events.
/// Resolved from DI by mate.Core — implementations live in module projects.
/// </summary>
public interface IMonitoringService
{
    void TrackRunStarted(RunStartedEvent evt);
    void TrackTestCaseResult(TestCaseResultEvent evt);
    void TrackRunCompleted(RunCompletedEvent evt);
    void TrackLlmUsage(LlmUsageEvent evt);
    void TrackException(Exception ex, Dictionary<string, string>? properties = null);
    void TrackCustomEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null);
}

/// <summary>
/// Module descriptor for a monitoring/telemetry provider.
/// Implementations: OpenTelemetry, ApplicationInsights, or custom.
/// </summary>
public interface IMonitoringModule
{
    string ProviderType { get; }
    string DisplayName { get; }
    ModuleTier Tier { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
}
