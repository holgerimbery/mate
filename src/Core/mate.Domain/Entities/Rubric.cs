// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// A named rubric containing one or more RubricCriteria.
/// Linked to a JudgeSetting with ProviderType = "Rubrics" or "Hybrid".
/// All criteria are evaluated deterministically against the bot response — no LLM call.
/// </summary>
public class RubricSet
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid JudgeSettingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// When true, any single IsMandatory=true criterion that fails causes
    /// the overall verdict to be Fail regardless of weighted score.
    /// </summary>
    public bool RequireAllMandatory { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public JudgeSetting? JudgeSetting { get; set; }
    public ICollection<RubricCriteria> Criteria { get; set; } = [];
}

/// <summary>
/// A single evaluation rule within a RubricSet.
/// </summary>
public class RubricCriteria
{
    public Guid Id { get; set; }
    public Guid RubricSetId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;   // e.g. "Mentions return policy"

    /// <summary>Contains | NotContains | Regex | Custom</summary>
    public string EvaluationType { get; set; } = "Contains";

    /// <summary>Substring, regex pattern, or custom evaluator key.</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Relative weight in the overall rubric score (0.0–N).</summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>When true a failed criterion overrides the weighted score to 0 / Fail.</summary>
    public bool IsMandatory { get; set; } = false;

    public int SortOrder { get; set; }

    // Navigation
    public RubricSet? RubricSet { get; set; }
}
