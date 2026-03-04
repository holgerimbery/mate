// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using mate.Domain.Contracts.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace mate.Infrastructure.Azure;

/// <summary>
/// Phase 2 blob storage implementation backed by Azure Blob Storage (or Azurite in Container mode).
/// Configured via <see cref="AzureInfrastructureOptions"/>.
/// </summary>
public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _serviceClient;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        IOptions<AzureInfrastructureOptions> options,
        ILogger<AzureBlobStorageService> logger)
    {
        var connectionString = options.Value.BlobConnectionString
            ?? throw new InvalidOperationException(
                "Azure Blob Storage connection string is not configured. " +
                "Set AzureInfrastructure__BlobConnectionString.");

        // Parse the connection string into its components and build the client with an
        // explicit StorageSharedKeyCredential. This is required when BlobEndpoint uses a
        // non-localhost hostname (e.g. "azurite" in Docker) because the SDK's emulator
        // detection only triggers for 127.0.0.1/localhost, causing HMAC signature
        // mismatches when connection-string-based construction is used with newer SDK versions.
        var parts = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        if (parts.TryGetValue("BlobEndpoint", out var blobEndpoint)
            && parts.TryGetValue("AccountName", out var accountName)
            && parts.TryGetValue("AccountKey", out var accountKey))
        {
            // Explicit credential — works for both Azurite (any hostname) and real Azure Blob Storage
            var credential = new StorageSharedKeyCredential(accountName, accountKey);
            _serviceClient = new BlobServiceClient(new Uri(blobEndpoint), credential);
        }
        else
        {
            // Fallback: standard connection string (works for real Azure Blob Storage)
            _serviceClient = new BlobServiceClient(connectionString);
        }

        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        string containerName, string blobName, Stream content,
        string contentType, CancellationToken ct = default)
    {
        ValidateName(containerName, nameof(containerName));
        ValidateName(blobName, nameof(blobName));

        var container = _serviceClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blobClient = container.GetBlobClient(blobName);
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        await blobClient.UploadAsync(content, uploadOptions, ct);

        _logger.LogDebug("Uploaded blob: {Container}/{Blob}", containerName, blobName);
        return blobClient.Uri.ToString();
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(
        string containerName, string blobName, CancellationToken ct = default)
    {
        ValidateName(containerName, nameof(containerName));
        ValidateName(blobName, nameof(blobName));

        var blobClient = _serviceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);

        _logger.LogDebug("Downloaded blob: {Container}/{Blob}", containerName, blobName);
        return response.Value.Content;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string containerName, string blobName, CancellationToken ct = default)
    {
        ValidateName(containerName, nameof(containerName));
        ValidateName(blobName, nameof(blobName));

        var blobClient = _serviceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        var deleted = await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
        if (deleted)
            _logger.LogDebug("Deleted blob: {Container}/{Blob}", containerName, blobName);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string containerName, string blobName, CancellationToken ct = default)
    {
        ValidateName(containerName, nameof(containerName));
        ValidateName(blobName, nameof(blobName));

        var blobClient = _serviceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        var response = await blobClient.ExistsAsync(ct);
        return response.Value;
    }

    // ── Input validation ──────────────────────────────────────────────────────

    private static void ValidateName(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Name must not be null or whitespace.", paramName);
        // '/' is valid in Azure blob names (virtual directory prefix, e.g. "tenant-id/file.pdf").
        // Only block backslash and ".." to prevent path-traversal surprises.
        if (value.Contains('\\') || value.Contains(".."))
            throw new ArgumentException($"Name contains invalid path characters: {value}", paramName);
    }
}
