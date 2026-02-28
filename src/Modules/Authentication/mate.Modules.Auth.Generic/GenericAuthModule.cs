using System.Security.Claims;
using System.Text.Encodings.Web;
using mate.Domain.Contracts.Modules;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace mate.Modules.Auth.Generic;

/// <summary>
/// Generic / development authentication module.
///
/// Accepts any request as authenticated and injects a synthetic admin principal.
/// USE ONLY IN DEVELOPMENT OR LOCAL TESTING — never register this in production.
///
/// Set <c>Authentication:Scheme = "Generic"</c> in appsettings.Development.json.
/// </summary>
public sealed class GenericAuthModule : IAuthModule
{
    public string SchemeName => "Generic";
    public string DisplayName => "Generic / Development Auth (no authentication — dev only)";
    public bool SupportsDevelopmentBypass => true;

    public void ConfigureAuthentication(AuthenticationBuilder builder, IConfiguration config)
    {
        builder.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>(
            SchemeName, _ => { });
    }

    public void ConfigureAuthorization(AuthorizationOptions options, IConfiguration config)
    {
        options.FallbackPolicy = null; // Allow everything in dev mode
    }

    public Task<ClaimsPrincipal> TransformClaimsAsync(ClaimsPrincipal external)
        => Task.FromResult(external); // Pass through — the handler already sets all claims
}

// ── Development bypass handler ────────────────────────────────────────────────

/// <summary>
/// Always-successful authentication handler for local development.
/// Injects a synthetic tenant/user/role claim set.
/// </summary>
internal sealed class DevelopmentAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    // A deterministic development tenant/user ID so routes behave consistently
    private static readonly Guid DevTenantId = Guid.Parse("00000000-0000-0000-0000-000000000099");
    private static readonly Guid DevUserId   = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public DevelopmentAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("mate:externalTenantId", DevTenantId.ToString()),
            new Claim("mate:userId",            DevUserId.ToString()),
            new Claim("mate:role",              "Admin"),
            new Claim(ClaimTypes.Name,          "Development User"),
            new Claim(ClaimTypes.Email,         "dev@localhost"),
        };

        var identity  = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>DI extension for the Generic dev auth module.</summary>
public static class GenericAuthModuleExtensions
{
    public static IServiceCollection AddmateGenericAuth(this IServiceCollection services)
    {
        services.AddSingleton<IAuthModule, GenericAuthModule>();
        return services;
    }
}
