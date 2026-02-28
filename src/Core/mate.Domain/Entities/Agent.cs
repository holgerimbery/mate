namespace mate.Domain.Entities;

/// <summary>
/// An AI agent under test. One agent can have multiple connector configurations
/// (CopilotStudio, AIFoundry, Parloa, etc.) stored in AgentConnectorConfig.
/// </summary>
public class Agent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>dev | test | staging | production</summary>
    public string Environment { get; set; } = "production";

    public string[] Tags { get; set; } = [];
    public bool IsActive { get; set; } = true;

    /// <summary>Per-agent judge override. Null means use the tenant global JudgeSetting.</summary>
    public Guid? JudgeSettingId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    // Navigation
    public JudgeSetting? JudgeSetting { get; set; }
    public ICollection<AgentConnectorConfig> ConnectorConfigs { get; set; } = [];
    public ICollection<TestSuiteAgent> TestSuiteAgents { get; set; } = [];
    public ICollection<Run> Runs { get; set; } = [];
}
