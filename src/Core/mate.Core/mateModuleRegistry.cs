using mate.Domain.Contracts.Modules;
using mate.Domain.Contracts.Monitoring;
using mate.Domain.Contracts.RedTeaming;
using Microsoft.Extensions.Logging;

namespace mate.Core;

/// <summary>
/// Central registry for all pluggable module implementations.
/// Modules (connectors, judge providers, question generators, monitoring providers)
/// register themselves at startup; the registry resolves the correct implementation
/// at runtime based on provider-type strings stored in the database configuration.
/// </summary>
public sealed class mateModuleRegistry
{
    private readonly Dictionary<string, IAgentConnectorModule> _connectorModules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IJudgeProvider> _judgeProviders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IQuestionGenerationProvider> _questionProviders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IMonitoringModule> _monitoringModules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ITestingModule>   _testingModules   = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IRedTeamModule>    _redTeamModules   = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<mateModuleRegistry> _logger;

    public mateModuleRegistry(ILogger<mateModuleRegistry> logger)
    {
        _logger = logger;
    }

    // ── Registration ─────────────────────────────────────────────────────────

    public void RegisterConnector(IAgentConnectorModule module)
    {
        _connectorModules[module.ConnectorType] = module;
        _logger.LogInformation("Registered agent connector module: {ConnectorType} ({DisplayName})",
            module.ConnectorType, module.DisplayName);
    }

    public void RegisterJudgeProvider(IJudgeProvider provider)
    {
        _judgeProviders[provider.ProviderType] = provider;
        _logger.LogInformation("Registered judge provider: {ProviderType}", provider.ProviderType);
    }

    public void RegisterQuestionProvider(IQuestionGenerationProvider provider)
    {
        _questionProviders[provider.ProviderType] = provider;
        _logger.LogInformation("Registered question generation provider: {ProviderType}", provider.ProviderType);
    }

    public void RegisterMonitoring(IMonitoringModule module)
    {
        _monitoringModules[module.ProviderType] = module;
        _logger.LogInformation("Registered monitoring module: {ProviderType} ({DisplayName})",
            module.ProviderType, module.DisplayName);
    }

    public void RegisterTestingModule(ITestingModule module)
    {
        _testingModules[module.ProviderType] = module;
        _logger.LogInformation("Registered testing module: {ProviderType} ({DisplayName})",
            module.ProviderType, module.DisplayName);
    }

    public void RegisterRedTeamModule(IRedTeamModule module)
    {
        _redTeamModules[module.ProviderType] = module;
        _logger.LogInformation("Registered red-team module: {ProviderType} ({DisplayName})",
            module.ProviderType, module.DisplayName);
    }

    // ── Resolution ────────────────────────────────────────────────────────────

    /// <summary>Returns the connector module for the given type, or throws <see cref="ModuleNotFoundException"/>.</summary>
    public IAgentConnectorModule GetConnector(string connectorType)
    {
        if (!_connectorModules.TryGetValue(connectorType, out var module))
            throw new ModuleNotFoundException("AgentConnector", connectorType, _connectorModules.Keys);
        return module;
    }

    /// <summary>Returns the judge provider for the given type, or throws <see cref="ModuleNotFoundException"/>.</summary>
    public IJudgeProvider GetJudgeProvider(string providerType)
    {
        if (!_judgeProviders.TryGetValue(providerType, out var provider))
            throw new ModuleNotFoundException("JudgeProvider", providerType, _judgeProviders.Keys);
        return provider;
    }

    /// <summary>Returns the question generation provider for the given type, or throws <see cref="ModuleNotFoundException"/>.</summary>
    public IQuestionGenerationProvider GetQuestionProvider(string providerType)
    {
        if (!_questionProviders.TryGetValue(providerType, out var provider))
            throw new ModuleNotFoundException("QuestionGenerationProvider", providerType, _questionProviders.Keys);
        return provider;
    }

    public IReadOnlyList<IAgentConnectorModule>          GetAllConnectors()        => [.. _connectorModules.Values];
    public IReadOnlyList<IJudgeProvider>                  GetAllJudgeProviders()    => [.. _judgeProviders.Values];
    public IReadOnlyList<IMonitoringModule>               GetAllMonitoring()        => [.. _monitoringModules.Values];
    public IReadOnlyList<ITestingModule>                  GetAllTestingModules()    => [.. _testingModules.Values];
    public IReadOnlyList<IQuestionGenerationProvider>     GetAllQuestionProviders() => [.. _questionProviders.Values];
    public IReadOnlyList<IRedTeamModule>                  GetAllRedTeamModules()    => [.. _redTeamModules.Values];

    /// <summary>Returns the red-team module for the given provider type, or throws <see cref="ModuleNotFoundException"/>.</summary>
    public IRedTeamModule GetRedTeamModule(string providerType)
    {
        if (!_redTeamModules.TryGetValue(providerType, out var module))
            throw new ModuleNotFoundException("RedTeamModule", providerType, _redTeamModules.Keys);
        return module;
    }
}

/// <summary>
/// Thrown when a requested module type has not been registered.
/// Maps to HTTP 400 Bad Request — the caller specified a provider type that is not installed.
/// </summary>
public sealed class ModuleNotFoundException : Exception
{
    public string ModuleCategory { get; }
    public string RequestedType { get; }
    public IEnumerable<string> RegisteredTypes { get; }

    public ModuleNotFoundException(string category, string requested, IEnumerable<string> registered)
        : base($"No {category} module registered for type '{requested}'. " +
               $"Registered types: [{string.Join(", ", registered)}].")
    {
        ModuleCategory = category;
        RequestedType = requested;
        RegisteredTypes = registered;
    }
}
