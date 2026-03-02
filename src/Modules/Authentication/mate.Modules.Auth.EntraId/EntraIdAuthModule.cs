// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Security.Claims;
using mate.Domain.Contracts.Modules;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

namespace mate.Modules.Auth.EntraId;

/// <summary>
/// Microsoft Entra ID (Azure AD) OIDC authentication module for Blazor Server.
///
/// Registers cookie-based OIDC (browser sign-in) via Microsoft.Identity.Web.
/// A separate named JwtBearer scheme ("EntraId_Bearer") handles headless API clients
/// that supply an Authorization: Bearer token; this does NOT override the default
/// cookie/OIDC scheme selection used by Blazor pages.
///
/// ── Proxy / ForwardedHeaders requirement ──────────────────────────────────────
/// This module REQUIRES that app.UseForwardedHeaders() is called BEFORE
/// app.UseAuthentication() in the middleware pipeline (handled in Program.cs).
/// Without it, OIDC redirect_uri is built from the internal container address
/// (http://webui:8080) instead of the real public URL, and Entra ID rejects it.
///
/// ── IClaimsTransformation registration ────────────────────────────────────────
/// This class does NOT self-register IClaimsTransformation inside ConfigureAuthentication.
/// Program.cs registers the *same* IAuthModule instance as IClaimsTransformation
/// to guarantee a single instance handles both concerns.
///
/// Required configuration section "AzureAd":
///   Instance, TenantId, ClientId, ClientSecret, CallbackPath (/signin-oidc)
/// </summary>
public sealed class EntraIdAuthModule : IAuthModule, IClaimsTransformation
{
    // Separate scheme name for bearer token validation so it NEVER overrides the
    // DefaultScheme / DefaultChallengeScheme that Program.cs explicitly sets to Cookies/OIDC.
    public const string BearerSchemeName = "EntraId_Bearer";

    public string SchemeName => "EntraId";
    public string DisplayName => "Microsoft Entra ID";
    public bool SupportsDevelopmentBypass => true;

    public void ConfigureAuthentication(AuthenticationBuilder builder, IConfiguration config)
    {
        // ── Browser / Blazor OIDC path ─────────────────────────────────────────
        // AddMicrosoftIdentityWebApp registers:
        //   • "Cookies"       — session cookie (DefaultScheme, DefaultSignInScheme)
        //   • "OpenIdConnect" — OIDC redirect handler (DefaultChallengeScheme)
        //
        // Deliberately no PostConfigure<OpenIdConnectOptions> override here.
        // Microsoft.Identity.Web V4 internally configures the correct ResponseType,
        // ResponseMode, PKCE, nonce handling and cookie policies for the current
        // flow. Overriding those settings (e.g. ResponseType="code", custom
        // NonceCookie/CorrelationCookie) breaks nonce validation because the
        // library's own PostConfigure sets RequireNonce=false for PKCE/code flows —
        // but if our PostConfigure runs after theirs it re-enables nonce cookie
        // behaviour that then fails (IDX21323).
        // AddMicrosoftIdentityWebApp owns ALL cookie, OIDC, and nonce configuration.
        // No PostConfigure overrides — any override runs after the library's own
        // PostConfigure and silently breaks nonce/correlation/session cookie handling.
        builder.AddMicrosoftIdentityWebApp(config.GetSection("AzureAd"));

        // ── API / Bearer token path ────────────────────────────────────────────
        // Named scheme "EntraId_Bearer" – only used when callers explicitly supply
        // "Authorization: Bearer <jwt>" and the middleware triggers it.
        // Registering with a non-default name ensures it NEVER hijacks the Blazor
        // browser flow (which uses Cookies as DefaultScheme).
        builder.AddMicrosoftIdentityWebApi(
            config.GetSection("AzureAd"),
            jwtBearerScheme: BearerSchemeName);

        // ── IClaimsTransformation ──────────────────────────────────────────────
        // NOT registered here. Program.cs registers the same IAuthModule singleton
        // instance as IClaimsTransformation to prevent a second instance being created.
    }

    /// <summary>Register /MicrosoftIdentity/Account/* controller routes for sign-in/sign-out UI.</summary>
    public static void AddMicrosoftIdentityUI(IServiceCollection services)
        => services.AddControllersWithViews().AddMicrosoftIdentityUI();

    public void ConfigureAuthorization(AuthorizationOptions options, IConfiguration config)
    {
        // No FallbackPolicy — each page declares [Authorize] individually.
        options.AddPolicy("AnyAuthenticated", p => p.RequireAuthenticatedUser());
        options.AddPolicy("AdminOnly",        p => p.RequireRole("Admin", "PlatformAdmin"));
        options.AddPolicy("TesterOrAbove",    p => p.RequireRole("Admin", "PlatformAdmin", "Tester"));
        options.AddPolicy("ViewerOrAbove",    p => p.RequireRole("Admin", "PlatformAdmin", "Tester", "Viewer"));
    }

    // ── IClaimsTransformation ──────────────────────────────────────────────────

    /// <summary>Called by the ASP.NET Core auth pipeline on every authenticated request.</summary>
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        => TransformClaimsAsync(principal);

    /// <summary>Maps Entra ID claims → mate internal claim set (IAuthModule contract).</summary>
    public Task<ClaimsPrincipal> TransformClaimsAsync(ClaimsPrincipal external)
    {
        var claims = new List<Claim>(external.Claims);

        // mate:externalTenantId — from 'tid' (Entra tenant GUID)
        var tid = external.FindFirst("tid")?.Value
               ?? external.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        if (!string.IsNullOrEmpty(tid))
            claims.Add(new Claim("mate:externalTenantId", tid));

        // mate:userId — from 'oid' (object ID, unique per user per directory)
        var oid = external.FindFirst("oid")?.Value
               ?? external.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (!string.IsNullOrEmpty(oid))
            claims.Add(new Claim("mate:userId", oid));

        // mate:role — from app role or default to Viewer
        var role = external.FindAll("roles").FirstOrDefault()?.Value ?? "Viewer";
        claims.Add(new Claim("mate:role", role));

        var identity = new ClaimsIdentity(claims, external.Identity?.AuthenticationType ?? "EntraId",
            nameType: "name", roleType: ClaimTypes.Role);
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
