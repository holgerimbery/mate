namespace mate.Domain.Entities;

/// <summary>
/// Judge configuration: which provider to use, scoring weights, LLM parameters.
/// Used to select the IJudgeProvider and to pass settings to the chosen provider.
/// </summary>
public class JudgeSetting
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>ModelAsJudge | Rubrics | Hybrid — resolved to IJudgeProvider by ModuleRegistry</summary>
    public string ProviderType { get; set; } = "ModelAsJudge";

    /// <summary>Optional custom system prompt. Null = module's built-in default prompt.</summary>
    public string? PromptTemplate { get; set; }

    // Scoring weights for ModelAsJudge and HybridJudge (must sum to 1.0)
    public double TaskSuccessWeight { get; set; } = 0.3;
    public double IntentMatchWeight { get; set; } = 0.2;
    public double FactualityWeight { get; set; } = 0.2;
    public double HelpfulnessWeight { get; set; } = 0.15;
    public double SafetyWeight { get; set; } = 0.15;

    public double PassThreshold { get; set; } = 0.7;
    public bool UseReferenceAnswer { get; set; } = false;

    // LLM parameters (for ModelAsJudge and HybridJudge)
    public string? Model { get; set; }       // null = inherit global setting
    public double Temperature { get; set; } = 0.2;
    public double TopP { get; set; } = 0.9;
    public int MaxOutputTokens { get; set; } = 800;

    /// <summary>Secret-store reference name for the LLM endpoint. Null = use global.</summary>
    public string? EndpointRef { get; set; }

    /// <summary>Secret-store reference name for the LLM API key. Null = use global.</summary>
    public string? ApiKeyRef { get; set; }

    /// <summary>When true, this is the tenant-wide fallback JudgeSetting.</summary>
    public bool IsDefault { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<RubricSet> RubricSets { get; set; } = [];
}
