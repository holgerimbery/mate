// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Text.RegularExpressions;
using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mate.Modules.Testing.RubricsJudge;

/// <summary>
/// Deterministic, rule-based judge using <see cref="RubricCriteria"/>.
///
/// Each criterion defines a pattern and an evaluation type:
/// - Contains    — bot response must contain the pattern (case-insensitive)
/// - NotContains — bot response must NOT contain the pattern
/// - Regex       — bot response must match the regex pattern
/// - Custom      — reserved for future extension
///
/// IsMandatory criteria act as hard gates: a single mandatory failure
/// forces the entire verdict to "fail", regardless of the weighted score.
/// </summary>
public sealed class RubricsJudgeProvider : IJudgeProvider
{
    private readonly ILogger<RubricsJudgeProvider> _logger;

    public string ProviderType => "Rubrics";

    public RubricsJudgeProvider(ILogger<RubricsJudgeProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<JudgeVerdict> EvaluateAsync(JudgeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ct.ThrowIfCancellationRequested();

        if (!request.RubricCriteria.Any())
        {
            _logger.LogWarning("RubricsJudge: no criteria provided; defaulting to pass.");
            return Task.FromResult(new JudgeVerdict
            {
                Verdict      = "pass",
                OverallScore = 1.0,
                Rationale    = "No rubric criteria configured — automatic pass.",
            });
        }

        var botResponse = request.BotResponse;
        double weightSum   = 0;
        double weightedHit = 0;
        bool mandatoryFail = false;
        var failedCriteria = new List<string>();
        var passedCriteria = new List<string>();

        foreach (var criterion in request.RubricCriteria)
        {
            bool passed = Evaluate(criterion, botResponse);

            if (passed)
            {
                passedCriteria.Add(criterion.Name);
                weightedHit += criterion.Weight;
            }
            else
            {
                failedCriteria.Add(criterion.Name);
                if (criterion.IsMandatory)
                    mandatoryFail = true;
            }

            weightSum += criterion.Weight;
        }

        // Normalize to [0, 1]; guard against zero-weight edge case
        var overall = weightSum > 0 ? weightedHit / weightSum : 0;

        string verdict;
        string rationale;

        if (mandatoryFail)
        {
            verdict  = "fail";
            rationale = $"Mandatory criterion failed. Failed: [{string.Join(", ", failedCriteria)}]. " +
                        $"Passed: [{string.Join(", ", passedCriteria)}].";
        }
        else if (overall >= request.JudgeSetting.PassThreshold)
        {
            verdict  = "pass";
            rationale = $"Score {overall:F3} >= threshold {request.JudgeSetting.PassThreshold:F3}. " +
                        $"Passed: [{string.Join(", ", passedCriteria)}].";
        }
        else
        {
            verdict  = "fail";
            rationale = $"Score {overall:F3} < threshold {request.JudgeSetting.PassThreshold:F3}. " +
                        $"Failed: [{string.Join(", ", failedCriteria)}]. " +
                        $"Passed: [{string.Join(", ", passedCriteria)}].";
        }

        _logger.LogDebug("RubricsJudge verdict: {Verdict}, score: {Score:F3}", verdict, overall);

        return Task.FromResult(new JudgeVerdict
        {
            Verdict      = verdict,
            OverallScore = overall,
            Rationale    = rationale,
        });
    }

    // ── Criterion evaluation ──────────────────────────────────────────────────

    private bool Evaluate(EvaluationCriterion criterion, string botResponse)
    {
        try
        {
            return criterion.EvaluationType switch
            {
                "Contains"    => botResponse.Contains(criterion.Pattern, StringComparison.OrdinalIgnoreCase),
                "NotContains" => !botResponse.Contains(criterion.Pattern, StringComparison.OrdinalIgnoreCase),
                "Regex"       => Regex.IsMatch(botResponse, criterion.Pattern,
                                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                                     TimeSpan.FromMilliseconds(500)),
                _ => LogAndSkip(criterion)
            };
        }
        catch (RegexMatchTimeoutException)
        {
            _logger.LogWarning("Regex timeout on criterion '{Name}' with pattern: {Pattern}",
                criterion.Name, criterion.Pattern);
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid regex on criterion '{Name}': {Pattern}",
                criterion.Name, criterion.Pattern);
            return false;
        }
    }

    private bool LogAndSkip(EvaluationCriterion criterion)
    {
        _logger.LogWarning("RubricsJudge: unknown evaluation type '{Type}' on criterion '{Name}' — skipping (treated as passed).",
            criterion.EvaluationType, criterion.Name);
        return true; // unknown = pass to not penalize misconfiguration
    }
}

// ── Module descriptor ─────────────────────────────────────────────────────────

/// <summary>Module descriptor for Rubrics-based deterministic evaluation.</summary>
public sealed class RubricsJudgeModule : ITestingModule
{
    public string ProviderType => "Rubrics";
    public string DisplayName  => "Rubrics Judge (deterministic evaluation)";
    public ModuleTier Tier     => ModuleTier.Free;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IJudgeProvider, RubricsJudgeProvider>();
        services.AddSingleton<ITestingModule, RubricsJudgeModule>();
    }

    public IEnumerable<ConfigFieldDefinition> GetJudgeConfigDefinition()
    {
        // Rubrics judge has no external service config — criteria come from the DB
        yield break;
    }
}

/// <summary>DI extension for the Rubrics Judge module.</summary>
public static class RubricsJudgeModuleExtensions
{
    public static IServiceCollection AddmateRubricsJudgeModule(this IServiceCollection services)
    {
        services.AddSingleton<RubricsJudgeProvider>();
        services.AddSingleton<IJudgeProvider>(sp => sp.GetRequiredService<RubricsJudgeProvider>());
        services.AddSingleton<ITestingModule, RubricsJudgeModule>();
        return services;
    }
}
