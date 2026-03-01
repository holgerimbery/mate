namespace mate.Domain.Entities;

/// <summary>
/// A named collection of test cases that can be run against one or more agents.
/// </summary>
public class TestSuite
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Minimum pass rate (0–1) to mark the run as passed. Overrides global default.</summary>
    public double PassThreshold { get; set; } = 0.7;

    /// <summary>Per-suite judge override. Null = use agent or global JudgeSetting.</summary>
    public Guid? JudgeSettingId { get; set; }

    /// <summary>
    /// Optional delay (milliseconds) between executing individual test cases.
    /// Use to avoid triggering rate-limit errors on the target agent (e.g. GenAIToolPlannerRateLimitReached).
    /// 0 = no delay (default).
    /// </summary>
    public int DelayBetweenTestsMs { get; set; } = 0;

    public string[] Tags { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    // Navigation
    public JudgeSetting? JudgeSetting { get; set; }
    public ICollection<TestCase> TestCases { get; set; } = [];
    public ICollection<TestSuiteAgent> TestSuiteAgents { get; set; } = [];
    public ICollection<Run> Runs { get; set; } = [];
}
