// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain;

/// <summary>
/// Customer-facing brand identity values.
/// <para>
/// Values can be overridden at runtime from configuration section <c>Branding</c>.
/// Hosts should call <see cref="ConfigureFromConfiguration"/> during startup.
/// </para>
/// <para>
/// Note: the <c>mate</c> code identifier (namespaces, project names, DbContext, DI extension methods)
/// is independent and intentionally NOT derived from this class.
/// </para>
/// </summary>
public static class BrandInfo
{
    private const string DefaultBrandName = "mate";
    private const string DefaultBrandTagline = "Multi-Agent Testing Environment — AI agent quality testing platform";
    private const string DefaultBrandCliDescription = "quality testing tool for conversational AI agents";
    private const string DefaultLogoUrl = "/mate-logo.png";
    private const string DefaultLogoWideUrl = "/mate-logo-wide.png";
    private const string DefaultApiKeyPrefix = "mate_";

    /// <summary>Customer-facing product name. Used in page titles, sidebar, and CLI.</summary>
    public static string BrandName { get; private set; } = DefaultBrandName;

    /// <summary>Full tagline shown on the home/welcome page.</summary>
    public static string BrandTagline { get; private set; } = DefaultBrandTagline;

    /// <summary>Short description used in CLI help text.</summary>
    public static string BrandCliDescription { get; private set; } = DefaultBrandCliDescription;

    /// <summary>Square logo used in the collapsed sidebar and as favicon. Served from wwwroot.</summary>
    public static string LogoUrl { get; private set; } = DefaultLogoUrl;

    /// <summary>Wide/horizontal logo used in the expanded sidebar header. Served from wwwroot.</summary>
    public static string LogoWideUrl { get; private set; } = DefaultLogoWideUrl;

    /// <summary>Prefix used when generating API keys (for example <c>mate_</c>).</summary>
    public static string ApiKeyPrefix { get; private set; } = DefaultApiKeyPrefix;

    /// <summary>
    /// Applies branding values from configuration section <c>Branding</c>.
    /// Missing values keep defaults.
    /// </summary>
    public static void ConfigureFromConfiguration(Microsoft.Extensions.Configuration.IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        BrandName = Read(config, "Branding:BrandName", DefaultBrandName);
        BrandTagline = Read(config, "Branding:BrandTagline", DefaultBrandTagline);
        BrandCliDescription = Read(config, "Branding:BrandCliDescription", DefaultBrandCliDescription);
        LogoUrl = Read(config, "Branding:LogoUrl", DefaultLogoUrl);
        LogoWideUrl = Read(config, "Branding:LogoWideUrl", DefaultLogoWideUrl);
        ApiKeyPrefix = NormalizeApiKeyPrefix(Read(config, "Branding:ApiKeyPrefix", DefaultApiKeyPrefix));
    }

    private static string Read(Microsoft.Extensions.Configuration.IConfiguration config, string key, string fallback)
        => string.IsNullOrWhiteSpace(config[key]) ? fallback : config[key]!;

    private static string NormalizeApiKeyPrefix(string value)
    {
        var filtered = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(filtered))
            filtered = DefaultApiKeyPrefix.TrimEnd('_');
        return filtered.EndsWith('_') ? filtered : filtered + "_";
    }
}
