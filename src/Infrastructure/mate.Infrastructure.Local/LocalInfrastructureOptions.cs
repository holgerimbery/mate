// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Infrastructure.Local;

/// <summary>
/// Configuration options for local infrastructure services (blob storage).
/// Bind from appsettings.json section "LocalInfrastructure" or from environment variables.
/// </summary>
public sealed class LocalInfrastructureOptions
{
    public const string Section = "LocalInfrastructure";

    /// <summary>Root directory for local blob (file) storage. Default: ./data/blobs</summary>
    public string BlobStoragePath { get; set; } = "./data/blobs";
}
