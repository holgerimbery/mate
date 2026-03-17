// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// A hashed API key for machine-to-machine access.
/// On creation, the raw key is returned once and never stored.
/// Only the SHA-256 hash and the humanreadable prefix are persisted.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Visual prefix shown in lists (e.g. "mcskey_abc123…"). Not the full key.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>SHA-256 hex of the full raw key. Used for constant-time lookup.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>SuperAdmin | TenantAdmin | Tester | Viewer</summary>
    public string Role { get; set; } = "Tester";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
