# mate — Changelog

All notable changes to this project will be documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [v0.2.1] — 2026-03-01

### Added
- **`AuditHelper` utility class** (`src/Host/mate.WebUI/AuditHelper.cs`): static `AuditHelper.Log(db, tenantId, action, entityType, ...)` helper — writes an `AuditLog` entity to the EF change tracker before every `SaveChangesAsync`; eliminates repeated inline `db.AuditLogs.Add(new AuditLog {...})` blocks across all mutation paths.
- **`ApiKeysPage.razor`** (`/api-keys`): new dedicated page for API key management with inline page-swap layout (matching TestSuites pattern) — generate, list, revoke, and delete API keys; linked from the main navigation sidebar.

### Fixed
- **`AuditLogPage.razor` — null `EntityId` display crash**: `EntityId?.ToString("D").Substring(0,8)` threw when `EntityId` was null; replaced with `log.EntityId.HasValue ? log.EntityId.Value.ToString("D")[..8] + "…" : "—"`.
- **`Settings.razor` — missing `AuthStateProvider` inject**: `OnInitializedAsync` called `AuthStateProvider.GetAuthenticationStateAsync()` without a corresponding `@inject AuthenticationStateProvider AuthStateProvider` directive; inject added.

### Changed
- **Audit logging wired into all Blazor mutation pages**: `Agents.razor`, `TestSuites.razor`, `Settings.razor`, `ApiKeysPage.razor` — every create/update/delete/run-start/key-revoke operation calls `AuditHelper.Log` before `SaveChangesAsync`; actions: `Created`, `Updated`, `Deleted`, `RunStarted`, `KeyRevoked`; entity types: `Agent`, `TestSuite`, `TestCase`, `JudgeSetting`, `QGenSetting`, `ApiKey`, `Run`.
- **Audit logging wired into all REST API mutation endpoints** (`Program.cs`): mirrors Blazor page coverage — `POST/PUT/DELETE /api/agents`, `/api/testsuites`, `/api/testcases`, `POST/DELETE /api/runs`, `POST/DELETE /api/documents`, `POST/PUT/DELETE /api/judgesettings`, `POST /api/admin/api-keys`, `DELETE /api/admin/api-keys/{id}` (action: `KeyRevoked`).

---

## [v0.2.0] — 2026-03-01

### Added
- **Microsoft Entra ID (Azure AD) authentication**: full OIDC browser sign-in flow via `Microsoft.Identity.Web 3.4.0`. `EntraIdAuthModule` registers `AddMicrosoftIdentityWebApp` (OIDC + session cookie) and `AddMicrosoftIdentityWebApi` (JWT Bearer `EntraId` scheme). `AddMicrosoftIdentityUI` registers `/MicrosoftIdentity/Account/*` MVC controllers for redirect handling.
- **`IClaimsTransformation` on `EntraIdAuthModule`**: injects `mate:externalTenantId` (from `tid`), `mate:userId` (from `oid`), and `mate:role` (from `roles`) claims — available in both HTTP pipeline and Blazor Server SignalR circuits.
- **Tenant mapping seeder**: `mateDbSeeder.SeedEntraIdTenantMappingAsync` idempotently updates the dev tenant row's `ExternalTenantId` to the configured Azure AD tenant GUID — resolves the tenant lookup when `tid` claim is present.
- **Tenant ID resolution across auth schemes**: `TenantLookupService` maps the Entra ID `tid` claim (external tenant GUID) to the internal `Tenant.Id` via database lookup, with 5-minute `IMemoryCache` caching. Enables seamless switching between `Generic` and `EntraId` auth without data loss.
- **Dark Mode**: Full dark theme via `[data-theme="dark"]` CSS variables in `app.css`; JS helpers (`mateInitDarkMode`, `mateSetDarkMode`, `mateGetDarkMode`) in `app.js` with localStorage persistence; sidebar toggle button in `MainLayout.razor`.
- **Settings — Appearance tab**: New tab in Settings to toggle dark mode with a switch control.
- **API Key Authentication**: `ApiKeyAuthHandler` — proper ASP.NET Core `IAuthenticationHandler`; registered as `"ApiKey"` auth scheme alongside primary; `FallbackPolicy` extended to accept both — fixes API key requests being rejected.
- **Help — OpenAPI download**: "Interactive API Explorer" (→`/scalar/v1`) and "Download OpenAPI spec" (→`/openapi/v1.json`) buttons added to REST API Reference section.
- **Home page rewrite**: Action card grid (Wizard, Test Suites, Documents, Agents, Dashboard, Quick Run); KPI stats row; Quick Run modal; Getting Started steps; Recent Runs feed; Module Status panel using `mateModuleRegistry`; version + changelog entry in header.
- **`mate.Modules.Testing.CopilotStudioJudge`**: new testing module combining deterministic rubrics with a citation-aware LLM judge tuned for Microsoft Copilot Studio agents. Features: three built-in default rubrics (NonEmpty mandatory gate, no rejection phrase, no error surfacing), citation block awareness (`[1]: cite:...` = positive grounding indicator), semantic equivalence evaluation, CopilotStudio-specific scoring weights (TaskSuccess 0.35, IntentMatch 0.25, Factuality 0.25, Helpfulness 0.10, Safety 0.05), 0.3×rubrics + 0.7×LLM blend, rubrics-only mode when LLM not configured, graceful fallback on LLM failure.
- **`CopilotStudioConnectorModule` — Web Channel Secret support**: `WebChannelSecretRef` config field added; `CreateConnector()` and `GenerateConversationTokenAsync()` honour `UseWebChannelSecret=true`; `ValidateConfig()` enforces the correct secret ref per mode; `GetConfigDefinition()` reordered with descriptive help text. Find the secret in Copilot Studio → Settings → Security → Web channel security.
- **Branding — logo**: `mate-logo.png` and `mate-logo-wide.png` added to `wwwroot`; `BrandInfo.LogoUrl` and `BrandInfo.LogoWideUrl` added to `BrandInfo.cs`; `MainLayout.razor` sidebar shows the logo image instead of the initial letter; `App.razor` `<head>` includes a `<link rel="icon">` favicon.
- **`CopilotStudioConnectorModule.GetConfigDefinition()`** updated: `EnvironmentId` is now optional; `UseWebChannelSecret` boolean field added (default: false); `ReplyTimeoutSeconds` default value set to "30".
- **WebUI pages**: `Discover.razor` (`/discover`), `Rubrics.razor` (`/rubrics`), `Help.razor` (`/help`), `AuditLogPage.razor` (`/audit-log`) — new standalone pages.
- **Settings — Modules tab**: new default tab showing all registered `IAgentConnectorModule`, `ITestingModule`, and `IMonitoringModule` implementations with their config field definitions and links to the creation wizard.
- **Wizard — module-driven Step 1**: agent creation wizard Step 1 dynamically lists all registered `IAgentConnectorModule` implementations; Step 2 config form generated from the module's `ConfigSchema`.
- **`mateModuleRegistry`**: added `RegisterTestingModule()` and `GetAllTestingModules()` to support testing module discovery.
- **Program.cs registry population**: module registry populated at application startup by resolving all `IAgentConnectorModule`, `ITestingModule`, `IJudgeProvider`, and `IMonitoringModule` services from DI.
- **`BrandInfo.cs`** (`mate.Domain`): new static class with `BrandName = "mate"`, `BrandTagline`, `BrandCliDescription`, `LogoUrl`, and `LogoWideUrl` — single source of truth for all brand display text and assets.
- **`BACKLOG.md`**, **`CHANGELOG.md`**, **`VERSION`** added to repository root.
- **`memory.txt`** added to repo root — persistent cross-session context file for AI-assisted development.
- **`.gitignore`** added; repository initialised and pushed to `https://github.com/holgerimbery/mate` (private).

### Changed
- **`EntraIdAuthModule.ConfigureAuthentication`**: uses `configureMicrosoftIdentityOptions` callback overload; sets `CorrelationCookie.SameSite = Unspecified` and `NonceCookie.SameSite = Unspecified` to fix OIDC callback failure caused by Azure AD's cross-site `form_post` response mode dropping `SameSite=Lax` cookies.
- **`Program.cs` auth scheme setup**: `DefaultScheme = "Cookies"`, `DefaultChallengeScheme = "OpenIdConnect"`, `DefaultSignInScheme = "Cookies"` set explicitly before `AddMicrosoftIdentityWebApp` — required because the `AuthenticationBuilder` overload of MIWA does not set these defaults.
- **`ITenantContext` factory in `Program.cs`**: added `AuthenticationStateProvider` fallback so tenant resolves correctly in Blazor Server SignalR circuits where `IHttpContextAccessor.HttpContext` is null.
- **`Agents.razor`, `TestSuites.razor`, `Wizard.razor`**: replaced local `_tenantId` field with `@inject ITenantContext TenantCtx` + `TenantCtx.TenantId` — tenant always read from the live context, not a stale field.
- **`mateDbSeeder.SeedDevTenantAsync`**: tenant existence check changed from `ExternalTenantId == "...0099"` to `Id == DevTenantId` — avoids false negatives after `ExternalTenantId` is updated by the EntraId mapping seeder.
- **`Settings.razor` judge modules filter**: excludes `ProviderType == "ModelQGen"` from the Judge/Evaluation modules list — `ModelQGen` only appears in the Question Generation section.
- **`DataServiceExtensions.AddmateSqlite`**: removed `sp.GetService<ITenantContext>()` call from the `AddDbContext` options factory — it was a no-op that triggered the circular DI chain.
- **`infra/local/.env`**: `Authentication__Scheme` default set to `None` — unauthenticated mode for local development; EntraId flow blocked by browser Mixed Content policy (HTTPS Azure AD → HTTP localhost form POST).
- **Auth default**: `appsettings.json` `Authentication:Scheme` changed to `None` — app starts without auth for first-time setup.
- **`Program.cs` auth**: added `None` case to auth scheme switch (maps to `GenericAuthModule`); removed old post-authorization API key middleware.
- **Help page**: removed Keyboard Shortcuts and FAQ sections.
- **Architecture naming compliance** (per `SaaS-Architecture-v2.md`): renamed 7 module projects — `mate.Modules.AgentConnectors.*` → `mate.Modules.AgentConnector.*` (singular), `mate.Modules.Authentication.*` → `mate.Modules.Auth.*`; updated `mate.sln`, 3 host `.csproj` ProjectReferences, 3 `Program.cs` using directives, 9 namespace declarations.
- **Infra folder restructure**: `docker/Dockerfile.webui` and `docker/Dockerfile.worker` moved into `infra/local/`; `docker/` root folder removed; `docker-compose.yml` `dockerfile:` references updated.
- **Generic modules hidden from UI**: `Settings.razor`, `Agents.razor`, and `Wizard.razor` filter `ConnectorType == "Generic"` and `ProviderType == "Generic"` — Generic modules remain registered in DI but are not shown to end users.

### Fixed
- **Circular DI infinite loop** (`TenantLookupService` / `mateDbContext`): fixed by injecting `DbContextOptions<mateDbContext>` and constructing `new mateDbContext(options, tenantContext: null)` directly, bypassing DI. The `null` tenant context correctly disables the global query filter for tenant lookup.
- **Blazor `RendererSynchronizationContext` deadlock** (`Program.cs`): fixed by wrapping the `ITenantContext` scoped factory call in `Task.Run(...)` to offload to the thread pool with no captured sync context.
- **`DataProtection` key persistence**: `PersistKeysToFileSystem` + `SetApplicationName` removed from `Program.cs` — persisted keys caused cookie decryption failures on container restart; in-memory keys (ASP.NET Core default) are correct for this deployment.
- **User display name shows "U User"** (`EntraIdAuthModule`): fixed by passing `nameType: "name", roleType: ClaimTypes.Role` to the `ClaimsIdentity` constructor in `TransformClaimsAsync`.
- **Auth log noise reduced**: `appsettings.json` Serilog override levels for authentication namespaces restored to `Warning`.
- **`EntraIdAuthModule.cs` cleanup**: removed duplicate `AddMicrosoftIdentityUI` static method and leftover `RegisterEntraIdAuth` static method; `ConfigureAuthentication` restored to canonical state (`AddMicrosoftIdentityWebApp` + `AddMicrosoftIdentityWebApi` + `IClaimsTransformation`).
- **`Program.cs` auth section**: restored `AuthenticationBuilder` ternary pattern with explicit `DefaultScheme/DefaultChallengeScheme/DefaultSignInScheme`; removed `RegisterEntraIdAuth` branch.
- **`AuthenticationFailureException: Correlation failed`**: root cause was `SameSite=Lax` on correlation cookie dropped by browser on Azure AD's cross-site `form_post` POST — fixed by setting `SameSite=Unspecified`.
- **`UNIQUE constraint failed: Tenants.Id`** on startup: seeder changed to UPDATE existing row instead of INSERT when dev tenant already exists.
- **Agents not visible after EntraId login**: Blazor Server `IHttpContextAccessor.HttpContext` is null in SignalR circuits — fixed via `AuthenticationStateProvider` fallback + `IClaimsTransformation` claim injection + per-page `ITenantContext` injection.
- **`No DefaultChallengeScheme found`**: fixed by explicitly calling `AddAuthentication(options => ...)` with scheme defaults before `AddMicrosoftIdentityWebApp`.
- **`Wizard.razor`**: restored all missing `@code` methods (`StepClass`, `GetConfigValue`, `SetConfigValue`, `GoStep2`, `GoStep3`, `TestConnection`, `GoStep4`, `SaveAgent`, `FinishWizard`); corrected `TestConnection` to call `module.CreateConnector(connConfig).StartConversationAsync(...)`; corrected `FinishWizard` to create `TestSuiteAgent` join entity; replaced `""` with `string.Empty` in `@onchange` handlers.
- **`Settings.razor` CS1501 Razor error**: fixed broken `title` attribute containing unescaped double-quotes inside a Razor expression by computing value in a local variable.
- **`AuditLogPage.razor` class name collision** (initial): renamed from `AuditLog.razor` — Blazor-generated class `AuditLogPage` no longer collides with `mate.Domain.Entities.AuditLog`.
- **Module `.csproj` files**: corrected stale `<RootNamespace>` values and inter-module `<ProjectReference>` paths in all 7 renamed module projects (was causing MSB9008 warnings in Docker builds).

### Known Issue
- **EntraId login on HTTP localhost is blocked by browser**: Azure AD's KMSI page at `https://login.microsoftonline.com/kmsi` performs a `form_post` back to `http://localhost:5000/signin-oidc`. Modern browsers (Edge, Chrome) silently block HTTPS→HTTP cross-origin form submissions as Mixed Content — even with Automatic HTTPS disabled. The container receives no callback. Resolution requires HTTPS on localhost (ASP.NET Core dev cert in Docker).

---

## [v0.1.0] — 2026-02-28

### Added
- Initial solution structure: `mate.sln` with Core, Domain, Data, WebUI, Worker, CLI, Tests, and Module projects
- Multi-tenant data layer: `mateDbContext` with EF Core Global Query Filters, `ITenantContext`, tenant entities
- `mate.Data` — SQLite (local) and PostgreSQL (production) provider support via `DataServiceExtensions`
- EF Core migration: `InitialCreate` covering all entities
- Domain entities: `Tenant`, `TenantSubscription`, `TenantUser`, `ApiKey`, `Agent`, `AgentConnectorConfig`, `TestSuite`, `TestCase`, `Run`, `Result`, `TranscriptMessage`, `JudgeSetting`, `RubricSet`, `RubricCriteria`, `Document`, `Chunk`
- Module system: `IAgentConnectorModule` interface with `ConfigSchema`, `IsHealthy()`, `ValidateConfig()`, `CreateConnector()`
- Agent connector module stubs: `CopilotStudio`, `AIFoundry`, `Generic`, `Parloa`
- Testing engine module stubs: `ModelAsJudge`, `RubricsJudge`, `HybridJudge`, `Generic`
- WebUI (Blazor Server): Dashboard, Agents, TestSuites, RunReport, Documents, Settings pages
- Minimal API endpoints: agents, test suites, test cases, runs, results, documents, metrics, API keys, audit log, health
- Scalar API reference UI at `/scalar/v1`; OpenAPI spec at `/openapi/v1.json`
- Serilog structured logging (Console + File sinks)
- Authentication modules: Entra ID (JWT Bearer) and Generic (dev/local) schemes
- Worker service: `TestRunWorker` consuming `IMessageQueue` for background test execution
- CLI: `mate.CLI` entry point with `dotnet-script` support
- Docker: `Dockerfile.webui`, `Dockerfile.worker`, `infra/local/docker-compose.yml`
- Unit tests: 27 passing (document processing, testing engine, module contract)

---

*Versions prior to v0.1.0 are not recorded — this is the initial tracked release.*
