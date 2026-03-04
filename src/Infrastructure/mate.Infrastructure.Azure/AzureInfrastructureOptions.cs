// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Infrastructure.Azure;

/// <summary>
/// Configuration options for Phase 2 Azure / Container infrastructure services.
/// Bind from the "AzureInfrastructure" config section or environment variables
/// prefixed with AzureInfrastructure__.
/// </summary>
public sealed class AzureInfrastructureOptions
{
    public const string Section = "AzureInfrastructure";

    /// <summary>
    /// Azure Blob Storage connection string.
    /// For production: use a storage account connection string or managed-identity URI.
    /// For Container mode (Azurite): use the well-known Azurite dev connection string.
    /// Example (Azurite): DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;...
    /// </summary>
    public string? BlobConnectionString { get; set; }

    /// <summary>
    /// Default blob container name for document storage. Default: "mate-blobs".
    /// </summary>
    public string BlobContainerName { get; set; } = "mate-blobs";
}
