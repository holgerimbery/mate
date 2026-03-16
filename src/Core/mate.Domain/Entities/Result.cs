// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// The evaluated outcome of running one TestCase in a Run.
/// All five LLM scoring dimensions are preserved; HumanVerdict allows
/// an operator to override the automated verdict.
/// </summary>
public class Result
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RunId { get; set; }
    public Guid TestCaseId { get; set; }

    /// <summary>pass | fail | skipped | error</summary>
    public string Verdict { get; set; } = "pending";

    // Scoring dimensions (0–1 each)
    public double TaskSuccessScore { get; set; }
    public double IntentMatchScore { get; set; }
    public double FactualityScore { get; set; }
    public double HelpfulnessScore { get; set; }
    public double SafetyScore { get; set; }
    public double OverallScore { get; set; }

    public string? Rationale { get; set; }
    public string[] Citations { get; set; } = [];

    public long LatencyMs { get; set; }

    // Human review override
    /// <summary>pass | fail | null (not reviewed)</summary>
    public string? HumanVerdict { get; set; }
    public string? HumanVerdictNote { get; set; }
    public DateTime? HumanVerdictAt { get; set; }
    public string? HumanVerdictBy { get; set; }

    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Run? Run { get; set; }
    public TestCase? TestCase { get; set; }
    public ICollection<TranscriptMessage> Transcript { get; set; } = [];
}
