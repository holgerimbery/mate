// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// Stores the typed configuration for one connector on an agent.
/// ConfigJson holds a connector-specific JSON blob; secret values inside it
/// are secret-store reference names (not raw secrets) so they can be safely
/// stored in the database.
/// </summary>
public class AgentConnectorConfig
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>CopilotStudio | AIFoundry | Parloa — matches IAgentConnector.ConnectorType</summary>
    public string ConnectorType { get; set; } = string.Empty;

    /// <summary>
    /// Serialised connector-specific config POCO.
    /// Secret fields must contain secret-store reference names, not raw values.
    /// </summary>
    public string ConfigJson { get; set; } = "{}";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Agent? Agent { get; set; }
}
