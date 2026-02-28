namespace mate.Domain.Entities;

/// <summary>
/// Many-to-many join between TestSuite and Agent.
/// </summary>
public class TestSuiteAgent
{
    public Guid TestSuiteId { get; set; }
    public Guid AgentId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public TestSuite? TestSuite { get; set; }
    public Agent? Agent { get; set; }
}
