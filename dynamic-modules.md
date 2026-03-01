# Dynamic Module System — Concept

> Status: **Concept** — approved for design review, not yet scheduled for implementation.
> Related epic: **E18** in `BACKLOG.md`

---

## Problem Statement

All modules are currently compiled into the host directly. `Program.cs` startup contains hard-coded `builder.Services.AddmateCopilotStudioModule(config)` calls for every module. Adding a new connector or judge requires modifying source code and redeploying the host. The goal is to allow modules to be distributed as self-contained `.dll` files (or NuGet packages) that the host discovers and loads at startup — without a source-level dependency on that module.

Scope: **Agent Connectors** (`IAgentConnectorModule`) and **Testing Modules** (`ITestingModule`) only. Auth and Monitoring modules remain host-compiled for v1.

---

## Architecture Overview

Two complementary delivery mechanisms — **folder-drop** for individual deployments (on-prem / Docker volume), **NuGet** for cloud/enterprise distribution. Both use the same host-side loading infrastructure.

```
mate.Domain                     ← pure contracts (IAgentConnectorModule, ITestingModule)
  └─ never changes its API        no application code, no DI framework refs

mate.Core                       ← mateModuleRegistry, PluginLoader (new)
  └─ owns AssemblyLoadContext     loads plugins, calls IModulePlugin.Register(IServiceCollection)

Module plugin DLL               ← ships as NuGet or raw .dll
  └─ implements IModulePlugin     single entry-point; wires own DI registrations
  └─ implements connector/judge   implements existing IAgentConnectorModule / ITestingModule
  └─ references mate.Domain ONLY  zero dependency on mate.Core, mate.WebUI, mate.Data
```

---

## The Single Required Interface: `IModulePlugin`

Every binary plugin exposes exactly one public type that implements `IModulePlugin`. This is the only contract the loader needs to know about — everything else stays internal to the plugin DLL.

```csharp
// mate.Domain — new file: Contracts/Modules/IModulePlugin.cs
public interface IModulePlugin
{
    string PluginId      { get; }   // e.g. "CopilotStudio", "MyCustomJudge"
    string PluginVersion { get; }   // semver
    string PluginType    { get; }   // "AgentConnector" | "TestingModule"

    /// Called once by the host at startup. Plugin registers its own services.
    void Register(IServiceCollection services, IConfiguration configuration);
}
```

Example third-party plugin entry-point:

```csharp
public class ParloaPlugin : IModulePlugin
{
    public string PluginId      => "Parloa";
    public string PluginVersion => "1.0.0";
    public string PluginType    => "AgentConnector";

    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IAgentConnectorModule, ParloaConnectorModule>();
        services.AddHttpClient<ParloaConnectorModule>();
        // own config binding, secrets, etc. — fully encapsulated
    }
}
```

---

## Host-Side Loading: `PluginLoader` in `mate.Core`

```
Startup
  │
  ├─ PluginLoader.DiscoverAndLoad(services, config)
  │     │
  │     ├─ scans  Plugins/*.dll  (configurable via MATE_PLUGIN_PATH env var)
  │     ├─ for each DLL:
  │     │     ├─ verify Authenticode signature against allowed thumbprint list
  │     │     ├─ load into MatePluginLoadContext (isolated AssemblyLoadContext)
  │     │     ├─ scan exported types for : IModulePlugin
  │     │     ├─ validate PluginType against allowed list ("AgentConnector","TestingModule")
  │     │     ├─ call plugin.Register(services, config)
  │     │     └─ write AuditLog entry: "PluginLoaded" for entity type "Plugin"
  │     │
  │     └─ existing hard-coded modules continue to work unchanged
  │
  └─ existing startup mateModuleRegistry population loop unchanged
```

`MatePluginLoadContext` extends `AssemblyLoadContext` with `isCollectible: true`, resolving `mate.Domain` against the host's already-loaded copy — preventing duplicate interface types that break `is` checks.

---

## Dependency Isolation Rules

| Allowed in plugin DLL | Not allowed |
|---|---|
| `mate.Domain` (contracts only) | `mate.Core` |
| Own third-party NuGet references | `mate.WebUI`, `mate.Data` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | Direct EF Core usage |
| `Microsoft.Extensions.Configuration.Abstractions` | Blazor, ASP.NET Core hosting |

Plugins get configuration via `IConfiguration` passed to `Register()`. Secrets (API keys) must use the existing `ISecretService` contract — resolved from DI at runtime via constructor injection in the connector/judge implementation.

---

## Plugin Discovery Modes

### Mode 1 — Folder drop (Docker / on-prem)

```
/app/plugins/
  mate.Modules.AgentConnector.MyBot.dll
  mate.Modules.Testing.MyJudge.dll
```

`appsettings.json`:
```json
{ "Plugins": { "Path": "/app/plugins", "Enabled": true } }
```

Docker Compose:
```yaml
volumes:
  - ./plugins:/app/plugins:ro
```

### Mode 2 — NuGet package (enterprise / cloud)

Plugin author publishes a NuGet package. A companion CLI tool `mate plugin add <package>` restores it into the plugins folder. The host only ever sees final DLLs — no NuGet restore at runtime.

### Mode 3 — Configuration-driven allow-list

Both built-in and plugin modules can be enabled/disabled without redeploying:

```
MATE_ENABLED_CONNECTORS=CopilotStudio,Parloa,MyBot
MATE_ENABLED_JUDGES=CopilotStudioJudge,MyJudge
```

The loader skips any module whose `PluginId` is absent from the list. If the list is absent entirely, all discovered plugins are loaded.

---

## Security Considerations

1. **Code signing** — each DLL must carry an Authenticode signature from a publisher thumbprint in the configured allow-list. Unsigned plugins are **rejected** at startup (hard failure, not a warning). This is mandatory — an unsigned DLL loaded into the host process can read credentials, exfiltrate DB data, and call external services without restriction.
2. **Minimal DI surface** — `Register()` receives `IServiceCollection`, not `IServiceProvider`. Plugins cannot resolve already-registered services at registration time.
3. **No Reflection::Emit** — `MatePluginLoadContext` loads assemblies without dynamic code generation permissions where the .NET runtime supports restriction.
4. **SSRF protection** — plugins that need HTTP declare base URLs in their `ConfigSchema`; the host validates these against the existing SSRF block-list before passing secrets to connectors at runtime.
5. **Audit trail** — every plugin load/unload attempt is written to the `AuditLogs` table (`AuditHelper.Log`, entity type `"Plugin"`, action `"PluginLoaded"` / `"PluginRejected"`) before any plugin registrations take effect.

---

## What Changes vs What Stays the Same

| Component | Changes | Unchanged |
|---|---|---|
| `mate.Domain` | Add `IModulePlugin` interface | All existing contracts |
| `mate.Core` | Add `PluginLoader`, `MatePluginLoadContext` | `mateModuleRegistry`, `CoreServiceExtensions` |
| `Program.cs` | Add `PluginLoader.DiscoverAndLoad(...)` call | All existing `AddmateXxxModule(...)` calls stay |
| Module DLLs | Implement `IModulePlugin` alongside existing interfaces | Internal connector/judge logic untouched |
| UI — Settings/Modules tab | Show plugin source (built-in vs external) and plugin version | Config form rendering, health checks unchanged |
| Docker Compose | Optional `plugins:` volume mount | Everything else |
| `mate.Worker` | Needs own `PluginLoader` call or shared plugins folder | Worker test execution logic unchanged |

---

## Open Decisions Before Implementation

| # | Question | Recommendation |
|---|---|---|
| OD-1 | Hot-reload at runtime (unload + reload without restart)? | Startup-only for v1 — avoids GC root leaks and complexity |
| OD-2 | Worker parity — separate plugin discovery or shared volume? | Shared volume; Worker loads same DLLs on its own startup |
| OD-3 | Config namespacing — flat root vs per-plugin section? | Namespaced: `Plugins:Parloa:ApiEndpoint` to avoid key collisions |
| OD-4 | Minimum .NET TFM for plugins? | `net9.0` only for v1 — mixed TFMs cause binding complexity |
| OD-5 | Signing enforcement in dev vs prod? | Dev: signature check skippable via `Plugins:SkipSignatureCheck=true`; prod: always enforced |
