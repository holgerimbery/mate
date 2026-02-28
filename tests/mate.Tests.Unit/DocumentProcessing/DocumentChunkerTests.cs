using FluentAssertions;
using mate.Core.DocumentProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace mate.Tests.Unit.DocumentProcessing;

/// <summary>
/// Unit tests for <see cref="DocumentChunker"/>.
/// Tests verify chunk count, overlap presence, and edge-case handling.
/// </summary>
public sealed class DocumentChunkerTests
{
    private static DocumentChunker CreateChunker(int maxTokens = 50, int overlapTokens = 10)
        => new(NullLogger<DocumentChunker>.Instance)
        {
            MaxChunkTokens = maxTokens,
            OverlapTokens  = overlapTokens,
        };

    // ── Null / empty inputs ───────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void Chunk_EmptyOrWhitespaceInput_ReturnsEmptyList(string? input)
    {
        var chunker = CreateChunker();
        var result  = chunker.Chunk(input);
        result.Should().BeEmpty();
    }

    // ── Single short paragraph ────────────────────────────────────────────────

    [Fact]
    public void Chunk_SingleShortParagraph_ReturnsSingleChunk()
    {
        var chunker = CreateChunker(maxTokens: 500);
        var text    = "Hello world, this is a short paragraph.";
        var result  = chunker.Chunk(text);
        result.Should().HaveCount(1);
        result[0].Should().Contain("Hello world");
    }

    // ── Multiple paragraphs below the token limit ─────────────────────────────

    [Fact]
    public void Chunk_TwoParagraphsBelowLimit_ReturnsSingleChunk()
    {
        var chunker = CreateChunker(maxTokens: 500);
        var text    = "Paragraph one.\n\nParagraph two.";
        var result  = chunker.Chunk(text);
        result.Should().HaveCount(1);
    }

    // ── Text that exceeds limit → multiple chunks ─────────────────────────────

    [Fact]
    public void Chunk_LargeText_ProducesMultipleChunks()
    {
        // Each paragraph is ~10 words = ~40 chars = ~10 estimated tokens
        // maxTokens = 30 → we expect a new chunk roughly every 3 paragraphs
        var chunker = CreateChunker(maxTokens: 30, overlapTokens: 5);

        var paragraphs = Enumerable.Range(1, 15)
            .Select(i => $"Paragraph number {i} contains some text here.")
            .ToArray();

        var text   = string.Join("\n\n", paragraphs);
        var result = chunker.Chunk(text);

        result.Should().HaveCountGreaterThan(1, "text should be split into multiple chunks");
        result.Should().AllSatisfy(c => c.Should().NotBeNullOrWhiteSpace());
    }

    // ── Overlap carries context ───────────────────────────────────────────────

    [Fact]
    public void Chunk_WithOverlap_SecondChunkContainsTailOfFirst()
    {
        // Build text long enough to span two chunks
        var chunker = CreateChunker(maxTokens: 30, overlapTokens: 10);

        // 10 paragraphs, each ~10 tokens
        var paragraphs = Enumerable.Range(1, 10)
            .Select(i => $"This is paragraph {i} with enough words to count.")
            .ToArray();

        var text   = string.Join("\n\n", paragraphs);
        var result = chunker.Chunk(text);

        if (result.Count >= 2)
        {
            // The second chunk should share some words from the end of the first
            var wordsInFirst  = result[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeLast(5);
            var secondChunk   = result[1];
            wordsInFirst.Any(w => secondChunk.Contains(w)).Should().BeTrue(
                "overlap should carry tail words from the first chunk into the second");
        }
    }

    // ── Long single paragraph is split ───────────────────────────────────────

    [Fact]
    public void Chunk_SingleVeryLongParagraph_IsSplitIntoMultiple()
    {
        var chunker = CreateChunker(maxTokens: 30, overlapTokens: 5);

        // ~600 chars → ~150 estimated tokens → should produce at least 2 sub-chunks
        var longParagraph = string.Concat(Enumerable.Repeat("word ", 120));
        var result = chunker.Chunk(longParagraph);

        result.Should().HaveCountGreaterThan(1);
    }

    // ── All chunks are within token budget ────────────────────────────────────

    [Fact]
    public void Chunk_AllChunks_AreWithinTokenBudget()
    {
        var maxTokens = 50;
        var chunker   = CreateChunker(maxTokens: maxTokens, overlapTokens: 10);

        var paragraphs = Enumerable.Range(1, 20)
            .Select(i => $"Sentence {i}: The quick brown fox jumps over the lazy dog, demonstrating text generation.")
            .ToArray();

        var text   = string.Join("\n\n", paragraphs);
        var result = chunker.Chunk(text);

        // Add a generous slack (2×) to account for overlap at chunk boundaries
        foreach (var chunk in result)
        {
            var estimatedTokens = (int)Math.Ceiling(chunk.Length / 4.0);
            estimatedTokens.Should().BeLessOrEqualTo(maxTokens * 2,
                $"chunk of length {chunk.Length} should not exceed 2× the token budget");
        }
    }
}
