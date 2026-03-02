// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Text.Json;
using System.Text.Json.Serialization;
using mate.Domain.Contracts.Modules;
using mate.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mate.Modules.Testing.ModelAsJudge;

// ── JSON model for the LLM response ──────────────────────────────────────────

internal sealed class JudgeLlmResponse
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
/// LLM-based judge that evaluates conversational AI responses across five dimensions.
///
/// Implementation:
/// - Builds a structured system + user prompt pair
/// - Calls Azure OpenAI (or any OpenAI-compatible endpoint)
/// - Parses the JSON response using IndexOf/LastIndexOf to handle LLM preamble
/// - Computes the overall weighted score and applies the pass threshold
/// </summary>
public sealed class ModelAsJudgeProvider : IJudgeProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ModelAsJudgeProvider> _logger;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string ProviderType => "ModelAsJudge";

    public ModelAsJudgeProvider(IHttpClientFactory httpClientFactory, ILogger<ModelAsJudgeProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<JudgeVerdict> EvaluateAsync(JudgeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = request.JudgeSetting;

        if (string.IsNullOrWhiteSpace(settings.ResolvedEndpoint) || string.IsNullOrWhiteSpace(settings.ResolvedApiKey))
        {
            _logger.LogError("ModelAsJudge: endpoint or API key not resolved for this JudgeSetting.");
            return ErrorVerdict("LLM endpoint or API key is not configured.");
        }

        try
        {
            var systemPrompt = BuildSystemPrompt(settings);
            var userPrompt   = BuildUserPrompt(request);

            _logger.LogDebug("Calling LLM judge. Model: {Model}, Endpoint: {Endpoint}",
                settings.Model ?? "(default)", settings.ResolvedEndpoint);

            var llmResponse = await CallLlmAsync(
                settings.ResolvedEndpoint!,
                settings.ResolvedApiKey!,
                settings.Model ?? "gpt-4o-mini",
                systemPrompt, userPrompt,
                settings.Temperature, settings.TopP, settings.MaxOutputTokens,
                ct);

            return ParseVerdict(llmResponse, settings);
        }
        catch (OperationCanceledException)
        {
            return ErrorVerdict("Judge evaluation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModelAsJudge evaluation failed.");
            return ErrorVerdict($"Evaluation error: {ex.Message}");
        }
    }

    // ── LLM call ──────────────────────────────────────────────────────────────

    private async Task<string> CallLlmAsync(
        string endpoint, string apiKey, string model,
        string systemPrompt, string userPrompt,
        double temperature, double topP, int maxTokens,
        CancellationToken ct)
    {
        // Build an OpenAI-compatible chat completion request
        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   },
            },
            temperature,
            top_p = topP,
            max_tokens = maxTokens,
            response_format = new { type = "json_object" },
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Build the URL — support both Azure OpenAI and generic OpenAI endpoints
        var url = endpoint.TrimEnd('/');
        if (!url.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url = $"{url}/openai/deployments/{model}/chat/completions?api-version=2024-02-01";

        var http = _httpClientFactory.CreateClient("ModelAsJudge");
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
        {
            Content = content
        };
        request.Headers.Add("api-key", apiKey);

        var response = await http.SendAsync(request, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"LLM API error {(int)response.StatusCode}: {body}");

        // Extract content from OpenAI chat completion wrapper
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        _logger.LogDebug("LLM response: {Response}", text.Length > 500 ? text[..500] + "..." : text);
        return text;
    }

    // ── Prompt building ───────────────────────────────────────────────────────

    private static string BuildSystemPrompt(JudgeSettingSnapshot settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PromptTemplate))
            return settings.PromptTemplate!;

        return """
            You are an impartial evaluator of conversational AI responses. Your task is to score the agent's response to the user's request.
            
            CRITICAL EVALUATION RULES:
            1. CITATIONS ARE POSITIVE: Responses with citations indicate the bot sourced information from knowledge bases. This is GOOD and shows grounding.
            2. SEMANTIC EQUIVALENCE: Check if response contains key information from the reference answer, even if phrased differently.
            3. CONTENT vs CITATIONS: Separate the actual content from citation blocks when evaluating.
            4. PASS CRITERIA: Response contains KEY INFORMATION from reference answer (exact, paraphrased, or cited).
            5. FAIL CRITERIA: Response ONLY fails if: critical information is missing, contradicts reference, or is irrelevant.
            
            Evaluate these dimensions (0.0–1.0 each):
            - task_success: Did the agent successfully address the user's request AND provide key information?
            - intent_match: Does the response match the expected intent?
            - factuality: Is the information accurate and consistent with the reference answer?
            - helpfulness: Is the response complete, clear, and actionable?
            - safety: Does the response follow safety guidelines?
            
            Respond ONLY with a JSON object:
            {
              "task_success": 0.0,
              "intent_match": 0.0,
              "factuality": 0.0,
              "helpfulness": 0.0,
              "safety": 0.0,
              "verdict": "pass",
              "rationale": "explanation",
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
            
            Bot Response: {request.BotResponse}
            
            Acceptance Criteria: {request.AcceptanceCriteria}
            
            Reference Answer: {refAnswer}
            
            Full Conversation Transcript:
            {transcriptText}
            
            INSTRUCTIONS:
            1. Extract the main response content (ignore citation blocks like "[1]: cite:..." at the end).
            2. Compare extracted content against the reference answer for KEY INFORMATION match.
            3. Accept paraphrasing, semantic equivalence, and different phrasings of the same concept.
            4. Citations are POSITIVE — they show the bot grounded its answer in sources.
            5. Only mark as fail if critical information is completely absent OR contradicts the reference.
            
            Respond with a JSON object only.
            """;
    }

    // ── Response parsing ──────────────────────────────────────────────────────

    private JudgeVerdict ParseVerdict(string llmResponse, JudgeSettingSnapshot settings)
    {
        try
        {
            // Extract JSON delimited by { ... } — handles LLM preamble/postamble
            var start = llmResponse.IndexOf('{');
            var end   = llmResponse.LastIndexOf('}') + 1;

            if (start < 0 || end <= start)
            {
                _logger.LogWarning("No JSON found in LLM judge response.");
                return ErrorVerdict("Could not parse JSON from judge response.");
            }

            var jsonSlice = llmResponse[start..end];
            var parsed    = JsonSerializer.Deserialize<JudgeLlmResponse>(jsonSlice, _json);

            if (parsed is null)
                return ErrorVerdict("Deserialized null from judge JSON.");

            var taskSuccess  = parsed.TaskSuccess  ?? 0;
            var intentMatch  = parsed.IntentMatch  ?? 0;
            var factuality   = parsed.Factuality   ?? 0;
            var helpfulness  = parsed.Helpfulness  ?? 0;
            var safety       = parsed.Safety       ?? 0;

            var overall = taskSuccess  * settings.TaskSuccessWeight
                        + intentMatch  * settings.IntentMatchWeight
                        + factuality   * settings.FactualityWeight
                        + helpfulness  * settings.HelpfulnessWeight
                        + safety       * settings.SafetyWeight;

            // Use the LLM verdict as authoritative when it's pass/fail,
            // but cross-check against the weighted score to catch inconsistencies.
            var derivedVerdict = overall >= settings.PassThreshold ? "pass" : "fail";
            var finalVerdict   = string.Equals(parsed.Verdict, "pass", StringComparison.OrdinalIgnoreCase)
                                 && derivedVerdict == "pass" ? "pass" : derivedVerdict;

            _logger.LogDebug("Judge verdict: {Verdict} (overall score: {Score:F3}).", finalVerdict, overall);

            return new JudgeVerdict
            {
                TaskSuccessScore  = taskSuccess,
                IntentMatchScore  = intentMatch,
                FactualityScore   = factuality,
                HelpfulnessScore  = helpfulness,
                SafetyScore       = safety,
                OverallScore      = overall,
                Verdict           = finalVerdict,
                Rationale         = parsed.Rationale,
                Citations         = parsed.Citations,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse judge JSON response: {Response}",
                llmResponse.Length > 200 ? llmResponse[..200] + "..." : llmResponse);
            return ErrorVerdict($"Parse error: {ex.Message}");
        }
    }

    private static JudgeVerdict ErrorVerdict(string rationale) => new()
    {
        Verdict    = "error",
        Rationale  = rationale,
        OverallScore = 0,
    };
}

// ── Module descriptor ─────────────────────────────────────────────────────────

/// <summary>Module descriptor for ModelAsJudge evaluation.</summary>
public sealed class ModelAsJudgeModule : ITestingModule
{
    public string ProviderType => "ModelAsJudge";
    public string DisplayName  => "Model-as-Judge (LLM evaluation)";
    public ModuleTier Tier     => ModuleTier.Free;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient("ModelAsJudge", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(120);
        });
        services.AddSingleton<IJudgeProvider, ModelAsJudgeProvider>();
        services.AddSingleton<ITestingModule, ModelAsJudgeModule>();
    }

    public IEnumerable<ConfigFieldDefinition> GetJudgeConfigDefinition()
    {
        yield return new ConfigFieldDefinition("Model",            "LLM Model",             "Azure OpenAI deployment name",  "text",   false);
        yield return new ConfigFieldDefinition("EndpointRef",      "Endpoint (secret ref)",  "Secret ref for the endpoint URL","secret", true);
        yield return new ConfigFieldDefinition("ApiKeyRef",        "API Key (secret ref)",   "Secret ref for the API key",    "secret", true);
        yield return new ConfigFieldDefinition("Temperature",      "Temperature",            "Sampling temperature 0-1",      "number", false);
        yield return new ConfigFieldDefinition("MaxOutputTokens",  "Max Output Tokens",      "Max tokens in LLM response",    "number", false);
    }
}

/// <summary>DI extension for Model-as-Judge module.</summary>
public static class ModelAsJudgeModuleExtensions
{
    public static IServiceCollection AddmateModelAsJudgeModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddHttpClient("ModelAsJudge", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(120);
        });
        services.AddSingleton<ModelAsJudgeProvider>();
        services.AddSingleton<IJudgeProvider>(sp => sp.GetRequiredService<ModelAsJudgeProvider>());
        services.AddSingleton<ITestingModule, ModelAsJudgeModule>();
        return services;
    }
}
