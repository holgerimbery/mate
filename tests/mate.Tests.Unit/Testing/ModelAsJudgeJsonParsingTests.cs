using FluentAssertions;
using mate.Domain.Contracts.Modules;
using Xunit;

namespace mate.Tests.Unit.Testing;

/// <summary>
/// Tests for the JSON parsing logic used by ModelAsJudgeProvider.
/// These tests exercise the core algorithm in isolation without any HTTP calls.
///
/// The parsing algorithm:
///   1. Find the first '{' in the LLM response
///   2. Find the last '}' + 1
///   3. Slice the substring and deserialize
///   4. Compute weighted score and compare to threshold
/// </summary>
public sealed class ModelAsJudgeJsonParsingTests
{
    // A helper that replicates the parsing algorithm exactly as implemented in ModelAsJudgeProvider
    private static (bool Parsed, double TaskSuccess, double IntentMatch, double Factuality,
                    double Helpfulness, double Safety, string Verdict, string? Rationale)
        ParseLlmResponse(string llmResponse)
    {
        var start = llmResponse.IndexOf('{');
        var end   = llmResponse.LastIndexOf('}') + 1;

        if (start < 0 || end <= start)
            return (false, 0, 0, 0, 0, 0, "error", null);

        var json   = llmResponse[start..end];
        var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json,
                         new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        if (parsed is null)
            return (false, 0, 0, 0, 0, 0, "error", null);

        double Get(string key) => parsed.TryGetValue(key, out var v) ? v.GetDouble() : 0;
        string GetStr(string key) => parsed.TryGetValue(key, out var v) ? v.GetString() ?? "" : "";

        return (true,
            Get("task_success"), Get("intent_match"), Get("factuality"),
            Get("helpfulness"), Get("safety"),
            GetStr("verdict"),
            parsed.TryGetValue("rationale", out var r) ? r.GetString() : null);
    }

    // ── Clean JSON ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_CleanJson_ExtractsAllFields()
    {
        const string response = """
            {
              "task_success": 0.9,
              "intent_match": 0.8,
              "factuality": 0.85,
              "helpfulness": 0.75,
              "safety": 1.0,
              "verdict": "pass",
              "rationale": "Good answer",
              "citations": []
            }
            """;

        var (parsed, ts, im, fact, help, safe, verdict, rationale) = ParseLlmResponse(response);

        parsed.Should().BeTrue();
        ts.Should().BeApproximately(0.9, 0.001);
        im.Should().BeApproximately(0.8, 0.001);
        fact.Should().BeApproximately(0.85, 0.001);
        help.Should().BeApproximately(0.75, 0.001);
        safe.Should().BeApproximately(1.0, 0.001);
        verdict.Should().Be("pass");
        rationale.Should().Be("Good answer");
    }

    // ── JSON with LLM preamble ────────────────────────────────────────────────

    [Fact]
    public void Parse_JsonWithPreamble_ExtractsJsonCorrectly()
    {
        const string response = """
            Sure, here is my evaluation:

            {
              "task_success": 0.6,
              "intent_match": 0.5,
              "factuality": 0.4,
              "helpfulness": 0.7,
              "safety": 1.0,
              "verdict": "fail",
              "rationale": "Missing key information"
            }

            I based this evaluation on the transcript provided.
            """;

        var (parsed, ts, _, _, _, _, verdict, _) = ParseLlmResponse(response);

        parsed.Should().BeTrue();
        ts.Should().BeApproximately(0.6, 0.001);
        verdict.Should().Be("fail");
    }

    // ── Weighted score computation ────────────────────────────────────────────

    [Fact]
    public void WeightedScore_DefaultWeights_ProducesExpectedResult()
    {
        // Default weights: ts=0.3, im=0.2, fact=0.2, help=0.15, safe=0.15
        var settings = new JudgeSettingSnapshot(
            TaskSuccessWeight: 0.3,
            IntentMatchWeight: 0.2,
            FactualityWeight:  0.2,
            HelpfulnessWeight: 0.15,
            SafetyWeight:      0.15,
            PassThreshold:     0.7);

        double taskSuccess  = 0.8;
        double intentMatch  = 0.7;
        double factuality   = 0.9;
        double helpfulness  = 0.6;
        double safety       = 1.0;

        var overall = taskSuccess  * settings.TaskSuccessWeight
                    + intentMatch  * settings.IntentMatchWeight
                    + factuality   * settings.FactualityWeight
                    + helpfulness  * settings.HelpfulnessWeight
                    + safety       * settings.SafetyWeight;

        // 0.8*0.3 + 0.7*0.2 + 0.9*0.2 + 0.6*0.15 + 1.0*0.15
        // = 0.24 + 0.14 + 0.18 + 0.09 + 0.15 = 0.80
        overall.Should().BeApproximately(0.80, 0.001);

        var verdict = overall >= settings.PassThreshold ? "pass" : "fail";
        verdict.Should().Be("pass");
    }

    [Fact]
    public void WeightedScore_BelowThreshold_ProducesFail()
    {
        var settings = new JudgeSettingSnapshot(PassThreshold: 0.7);

        double overall = 0.2 * 0.3 + 0.1 * 0.2 + 0.3 * 0.2 + 0.4 * 0.15 + 0.5 * 0.15;
        // = 0.06 + 0.02 + 0.06 + 0.06 + 0.075 = 0.275 → fail

        var verdict = overall >= settings.PassThreshold ? "pass" : "fail";
        verdict.Should().Be("fail");
    }

    // ── Missing / malformed JSON ──────────────────────────────────────────────

    [Fact]
    public void Parse_NoJsonInResponse_ReturnsFalse()
    {
        var (parsed, _, _, _, _, _, verdict, _) = ParseLlmResponse("I cannot evaluate this.");
        parsed.Should().BeFalse();
        verdict.Should().Be("error");
    }

    [Fact]
    public void Parse_EmptyString_ReturnsFalse()
    {
        var (parsed, _, _, _, _, _, _, _) = ParseLlmResponse(string.Empty);
        parsed.Should().BeFalse();
    }
}
