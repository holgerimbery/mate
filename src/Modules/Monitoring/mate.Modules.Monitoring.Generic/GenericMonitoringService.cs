using mate.Domain.Contracts.Modules;
using mate.Domain.Contracts.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mate.Modules.Monitoring.Generic;

/// <summary>
/// Generic / structured-log monitoring service.
///
/// Emits all observability events to the standard <see cref="ILogger"/> so that
/// any configured Serilog sink (console, file, Seq, etc.) captures the telemetry
/// without requiring an external monitoring service.
///
/// Use this module for local development and as a fallback when neither
/// Application Insights nor OpenTelemetry is configured.
/// </summary>
public sealed class GenericMonitoringService : IMonitoringService
{
    private readonly ILogger<GenericMonitoringService> _logger;

    public GenericMonitoringService(ILogger<GenericMonitoringService> logger)
    {
        _logger = logger;
    }

    public void TrackRunStarted(RunStartedEvent evt)
        => _logger.LogInformation("[RunStarted] RunId={RunId} TenantId={TenantId} SuiteId={SuiteId} AgentId={AgentId} TotalCases={Total} StartedAt={StartedAt}",
            evt.RunId, evt.TenantId, evt.SuiteId, evt.AgentId, evt.TotalTestCases, evt.StartedAt);

    public void TrackTestCaseResult(TestCaseResultEvent evt)
        => _logger.LogInformation("[TestCaseResult] ResultId={ResultId} RunId={RunId} Verdict={Verdict} Score={Score:F3} Latency={Latency}ms",
            evt.ResultId, evt.RunId, evt.Verdict, evt.OverallScore, evt.LatencyMs);

    public void TrackRunCompleted(RunCompletedEvent evt)
        => _logger.LogInformation("[RunCompleted] RunId={RunId} Status={Status} Pass={Pass} Fail={Fail} Error={Error} PassRate={Rate:P2} P95={P95}ms Duration={Duration}",
            evt.RunId, evt.FinalStatus, evt.PassCount, evt.FailCount, evt.ErrorCount, evt.PassRate, evt.P95LatencyMs, evt.Duration);

    public void TrackLlmUsage(LlmUsageEvent evt)
        => _logger.LogInformation("[LlmUsage] RunId={RunId} Model={Model} PromptTokens={Prompt} CompletionTokens={Completion} Purpose={Purpose}",
            evt.RunId, evt.Model, evt.PromptTokens, evt.CompletionTokens, evt.Purpose);

    public void TrackException(Exception ex, Dictionary<string, string>? properties = null)
    {
        var props = properties is null ? string.Empty : string.Join(", ", properties.Select(p => $"{p.Key}={p.Value}"));
        _logger.LogError(ex, "[Exception] {Props}", props);
    }

    public void TrackCustomEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null)
    {
        var props   = properties is null ? string.Empty : string.Join(", ", properties.Select(p => $"{p.Key}={p.Value}"));
        var metrStr = metrics is null ? string.Empty : string.Join(", ", metrics.Select(m => $"{m.Key}={m.Value:F3}"));
        _logger.LogInformation("[CustomEvent] {EventName} Props=[{Props}] Metrics=[{Metrics}]", eventName, props, metrStr);
    }
}

// ── Module descriptor ─────────────────────────────────────────────────────────

/// <summary>Generic monitoring module that routes telemetry to ILogger.</summary>
public sealed class GenericMonitoringModule : IMonitoringModule
{
    public string ProviderType => "Generic";
    public string DisplayName  => "Generic (structured log)";
    public ModuleTier Tier     => ModuleTier.Free;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IMonitoringService, GenericMonitoringService>();
        services.AddSingleton<IMonitoringModule, GenericMonitoringModule>();
    }
}

/// <summary>DI extension for the Generic monitoring module.</summary>
public static class GenericMonitoringModuleExtensions
{
    public static IServiceCollection AddmateGenericMonitoring(this IServiceCollection services)
    {
        services.AddSingleton<GenericMonitoringService>();
        services.AddSingleton<GenericMonitoringModule>();
        return services;
    }
}
