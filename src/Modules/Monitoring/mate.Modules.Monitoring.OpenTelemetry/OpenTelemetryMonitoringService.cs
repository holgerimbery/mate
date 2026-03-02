using mate.Domain.Contracts.Modules;
using mate.Domain.Contracts.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace mate.Modules.Monitoring.OpenTelemetry;

/// <summary>
/// OpenTelemetry monitoring service.
///
/// Emits structured metrics and traces using the .NET OpenTelemetry SDK.
/// Exporters (OTLP, Prometheus, console…) are configured via <c>OpenTelemetry</c>
/// section in appsettings.json.
///
/// Key instruments:
///   - Meter:   "mate.runs"
///   - Counter: "mate.runs.total", "mate.runs.cases.pass", "mate.runs.cases.fail"
///   - Histogram: "mate.runs.latency_ms"
///   - ActivitySource: "mate.execution"
/// </summary>
public sealed class OpenTelemetryMonitoringService : IMonitoringService
{
    private static readonly ActivitySource ActivitySource = new("mate.execution");

    private readonly Meter _meter = new("mate.runs", "1.0.0");
    private readonly Counter<long> _runsTotal;
    private readonly Counter<long> _casesPass;
    private readonly Counter<long> _casesFail;
    private readonly Histogram<long> _latencyMs;
    private readonly ILogger<OpenTelemetryMonitoringService> _logger;

    public OpenTelemetryMonitoringService(ILogger<OpenTelemetryMonitoringService> logger)
    {
        _logger    = logger;
        _runsTotal = _meter.CreateCounter<long>("mate.runs.total",       "runs",   "Total test runs started.");
        _casesPass = _meter.CreateCounter<long>("mate.runs.cases.pass",  "cases",  "Passed test case evaluations.");
        _casesFail = _meter.CreateCounter<long>("mate.runs.cases.fail",  "cases",  "Failed test case evaluations.");
        _latencyMs = _meter.CreateHistogram<long>("mate.runs.latency_ms","ms",     "Agent response latency per test case.");
    }

    public void TrackRunStarted(RunStartedEvent evt)
    {
        _runsTotal.Add(1,
            new KeyValuePair<string, object?>("tenant_id", evt.TenantId.ToString()),
            new KeyValuePair<string, object?>("suite_id", evt.SuiteId.ToString()));

        _logger.LogInformation("[OTel] RunStarted {RunId}", evt.RunId);
    }

    public void TrackTestCaseResult(TestCaseResultEvent evt)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("tenant_id", evt.TenantId.ToString()),
            new KeyValuePair<string, object?>("run_id", evt.RunId.ToString()),
            new KeyValuePair<string, object?>("verdict", evt.Verdict),
        };

        if (evt.Verdict == "pass") _casesPass.Add(1, tags);
        else                       _casesFail.Add(1, tags);

        _latencyMs.Record(evt.LatencyMs, tags);
    }

    public void TrackRunCompleted(RunCompletedEvent evt)
    {
        _logger.LogInformation("[OTel] RunCompleted {RunId} status={Status} passRate={Rate:P2} P95={P95}ms",
            evt.RunId, evt.FinalStatus, evt.PassRate, evt.P95LatencyMs);
    }

    public void TrackLlmUsage(LlmUsageEvent evt)
    {
        _logger.LogInformation("[OTel] LlmUsage RunId={RunId} model={Model} promptTokens={P} completionTokens={C}",
            evt.RunId, evt.Model, evt.PromptTokens, evt.CompletionTokens);
    }

    public void TrackException(Exception ex, Dictionary<string, string>? properties = null)
        => _logger.LogError(ex, "[OTel] Exception");

    public void TrackCustomEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null)
        => _logger.LogInformation("[OTel] CustomEvent {Name}", eventName);
}

// ── Module descriptor ─────────────────────────────────────────────────────────

/// <summary>OpenTelemetry monitoring module.</summary>
public sealed class OpenTelemetryMonitoringModule : IMonitoringModule
{
    public string ProviderType => "OpenTelemetry";
    public string DisplayName  => "OpenTelemetry (OTLP / Prometheus)";
    public ModuleTier Tier     => ModuleTier.Free;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IMonitoringService, OpenTelemetryMonitoringService>();
        services.AddSingleton<IMonitoringModule, OpenTelemetryMonitoringModule>();

        var otlpEndpoint = config["OpenTelemetry:Endpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("mate"))
            .WithMetrics(b =>
            {
                b.AddMeter("mate.runs");
                b.AddAspNetCoreInstrumentation();
                if (!string.IsNullOrEmpty(otlpEndpoint))
                    b.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                else
                    b.AddConsoleExporter();
            })
            .WithTracing(b =>
            {
                b.AddSource("mate.execution");
                b.AddAspNetCoreInstrumentation();
                if (!string.IsNullOrEmpty(otlpEndpoint))
                    b.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                else
                    b.AddConsoleExporter();
            });
    }
}

/// <summary>DI extension for the OpenTelemetry monitoring module.</summary>
public static class OpenTelemetryMonitoringModuleExtensions
{
    public static IServiceCollection AddmateOpenTelemetryMonitoring(
        this IServiceCollection services,
        IConfiguration config)
    {
        var module = new OpenTelemetryMonitoringModule();
        module.RegisterServices(services, config);
        return services;
    }
}
