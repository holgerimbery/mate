using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mate.Modules.Testing.CopilotStudioJudge;

// ── Built-in CopilotStudio default rubrics ────────────────────────────────────

/// <summary>
/// Pre-defined rubric criteria applied when no custom rubrics are configured.
/// These enforce the baseline quality bar for Copilot Studio agent responses.
/// </summary>
internal static class CopilotStudioDefaultRubrics
{
    public static readonly IReadOnlyList<EvaluationCriterion> DefaultCriteria =
    [
        // Hard gate: a completely empty response is always a fail
        new EvaluationCriterion(
            Name:           "Non-empty response",
            EvaluationType: "Custom:NonEmpty",
            Pattern:        string.Empty,
            Weight:         1.0,
            IsMandatory:    true),

        // Soft: detect a full-stop rejection without content
        new EvaluationCriterion(
            Name:           "No blank rejection",
            EvaluationType: "NotContains",
            Pattern:        "I'm not able to help with that",
            Weight:         0.5,
            IsMandatory:    false),

        // Soft: detect an unhandled error surfaced to the user
        new EvaluationCriterion(
            Name:           "No unhandled error message",
            EvaluationType: "NotContains",
            Pattern:        "Something went wrong",
            Weight:         0.5,
            IsMandatory:    false),
    ];
}

// ── LLM response model ────────────────────────────────────────────────────────

internal sealed class CsJudgeLlmResponse
{
    [JsonPropertyName("task_success")]  public double? TaskSuccess  { get; set; }
    [JsonPropertyName("intent_match")]  public double? IntentMatch  { get; set; }
    [JsonPropertyName("factuality")]    public double? Factuality   { get; set; }
    [JsonPropertyName("helpfulness")]   public double? Helpfulness  { get; set; }
    [JsonPropertyName("safety")]        public double? Safety       { get; set; }
    [JsonPropertyName("verdict")]       public string  Verdict      { get; set; } = "fail";
    [JsonPropertyName("rationale")]     public string? Rationale    { get; set; }
    [JsonPropertyName("citations")]     public List<string> Citations { get; set; } = [];
}

// ── Judge provider ────────────────────────────────────────────────────────────

/// <summary>
/// Combined rubrics + LLM judge specifically tuned for Microsoft Copilot Studio agents.
///
/// Evaluation strategy (ported from MaaJforMCS <c>AzureAIFoundryJudgeService</c>):
///
/// 1. Run deterministic rubrics first (fast, free, zero LLM cost).
///    - Mandatory failure → immediate "fail", no LLM call.
///    - Rubrics score below <see cref="RubricsGate"/> → "fail", no LLM call.
///
/// 2. If LLM credentials are configured, call the LLM with a Copilot Studio-optimized
///    system prompt that knows about:
///    - Citation blocks: "[1]: cite:https://..." are POSITIVE grounding indicators.
///    - Semantic equivalence: paraphrased answers that cover key information should pass.
///    - Content/citation separation: strip citation blocks before comparing to reference.
///
/// 3. If LLM is not configured, return rubrics-only verdict.
///
/// 4. Blend scores: rubrics × <see cref="RubricsBlendWeight"/> + LLM × <see cref="LlmBlendWeight"/>.
///
/// Scoring weights are Copilot Studio-optimised (task_success and factuality weighted higher).
/// </summary>
public sealed class CopilotStudioJudgeProvider : IJudgeProvider
{
    // CopilotStudio-specific LLM dimension weights (must sum to 1.0)
    private const double TaskSuccessWeight = 0.35;
    private const double IntentMatchWeight = 0.25;
    private const double FactualityWeight  = 0.25;
    private const double HelpfulnessWeight = 0.10;
    private const double SafetyWeight      = 0.05;

    // Combined score blend
    private const double RubricsBlendWeight = 0.30;
    private const double LlmBlendWeight     = 0.70;

    // Rubrics score below this gate → immediately return fail, skip LLM call
    private const double RubricsGate = 0.40;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CopilotStudioJudgeProvider> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public string ProviderType => "CopilotStudioJudge";

    public CopilotStudioJudgeProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<CopilotStudioJudgeProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<JudgeVerdict> EvaluateAsync(JudgeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        // Apply custom rubrics if configured, otherwise use built-in CopilotStudio defaults
        var criteria = request.RubricCriteria.Count > 0
            ? request.RubricCriteria
            : CopilotStudioDefaultRubrics.DefaultCriteria;

        // ── Step 1: Deterministic rubrics pass ───────────────────────────────
        var (rubricsScore, mandatoryFailed, rubRationale) = EvaluateRubrics(criteria, request.BotResponse);

        if (mandatoryFailed)
        {
            _logger.LogDebug("CopilotStudioJudge: mandatory rubric failed → immediate fail.");
            return new JudgeVerdict
            {
                Verdict      = "fail",
                OverallScore = 0,
                Rationale    = $"[CopilotStudioJudge] Mandatory criterion failed. {rubRationale}",
            };
        }

        if (rubricsScore < RubricsGate)
        {
            _logger.LogDebug(
                "CopilotStudioJudge: rubrics score {Score:F3} below gate {Gate}; returning rubrics-only fail.",
                rubricsScore, RubricsGate);
            return new JudgeVerdict
            {
                Verdict      = "fail",
                OverallScore = rubricsScore,
                Rationale    = $"[CopilotStudioJudge] Rubrics score {rubricsScore:F3} below gate {RubricsGate:F3}. LLM not consulted. {rubRationale}",
            };
        }

        // ── Step 2: LLM evaluation (optional — falls back to rubrics if unconfigured) ─
        var settings = request.JudgeSetting;
        if (string.IsNullOrWhiteSpace(settings.ResolvedEndpoint) ||
            string.IsNullOrWhiteSpace(settings.ResolvedApiKey))
        {
            _logger.LogDebug("CopilotStudioJudge: LLM not configured — returning rubrics-only verdict.");
            var rubricVerdict = rubricsScore >= settings.PassThreshold ? "pass" : "fail";
            return new JudgeVerdict
            {
                Verdict      = rubricVerdict,
                OverallScore = rubricsScore,
                Rationale    = $"[CopilotStudioJudge] Rubrics-only evaluation (LLM not configured). {rubRationale}",
            };
        }

        JudgeVerdict llmVerdict;
        try
        {
            var systemPrompt = BuildCopilotStudioSystemPrompt(settings);
            var userPrompt   = BuildUserPrompt(request);

            var llmJson = await CallLlmAsync(
                settings.ResolvedEndpoint!,
                settings.ResolvedApiKey!,
                settings.Model ?? "gpt-4o-mini",
                systemPrompt, userPrompt,
                settings.Temperature, settings.TopP, settings.MaxOutputTokens,
                ct);

            llmVerdict = ParseLlmResponse(llmJson, settings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CopilotStudioJudge: LLM call failed; falling back to rubrics-only.");
            var fallbackVerdict = rubricsScore >= settings.PassThreshold ? "pass" : "fail";
            return new JudgeVerdict
            {
                Verdict      = fallbackVerdict,
                OverallScore = rubricsScore,
                Rationale    = $"[CopilotStudioJudge] LLM failed ('{ex.Message}'); rubrics-only result. {rubRationale}",
            };
        }

        if (llmVerdict.Verdict == "error")
        {
            _logger.LogWarning("CopilotStudioJudge: LLM returned error verdict; using rubrics-only.");
            var fallbackVerdict = rubricsScore >= settings.PassThreshold ? "pass" : "fail";
            return new JudgeVerdict
            {
                Verdict      = fallbackVerdict,
                OverallScore = rubricsScore,
                Rationale    = $"[CopilotStudioJudge] LLM error; rubrics-only result. {rubRationale}",
            };
        }

        // ── Step 3: Blend rubrics + LLM scores ───────────────────────────────
        var combined     = rubricsScore * RubricsBlendWeight + llmVerdict.OverallScore * LlmBlendWeight;
        var finalVerdict = combined >= settings.PassThreshold ? "pass" : "fail";

        _logger.LogDebug(
            "CopilotStudioJudge — rubrics: {R:F3} (×{RW}), LLM: {L:F3} (×{LW}), combined: {C:F3}, verdict: {V}",
            rubricsScore, RubricsBlendWeight,
            llmVerdict.OverallScore, LlmBlendWeight,
            combined, finalVerdict);

        return new JudgeVerdict
        {
            TaskSuccessScore = llmVerdict.TaskSuccessScore,
            IntentMatchScore = llmVerdict.IntentMatchScore,
            FactualityScore  = llmVerdict.FactualityScore,
            HelpfulnessScore = llmVerdict.HelpfulnessScore,
            SafetyScore      = llmVerdict.SafetyScore,
            OverallScore     = combined,
            Verdict          = finalVerdict,
            Rationale        = $"[CopilotStudioJudge] Rubrics: {rubricsScore:F3} (×{RubricsBlendWeight}), " +
                               $"LLM: {llmVerdict.OverallScore:F3} (×{LlmBlendWeight}), " +
                               $"Combined: {combined:F3}. {llmVerdict.Rationale}",
            Citations        = llmVerdict.Citations,
        };
    }

    // ── Rubrics evaluation ────────────────────────────────────────────────────

    private (double score, bool mandatoryFailed, string rationale) EvaluateRubrics(
        IReadOnlyList<EvaluationCriterion> criteria,
        string botResponse)
    {
        double weightSum   = 0;
        double weightedHit = 0;
        bool   mandatoryFailed = false;
        var    passed = new List<string>();
        var    failed = new List<string>();

        foreach (var criterion in criteria)
        {
            bool ok = EvaluateCriterion(criterion, botResponse);
            if (ok)
            {
                passed.Add(criterion.Name);
                weightedHit += criterion.Weight;
            }
            else
            {
                failed.Add(criterion.Name);
                if (criterion.IsMandatory) mandatoryFailed = true;
            }
            weightSum += criterion.Weight;
        }

        var score = weightSum > 0 ? weightedHit / weightSum : 1.0;

        var rationale = mandatoryFailed
            ? $"Mandatory failed: [{string.Join(", ", failed)}]. Passed: [{string.Join(", ", passed)}]."
            : failed.Count > 0
                ? $"Score {score:F3}. Failed: [{string.Join(", ", failed)}]. Passed: [{string.Join(", ", passed)}]."
                : $"Score {score:F3}. All criteria passed: [{string.Join(", ", passed)}].";

        return (score, mandatoryFailed, rationale);
    }

    private bool EvaluateCriterion(EvaluationCriterion criterion, string botResponse)
    {
        return criterion.EvaluationType switch
        {
            "Custom:NonEmpty" => !string.IsNullOrWhiteSpace(botResponse),
            "Contains"        => botResponse.Contains(criterion.Pattern, StringComparison.OrdinalIgnoreCase),
            "NotContains"     => !botResponse.Contains(criterion.Pattern, StringComparison.OrdinalIgnoreCase),
            "Regex"           => TryMatch(criterion.Pattern, botResponse),
            _                 => true,   // unknown type = pass (non-penalising)
        };
    }

    private bool TryMatch(string pattern, string input)
    {
        try
        {
            return Regex.IsMatch(input, pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(500));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CopilotStudioJudge: regex evaluation failed for pattern '{Pattern}'.", pattern);
            return false;
        }
    }

    // ── LLM call ──────────────────────────────────────────────────────────────

    private async Task<string> CallLlmAsync(
        string endpoint, string apiKey, string model,
        string systemPrompt, string userPrompt,
        double temperature, double topP, int maxTokens,
        CancellationToken ct)
    {
        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   },
            },
            temperature,
            top_p           = topP,
            max_tokens      = maxTokens,
            response_format = new { type = "json_object" },
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Support both Azure OpenAI and generic OpenAI-compatible endpoints
        var url = endpoint.TrimEnd('/');
        if (!url.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url = $"{url}/openai/deployments/{model}/chat/completions?api-version=2024-02-01";

        var http    = _httpClientFactory.CreateClient("CopilotStudioJudge");
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
        {
            Content = content,
        };
        request.Headers.Add("api-key", apiKey);

        var response = await http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"LLM API error {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    // ── Prompt building ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a Copilot Studio-optimized system prompt.
    /// Ported from MaaJforMCS <c>AzureAIFoundryJudgeService.BuildSystemPrompt</c> with:
    /// - Citation block awareness (positive grounding indicator)
    /// - Semantic equivalence examples using real Copilot Studio response patterns
    /// - CopilotStudio-specific pass/fail criteria
    /// </summary>
    private static string BuildCopilotStudioSystemPrompt(JudgeSettingSnapshot settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PromptTemplate))
            return settings.PromptTemplate!;

        return """
            You are an impartial evaluator of Microsoft Copilot Studio agent responses.
            Your task is to score the agent's response against expected criteria and a reference answer.

            CRITICAL EVALUATION RULES FOR COPILOT STUDIO AGENTS:

            1. CITATIONS ARE POSITIVE: Responses containing citation blocks (e.g. "[1]: cite:https://...")
               indicate the agent sourced information from knowledge bases. Treat this as grounding — GOOD.

            2. SEMANTIC EQUIVALENCE: Accept responses that convey the same key information as the reference
               answer, even if phrased differently. Examples:
               - "processed within 10 business days" ≈ "refund processing takes 10 business days"
               - "return within 7 days" ≈ "7-day return window"
               - "original packaging required" ≈ "must be in original packaging"

            3. CONTENT vs CITATIONS: Strip citation blocks before comparing to the reference answer.
               Example: "Product must be in original packaging[1]" → evaluate as "Product must be in original packaging".

            4. PASS CRITERIA — response passes when it:
               a) Contains the key information from the reference answer (exact or paraphrased)
               b) Contains cited sources showing knowledge base grounding
               c) Meaningfully addresses the user's intent even if differently worded

            5. FAIL CRITERIA — response fails ONLY when:
               a) Critical information is completely absent (not merely paraphrased differently)
               b) Response contradicts the reference answer
               c) Response is completely irrelevant or off-topic
               d) Response contains only an apology or refusal with no useful content

            Evaluate these dimensions (0.0–1.0 scale):
            - task_success (0.35): Did the agent address the user's request AND provide key information?
            - intent_match (0.25): Does the response match the user's expected intent?
            - factuality  (0.25): Is information accurate and consistent? Citations count as grounding.
            - helpfulness (0.10): Is the response complete, clear, and actionable?
            - safety      (0.05): Does the response follow safety guidelines?

            Respond ONLY with a JSON object — no explanation outside the JSON:
            {
              "task_success": 0.0,
              "intent_match": 0.0,
              "factuality": 0.0,
              "helpfulness": 0.0,
              "safety": 0.0,
              "verdict": "pass",
              "rationale": "one-sentence explanation",
              "citations": []
            }
            """;
    }

    private static string BuildUserPrompt(JudgeRequest request)
    {
        var settings = request.JudgeSetting;

        var transcriptText = string.Join("\n",
            request.Transcript.Select(m => $"[{m.TurnIndex}] {m.Role.ToUpper()}: {m.Content}"));

        var refAnswer = settings.UseReferenceAnswer && !string.IsNullOrEmpty(request.ReferenceAnswer)
            ? request.ReferenceAnswer
            : "None";

        var userInputSummary = request.UserInput.Length == 1
            ? request.UserInput[0]
            : string.Join(" → ", request.UserInput);

        return $"""
            User Input: {userInputSummary}

            Copilot Studio Agent Response:
            {request.BotResponse}

            Acceptance Criteria: {request.AcceptanceCriteria}

            Reference Answer: {refAnswer}

            Full Conversation Transcript:
            {transcriptText}

            EVALUATION INSTRUCTIONS:
            1. Extract the main response content — ignore citation blocks like "[1]: cite:https://..." at the end.
            2. Compare extracted content against the reference answer for KEY INFORMATION match.
            3. Citation blocks are POSITIVE — they show the agent grounded its answer in knowledge base sources.
            4. Accept paraphrasing, semantic equivalence, and different phrasing of the same concepts.
            5. Only mark as fail if critical information is completely absent OR contradicts the reference.
            6. Accept different formatting (bullets, paragraphs, lists) when the information is equivalent.

            Example:
            - Reference: "Smart Brew 300 can be returned within 7 days and must be in original packaging."
            - Agent:     "The Smart Brew 300 has a 7-day return window and requires original packaging[1]."
            - Verdict:   PASS — same info, different wording, citation shows knowledge base grounding.

            Respond with JSON only.
            """;
    }

    // ── LLM response parsing ──────────────────────────────────────────────────

    private JudgeVerdict ParseLlmResponse(string llmResponse, JudgeSettingSnapshot settings)
    {
        try
        {
            var start = llmResponse.IndexOf('{');
            var end   = llmResponse.LastIndexOf('}') + 1;

            if (start < 0 || end <= start)
            {
                _logger.LogWarning("CopilotStudioJudge: no JSON object found in LLM response.");
                return ErrorVerdict("Could not extract JSON from LLM response.");
            }

            var parsed = JsonSerializer.Deserialize<CsJudgeLlmResponse>(llmResponse[start..end], _jsonOptions);
            if (parsed is null)
                return ErrorVerdict("Deserialized null from LLM response.");

            var ts = parsed.TaskSuccess ?? 0;
            var im = parsed.IntentMatch ?? 0;
            var ft = parsed.Factuality  ?? 0;
            var hp = parsed.Helpfulness ?? 0;
            var sf = parsed.Safety      ?? 0;

            // Apply CopilotStudio-specific weights (not the generic JudgeSetting defaults)
            var overall = ts * TaskSuccessWeight
                        + im * IntentMatchWeight
                        + ft * FactualityWeight
                        + hp * HelpfulnessWeight
                        + sf * SafetyWeight;

            // Cross-check LLM verdict against derived score to catch inconsistencies
            var derivedVerdict = overall >= settings.PassThreshold ? "pass" : "fail";
            var finalVerdict   = string.Equals(parsed.Verdict, "pass", StringComparison.OrdinalIgnoreCase)
                                 && derivedVerdict == "pass" ? "pass" : derivedVerdict;

            _logger.LogDebug(
                "CopilotStudioJudge LLM verdict: {Verdict} (score: {Score:F3}).",
                finalVerdict, overall);

            return new JudgeVerdict
            {
                TaskSuccessScore = ts,
                IntentMatchScore = im,
                FactualityScore  = ft,
                HelpfulnessScore = hp,
                SafetyScore      = sf,
                OverallScore     = overall,
                Verdict          = finalVerdict,
                Rationale        = parsed.Rationale,
                Citations        = parsed.Citations,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CopilotStudioJudge: failed to parse LLM JSON.");
            return ErrorVerdict($"Parse error: {ex.Message}");
        }
    }

    private static JudgeVerdict ErrorVerdict(string rationale) => new()
    {
        Verdict      = "error",
        Rationale    = rationale,
        OverallScore = 0,
    };
}

// ── Module descriptor ─────────────────────────────────────────────────────────

/// <summary>
/// Testing module for Copilot Studio-specific evaluation.
///
/// Provides the <see cref="CopilotStudioJudgeProvider"/> which combines:
/// - Deterministic rubrics (built-in Copilot Studio defaults when no custom rubrics are configured)
/// - LLM evaluation with a citation-aware, grounding-positive system prompt
/// - Copilot Studio-optimised scoring weights
/// </summary>
public sealed class CopilotStudioJudgeModule : ITestingModule
{
    public string ProviderType => "CopilotStudioJudge";
    public string DisplayName  => "Copilot Studio Judge (Rubrics + Citation-Aware LLM)";

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient("CopilotStudioJudge", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(120);
        });
        services.AddSingleton<IJudgeProvider, CopilotStudioJudgeProvider>();
        services.AddSingleton<ITestingModule, CopilotStudioJudgeModule>();
    }

    public IEnumerable<ConfigFieldDefinition> GetJudgeConfigDefinition()
    {
        // LLM config is optional — when absent, judge runs in rubrics-only mode
        yield return new ConfigFieldDefinition(
            "Model",
            "LLM Model",
            "Azure OpenAI deployment name (e.g. gpt-4o-mini). Leave blank for rubrics-only evaluation.",
            "text", false, "gpt-4o-mini");
        yield return new ConfigFieldDefinition(
            "EndpointRef",
            "Endpoint (secret ref)",
            "Secret reference for the Azure OpenAI endpoint URL. Optional — leave blank for rubrics-only evaluation.",
            "secret", false);
        yield return new ConfigFieldDefinition(
            "ApiKeyRef",
            "API Key (secret ref)",
            "Secret reference for the Azure OpenAI API key. Optional — leave blank for rubrics-only evaluation.",
            "secret", false);
        yield return new ConfigFieldDefinition(
            "PassThreshold",
            "Pass Threshold",
            "Minimum combined score to pass (0.0–1.0). Default: 0.7.",
            "number", false, "0.7");
    }
}

/// <summary>DI extension for the Copilot Studio Judge module.</summary>
public static class CopilotStudioJudgeModuleExtensions
{
    public static IServiceCollection AddmateCopilotStudioJudgeModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddHttpClient("CopilotStudioJudge", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(120);
        });
        services.AddSingleton<CopilotStudioJudgeProvider>();
        services.AddSingleton<CopilotStudioJudgeModule>();
        return services;
    }
}
