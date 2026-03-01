using System.Text.Json;
using mate.Domain.Contracts.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace mate.Modules.AgentConnector.CopilotStudio;

/// <summary>
/// Configuration POCO for the CopilotStudio connector.
/// Values are deserialized from <see cref="AgentConnectorConfig.ConfigJson"/>;
/// secrets like <see cref="DirectLineSecret"/> come from <see cref="AgentConnectionConfig.ResolvedSecrets"/>.
/// </summary>
public sealed class CopilotStudioConnectorConfig
{
    public string BotId             { get; set; } = string.Empty;
    public string EnvironmentId     { get; set; } = string.Empty;

    /// <summary>Secret reference for the Direct Line v3 secret (required when UseWebChannelSecret=false).</summary>
    public string DirectLineSecretRef { get; set; } = string.Empty;

    /// <summary>Secret reference for the Web Channel Security secret (required when UseWebChannelSecret=true).</summary>
    public string WebChannelSecretRef { get; set; } = string.Empty;

    /// <summary>Populated at runtime from ResolvedSecrets by the module's CreateConnector.</summary>
    internal string DirectLineSecret { get; set; } = string.Empty;

    /// <summary>Populated at runtime from ResolvedSecrets by the module's CreateConnector.</summary>
    internal string WebChannelSecret { get; set; } = string.Empty;

    public int ReplyTimeoutSeconds  { get; set; } = 30;
    public bool UseWebChannelSecret { get; set; } = false;
}

/// <summary>
/// Module descriptor for Copilot Studio (Direct Line v3) agent connections.
/// Registered at startup via AddmateCopilotStudioModule() extension.
/// </summary>
public sealed class CopilotStudioConnectorModule : IAgentConnectorModule
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public string ConnectorType => "CopilotStudio";
    public string DisplayName   => "Microsoft Copilot Studio";

    public CopilotStudioConnectorModule(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory     = loggerFactory;
    }

    public IAgentConnector CreateConnector(AgentConnectionConfig config)
    {
        var raw = JsonSerializer.Deserialize<CopilotStudioConnectorConfig>(config.ConfigJson)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize {nameof(CopilotStudioConnectorConfig)} from ConfigJson.");

        // Resolve the appropriate secret based on the authentication mode.
        // Resolution order: ResolvedSecrets dictionary (populated by Core from ISecretService),
        // then fall back to using the ref value itself (supports wizard direct-entry and
        // local dev where the raw secret is pasted directly into the form).
        if (raw.UseWebChannelSecret)
        {
            config.ResolvedSecrets.TryGetValue(raw.WebChannelSecretRef, out var wcSecret);
            if (string.IsNullOrWhiteSpace(wcSecret))
                wcSecret = raw.WebChannelSecretRef; // direct-value fallback
            if (string.IsNullOrWhiteSpace(wcSecret))
                throw new InvalidOperationException(
                    "Web Channel Security secret is not configured. " +
                    "Enter the secret value or set an environment variable with the reference name.");
            raw.WebChannelSecret = wcSecret;
        }
        else
        {
            config.ResolvedSecrets.TryGetValue(raw.DirectLineSecretRef, out var dlSecret);
            if (string.IsNullOrWhiteSpace(dlSecret))
                dlSecret = raw.DirectLineSecretRef; // direct-value fallback
            if (string.IsNullOrWhiteSpace(dlSecret))
                throw new InvalidOperationException(
                    "Direct Line secret is not configured. " +
                    "Enter the secret value or set an environment variable with the reference name.");
            raw.DirectLineSecret = dlSecret;
        }

        var http   = _httpClientFactory.CreateClient("CopilotStudio");
        var logger = _loggerFactory.CreateLogger<CopilotStudioConnector>();
        return new CopilotStudioConnector(raw, http, logger);
    }

    public ValidationResult ValidateConfig(string configJson)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<CopilotStudioConnectorConfig>(configJson);
            if (raw is null || string.IsNullOrWhiteSpace(raw.BotId))
                return ValidationResult.Fail("BotId is required.");
            if (raw.UseWebChannelSecret)
            {
                if (string.IsNullOrWhiteSpace(raw.WebChannelSecretRef))
                    return ValidationResult.Fail("WebChannelSecretRef is required when UseWebChannelSecret is true.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(raw.DirectLineSecretRef))
                    return ValidationResult.Fail("DirectLineSecretRef is required when UseWebChannelSecret is false.");
            }
        }
        catch (JsonException ex)
        {
            return ValidationResult.Fail($"Invalid JSON: {ex.Message}");
        }

        return ValidationResult.Ok();
    }

    public IEnumerable<ConfigFieldDefinition> GetConfigDefinition()
    {
        yield return new ConfigFieldDefinition("BotId",               "Bot ID",                                  "Bot identifier from Copilot Studio (Schema name of the agent)",                                                                                                                             "text",    true);
        yield return new ConfigFieldDefinition("EnvironmentId",       "Power Platform Environment",              "Power Platform environment GUID (optional)",                                                                                                                                                "text",    false);
        yield return new ConfigFieldDefinition("UseWebChannelSecret", "Use Web Channel Security Secret",         "When true, authenticate using a Web Channel Security secret (recommended). When false, use a Direct Line v3 secret.",                                                                        "boolean", false, "false");
        yield return new ConfigFieldDefinition("WebChannelSecretRef", "Web Channel Security Secret (ref)",       "Secret reference for the Web Channel Security secret. Find it in Copilot Studio → Settings → Security → Web channel security. Required when Use Web Channel Security Secret is true.",  "secret",  false);
        yield return new ConfigFieldDefinition("DirectLineSecretRef", "Direct Line Secret (ref)",                "Secret reference for the Direct Line v3 secret from Azure Bot Service. Required when Use Web Channel Security Secret is false.",                                                           "secret",  false);
        yield return new ConfigFieldDefinition("ReplyTimeoutSeconds", "Reply Timeout (s)",                       "Maximum seconds to wait for a bot reply (default 30)",                                                                                                                                      "number",  false, "30");
    }

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient("CopilotStudio", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddSingleton<IAgentConnectorModule, CopilotStudioConnectorModule>();
    }
}

/// <summary>
/// DI extension consumed by the host's Program.cs.
/// </summary>
public static class CopilotStudioModuleExtensions
{
    public static IServiceCollection AddmateCopilotStudioModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddHttpClient("CopilotStudio", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        // Register as IAgentConnectorModule so the module registry can discover it
        // via GetServices<IAgentConnectorModule>() at application startup.
        services.AddSingleton<IAgentConnectorModule, CopilotStudioConnectorModule>();

        return services;
    }
}
