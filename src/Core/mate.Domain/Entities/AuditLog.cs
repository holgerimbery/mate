// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// Immutable audit record written on every mutating operation.
/// Covers all CRUD on tenanted data plus auth events.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public string Action { get; set; } = string.Empty;   // Created | Updated | Deleted | RunStarted | etc.
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? UserId { get; set; }
    public string? OldValue { get; set; }    // JSON snapshot before change
    public string? NewValue { get; set; }    // JSON snapshot after change
    public string? Details { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
}
