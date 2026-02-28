namespace mate.Domain;

/// <summary>
/// Customer-facing brand identity constants.
/// <para>
/// Change <see cref="BrandName"/> here to rebrand the product without touching any other file.
/// All UI page titles, navigation labels, CLI descriptions and welcome text are driven by this class.
/// </para>
/// <para>
/// Note: the <c>mate</c> code identifier (namespaces, project names, DbContext, DI extension methods)
/// is independent and intentionally NOT derived from this class — see SaaS-Architecture-v2.md §0.
/// </para>
/// </summary>
public static class BrandInfo
{
    /// <summary>Customer-facing product name. Used in page titles, sidebar, and CLI.</summary>
    public const string BrandName = "mate";

    /// <summary>Full tagline shown on the home/welcome page.</summary>
    public const string BrandTagline = "Multi-Agent Testing Environment — AI agent quality testing platform";

    /// <summary>Short description used in CLI help text.</summary>
    public const string BrandCliDescription = "quality testing tool for conversational AI agents";

    /// <summary>Square logo used in the collapsed sidebar and as favicon. Served from wwwroot.</summary>
    public const string LogoUrl = "/mate-logo.png";

    /// <summary>Wide/horizontal logo used in the expanded sidebar header. Served from wwwroot.</summary>
    public const string LogoWideUrl = "/mate-logo-wide.png";
}
