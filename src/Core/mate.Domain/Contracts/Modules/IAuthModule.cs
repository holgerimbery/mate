// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace mate.Domain.Contracts.Modules;

/// <summary>
/// Pluggable identity-provider module.
/// Entra ID is the Phase 1 default; OAuth is the Phase 2 / future option.
/// Register via Add{CodePrefix}AuthModule&lt;T&gt;() at startup.
/// </summary>
public interface IAuthModule
{
    string SchemeName { get; }
    string DisplayName { get; }

    /// <summary>When true the module can register a DevelopmentAuthHandler bypass.</summary>
    bool SupportsDevelopmentBypass { get; }

    void ConfigureAuthentication(AuthenticationBuilder builder, IConfiguration config);
    void ConfigureAuthorization(AuthorizationOptions options, IConfiguration config);

    /// <summary>
    /// Maps provider-specific claims to the internal mate claim set.
    /// Must produce: ExternalTenantId, UserId, Role (SuperAdmin|TenantAdmin|Tester|Viewer).
    /// </summary>
    Task<ClaimsPrincipal> TransformClaimsAsync(ClaimsPrincipal external);
}
