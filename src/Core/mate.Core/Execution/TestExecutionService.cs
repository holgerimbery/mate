// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using mate.Data;
using mate.Domain.Contracts.Infrastructure;
using mate.Domain.Contracts.Modules;
using mate.Domain.Contracts.Monitoring;
using mate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace mate.Core.Execution;

/// <summary>
/// Executes a single test run: iterates over all <see cref="TestCase"/>s in the suite,
/// sends messages to the agent via the registered <see cref="IAgentConnector"/>,
/// evaluates responses through the <see cref="IJudgeProvider"/>, and persists results.
///
/// Progress is persisted to the database after each test case — a crash mid-run
/// will leave a partial run in "running" state which can be inspected.
/// </summary>
public sealed class TestExecutionService
{
    private readonly mateDbContext _db;
    private readonly mateModuleRegistry _registry;
    private readonly ISecretService _secrets;
    private readonly IMonitoringService? _monitoring;
    private readonly ILogger<TestExecutionService> _logger;
    private readonly bool _allowRawSecretFallback;

    public TestExecutionService(
        mateDbContext db,
        mateModuleRegistry registry,
        ISecretService secrets,
        IConfiguration config,
        ILogger<TestExecutionService> logger,
        IMonitoringService? monitoring = null)
    {
        _db = db;
        _registry = registry;
        _secrets = secrets;
        _logger = logger;
        _monitoring = monitoring;
        _allowRawSecretFallback = !config.GetValue<bool>("AzureInfrastructure:UseKeyVaultForSecrets", false);
    }

    /// <summary>
    /// Runs a previously-created <see cref="Run"/> record.
    /// The run must be in "pending" state; throws <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    public async Task ExecuteAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await LoadRunAsync(runId, ct);
        if (run.Status != "pending")
            throw new InvalidOperationException($"Run {runId} is in status '{run.Status}' and cannot be started.");

        var suite = run.Suite ?? throw new InvalidOperationException($"Run {runId} has no associated TestSuite.");
        var agent = run.Agent ?? throw new InvalidOperationException($"Run {runId} has no associated Agent.");

        _logger.LogInformation("Starting run {RunId} — suite '{SuiteName}', agent '{AgentName}'.",
            runId, suite.Name, agent.Name);

        var started = DateTimeOffset.UtcNow;
        run.Status = "running";
        run.StartedAt = started.UtcDateTime;
        await _db.SaveChangesAsync(ct);

        _monitoring?.TrackRunStarted(new RunStartedEvent(
            run.Id, run.TenantId, suite.Id, agent.Id,
            run.TotalTestCases, started));

        // Load judge setting with fallback priority: suite override → agent override → tenant default
        var judgeSetting = await ResolveJudgeSettingAsync(suite, agent, run.TenantId, ct);
        var judgeProvider = _registry.GetJudgeProvider(judgeSetting.ProviderType);
        var judgeSnapshot = MapToSnapshot(judgeSetting, await ResolveSecretsAsync(judgeSetting, ct));
        var rubricCriteria = await LoadRubricCriteriaAsync(judgeSetting.Id, ct);

        // Load connector for this agent
        var connectorConfig = agent.ConnectorConfigs
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Agent {agent.Id} has no connector configurations.");
        var connectorModule = _registry.GetConnector(connectorConfig.ConnectorType);

        var agentConfig = BuildConnectionConfig(connectorConfig, await ResolveConnectorSecretsAsync(connectorConfig, ct));
        var connector = connectorModule.CreateConnector(agentConfig);

        var selectedCaseIds = ParseSelectedCaseIds(run.ErrorMessage);
        var testCases = await LoadTestCasesAsync(suite.Id, selectedCaseIds, ct);
        int pass = 0, fail = 0, skip = 0, error = 0;
        var latencies = new List<long>(testCases.Count);

        foreach (var testCase in testCases)
        {
            if (ct.IsCancellationRequested) break;

            var result = await ExecuteTestCaseAsync(run, testCase, connector, agentConfig, judgeProvider, judgeSnapshot, rubricCriteria, ct);

            _db.Results.Add(result);
            _db.TranscriptMessages.AddRange(result.Transcript);
            await _db.SaveChangesAsync(ct);

            latencies.Add(result.LatencyMs);

            switch (result.Verdict)
            {
                case "pass":    pass++;  break;
                case "fail":    fail++;  break;
                case "skipped": skip++;  break;
                default:        error++; break;
            }

            _monitoring?.TrackTestCaseResult(new TestCaseResultEvent(
                result.Id, run.Id, run.TenantId,
                result.Verdict, result.OverallScore, result.LatencyMs));

            // Throttle: wait between tests to avoid rate-limit errors on the target agent
            if (suite.DelayBetweenTestsMs > 0 && !ct.IsCancellationRequested)
            {
                _logger.LogDebug("Throttling: waiting {DelayMs} ms before next test case.", suite.DelayBetweenTestsMs);
                await Task.Delay(suite.DelayBetweenTestsMs, ct);
            }
        }

        // Compute run statistics
        latencies.Sort();
        run.PassedCount    = pass;
        run.FailedCount    = fail;
        run.SkippedCount   = skip;
        run.TotalTestCases = pass + fail + skip + error;
        run.AverageLatencyMs  = latencies.Count > 0 ? latencies.Average() : 0;
        run.MedianLatencyMs   = CalculatePercentile(latencies, 50);
        run.P95LatencyMs      = CalculatePercentile(latencies, 95);
        run.Status     = ct.IsCancellationRequested ? "failed" : "completed";
        run.CompletedAt = DateTime.UtcNow;
        if (run.Status == "completed")
        {
            // Clear selected-testcase metadata once rerun completed successfully.
            run.ErrorMessage = null;
        }

        await _db.SaveChangesAsync(ct);

        _monitoring?.TrackRunCompleted(new RunCompletedEvent(
            run.Id, run.TenantId, run.Status,
            pass, fail, error,
            run.TotalTestCases > 0 ? (double)pass / run.TotalTestCases : 0,
            latencies.Count > 0 ? latencies.Average() : 0,
            (long)run.MedianLatencyMs,
            (long)run.P95LatencyMs,
            DateTime.UtcNow - run.StartedAt));

        _logger.LogInformation(
            "Run {RunId} completed — {Pass} pass / {Fail} fail / {Skip} skipped / {Error} error. Status: {Status}.",
            run.Id, pass, fail, skip, error, run.Status);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<Result> ExecuteTestCaseAsync(
        Run run,
        TestCase testCase,
        IAgentConnector connector,
        AgentConnectionConfig agentConfig,
        IJudgeProvider judge,
        JudgeSettingSnapshot judgeSnapshot,
        IReadOnlyList<EvaluationCriterion> rubricCriteria,
        CancellationToken ct)
    {
        var result = new Result
        {
            Id = Guid.NewGuid(),
            TenantId = run.TenantId,
            RunId = run.Id,
            TestCaseId = testCase.Id,
            Verdict = "error",
            EvaluatedAt = DateTime.UtcNow,
        };

        var transcript = new List<TranscriptMessage>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var session = await connector.StartConversationAsync(agentConfig, ct);

            // Send each user turn in sequence (multi-turn support)
            AgentResponse? lastResponse = null;
            for (int i = 0; i < testCase.UserInput.Length; i++)
            {
                var userMsg = testCase.UserInput[i];

                transcript.Add(new TranscriptMessage
                {
                    Id = Guid.NewGuid(), TenantId = run.TenantId, ResultId = result.Id,
                    Role = "user", Content = userMsg, TurnIndex = i * 2,
                    Timestamp = DateTime.UtcNow,
                });

                lastResponse = await connector.SendMessageAsync(session, userMsg, ct);

                transcript.Add(new TranscriptMessage
                {
                    Id = Guid.NewGuid(), TenantId = run.TenantId, ResultId = result.Id,
                    Role = "bot", Content = lastResponse.Text,
                    RawActivityJson = lastResponse.RawActivityJson,
                    TurnIndex = i * 2 + 1, LatencyMs = lastResponse.LatencyMs,
                    Timestamp = DateTime.UtcNow,
                });
            }

            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;

            if (lastResponse is null)
            {
                result.Verdict = "error";
                result.Rationale = "No response received.";
                return result;
            }

            // Evaluate with the judge
            var request = new JudgeRequest
            {
                UserInput = testCase.UserInput,
                BotResponse = lastResponse.Text,
                AcceptanceCriteria = testCase.AcceptanceCriteria,
                ReferenceAnswer = testCase.ReferenceAnswer,
                Transcript = transcript,
                JudgeSetting = judgeSnapshot,
                RubricCriteria = rubricCriteria,
            };

            var verdict = await judge.EvaluateAsync(request, ct);

            result.TaskSuccessScore  = verdict.TaskSuccessScore;
            result.IntentMatchScore  = verdict.IntentMatchScore;
            result.FactualityScore   = verdict.FactualityScore;
            result.HelpfulnessScore  = verdict.HelpfulnessScore;
            result.SafetyScore       = verdict.SafetyScore;
            result.OverallScore      = verdict.OverallScore;
            result.Verdict           = verdict.Verdict;
            result.Rationale         = verdict.Rationale;
            result.Citations         = verdict.Citations.ToArray();
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            result.Verdict   = "error";
            result.Rationale = "Test case cancelled.";
            _logger.LogWarning("Test case {TestCaseId} cancelled.", testCase.Id);
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = sw.ElapsedMilliseconds;
            // Detect rate-limit errors by exception type name — avoids a hard assembly reference
            // to the connector module from the Core layer.
            if (ex.GetType().Name == "AgentRateLimitException")
            {
                result.Verdict   = "skipped";
                result.Rationale = $"Rate limit reached — retry later. ({ex.Message})";
                _logger.LogWarning("Test case {TestCaseId} skipped due to rate limit: {Message}", testCase.Id, ex.Message);
            }
            else
            {
                result.Verdict   = "error";
                result.Rationale = ex.Message;
                _logger.LogError(ex, "Test case {TestCaseId} failed with exception.", testCase.Id);
                _monitoring?.TrackException(ex, new Dictionary<string, string>
                {
                    ["RunId"]      = run.Id.ToString(),
                    ["TestCaseId"] = testCase.Id.ToString(),
                });
            }
        }
        finally
        {
            result.Transcript = transcript;
        }

        return result;
    }

    private async Task<Run> LoadRunAsync(Guid runId, CancellationToken ct)
    {
        return await _db.Runs
            .IgnoreQueryFilters()
            .Include(r => r.Suite)
            .Include(r => r.Agent).ThenInclude(a => a!.ConnectorConfigs)
            .FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new InvalidOperationException($"Run {runId} not found.");
    }

    private static IReadOnlyCollection<Guid>? ParseSelectedCaseIds(string? runErrorMessage)
    {
        const string Prefix = "selected-testcases:";
        if (string.IsNullOrWhiteSpace(runErrorMessage) || !runErrorMessage.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return runErrorMessage[Prefix.Length..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
    }

    private async Task<List<TestCase>> LoadTestCasesAsync(Guid suiteId, IReadOnlyCollection<Guid>? selectedCaseIds, CancellationToken ct)
    {
        IQueryable<TestCase> query = _db.TestCases.Where(tc => tc.SuiteId == suiteId);

        if (selectedCaseIds is { Count: > 0 })
            query = query.Where(tc => selectedCaseIds.Contains(tc.Id));
        else
            query = query.Where(tc => tc.IsActive);

        return await query
            .OrderBy(tc => tc.SortOrder)
            .ToListAsync(ct);
    }

    private async Task<JudgeSetting> ResolveJudgeSettingAsync(
        TestSuite suite, Agent agent, Guid tenantId, CancellationToken ct)
    {
        // Priority: suite override → agent override → tenant/platform default
        var judgeId = suite.JudgeSettingId ?? agent.JudgeSettingId;

        if (judgeId.HasValue)
        {
            var setting = await _db.JudgeSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(j => j.Id == judgeId.Value, ct);
            if (setting is not null) return setting;
        }

        // Fallback: tenant-specific default → platform-level default (seeded under PlatformTenantId)
        // Note: mateDbSeeder seeds the global default with TenantId = 00000000-0000-0000-0000-000000000001
        var platformTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var platformDefault = await _db.JudgeSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.TenantId == tenantId && j.IsDefault, ct)
            ?? await _db.JudgeSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(j => j.TenantId == platformTenantId, ct)
            ?? throw new InvalidOperationException("No JudgeSetting configured. Seed the database or create one.");

        return platformDefault;
    }

    private async Task<IReadOnlyList<EvaluationCriterion>> LoadRubricCriteriaAsync(
        Guid judgeSettingId, CancellationToken ct)
    {
        var criteria = await _db.RubricCriterias
            .IgnoreQueryFilters()
            .Where(c => _db.RubricSets
                .IgnoreQueryFilters()
                .Where(rs => rs.JudgeSettingId == judgeSettingId && !rs.IsDraft)
                .Select(rs => rs.Id)
                .Contains(c.RubricSetId))
            .OrderBy(c => c.SortOrder)
            .Select(c => new EvaluationCriterion(c.Name, c.EvaluationType, c.Pattern, c.Weight, c.IsMandatory))
            .ToListAsync(ct);
        return criteria;
    }

    private async Task<(string? endpoint, string? apiKey)> ResolveSecretsAsync(
        JudgeSetting setting, CancellationToken ct)
    {
        var endpoint = string.IsNullOrWhiteSpace(setting.EndpointRef)
            ? null
            : await TryGetSecretAsync(setting.EndpointRef, ct, allowRawFallback: true);

        var apiKey = string.IsNullOrWhiteSpace(setting.ApiKeyRef)
            ? null
            : await TryGetSecretAsync(setting.ApiKeyRef, ct, allowRawFallback: _allowRawSecretFallback);

        return (endpoint, apiKey);
    }

    private async Task<Dictionary<string, string>> ResolveConnectorSecretsAsync(
        AgentConnectorConfig config, CancellationToken ct)
    {
        // Discover fields typed "secret" or "password" via the module's ConfigFieldDefinition,
        // read the ref name from ConfigJson, and resolve it via ISecretService.
        // Falls back to using the ref name as the raw value if the secret store has no entry
        // (supports local dev where the raw secret is stored directly in ConfigJson).
        var module = _registry.GetConnector(config.ConnectorType);
        var fieldDefs = module.GetConfigDefinition()
            .Where(f => f.FieldType is "secret" or "password");

        var configNode = System.Text.Json.JsonSerializer
            .Deserialize<System.Text.Json.Nodes.JsonObject>(config.ConfigJson);
        if (configNode is null) return [];

        var resolved = new Dictionary<string, string>();
        foreach (var field in fieldDefs)
        {
            if (!configNode.TryGetPropertyValue(field.Key, out var node) || node is null) continue;
            var refName = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(refName)) continue;
            var resolvedValue = await TryGetSecretAsync(refName, ct, allowRawFallback: _allowRawSecretFallback);
            if (!string.IsNullOrWhiteSpace(resolvedValue))
                resolved[refName] = resolvedValue;
        }
        return resolved;
    }

    private async Task<string?> TryGetSecretAsync(string secretRef, CancellationToken ct, bool allowRawFallback)
    {
        try
        {
            return await _secrets.GetSecretAsync(secretRef, ct);
        }
        catch (Exception ex)
        {
            if (allowRawFallback)
            {
                _logger.LogWarning(ex, "Secret reference '{SecretRef}' could not be resolved; treating ref as raw value.", secretRef);
                // Fall back to treating the ref itself as the raw value.
                // This supports legacy configs where raw keys were stored directly in the ref field.
                return secretRef;
            }

            throw new InvalidOperationException(
                $"Secret reference '{secretRef}' could not be resolved in key-vault mode. Store the secret via Settings/Wizard so a valid reference is persisted.",
                ex);
        }
    }

    private static JudgeSettingSnapshot MapToSnapshot(
        JudgeSetting s, (string? endpoint, string? apiKey) secrets)
        => new(
            s.ProviderType, s.PromptTemplate,
            s.TaskSuccessWeight, s.IntentMatchWeight, s.FactualityWeight,
            s.HelpfulnessWeight, s.SafetyWeight, s.PassThreshold, s.UseReferenceAnswer,
            s.Model, s.Temperature, s.TopP, s.MaxOutputTokens,
            secrets.endpoint, secrets.apiKey);

    private static AgentConnectionConfig BuildConnectionConfig(
        AgentConnectorConfig config, Dictionary<string, string> resolvedSecrets)
        => new()
        {
            ConnectorType   = config.ConnectorType,
            ConfigJson      = config.ConfigJson,
            ResolvedSecrets = resolvedSecrets,
        };

    /// <summary>
    /// Calculates a percentile from a sorted list of values.
    /// </summary>
    private static double CalculatePercentile(List<long> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0) return 0;

        double index = (percentile / 100.0) * (sortedValues.Count - 1);
        int lower = (int)index;
        int upper = lower + 1;

        if (upper >= sortedValues.Count)
            return sortedValues[lower];

        double fraction = index - lower;
        return sortedValues[lower] + fraction * (sortedValues[upper] - sortedValues[lower]);
    }
}
