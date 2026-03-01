namespace mate.Domain.Entities;

/// <summary>
/// Named LLM configuration for the question-generation feature.
/// Multiple configurations can coexist; the one with IsDefault=true is used
/// when no specific configuration is selected.
/// Per-agent overrides take precedence over any global values.
/// </summary>
public class GlobalQuestionGenerationSetting
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Human-readable name, e.g. "Default GPT-4o Mini".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When true, this is the tenant-wide fallback configuration.</summary>
    public bool IsDefault { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Provider-neutral URL or service identifier. Interpreted by the active ILlmClient.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Secret-store reference name — not the raw API key.</summary>
    public string ApiKeyRef { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 1.0;
    public int MaxOutputTokens { get; set; } = 1000;

    /// <summary>Null = use the built-in default prompt from ModelAsJudge module.</summary>
    public string? SystemPrompt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;
}
