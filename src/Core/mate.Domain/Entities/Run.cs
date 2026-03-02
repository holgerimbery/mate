// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// A test execution run for one suite against one agent.
/// Status transitions: pending → running → completed | failed.
/// </summary>
public class Run
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SuiteId { get; set; }
    public Guid? AgentId { get; set; }

    /// <summary>pending | running | completed | failed</summary>
    public string Status { get; set; } = "pending";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public int TotalTestCases { get; set; }
    public int PassedCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }

    public double AverageLatencyMs { get; set; }
    public double MedianLatencyMs { get; set; }
    public double P95LatencyMs { get; set; }

    public string RequestedBy { get; set; } = string.Empty;

    // Navigation
    public TestSuite? Suite { get; set; }
    public Agent? Agent { get; set; }
    public ICollection<Result> Results { get; set; } = [];
}
