// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// A single test scenario within a suite.
/// UserInput is an array to support multi-turn conversations — each entry
/// is one user message sent in sequence to the agent.
/// </summary>
public class TestCase
{
    public Guid Id { get; set; }
    public Guid SuiteId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Ordered user messages for this test case.
    /// Single-turn tests have one entry; multi-turn tests have many.
    /// </summary>
    public string[] UserInput { get; set; } = [];

    public string AcceptanceCriteria { get; set; } = string.Empty;
    public string? ExpectedIntent { get; set; }
    public string[] ExpectedEntities { get; set; } = [];
    public string? ReferenceAnswer { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>
    /// Free-form tags for grouping and filtering test cases (e.g. "billing", "auth", "happy-path").
    /// Used in the Run Report pass-rate-by-tag breakdown.
    /// </summary>
    public string[] Tags { get; set; } = [];

    /// <summary>Optional link to the source document this test was generated from.</summary>
    public Guid? SourceDocumentId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public TestSuite? Suite { get; set; }
    public Document? SourceDocument { get; set; }
    public ICollection<Result> Results { get; set; } = [];
}
