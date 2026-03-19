# Copyright (c) Holger Imbery. All rights reserved.
# Licensed under the mate Custom License. See LICENSE in the project root.
# Shared Key Vault naming helpers for core and enterprise provisioning scripts.
# Keeps naming deterministic across single-vault and multi-vault modes.

function Get-NormalizedSuffix {
    param(
        [Parameter(Mandatory = $false)]
        [string]$Suffix
    )

    if ([string]::IsNullOrWhiteSpace($Suffix)) {
        return ""
    }

    $suffixNormalized = ($Suffix.Trim().ToLowerInvariant() -replace "[^a-z0-9]", "")
    if ([string]::IsNullOrWhiteSpace($suffixNormalized)) {
        return ""
    }

    return "-$suffixNormalized"
}

function Get-NormalizedEnvironment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Environment
    )

    $envLower = $Environment.Trim().ToLowerInvariant()
    if ($envLower -notin @('dev', 'test', 'prod')) {
        throw "Unsupported environment '$Environment'. Allowed: dev, test, prod."
    }

    return $envLower
}

function Get-CoreVaultName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Environment,

        [Parameter(Mandatory = $false)]
        [string]$Suffix
    )

    $envLower = Get-NormalizedEnvironment -Environment $Environment
    $suffixNormalized = Get-NormalizedSuffix -Suffix $Suffix
    return "mate-$envLower$suffixNormalized-kv"
}

function Get-PlatformVaultName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Environment,

        [Parameter(Mandatory = $false)]
        [string]$Suffix
    )

    $envLower = Get-NormalizedEnvironment -Environment $Environment
    $suffixNormalized = Get-NormalizedSuffix -Suffix $Suffix
    return "mate-$envLower$suffixNormalized-platform-kv"
}

function Get-TenantVaultName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Environment,

        [Parameter(Mandatory = $true)]
        [string]$TenantId,

        [Parameter(Mandatory = $false)]
        [string]$Suffix
    )

    $envLower = Get-NormalizedEnvironment -Environment $Environment
    $suffixNormalized = Get-NormalizedSuffix -Suffix $Suffix
    $tenantPrefix = $TenantId.Trim().ToLowerInvariant().Replace('-', '')
    if ($tenantPrefix.Length -lt 8) {
        throw "TenantId '$TenantId' is too short for vault naming."
    }

    # Keep tenant vault names <= 24 chars:
    #   mate-<env><-suffix>-t-<tenant8>-kv
    return "mate-$envLower$suffixNormalized-t-$($tenantPrefix.Substring(0, 8))-kv"
}

function Get-TenantVaultUriTemplate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Environment,

        [Parameter(Mandatory = $false)]
        [string]$Suffix
    )

    $envLower = Get-NormalizedEnvironment -Environment $Environment
    $suffixNormalized = Get-NormalizedSuffix -Suffix $Suffix
    # Must remain Key Vault-name compliant after placeholder substitution.
    # Runtime replaces {tenantId} with a compact tenant key (first 8 hex chars).
    return "https://mate-$envLower$suffixNormalized-t-{tenantId}-kv.vault.azure.net/"
}

function New-RandomVaultSuffix {
    $chars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray()
    $token = -join (1..6 | ForEach-Object { $chars | Get-Random })
    return $token
}
