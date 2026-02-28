namespace mate.Domain.Entities;

/// <summary>
/// Tenant-wide LLM settings for the question-generation feature.
/// Per-agent overrides take precedence over these global values.
/// </summary>
public class GlobalQuestionGenerationSetting
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

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
