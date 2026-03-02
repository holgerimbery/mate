// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// A text chunk produced by DocumentChunker from a Document's extracted text.
/// Embedding is stored but population is deferred to a future semantic-search module.
/// </summary>
public class Chunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid TenantId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public int ChunkIndex { get; set; }
    public double? StartChapter { get; set; }
    public double? EndChapter { get; set; }
    public string? Category { get; set; }

    /// <summary>Optional semantic search vector. Null until a vector-generation module populates it.</summary>
    public byte[]? Embedding { get; set; }

    // Navigation
    public Document? Document { get; set; }
}
