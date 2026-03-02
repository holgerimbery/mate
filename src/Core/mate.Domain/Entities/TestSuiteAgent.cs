// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
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
