using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mate.Modules.Testing.Generic;

/// <summary>
/// Generic / pass-through testing module.
/// Evaluates by checking the bot response against the acceptance criteria using a simple
/// text-contains check. No LLM required. Use this for quick smoke-tests or when no
/// judge provider is configured.
/// </summary>
public sealed class GenericJudgeProvider : IJudgeProvider
{
    private readonly ILogger<GenericJudgeProvider> _logger;

    public string ProviderType => "Generic";

    public GenericJudgeProvider(ILogger<GenericJudgeProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<JudgeVerdict> EvaluateAsync(JudgeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ct.ThrowIfCancellationRequested();

        // Simple heuristic: if acceptance criteria is empty, the test always passes
        if (string.IsNullOrWhiteSpace(request.AcceptanceCriteria))
        {
            return Task.FromResult(new JudgeVerdict
            {
                Verdict      = "pass",
                OverallScore = 1.0,
                Rationale    = "No acceptance criteria defined — automatic pass (Generic judge).",
            });
        }

        // Check whether the bot response contains the acceptance criteria text
        bool criteriaFound = request.BotResponse.Contains(
            request.AcceptanceCriteria,
            StringComparison.OrdinalIgnoreCase);

        var verdict = criteriaFound ? "pass" : "fail";
        var score   = criteriaFound ? 1.0 : 0.0;

        _logger.LogDebug("Generic judge verdict: {Verdict}", verdict);

        return Task.FromResult(new JudgeVerdict
        {
            Verdict      = verdict,
            OverallScore = score,
            Rationale    = criteriaFound
                ? "Bot response contains acceptance criteria text."
                : "Bot response does not contain acceptance criteria text.",
        });
    }
}

// ── Module descriptor ─────────────────────────────────────────────────────────

/// <summary>Module descriptor for the Generic judge.</summary>
public sealed class GenericTestingModule : ITestingModule
{
    public string ProviderType => "Generic";
    public string DisplayName  => "Generic Judge (text-contains check)";

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IJudgeProvider, GenericJudgeProvider>();
        services.AddSingleton<ITestingModule, GenericTestingModule>();
    }

    public IEnumerable<ConfigFieldDefinition> GetJudgeConfigDefinition()
    {
        yield break; // No external config required
    }
}

/// <summary>DI extension for the Generic testing module.</summary>
public static class GenericTestingModuleExtensions
{
    public static IServiceCollection AddmateGenericTestingModule(this IServiceCollection services)
    {
        services.AddSingleton<GenericJudgeProvider>();
        services.AddSingleton<IJudgeProvider>(sp => sp.GetRequiredService<GenericJudgeProvider>());
        services.AddSingleton<GenericTestingModule>();
        return services;
    }
}
