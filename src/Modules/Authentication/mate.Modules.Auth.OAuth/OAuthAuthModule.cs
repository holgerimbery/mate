// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Security.Claims;
using mate.Domain.Contracts.Modules;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace mate.Modules.Auth.OAuth;

/// <summary>
/// Generic OAuth 2.0 / OIDC authentication module.
///
/// Supports any OIDC-compliant identity provider (Okta, Auth0, Keycloak, …).
/// Uses standard JwtBearer middleware; the issuer and audience come from config.
///
/// Required configuration section: "OAuth" with:
///   - Authority   (e.g. https://your-idp.com)
///   - Audience    (API audience / client_id)
///   - TenantClaim (claim name that carries the tenant identifier, default: "tid")
///   - UserClaim   (claim name for user ID, default: "sub")
///   - RoleClaim   (claim name for role, default: "mate_role")
/// </summary>
public sealed class OAuthAuthModule : IAuthModule
{
    public string SchemeName => "OAuth";
    public string DisplayName => "Generic OAuth 2.0 / OIDC";
    public bool SupportsDevelopmentBypass => true;

    public void ConfigureAuthentication(AuthenticationBuilder builder, IConfiguration config)
    {
        var section   = config.GetSection("OAuth");
        var authority = section["Authority"] ?? throw new InvalidOperationException("OAuth:Authority is required.");
        var audience  = section["Audience"]  ?? throw new InvalidOperationException("OAuth:Audience is required.");

        builder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opt =>
        {
            opt.Authority = authority;
            opt.Audience  = audience;
            opt.RequireHttpsMetadata = true;
        });
    }

    public void ConfigureAuthorization(AuthorizationOptions options, IConfiguration config)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        options.AddPolicy("AdminOnly",     p => p.RequireRole("SuperAdmin", "TenantAdmin"));
        options.AddPolicy("TesterOrAbove", p => p.RequireRole("SuperAdmin", "TenantAdmin", "Tester"));
        options.AddPolicy("ViewerOrAbove", p => p.RequireRole("SuperAdmin", "TenantAdmin", "Tester", "Viewer"));
    }

    public Task<ClaimsPrincipal> TransformClaimsAsync(ClaimsPrincipal external)
    {
        var claims = new List<Claim>(external.Claims);

        var tenantClaim = "tid";
        var userClaim   = "sub";
        var roleClaim   = "mate_role";

        var tid  = external.FindFirst(tenantClaim)?.Value;
        var uid  = external.FindFirst(userClaim)?.Value;
        var role = external.FindFirst(roleClaim)?.Value ?? "Viewer";

        if (!string.IsNullOrEmpty(tid))
            claims.Add(new Claim("mate:externalTenantId", tid));
        if (!string.IsNullOrEmpty(uid))
            claims.Add(new Claim("mate:userId", uid));

        claims.Add(new Claim(ClaimTypes.Role, role));
        claims.Add(new Claim("mate:role", role));

        var identity = new ClaimsIdentity(claims, external.Identity?.AuthenticationType ?? "OAuth");
        return Task.FromResult(new ClaimsPrincipal(identity));
    }
}

/// <summary>DI extension for the OAuth auth module.</summary>
public static class OAuthAuthModuleExtensions
{
    public static IServiceCollection AddmateOAuthAuth(this IServiceCollection services)
    {
        services.AddSingleton<IAuthModule, OAuthAuthModule>();
        return services;
    }
}
