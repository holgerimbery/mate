// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// A user belonging to a tenant. Platform-level users (operators) carry the PlatformAdmin role.
/// </summary>
public class TenantUser
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Subject claim from the identity provider.</summary>
    public string ExternalUserId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Admin | Tester | Viewer | PlatformAdmin</summary>
    public string Role { get; set; } = "Viewer";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
