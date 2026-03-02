// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using mate.Domain.Contracts.Infrastructure;
using Microsoft.Extensions.Logging;

namespace mate.Infrastructure.Local;

/// <summary>
/// Phase 1 in-process message queue backed by <see cref="System.Threading.Channels.Channel{T}"/>.
///
/// Characteristics:
/// - Not durable — messages are lost on process restart.
/// - Suitable for single-instance local development and test environments.
/// - For multi-instance or durable queuing, use the RabbitMQ implementation or Phase 2 Service Bus.
///
/// One channel is created per queue name on first use.
/// </summary>
public sealed class InProcessMessageQueue : IMessageQueue, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, object> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<InProcessMessageQueue> _logger;
    private bool _disposed;

    public InProcessMessageQueue(ILogger<InProcessMessageQueue> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync<T>(string queueName, T payload, CancellationToken ct = default) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var channel = GetOrCreateChannel<T>(queueName);
        var json = JsonSerializer.Serialize(payload);
        var message = new InProcessQueueMessage(json, queueName);

        await channel.Writer.WriteAsync(message, ct);
        _logger.LogDebug("Enqueued message to '{QueueName}' ({Type}).", queueName, typeof(T).Name);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<QueueMessage<T>> ConsumeAsync<T>(
        string queueName,
        [EnumeratorCancellation] CancellationToken ct = default) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var channel = GetOrCreateChannel<T>(queueName);

        await foreach (var raw in channel.Reader.ReadAllAsync(ct))
        {
            T payload;
            try
            {
                payload = JsonSerializer.Deserialize<T>(raw.Json)
                    ?? throw new InvalidOperationException($"Deserialized null payload from queue '{queueName}'.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize message from queue '{QueueName}'.", queueName);
                continue;
            }

            var msg = new QueueMessage<T>
            {
                Payload       = payload,
                MessageId     = raw.MessageId,
                DeliveryCount = 1,
                EnqueuedAt    = raw.EnqueuedAt,
            };
            // In-process queue: no acknowledgement needed — no-ops are wired by default
            msg.SetCallbacks(_ => Task.CompletedTask, _ => Task.CompletedTask);
            yield return msg;
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private System.Threading.Channels.Channel<InProcessQueueMessage> GetOrCreateChannel<T>(string queueName)
    {
        return (System.Threading.Channels.Channel<InProcessQueueMessage>)
            _channels.GetOrAdd(queueName, _ =>
                System.Threading.Channels.Channel.CreateUnbounded<InProcessQueueMessage>(
                    new System.Threading.Channels.UnboundedChannelOptions { SingleReader = false }));
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        foreach (var channel in _channels.Values)
        {
            if (channel is System.Threading.Channels.Channel<InProcessQueueMessage> ch)
                ch.Writer.TryComplete();
        }
        return ValueTask.CompletedTask;
    }

    private sealed record InProcessQueueMessage(
        string Json,
        string QueueName,
        string MessageId = "",
        DateTimeOffset EnqueuedAt = default)
    {
        public string MessageId  { get; } = string.IsNullOrEmpty(MessageId) ? Guid.NewGuid().ToString() : MessageId;
        public DateTimeOffset EnqueuedAt { get; } = EnqueuedAt == default ? DateTimeOffset.UtcNow : EnqueuedAt;
    }
}
