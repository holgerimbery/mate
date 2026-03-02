// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// An uploaded document used to generate test cases or to ground answers.
/// File bytes are stored in IBlobStorageService; only metadata lives here.
/// </summary>
public class Document
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    /// <summary>SHA-256 hex hash of the full extracted text, used for deduplication.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }

    /// <summary>Blob storage container name (e.g. "uploads").</summary>
    public string BlobContainer { get; set; } = "uploads";

    /// <summary>Blob storage path within the container.</summary>
    public string BlobName { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string UploadedBy { get; set; } = string.Empty;

    // Navigation
    public ICollection<Chunk> Chunks { get; set; } = [];
}
