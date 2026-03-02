// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Infrastructure.Local;

/// <summary>
/// Configuration options for Phase 1 local infrastructure services.
/// Bind from appsettings.json section "LocalInfrastructure" or from environment variables
/// prefixed with MATE_.
/// </summary>
public sealed class LocalInfrastructureOptions
{
    public const string Section = "LocalInfrastructure";

    /// <summary>Root directory for local blob (file) storage. Default: ./data/blobs</summary>
    public string BlobStoragePath { get; set; } = "./data/blobs";

    /// <summary>Path to the SQLite database file. Default: ./data/mate-local.db</summary>
    public string SqliteDatabasePath { get; set; } = "./data/mate-local.db";

    /// <summary>Directory where SQLite backups are written. Default: ./data/backups</summary>
    public string BackupPath { get; set; } = "./data/backups";
}
