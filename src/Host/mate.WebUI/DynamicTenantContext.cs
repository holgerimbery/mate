// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
using System.Security.Claims;
using mate.Core.Tenancy;
using mate.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace mate.WebUI;

/// <summary>
/// Resolves the current tenant dynamically for Blazor Server circuits.
/// Unlike a one-time scoped snapshot, this can recover from prerender/circuit timing
/// where the initial scope is created before auth claims are fully available.
/// </summary>
public sealed class DynamicTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider? _authStateProvider;
    private readonly TenantLookupService _tenantLookupService;
    private readonly IConfiguration _config;

    private Guid? _cachedTenantId;

    public DynamicTenantContext(
        IHttpContextAccessor httpContextAccessor,
        TenantLookupService tenantLookupService,
        IConfiguration config,
        AuthenticationStateProvider? authStateProvider = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantLookupService = tenantLookupService;
        _config = config;
        _authStateProvider = authStateProvider;
    }

    public Guid TenantId => ResolveTenantId();

    private Guid ResolveTenantId()
    {
        if (_cachedTenantId.HasValue && _cachedTenantId.Value != Guid.Empty)
            return _cachedTenantId.Value;

        var user = _httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true && _authStateProvider is not null)
        {
            try
            {
                user = _authStateProvider.GetAuthenticationStateAsync().GetAwaiter().GetResult().User;
            }
            catch
            {
                user = null;
            }
        }

        var externalIds = new List<string?>
        {
            user?.FindFirst("mate:externalTenantId")?.Value,
            user?.FindFirst("tid")?.Value,
            user?.FindFirst("tenantid")?.Value,
            user?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value,
            _config["AzureAd:TenantId"],
            _config["AzureContext:TenantId"]
        };

        foreach (var externalId in externalIds.Where(v => !string.IsNullOrWhiteSpace(v) && !v!.StartsWith("__", StringComparison.Ordinal)))
        {
            var resolved = Task.Run(() => _tenantLookupService.LookupByExternalIdAsync(externalId!))
                .GetAwaiter().GetResult();

            if (resolved.HasValue)
            {
                _cachedTenantId = resolved.Value;
                return resolved.Value;
            }

            if (Guid.TryParse(externalId, out var directTenantId) && directTenantId != Guid.Empty)
            {
                _cachedTenantId = directTenantId;
                return directTenantId;
            }
        }

        return Guid.Empty;
    }
}