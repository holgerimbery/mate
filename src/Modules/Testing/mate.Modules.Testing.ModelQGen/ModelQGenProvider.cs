// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Text.Json;
using System.Text.Json.Serialization;
using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mate.Modules.Testing.ModelQGen;

// ── JSON model for the LLM response ──────────────────────────────────────────

internal sealed class QGenLlmQuestion
{
    [JsonPropertyName("question")]          public string         Question         { get; set; } = string.Empty;
    [JsonPropertyName("expected_answer")]   public string         ExpectedAnswer   { get; set; } = string.Empty;
    [JsonPropertyName("expected_intent")]   public string?        ExpectedIntent   { get; set; }
    [JsonPropertyName("expected_entities")] public List<string>   ExpectedEntities { get; set; } = [];
    [JsonPropertyName("context")]           public string?        Context          { get; set; }
    [JsonPropertyName("rationale")]         public string?        Rationale        { get; set; }
}

// ── Provider ──────────────────────────────────────────────────────────────────

/// <summary>
/// LLM-based question generator that calls an OpenAI-compatible endpoint and
/// generates structured <see cref="GeneratedQuestion"/> objects from document content.
///
/// The caller (e.g. TestSuites page) resolves endpoint and API key via
/// <c>ISecretService</c> and passes them in <see cref="QuestionGenerationRequest"/>.
/// </summary>
public sealed class ModelQGenProvider : IQuestionGenerationProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ModelQGenProvider> _logger;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string ProviderType => "ModelQGen";
    public ModuleTier Tier     => ModuleTier.Free;

    public ModelQGenProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<ModelQGenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(
        QuestionGenerationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ResolvedEndpoint) ||
            string.IsNullOrWhiteSpace(request.ResolvedApiKey))
        {
            _logger.LogError("ModelQGen: endpoint or API key not resolved.");
            return [];
        }

        if (string.IsNullOrWhiteSpace(request.DocumentContent))
        {
            _logger.LogWarning("ModelQGen: no document content provided.");
            return [];
        }

        try
        {
            var systemPrompt = string.IsNullOrWhiteSpace(request.SystemPromptOverride)
                ? BuildDefaultSystemPrompt(request)
                : request.SystemPromptOverride;

            var userPrompt = BuildUserPrompt(request);

            _logger.LogDebug("Calling LLM for question generation. Model: {Model}, Count: {Count}",
                request.Model ?? "(default)", request.NumberOfQuestions);

            var raw = await CallLlmAsync(
                request.ResolvedEndpoint,
                request.ResolvedApiKey,
                request.Model ?? "gpt-4o-mini",
                systemPrompt, userPrompt,
                ct);

            var questions = ParseQuestions(raw);
            _logger.LogInformation("ModelQGen generated {Count} questions.", questions.Count);
            return questions;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ModelQGen generation failed.");
            throw;
        }
    }

    // ── Prompt builders ───────────────────────────────────────────────────────

    private static string BuildDefaultSystemPrompt(QuestionGenerationRequest request)
    {
        var domainHint = string.IsNullOrWhiteSpace(request.Domain)
            ? string.Empty
            : $" Focus on the {request.Domain} domain.";

        return
            $"You are a test case generator for AI assistants.{domainHint}\n" +
            $"Analyze the provided document content and generate {request.NumberOfQuestions} high-quality test questions.\n\n" +
            "Requirements:\n" +
            "- Make questions natural and conversational\n" +
            "- Cover different complexity levels and different aspects of the document\n" +
            "- Include a mix of factual lookup, summarisation, and reasoning questions\n" +
            "- Avoid yes/no questions; prefer open-ended or task-based questions\n" +
            "- If existing questions are provided, generate DIFFERENT questions to maximise coverage\n\n" +
            "Return a JSON object with a \"questions\" array. Each element must have these fields:\n" +
            "{\n" +
            "  \"questions\": [\n" +
            "    {\n" +
            "      \"question\":          \"the test question text\",\n" +
            "      \"expected_answer\":   \"what a correct, complete answer should contain\",\n" +
            "      \"expected_intent\":   \"inferred intent label (e.g. get-information, how-to, book-appointment)\",\n" +
            "      \"expected_entities\": [\"key\", \"entities\", \"the\", \"answer\", \"must\", \"mention\"],\n" +
            "      \"context\":           \"relevant verbatim snippet from the document that grounds this question\",\n" +
            "      \"rationale\":         \"why this is a valuable test question\"\n" +
            "    }\n" +
            "  ]\n" +
            "}";
    }

    private static string BuildUserPrompt(QuestionGenerationRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Document content");
        sb.AppendLine();
        sb.AppendLine(request.DocumentContent);

        if (request.ExistingQuestions?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Existing questions (generate DIFFERENT ones)");
            foreach (var q in request.ExistingQuestions)
                sb.AppendLine($"- {q}");
        }

        sb.AppendLine();
        sb.AppendLine($"Generate exactly {request.NumberOfQuestions} test question(s). Return valid JSON only.");
        return sb.ToString();
    }

    // ── LLM call ──────────────────────────────────────────────────────────────

    private async Task<string> CallLlmAsync(
        string endpoint, string apiKey, string model,
        string systemPrompt, string userPrompt,
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
            temperature        = 0.7,
            max_tokens         = 4000,
            response_format    = new { type = "json_object" },
        };

        var json    = JsonSerializer.Serialize(requestBody);
        var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Support both Azure OpenAI and generic OpenAI-compatible endpoints
        var url = endpoint.TrimEnd('/');
        if (!url.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url = $"{url}/openai/deployments/{model}/chat/completions?api-version=2024-02-01";

        var client = _httpClientFactory.CreateClient("ModelQGen");
        client.DefaultRequestHeaders.Remove("api-key");
        client.DefaultRequestHeaders.Add("api-key", apiKey);

        var response = await client.PostAsync(url, content, ct);
        var body     = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"LLM returned {(int)response.StatusCode}: {body[..Math.Min(300, body.Length)]}");

        // Extract content from OpenAI choices array
        using var doc     = JsonDocument.Parse(body);
        var rawContent = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        return rawContent;
    }

    // ── Response parser ───────────────────────────────────────────────────────

    private IReadOnlyList<GeneratedQuestion> ParseQuestions(string raw)
    {
        try
        {
            // Strip markdown fences if the LLM wrapped the JSON
            var text = raw.Trim();
            if (text.StartsWith("```")) text = text[(text.IndexOf('\n') + 1)..];
            if (text.EndsWith("```"))  text = text[..text.LastIndexOf("```")];
            text = text.Trim();

            using var doc = JsonDocument.Parse(text);

            // Accept {"questions":[...]} or a bare array
            JsonElement arr;
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                arr = doc.RootElement;
            }
            else if (doc.RootElement.TryGetProperty("questions", out arr))
            {
                // found "questions" key — use it
            }
            else
            {
                // Fall back: find the first array-valued property
                arr = doc.RootElement.EnumerateObject()
                    .FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array)
                    .Value;
            }

            var result = new List<GeneratedQuestion>();
            foreach (var el in arr.EnumerateArray())
            {
                var q = el.Deserialize<QGenLlmQuestion>(_json);
                if (q is null || string.IsNullOrWhiteSpace(q.Question)) continue;

                result.Add(new GeneratedQuestion
                {
                    Question         = q.Question.Trim(),
                    ExpectedAnswer   = q.ExpectedAnswer,
                    ExpectedIntent   = q.ExpectedIntent,
                    ExpectedEntities = [.. q.ExpectedEntities],
                    Context          = q.Context,
                    Rationale        = q.Rationale,
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse question generation response. Raw (first 500 chars): {Raw}",
                raw.Length > 500 ? raw[..500] : raw);
            return [];
        }
    }
}

// ── Module descriptor ─────────────────────────────────────────────────────────

/// <summary>
/// Module descriptor for the Model-as-Question-Generator.
/// Registers the provider and declares the required configuration fields.
/// </summary>
public sealed class ModelQGenModule : ITestingModule
{
    public string ProviderType => "ModelQGen";
    public string DisplayName  => "Model Question Generator (LLM-based)";
    public ModuleTier Tier     => ModuleTier.Free;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient("ModelQGen", c => c.Timeout = TimeSpan.FromSeconds(180));
        services.AddSingleton<IQuestionGenerationProvider, ModelQGenProvider>();
    }

    public IEnumerable<ConfigFieldDefinition> GetJudgeConfigDefinition()
    {
        yield return new ConfigFieldDefinition(
            "Endpoint",  "Endpoint URL",        "Azure OpenAI or compatible endpoint",  "text",   true);
        yield return new ConfigFieldDefinition(
            "ApiKeyRef", "API Key (secret ref)", "Secret ref for the API key",           "secret", true);
        yield return new ConfigFieldDefinition(
            "Model",     "LLM Model",            "Deployment name (e.g. gpt-4o)",        "text",   false);
    }
}

// ── DI extension ──────────────────────────────────────────────────────────────

/// <summary>DI registration extension for the ModelQGen module.</summary>
public static class ModelQGenModuleExtensions
{
    public static IServiceCollection AddmateModelQGenModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddHttpClient("ModelQGen", c => c.Timeout = TimeSpan.FromSeconds(180));
        services.AddSingleton<IQuestionGenerationProvider, ModelQGenProvider>();
        services.AddSingleton<ITestingModule, ModelQGenModule>();
        return services;
    }
}
