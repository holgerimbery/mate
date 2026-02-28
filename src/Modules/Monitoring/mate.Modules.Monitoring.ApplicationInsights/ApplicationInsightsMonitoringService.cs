using mate.Domain.Contracts.Monitoring;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mate.Modules.Monitoring.ApplicationInsights;

/// <summary>
/// Application Insights monitoring service.
///
/// Tracks all mate telemetry events as Application Insights custom events
/// and metrics, using the <see cref="TelemetryClient"/>.
///
/// Required configuration: "ApplicationInsights:InstrumentationKey" (or
/// "ApplicationInsights:ConnectionString" for workspace-based resources).
/// </summary>
public sealed class ApplicationInsightsMonitoringService : IMonitoringService
{
    private readonly TelemetryClient _telemetry;
    private readonly ILogger<ApplicationInsightsMonitoringService> _logger;

    public ApplicationInsightsMonitoringService(
        TelemetryClient telemetry,
        ILogger<ApplicationInsightsMonitoringService> logger)
    {
        _telemetry = telemetry;
        _logger    = logger;
    }

    public void TrackRunStarted(RunStartedEvent evt)
    {
        var t = new EventTelemetry("mate:RunStarted");
        t.Properties["RunId"]      = evt.RunId.ToString();
        t.Properties["TenantId"]   = evt.TenantId.ToString();
        t.Properties["SuiteId"]    = evt.SuiteId.ToString();
        t.Properties["AgentId"]    = evt.AgentId.ToString();
        t.Metrics["TotalCases"]    = evt.TotalTestCases;
        _telemetry.TrackEvent(t);
        _logger.LogDebug("[AppInsights] RunStarted {RunId}", evt.RunId);
    }

    public void TrackTestCaseResult(TestCaseResultEvent evt)
    {
        var t = new EventTelemetry("mate:TestCaseResult");
        t.Properties["RunId"]    = evt.RunId.ToString();
        t.Properties["TenantId"] = evt.TenantId.ToString();
        t.Properties["Verdict"]  = evt.Verdict;
        t.Metrics["Score"]       = evt.OverallScore;
        t.Metrics["LatencyMs"]   = evt.LatencyMs;
        _telemetry.TrackEvent(t);

        _telemetry.TrackMetric("mate:latency_ms", evt.LatencyMs);
    }

    public void TrackRunCompleted(RunCompletedEvent evt)
    {
        var t = new EventTelemetry("mate:RunCompleted");
        t.Properties["RunId"]    = evt.RunId.ToString();
        t.Properties["TenantId"] = evt.TenantId.ToString();
        t.Properties["Status"]   = evt.FinalStatus;
        t.Metrics["PassCount"]   = evt.PassCount;
        t.Metrics["FailCount"]   = evt.FailCount;
        t.Metrics["PassRate"]    = evt.PassRate;
        t.Metrics["P95LatencyMs"]= evt.P95LatencyMs;
        t.Metrics["DurationMs"]  = evt.Duration.TotalMilliseconds;
        _telemetry.TrackEvent(t);
    }

    public void TrackLlmUsage(LlmUsageEvent evt)
    {
        var t = new EventTelemetry("mate:LlmUsage");
        t.Properties["RunId"]    = evt.RunId.ToString();
        t.Properties["TenantId"] = evt.TenantId.ToString();
        t.Properties["Model"]    = evt.Model;
        t.Properties["Purpose"]  = evt.Purpose;
        t.Metrics["PromptTokens"]     = evt.PromptTokens;
        t.Metrics["CompletionTokens"] = evt.CompletionTokens;
        _telemetry.TrackEvent(t);
    }

    public void TrackException(Exception ex, Dictionary<string, string>? properties = null)
    {
        var t = new ExceptionTelemetry(ex);
        if (properties is not null)
            foreach (var (k, v) in properties)
                t.Properties[k] = v;
        _telemetry.TrackException(t);
    }

    public void TrackCustomEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null)
    {
        var t = new EventTelemetry(eventName);
        if (properties is not null)
            foreach (var (k, v) in properties)
                t.Properties[k] = v;
        if (metrics is not null)
            foreach (var (k, v) in metrics)
                t.Metrics[k] = v;
        _telemetry.TrackEvent(t);
    }
}

// ── Module descriptor ─────────────────────────────────────────────────────────

/// <summary>Application Insights monitoring module.</summary>
public sealed class ApplicationInsightsMonitoringModule : IMonitoringModule
{
    public string ProviderType => "ApplicationInsights";
    public string DisplayName  => "Azure Application Insights";

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddApplicationInsightsTelemetry(config);
        services.AddSingleton<IMonitoringService, ApplicationInsightsMonitoringService>();
        services.AddSingleton<IMonitoringModule, ApplicationInsightsMonitoringModule>();
    }
}

/// <summary>DI extension for Application Insights monitoring module.</summary>
public static class ApplicationInsightsModuleExtensions
{
    public static IServiceCollection AddmateApplicationInsightsMonitoring(
        this IServiceCollection services,
        IConfiguration config)
    {
        var module = new ApplicationInsightsMonitoringModule();
        module.RegisterServices(services, config);
        return services;
    }
}
