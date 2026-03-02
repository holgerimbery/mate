// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Text;
using Microsoft.Extensions.Logging;

namespace mate.Core.DocumentProcessing;

/// <summary>
/// Splits extracted document text into overlapping chunks suitable for embedding and retrieval.
///
/// Algorithm:
/// 1. Split text into paragraphs (one or more blank lines).
/// 2. Accumulate paragraphs into a chunk until <see cref="MaxChunkTokens"/> is reached.
/// 3. Apply overlap: the last <see cref="OverlapTokens"/> tokens are prepended to the next chunk.
/// 4. Token count is estimated as Math.Ceiling(charCount / 4.0) — fast approximation.
/// </summary>
public sealed class DocumentChunker
{
    private readonly ILogger<DocumentChunker> _logger;

    /// <summary>Target maximum tokens per chunk (default 500).</summary>
    public int MaxChunkTokens { get; init; } = 500;

    /// <summary>Tokens repeated between adjacent chunks for context overlap (default 50).</summary>
    public int OverlapTokens { get; init; } = 50;

    public DocumentChunker(ILogger<DocumentChunker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into overlapping text chunks.
    /// Returns an empty list when the text is null or whitespace.
    /// </summary>
    public IReadOnlyList<string> Chunk(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var paragraphs = SplitIntoParagraphs(text);
        if (paragraphs.Count == 0)
            return [];

        var chunks = new List<string>();
        var current = new StringBuilder();
        int currentTokens = 0;

        foreach (var paragraph in paragraphs)
        {
            int paraTokens = EstimateTokens(paragraph);

            // If a single paragraph exceeds the limit, force-split it
            if (paraTokens > MaxChunkTokens)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                    currentTokens = 0;
                }

                foreach (var subChunk in SplitLongParagraph(paragraph))
                    chunks.Add(subChunk);

                continue;
            }

            if (currentTokens + paraTokens > MaxChunkTokens && current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());

                // Carry over overlap from the end of the completed chunk
                var overlap = ExtractOverlap(current.ToString(), OverlapTokens);
                current.Clear();
                if (!string.IsNullOrWhiteSpace(overlap))
                {
                    current.Append(overlap).Append('\n');
                    currentTokens = EstimateTokens(overlap);
                }
                else
                {
                    currentTokens = 0;
                }
            }

            current.Append(paragraph).Append('\n');
            currentTokens += paraTokens;
        }

        if (current.Length > 0 && !string.IsNullOrWhiteSpace(current.ToString()))
            chunks.Add(current.ToString().Trim());

        _logger.LogDebug("Chunked document into {Count} chunks (max {Max} tokens, {Overlap} overlap).",
            chunks.Count, MaxChunkTokens, OverlapTokens);

        return chunks;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<string> SplitIntoParagraphs(string text)
    {
        return text
            .Split(["\r\n\r\n", "\n\n", "\r\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    private IEnumerable<string> SplitLongParagraph(string paragraph)
    {
        // First try sentence-level split
        var sentences = paragraph
            .Split(['.', '!', '?'])
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        // If no sentences found (no punctuation), fall back to word-level splitting
        if (sentences.Length == 0)
            sentences = [paragraph];

        var buffer = new StringBuilder();
        int tokens = 0;

        foreach (var sentence in sentences)
        {
            int st = EstimateTokens(sentence);

            // If even a single sentence exceeds the limit, split it by words
            if (st > MaxChunkTokens)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString().Trim();
                    buffer.Clear();
                    tokens = 0;
                }

                foreach (var wordChunk in SplitByWords(sentence))
                    yield return wordChunk;

                continue;
            }

            if (tokens + st > MaxChunkTokens && buffer.Length > 0)
            {
                yield return buffer.ToString().Trim();
                buffer.Clear();
                tokens = 0;
            }

            buffer.Append(sentence).Append(". ");
            tokens += st;
        }

        if (buffer.Length > 0)
            yield return buffer.ToString().Trim();
    }

    /// <summary>Splits text into word-boundary chunks when even sentence splitting is insufficient.</summary>
    private IEnumerable<string> SplitByWords(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var buffer = new StringBuilder();
        int tokens = 0;

        foreach (var word in words)
        {
            int wt = EstimateTokens(word + " ");
            if (tokens + wt > MaxChunkTokens && buffer.Length > 0)
            {
                yield return buffer.ToString().Trim();
                buffer.Clear();
                tokens = 0;
            }

            buffer.Append(word).Append(' ');
            tokens += wt;
        }

        if (buffer.Length > 0)
            yield return buffer.ToString().Trim();
    }

    /// <summary>
    /// Extracts the last <paramref name="tokenCount"/> tokens from <paramref name="text"/>
    /// as an overlap prefix for the next chunk.
    /// </summary>
    private static string ExtractOverlap(string text, int tokenCount)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        int charTarget = tokenCount * 4; // approximate inverse of token estimate
        if (text.Length <= charTarget) return text;

        return text[^charTarget..];
    }

    /// <summary>
    /// Fast token count approximation: 1 token ≈ 4 characters.
    /// This avoids a dependency on a tokeniser library for the chunker.
    /// </summary>
    private static int EstimateTokens(string text)
        => (int)Math.Ceiling(text.Length / 4.0);
}
