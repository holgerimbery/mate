using mate.Core.Execution;
using mate.Data;
using mate.Domain.Contracts.Infrastructure;
using mate.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace mate.Worker;

/// <summary>
/// Background service that dequeues <see cref="TestRunJob"/> messages from the
/// <c>"test-runs"</c> queue and delegates execution to <see cref="TestExecutionService"/>.
///
/// The worker honours graceful shutdown: it completes the current test case before
/// exiting so that results are not lost.
///
/// Message flow:
///   1. Dequeue a <see cref="TestRunJob"/> from IMessageQueue
///   2. Create a DI scope (so EF DbContext + ITenantContext are properly scoped)
///   3. Set the tenant context for the current scope
///   4. Call TestExecutionService.ExecuteAsync(job.RunId)
///   5. Loop
/// </summary>
public sealed class TestRunWorker : BackgroundService
{
    private const string QueueName = "test-runs";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageQueue _queue;
    private readonly ILogger<TestRunWorker> _logger;

    public TestRunWorker(
        IServiceScopeFactory scopeFactory,
        IMessageQueue queue,
        ILogger<TestRunWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue        = queue;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TestRunWorker started. Listening on queue '{Queue}'.", QueueName);

        await foreach (var message in _queue.ConsumeAsync<TestRunJob>(QueueName, stoppingToken))
        {
            if (message is null) continue;

            _logger.LogInformation(
                "Dequeued TestRunJob. RunId={RunId} TenantId={TenantId} JobId={JobId}",
                message.Payload.RunId, message.Payload.TenantId, message.Payload.JobId);

            await ExecuteJobAsync(message, stoppingToken);
        }

        _logger.LogInformation("TestRunWorker stopped.");
    }

    private async Task ExecuteJobAsync(QueueMessage<TestRunJob> message, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        // Override the tenant context for this scope so EF query filters apply correctly
        var tenantContext = scope.ServiceProvider
            .GetRequiredService<mate.Data.ITenantContext>();

        // If the tenant context supports mutation (e.g. StaticTenantContext), set it
        if (tenantContext is mate.Data.StaticTenantContext staticCtx)
            staticCtx.SetTenantId(message.Payload.TenantId);

        var executor = scope.ServiceProvider.GetRequiredService<TestExecutionService>();

        try
        {
            await executor.ExecuteAsync(message.Payload.RunId, ct);
            _logger.LogInformation("TestRunJob {JobId} completed (RunId={RunId}).",
                message.Payload.JobId, message.Payload.RunId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("TestRunJob {JobId} was cancelled.", message.Payload.JobId);
            // No re-throw — let the worker loop handle it via the stoppingToken
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TestRunJob {JobId} failed (RunId={RunId}).",
                message.Payload.JobId, message.Payload.RunId);
            // Job stays in the queue if it was peek-mode; currently message is simply dropped
            // In a production queue-based system (AzureServiceBus) you would dead-letter here
        }
    }
}
