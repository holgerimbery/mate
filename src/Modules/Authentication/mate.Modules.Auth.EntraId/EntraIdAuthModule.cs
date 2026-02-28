using System.Security.Claims;
using mate.Domain.Contracts.Modules;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;

namespace mate.Modules.Auth.EntraId;

/// <summary>
/// Microsoft Entra ID (Azure AD) authentication module.
///
/// Uses Microsoft.Identity.Web to validate Bearer tokens issued by Entra ID.
/// Tenant isolation is derived from the 'tid' claim (Entra tenant ID = external tenant ID).
///
/// Required configuration section: "AzureAd" with:
///   - TenantId
///   - ClientId (audience)
///   - Scope (optional, for delegated flows)
/// </summary>
public sealed class EntraIdAuthModule : IAuthModule
{
    public string SchemeName => "EntraId";
    public string DisplayName => "Microsoft Entra ID";
    public bool SupportsDevelopmentBypass => true;

    public void ConfigureAuthentication(AuthenticationBuilder builder, IConfiguration config)
    {
        builder.AddMicrosoftIdentityWebApi(config.GetSection("AzureAd"));
    }

    public void ConfigureAuthorization(AuthorizationOptions options, IConfiguration config)
    {
        // Require authenticated user by default on all API endpoints
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        // Role-based policies
        options.AddPolicy("AdminOnly",   p => p.RequireRole("Admin", "PlatformAdmin"));
        options.AddPolicy("TesterOrAbove", p => p.RequireRole("Admin", "PlatformAdmin", "Tester"));
        options.AddPolicy("ViewerOrAbove", p => p.RequireRole("Admin", "PlatformAdmin", "Tester", "Viewer"));
    }

    public Task<ClaimsPrincipal> TransformClaimsAsync(ClaimsPrincipal external)
    {
        // Map Entra-specific claims to mate internal claims
        var claims = new List<Claim>(external.Claims);

        // ExternalTenantId from 'tid' (Entra tenant ID)
        var tid = external.FindFirst("tid")?.Value
               ?? external.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        if (!string.IsNullOrEmpty(tid))
            claims.Add(new Claim("mate:externalTenantId", tid));

        // UserId from oid (object ID) — unique per user per directory
        var oid = external.FindFirst("oid")?.Value
               ?? external.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (!string.IsNullOrEmpty(oid))
            claims.Add(new Claim("mate:userId", oid));

        // Role from 'roles' claim array, defaulting to Viewer
        var role = external.FindAll("roles").FirstOrDefault()?.Value ?? "Viewer";
        claims.Add(new Claim("mate:role", role));

        var identity = new ClaimsIdentity(claims, external.Identity?.AuthenticationType ?? "EntraId");
        return Task.FromResult(new ClaimsPrincipal(identity));
    }
}

/// <summary>DI extension for Entra ID auth module.</summary>
public static class EntraIdAuthModuleExtensions
{
    public static IServiceCollection AddmateEntraIdAuth(this IServiceCollection services)
    {
        services.AddSingleton<IAuthModule, EntraIdAuthModule>();
        return services;
    }
}
