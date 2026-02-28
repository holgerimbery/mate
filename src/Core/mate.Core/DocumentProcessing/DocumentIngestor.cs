using System.Security.Cryptography;
using System.Text;
using mate.Data;
using mate.Domain.Contracts.Infrastructure;
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using DocumentFormat.OpenXml.Packaging;

namespace mate.Core.DocumentProcessing;

/// <summary>
/// High-level service that processes an uploaded file end-to-end:
/// 1. Validates the file type (PDF or DOCX).
/// 2. Persists the binary to blob storage via <see cref="IBlobStorageService"/>.
/// 3. Extracts text from the file.
/// 4. Chunks the text using <see cref="DocumentChunker"/>.
/// 5. Saves the <see cref="Document"/> and its <see cref="Chunk"/> entities to the database.
///
/// Deduplication: if a file with the same ContentHash already exists for the tenant,
/// the existing document is returned without re-processing.
/// </summary>
public sealed class DocumentIngestor
{
    private const string BlobContainer = "documents";

    private readonly mateDbContext _db;
    private readonly IBlobStorageService _blobStorage;
    private readonly DocumentChunker _chunker;
    private readonly ILogger<DocumentIngestor> _logger;

    public DocumentIngestor(
        mateDbContext db,
        IBlobStorageService blobStorage,
        DocumentChunker chunker,
        ILogger<DocumentIngestor> logger)
    {
        _db = db;
        _blobStorage = blobStorage;
        _chunker = chunker;
        _logger = logger;
    }

    /// <summary>
    /// Ingests a document from a stream.  Returns the stored <see cref="Document"/> entity.
    /// </summary>
    /// <param name="fileName">Original file name (used to infer content type).</param>
    /// <param name="stream">Document content stream (caller disposes).</param>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<Document> IngestAsync(
        string fileName,
        Stream stream,
        Guid tenantId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".pdf" and not ".docx")
            throw new InvalidOperationException(
                $"Unsupported file type '{extension}'. Only .pdf and .docx are accepted.");

        // Read into memory buffer so we can compute hash and pass to blob storage
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var contentHash = ComputeSha256(buffer.ToArray());

        // Deduplication check — skip reprocessing the same file
        var existing = await _db.Documents
            .Where(d => d.TenantId == tenantId && d.ContentHash == contentHash)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Document '{FileName}' already exists (hash {Hash}). Returning existing record {DocumentId}.",
                fileName, contentHash, existing.Id);
            return existing;
        }

        // Upload to blob storage
        var blobName = $"{tenantId}/{Guid.NewGuid()}{extension}";
        var contentType = extension == ".pdf" ? "application/pdf"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        buffer.Position = 0;
        var blobReference = await _blobStorage.UploadAsync(BlobContainer, blobName, buffer, contentType, ct);

        // Extract text
        buffer.Position = 0;
        var text = extension == ".pdf"
            ? ExtractPdfText(buffer)
            : ExtractDocxText(buffer);

        // Create the document entity
        var document = new Document
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = fileName,
            ContentType = contentType,
            BlobContainer = BlobContainer,
            BlobName = blobName,
            ContentHash = contentHash,
            FileSizeBytes = buffer.Length,
            UploadedAt = DateTime.UtcNow,
        };

        // Chunk and create Chunk entities
        var chunkTexts = _chunker.Chunk(text);
        var chunks = chunkTexts
            .Select((chunkText, index) => new Chunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                TenantId = tenantId,
                ChunkIndex = index,
                Text = chunkText,
                TokenCount = (int)Math.Ceiling(chunkText.Length / 4.0),
            })
            .ToList();

        document.ChunkCount = chunks.Count;

        _db.Documents.Add(document);
        _db.Chunks.AddRange(chunks);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Ingested document '{FileName}' → {DocumentId} with {ChunkCount} chunks.",
            fileName, document.Id, chunks.Count);

        return document;
    }

    // ── Text extraction ───────────────────────────────────────────────────────

    private static string ExtractPdfText(Stream stream)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(stream);
        foreach (Page page in pdf.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    private static string ExtractDocxText(Stream stream)
    {
        var sb = new StringBuilder();
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body is null) return string.Empty;

        foreach (var para in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            sb.AppendLine(para.InnerText);
        }
        return sb.ToString();
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
