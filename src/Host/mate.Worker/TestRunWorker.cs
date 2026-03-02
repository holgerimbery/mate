// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Core.Execution;
using mate.Data;
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace mate.Worker;

/// <summary>
/// Background service that polls the shared SQLite database for <see cref="Run"/> records
/// in "pending" status and executes them via <see cref="TestExecutionService"/>.
///
/// Polling the database (rather than an in-process queue) is required because the WebUI
/// and Worker run as separate Docker containers that share a single SQLite volume — the
/// in-process <c>IMessageQueue</c> cannot cross process boundaries.
/// </summary>
public sealed class TestRunWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TestRunWorker> _logger;

    public TestRunWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<TestRunWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TestRunWorker started. Polling database for pending runs every {Interval}s.",
            PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var executed = await PollAndExecuteOneAsync(stoppingToken);
                if (!executed)
                    await Task.Delay(PollInterval, stoppingToken);
                // If there was work, immediately poll again for the next pending run
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in TestRunWorker poll loop.");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("TestRunWorker stopped.");
    }

    /// <returns>true if a run was picked up and executed; false if no pending runs exist.</returns>
    private async Task<bool> PollAndExecuteOneAsync(CancellationToken ct)
    {
        // Phase 1: find the oldest pending run (short-lived scope, no tenant filter)
        Guid runId;
        Guid tenantId;

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<mateDbContext>();
            var pending = await db.Runs
                .IgnoreQueryFilters()
                .Where(r => r.Status == "pending")
                .OrderBy(r => r.StartedAt)
                .Select(r => new { r.Id, r.TenantId })
                .FirstOrDefaultAsync(ct);

            if (pending is null) return false;

            runId   = pending.Id;
            tenantId = pending.TenantId;
        }

        _logger.LogInformation(
            "Picked up pending Run {RunId} (TenantId={TenantId}). Executing…",
            runId, tenantId);

        // Phase 2: execute in a dedicated scope with the correct tenant context
        await using var execScope = _scopeFactory.CreateAsyncScope();

        if (execScope.ServiceProvider.GetRequiredService<mate.Data.ITenantContext>()
                is mate.Data.StaticTenantContext staticCtx)
            staticCtx.SetTenantId(tenantId);

        var executor = execScope.ServiceProvider.GetRequiredService<TestExecutionService>();

        try
        {
            await executor.ExecuteAsync(runId, ct);
            _logger.LogInformation("Run {RunId} completed successfully.", runId);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("cannot be started"))
        {
            // Another worker (or manual status change) already claimed this run — skip silently
            _logger.LogWarning("Run {RunId} is no longer pending: {Message}", runId, ex.Message);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Run {RunId} was cancelled during execution.", runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run {RunId} failed with an unhandled exception.", runId);

            // Mark the run as failed so the UI doesn't show it stuck as "running"
            try
            {
                await using var failScope = _scopeFactory.CreateAsyncScope();
                if (failScope.ServiceProvider.GetRequiredService<mate.Data.ITenantContext>()
                        is mate.Data.StaticTenantContext fCtx)
                    fCtx.SetTenantId(tenantId);

                var failDb = failScope.ServiceProvider.GetRequiredService<mateDbContext>();
                var run = await failDb.Runs.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Id == runId, ct);
                if (run is not null && run.Status is "pending" or "running")
                {
                    run.Status      = "failed";
                    run.CompletedAt = DateTime.UtcNow;
                    await failDb.SaveChangesAsync(ct);
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Failed to mark Run {RunId} as failed in the database.", runId);
            }
        }

        return true;
    }
}

