using mate.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Domain.Contracts.Modules;

// ─── Shared DTOs ──────────────────────────────────────────────────────────────

/// <summary>
/// A single evaluation criterion passed to judge providers at evaluation time.
/// Hydrated from <see cref="mate.Domain.Entities.RubricCriteria"/> by the execution coordinator.
/// </summary>
public sealed record EvaluationCriterion(
    string Name,
    string EvaluationType,
    string Pattern,
    double Weight,
    bool   IsMandatory
);

/// <summary>Input to the judge for evaluating one test case.</summary>
public class JudgeRequest
{
    public string[] UserInput { get; init; } = [];

    /// <summary>The bot's final response text.</summary>
    public string BotResponse { get; init; } = string.Empty;

    public string AcceptanceCriteria { get; init; } = string.Empty;
    public string? ReferenceAnswer { get; init; }

    /// <summary>Full conversation transcript for multi-turn context.</summary>
    public IReadOnlyList<TranscriptMessage> Transcript { get; init; } = [];

    /// <summary>Snapshot of the JudgeSetting at execution time (weights, thresholds, model params).</summary>
    public JudgeSettingSnapshot JudgeSetting { get; init; } = new();

    /// <summary>
    /// Rubric criteria for deterministic / hybrid evaluation.
    /// Empty when using pure ModelAsJudge evaluation.
    /// </summary>
    public IReadOnlyList<EvaluationCriterion> RubricCriteria { get; init; } = [];
}

/// <summary>Immutable copy of JudgeSetting passed to modules so they can't modify the DB entity.</summary>
public record JudgeSettingSnapshot(
    string ProviderType = "ModelAsJudge",
    string? PromptTemplate = null,
    double TaskSuccessWeight = 0.3,
    double IntentMatchWeight = 0.2,
    double FactualityWeight = 0.2,
    double HelpfulnessWeight = 0.15,
    double SafetyWeight = 0.15,
    double PassThreshold = 0.7,
    bool UseReferenceAnswer = false,
    string? Model = null,
    double Temperature = 0.2,
    double TopP = 0.9,
    int MaxOutputTokens = 800,
    string? ResolvedEndpoint = null,
    string? ResolvedApiKey = null
);

/// <summary>Evaluation result returned by an IJudgeProvider.</summary>
public record JudgeVerdict
{
    public double TaskSuccessScore { get; init; }
    public double IntentMatchScore { get; init; }
    public double FactualityScore { get; init; }
    public double HelpfulnessScore { get; init; }
    public double SafetyScore { get; init; }
    public double OverallScore { get; init; }

    /// <summary>pass | fail | error</summary>
    public string Verdict { get; init; } = "fail";

    public string? Rationale { get; init; }
    public IReadOnlyList<string> Citations { get; init; } = [];
}

/// <summary>Input to the question generator.</summary>
public class QuestionGenerationRequest
{
    public string DocumentContent { get; init; } = string.Empty;
    public int NumberOfQuestions { get; init; } = 5;
    public string? Domain { get; init; }
    public IReadOnlyList<string>? ExistingQuestions { get; init; }
    public string? SystemPromptOverride { get; init; }
    public string? ResolvedEndpoint { get; init; }
    public string? ResolvedApiKey { get; init; }
    public string? Model { get; init; }
}

/// <summary>One AI-generated test question.</summary>
public class GeneratedQuestion
{
    public string Question { get; init; } = string.Empty;
    public string ExpectedAnswer { get; init; } = string.Empty;
    public string? ExpectedIntent { get; init; }
    public string[] ExpectedEntities { get; init; } = [];
    public string? Context { get; init; }
    public string? Rationale { get; init; }
}

// ─── Testing module contracts ─────────────────────────────────────────────────

/// <summary>
/// Evaluates one test case and returns a scored verdict.
/// Implementations: ModelAsJudge, RubricsJudge, HybridJudge, or custom.
/// </summary>
public interface IJudgeProvider
{
    /// <summary>Must match JudgeSetting.ProviderType (e.g. "ModelAsJudge").</summary>
    string ProviderType { get; }

    Task<JudgeVerdict> EvaluateAsync(JudgeRequest request, CancellationToken ct = default);
}

/// <summary>Generates test questions from document content.</summary>
public interface IQuestionGenerationProvider
{
    string ProviderType { get; }
    ModuleTier Tier { get; }
    Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(QuestionGenerationRequest request, CancellationToken ct = default);
}

/// <summary>
/// Module descriptor for testing/evaluation capabilities.
/// Register via Add{CodePrefix}TestingModule&lt;T&gt;() at startup.
/// </summary>
public interface ITestingModule
{
    string ProviderType { get; }
    string DisplayName { get; }
    ModuleTier Tier { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    IEnumerable<ConfigFieldDefinition> GetJudgeConfigDefinition();
}
