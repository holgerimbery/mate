// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Domain.Contracts.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace mate.Infrastructure.Local;

/// <summary>
/// Phase 1 blob storage implementation that stores files on the local filesystem.
/// Root directory is configured via <see cref="LocalInfrastructureOptions.BlobStoragePath"/>.
///
/// Thread safety: concurrent reads are safe; concurrent writes to the same blob are not
/// expected in normal operation (each run generates unique blob names).
/// </summary>
public sealed class LocalBlobStorageService : IBlobStorageService
{
    private readonly string _rootPath;
    private readonly ILogger<LocalBlobStorageService> _logger;

    public LocalBlobStorageService(
        IOptions<LocalInfrastructureOptions> options,
        ILogger<LocalBlobStorageService> logger)
    {
        _rootPath = options.Value.BlobStoragePath
            ?? throw new InvalidOperationException("Local blob storage path is not configured.");
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        string containerName, string blobName, Stream content,
        string contentType, CancellationToken ct = default)
    {
        ValidateName(containerName, nameof(containerName));
        ValidateName(blobName, nameof(blobName));

        var filePath = BuildPath(containerName, blobName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using var fileStream = new FileStream(
            filePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        await content.CopyToAsync(fileStream, ct);

        _logger.LogDebug("Uploaded blob: {Container}/{Blob}", containerName, blobName);

        // Return a local-path reference (Azure implementation returns a URI)
        return $"local://{containerName}/{blobName}";
    }

    /// <inheritdoc />
    public Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        ValidateName(containerName, nameof(containerName));
        ValidateName(blobName, nameof(blobName));

        var filePath = BuildPath(containerName, blobName);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Blob '{containerName}/{blobName}' not found.", filePath);

        Stream stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        _logger.LogDebug("Downloaded blob: {Container}/{Blob}", containerName, blobName);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        ValidateName(containerName, nameof(containerName));
        ValidateName(blobName, nameof(blobName));

        var filePath = BuildPath(containerName, blobName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogDebug("Deleted blob: {Container}/{Blob}", containerName, blobName);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        ValidateName(containerName, nameof(containerName));
        ValidateName(blobName, nameof(blobName));
        return Task.FromResult(File.Exists(BuildPath(containerName, blobName)));
    }

    // ── Path helpers ──────────────────────────────────────────────────────────

    private string BuildPath(string containerName, string blobName)
    {
        // Normalise slashes and prevent directory traversal
        var safeContainer = Path.GetFileName(containerName);
        var safeBlobName  = blobName.Replace('\\', '/');

        // Block attempts to escape the root directory
        var combined = Path.GetFullPath(Path.Combine(_rootPath, safeContainer, safeBlobName));
        if (!combined.StartsWith(Path.GetFullPath(_rootPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Attempted path traversal in blob storage access.");

        return combined;
    }

    private static void ValidateName(string name, string paramName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"'{paramName}' must not be empty.", paramName);
    }
}
