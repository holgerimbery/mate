using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mate.Modules.Testing.HybridJudge;

/// <summary>
/// Hybrid judge that combines deterministic rubrics evaluation with LLM scoring.
///
/// Strategy:
/// 1. Run RubricsJudge first (fast, free, deterministic).
///    - If any mandatory criterion fails → immediate "fail", no LLM call.
///    - If weighted rubrics score is below the rubrics-fail-threshold (default 0.5) → "fail", no LLM call.
/// 2. Call ModelAsJudge for the LLM-based dimensions.
/// 3. Combine both scores using configurable weights:
///    - RubricsWeight (default 0.4) for the rubrics score
///    - LlmWeight     (default 0.6) for the LLM overall score
///    Combined score is compared against JudgeSetting.PassThreshold.
/// </summary>
public sealed class HybridJudgeProvider : IJudgeProvider
{
    private readonly IJudgeProvider _rubricsProvider;
    private readonly IJudgeProvider _llmProvider;
    private readonly ILogger<HybridJudgeProvider> _logger;

    // Configurable blend weights (could be surfaced as JudgeSetting fields in future)
    private const double RubricsWeight      = 0.4;
    private const double LlmWeight          = 0.6;
    private const double RubricsFailGate    = 0.5;  // mandatory-pass threshold before LLM is consulted

    public string ProviderType => "Hybrid";

    public HybridJudgeProvider(
        [FromKeyedServices("Rubrics")] IJudgeProvider rubricsProvider,
        [FromKeyedServices("ModelAsJudge")] IJudgeProvider llmProvider,
        ILogger<HybridJudgeProvider> logger)
    {
        _rubricsProvider = rubricsProvider;
        _llmProvider     = llmProvider;
        _logger          = logger;
    }

    /// <inheritdoc />
    public async Task<JudgeVerdict> EvaluateAsync(JudgeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ── Step 1: Deterministic rubrics pass ───────────────────────────────
        JudgeVerdict rubricsVerdict;
        if (request.RubricCriteria.Count > 0)
        {
            rubricsVerdict = await _rubricsProvider.EvaluateAsync(request, ct);
            _logger.LogDebug("Hybrid — rubrics verdict: {V}, score: {S:F3}", rubricsVerdict.Verdict, rubricsVerdict.OverallScore);

            if (rubricsVerdict.Verdict == "fail" &&
                ContainsMandatoryFailure(rubricsVerdict.Rationale))
            {
                // Hard gate: do not call the LLM
                return rubricsVerdict with
                {
                    Rationale = $"[Hybrid] Mandatory rubric failed. {rubricsVerdict.Rationale}"
                };
            }

            if (rubricsVerdict.OverallScore < RubricsFailGate)
            {
                _logger.LogDebug("Hybrid: rubrics score below gate ({S:F3} < {G}); skipping LLM.", rubricsVerdict.OverallScore, RubricsFailGate);
                return rubricsVerdict with
                {
                    Rationale = $"[Hybrid] Rubrics score {rubricsVerdict.OverallScore:F3} below gate {RubricsFailGate}. LLM not consulted. {rubricsVerdict.Rationale}"
                };
            }
        }
        else
        {
            // No rubrics defined — treat as perfect rubrics score
            rubricsVerdict = new JudgeVerdict { Verdict = "pass", OverallScore = 1.0 };
        }

        // ── Step 2: LLM evaluation ────────────────────────────────────────────
        var llmVerdict = await _llmProvider.EvaluateAsync(request, ct);
        _logger.LogDebug("Hybrid — LLM verdict: {V}, score: {S:F3}", llmVerdict.Verdict, llmVerdict.OverallScore);

        if (llmVerdict.Verdict == "error")
        {
            // LLM failed; fall back to rubrics only
            _logger.LogWarning("Hybrid: LLM evaluation failed; using rubrics-only verdict.");
            return rubricsVerdict with
            {
                Rationale = $"[Hybrid] LLM failed ('{llmVerdict.Rationale}'); rubrics-only result. {rubricsVerdict.Rationale}"
            };
        }

        // ── Step 3: Combine scores ────────────────────────────────────────────
        var combined       = rubricsVerdict.OverallScore * RubricsWeight + llmVerdict.OverallScore * LlmWeight;
        var finalVerdict   = combined >= request.JudgeSetting.PassThreshold ? "pass" : "fail";

        _logger.LogDebug("Hybrid — combined score: {C:F3}, threshold: {T:F3}, verdict: {V}",
            combined, request.JudgeSetting.PassThreshold, finalVerdict);

        return new JudgeVerdict
        {
            TaskSuccessScore = llmVerdict.TaskSuccessScore,
            IntentMatchScore = llmVerdict.IntentMatchScore,
            FactualityScore  = llmVerdict.FactualityScore,
            HelpfulnessScore = llmVerdict.HelpfulnessScore,
            SafetyScore      = llmVerdict.SafetyScore,
            OverallScore     = combined,
            Verdict          = finalVerdict,
            Rationale        = $"[Hybrid] Rubrics: {rubricsVerdict.OverallScore:F3} (×{RubricsWeight}), " +
                               $"LLM: {llmVerdict.OverallScore:F3} (×{LlmWeight}), " +
                               $"Combined: {combined:F3}. " +
                               llmVerdict.Rationale,
            Citations        = llmVerdict.Citations,
        };
    }

    // Detect mandatory failure from the rationale string set by RubricsJudgeProvider
    private static bool ContainsMandatoryFailure(string? rationale) =>
        rationale?.Contains("Mandatory criterion failed", StringComparison.OrdinalIgnoreCase) == true;
}

// ── Module descriptor ─────────────────────────────────────────────────────────

/// <summary>Module descriptor for Hybrid evaluation (Rubrics + LLM).</summary>
public sealed class HybridJudgeModule : ITestingModule
{
    public string ProviderType => "Hybrid";
    public string DisplayName  => "Hybrid Judge (Rubrics + LLM)";

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddKeyedSingleton<IJudgeProvider, mate.Modules.Testing.RubricsJudge.RubricsJudgeProvider>("Rubrics");
        services.AddKeyedSingleton<IJudgeProvider, mate.Modules.Testing.ModelAsJudge.ModelAsJudgeProvider>("ModelAsJudge");
        services.AddSingleton<IJudgeProvider, HybridJudgeProvider>();
        services.AddSingleton<ITestingModule, HybridJudgeModule>();
    }

    public IEnumerable<ConfigFieldDefinition> GetJudgeConfigDefinition()
    {
        // Hybrid uses the same LLM config fields as ModelAsJudge
        yield return new ConfigFieldDefinition("Model",         "LLM Model",         "Azure OpenAI deployment name",   "text",   false);
        yield return new ConfigFieldDefinition("EndpointRef",   "Endpoint (secret)", "Secret ref for the endpoint URL","secret", true);
        yield return new ConfigFieldDefinition("ApiKeyRef",     "API Key (secret)",  "Secret ref for the API key",     "secret", true);
        yield return new ConfigFieldDefinition("PassThreshold", "Pass Threshold",    "Minimum score to pass (0-1)",    "number", false);
    }
}

/// <summary>DI extension for the Hybrid Judge module.</summary>
public static class HybridJudgeModuleExtensions
{
    public static IServiceCollection AddmateHybridJudgeModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Register inner providers as keyed singletons so HybridJudgeProvider can resolve them
        services.AddKeyedSingleton<IJudgeProvider, mate.Modules.Testing.RubricsJudge.RubricsJudgeProvider>("Rubrics");
        services.AddKeyedSingleton<IJudgeProvider, mate.Modules.Testing.ModelAsJudge.ModelAsJudgeProvider>("ModelAsJudge");
        services.AddHttpClient("ModelAsJudge", c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddSingleton<HybridJudgeProvider>();
        services.AddSingleton<IJudgeProvider>(sp => sp.GetRequiredService<HybridJudgeProvider>());
        services.AddSingleton<ITestingModule, HybridJudgeModule>();
        return services;
    }
}
