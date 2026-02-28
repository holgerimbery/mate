using FluentAssertions;
using mate.Domain.Contracts.Modules;
using mate.Modules.Testing.RubricsJudge;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace mate.Tests.Unit.Testing;

/// <summary>
/// Unit tests for <see cref="RubricsJudgeProvider"/>.
/// Tests verify Contains, NotContains, Regex matching, mandatory-fail gate, and scoring.
/// </summary>
public sealed class RubricsJudgeProviderTests
{
    private static RubricsJudgeProvider CreateJudge()
        => new(NullLogger<RubricsJudgeProvider>.Instance);

    private static JudgeRequest BuildRequest(string botResponse, IReadOnlyList<EvaluationCriterion> criteria)
        => new()
        {
            BotResponse    = botResponse,
            JudgeSetting   = new JudgeSettingSnapshot(PassThreshold: 0.7),
            RubricCriteria = criteria,
        };

    // ── No criteria → automatic pass ─────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_NoCriteria_ReturnsAutomaticPass()
    {
        var judge  = CreateJudge();
        var result = await judge.EvaluateAsync(BuildRequest("any response", []));

        result.Verdict.Should().Be("pass");
        result.OverallScore.Should().Be(1.0);
    }

    // ── Contains — matching ───────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_ContainsCriteria_Passes_WhenTextPresent()
    {
        var criteria = new List<EvaluationCriterion>
        {
            new("HasGreeting", "Contains", "hello", Weight: 1.0, IsMandatory: false)
        };

        var result = await CreateJudge().EvaluateAsync(
            BuildRequest("Hello, how can I help you?", criteria));

        result.Verdict.Should().Be("pass");
        result.OverallScore.Should().Be(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_ContainsCriteria_Fails_WhenTextAbsent()
    {
        var criteria = new List<EvaluationCriterion>
        {
            new("HasGreeting", "Contains", "hello", Weight: 1.0, IsMandatory: false)
        };

        var result = await CreateJudge().EvaluateAsync(
            BuildRequest("Goodbye!", criteria));

        result.Verdict.Should().Be("fail");
        result.OverallScore.Should().Be(0.0);
    }

    // ── NotContains ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_NotContainsCriteria_Passes_WhenPatternAbsent()
    {
        var criteria = new List<EvaluationCriterion>
        {
            new("NoSorry", "NotContains", "sorry", Weight: 1.0, IsMandatory: false)
        };

        var result = await CreateJudge().EvaluateAsync(
            BuildRequest("Here is your answer.", criteria));

        result.Verdict.Should().Be("pass");
    }

    [Fact]
    public async Task EvaluateAsync_NotContainsCriteria_Fails_WhenPatternPresent()
    {
        var criteria = new List<EvaluationCriterion>
        {
            new("NoSorry", "NotContains", "sorry", Weight: 1.0, IsMandatory: false)
        };

        var result = await CreateJudge().EvaluateAsync(
            BuildRequest("I'm sorry, I cannot help.", criteria));

        result.Verdict.Should().Be("fail");
    }

    // ── Regex ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_RegexCriteria_Passes_WhenMatches()
    {
        var criteria = new List<EvaluationCriterion>
        {
            new("HasPhoneNumber", "Regex", @"\+\d{10,15}", Weight: 1.0, IsMandatory: false)
        };

        var result = await CreateJudge().EvaluateAsync(
            BuildRequest("Call us at +12345678901", criteria));

        result.Verdict.Should().Be("pass");
    }

    [Fact]
    public async Task EvaluateAsync_RegexCriteria_Fails_WhenNoMatch()
    {
        var criteria = new List<EvaluationCriterion>
        {
            new("HasPhoneNumber", "Regex", @"\+\d{10,15}", Weight: 1.0, IsMandatory: false)
        };

        var result = await CreateJudge().EvaluateAsync(
            BuildRequest("We will contact you soon.", criteria));

        result.Verdict.Should().Be("fail");
    }

    // ── Mandatory failure gate ─────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_MandatoryCriteriaFails_ForcesFailRegardlessOfOtherScores()
    {
        var criteria = new List<EvaluationCriterion>
        {
            // This criterion passes (weight 10.0) but is not mandatory
            new("HasAnswer", "Contains", "answer", Weight: 10.0, IsMandatory: false),
            // This mandatory criterion fails
            new("MustGreet", "Contains", "hello", Weight: 1.0,  IsMandatory: true),
        };

        var result = await CreateJudge().EvaluateAsync(
            BuildRequest("Here is your answer.", criteria));

        result.Verdict.Should().Be("fail",
            "a mandatory criterion failure must override all other scores");
        result.Rationale.Should().Contain("Mandatory criterion failed");
    }

    // ── Weighted scoring ──────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WeightedCriteria_ScoreIsWeightedAverage()
    {
        var criteria = new List<EvaluationCriterion>
        {
            new("A", "Contains", "apple",  Weight: 2.0, IsMandatory: false),  // passes → 2.0
            new("B", "Contains", "banana", Weight: 1.0, IsMandatory: false),  // fails  → 0.0
        };

        var result = await CreateJudge().EvaluateAsync(
            BuildRequest("I have an apple.", criteria));

        result.OverallScore.Should().BeApproximately(2.0 / 3.0, precision: 0.001);
    }

    // ── Invalid regex ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_InvalidRegexPattern_DoesNotThrow_ReturnsFail()
    {
        var criteria = new List<EvaluationCriterion>
        {
            new("BadRegex", "Regex", "[invalid(", Weight: 1.0, IsMandatory: false)
        };

        Func<Task> act = () => CreateJudge().EvaluateAsync(BuildRequest("any text", criteria));
        await act.Should().NotThrowAsync();

        var result = await CreateJudge().EvaluateAsync(BuildRequest("any text", criteria));
        result.Verdict.Should().Be("fail"); // invalid pattern → criterion failed
    }

    // ── Case-insensitivity ────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_ContainsCriteria_IsCaseInsensitive()
    {
        var criteria = new List<EvaluationCriterion>
        {
            new("CaseCheck", "Contains", "HELLO", Weight: 1.0, IsMandatory: false)
        };

        var result = await CreateJudge().EvaluateAsync(
            BuildRequest("hello world", criteria));

        result.Verdict.Should().Be("pass");
    }
}
