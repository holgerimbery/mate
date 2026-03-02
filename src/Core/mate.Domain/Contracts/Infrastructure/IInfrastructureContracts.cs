// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Contracts.Infrastructure;

// ─── Blob storage ─────────────────────────────────────────────────────────────

/// <summary>
/// Abstraction for binary / large-object storage.
/// Phase 1 implementation: local filesystem.
/// Phase 2 implementation: Azure Blob Storage.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>Upload content from a stream. Returns the blob's URL or path reference.</summary>
    Task<string> UploadAsync(string containerName, string blobName, Stream content, string contentType, CancellationToken ct = default);

    Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken ct = default);

    Task DeleteAsync(string containerName, string blobName, CancellationToken ct = default);

    Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken ct = default);
}

// ─── Secret management ────────────────────────────────────────────────────────

/// <summary>
/// Read/write interface for secret values.
/// Phase 1: environment variables or a local encrypted file.
/// Phase 2: Azure Key Vault.
/// </summary>
public interface ISecretService
{
    /// <param name="secretRef">
    /// The reference name stored in entity ConfigJson — never the raw secret value.
    /// </param>
    Task<string> GetSecretAsync(string secretRef, CancellationToken ct = default);

    Task SetSecretAsync(string secretRef, string secretValue, CancellationToken ct = default);

    Task DeleteSecretAsync(string secretRef, CancellationToken ct = default);
}

// ─── Message queue ────────────────────────────────────────────────────────────

/// <summary>
/// Represents one dequeued message. Callers must call
/// <see cref="CompleteAsync"/> or <see cref="AbandonAsync"/> exactly once.
/// </summary>
public class QueueMessage<T>
{
    public required T Payload { get; init; }
    public required string MessageId { get; init; }
    public int DeliveryCount { get; init; }
    public DateTimeOffset EnqueuedAt { get; init; }

    // Nullable — populated by InProcessMessageQueue; null means no-op (fire-and-forget queue)
    private Func<CancellationToken, Task>? _completeDelegate;
    private Func<CancellationToken, Task>? _abandonDelegate;

    /// <summary>Called by queue implementations to wire up completion callbacks.</summary>
    public void SetCallbacks(
        Func<CancellationToken, Task> complete,
        Func<CancellationToken, Task> abandon)
    {
        _completeDelegate = complete;
        _abandonDelegate  = abandon;
    }

    public Task CompleteAsync(CancellationToken ct = default)
        => _completeDelegate?.Invoke(ct) ?? Task.CompletedTask;

    public Task AbandonAsync(CancellationToken ct = default)
        => _abandonDelegate?.Invoke(ct) ?? Task.CompletedTask;
}

/// <summary>
/// Generic async message queue.
/// Phase 1: in-process <see cref="System.Threading.Channels.Channel{T}"/> or RabbitMQ.
/// Phase 2: Azure Service Bus.
/// </summary>
public interface IMessageQueue
{
    Task EnqueueAsync<T>(string queueName, T payload, CancellationToken ct = default) where T : class;

    IAsyncEnumerable<QueueMessage<T>> ConsumeAsync<T>(string queueName, CancellationToken ct = default) where T : class;
}

// ─── Backup service ───────────────────────────────────────────────────────────

/// <summary>
/// Backup and restore the application database.
/// Phase 1: SQLite file copy.
/// Phase 2: Azure SQL Export / restore.
/// </summary>
public interface IBackupService
{
    /// <summary>Creates a backup and writes it to the returned stream (caller disposes).</summary>
    Task<Stream> CreateBackupStreamAsync(CancellationToken ct = default);

    Task RestoreAsync(Stream backupStream, CancellationToken ct = default);
}
