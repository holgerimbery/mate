// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// One message in the conversation replay for a Result.
/// RawActivityJson preserves the full Bot Framework Activity / Parloa response JSON
/// for audit and debugging purposes.
/// </summary>
public class TranscriptMessage
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ResultId { get; set; }

    /// <summary>user | bot</summary>
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    /// <summary>Full platform-native JSON activity for auditability. May be null for simple connectors.</summary>
    public string? RawActivityJson { get; set; }

    public int TurnIndex { get; set; }
    public long LatencyMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation
    public Result? Result { get; set; }
}
