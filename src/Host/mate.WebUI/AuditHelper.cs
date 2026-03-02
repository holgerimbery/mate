// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Data;
using mate.Domain.Entities;

namespace mate.WebUI;

/// <summary>
/// Lightweight helper to append an immutable audit record to the DbContext change-tracker.
/// Call before SaveChangesAsync — the record is saved in the same transaction.
/// </summary>
internal static class AuditHelper
{
    internal static void Log(
        mateDbContext db,
        Guid tenantId,
        string action,
        string entityType,
        Guid? entityId = null,
        string? userId = null,
        string? details = null,
        string? ipAddress = null,
        string? oldValue = null,
        string? newValue = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            UserId = userId,
            Details = details,
            IpAddress = ipAddress,
            OldValue = oldValue,
            NewValue = newValue,
            OccurredAt = DateTime.UtcNow
        });
    }
}
