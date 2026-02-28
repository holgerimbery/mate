# mate — Changelog

All notable changes to this project will be documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **`mate.Modules.Testing.CopilotStudioJudge`** — new testing module combining deterministic rubrics with a citation-aware LLM judge tuned for Microsoft Copilot Studio agents. Features: three built-in default rubrics (NonEmpty mandatory gate, no rejection phrase, no error surfacing), citation block awareness (`[1]: cite:...` = positive grounding indicator), semantic equivalence evaluation, CopilotStudio-specific scoring weights (TaskSuccess 0.35, IntentMatch 0.25, Factuality 0.25, Helpfulness 0.10, Safety 0.05), 0.3×rubrics + 0.7×LLM blend, rubrics-only mode when LLM not configured, graceful fallback on LLM failure. Ported from MaaJforMCS `AzureAIFoundryJudgeService`.
- **`CopilotStudioConnectorModule` — Web Channel Secret support**: `WebChannelSecretRef` config field added; `CreateConnector()` and `GenerateConversationTokenAsync()` now honour `UseWebChannelSecret=true` by passing the Web Channel Security secret to `/tokens/generate` instead of the Direct Line secret; `ValidateConfig()` enforces the correct secret ref per mode; `GetConfigDefinition()` reordered with descriptive help text for each field. Find the secret in Copilot Studio → Settings → Security → Web channel security.
- **Branding — logo**: `mate-logo.png` and `mate-logo-wide.png` added to `wwwroot`; `BrandInfo.LogoUrl` and `BrandInfo.LogoWideUrl` constants added to `BrandInfo.cs`; `MainLayout.razor` sidebar now shows the logo image instead of the initial letter; `App.razor` `<head>` now includes a `<link rel="icon">` favicon pointing to `BrandInfo.LogoUrl`; CSS in `app.css` updated (`.sidebar-brand-logo`, `.sidebar-brand-logo-wide`).
- **`CopilotStudioConnectorModule.GetConfigDefinition()`** updated: `EnvironmentId` is now optional; added `UseWebChannelSecret` boolean field (default: false); `ReplyTimeoutSeconds` now has default value "30".
- `BACKLOG.md` — full product backlog with epics, module contract, and tech debt log
- `CHANGELOG.md` — this file
- `VERSION` — version file, starting at `v0.1.0`
- **WebUI pages**: `Discover.razor` (`/discover`), `Rubrics.razor` (`/rubrics`), `Help.razor` (`/help`), `AuditLogPage.razor` (`/audit-log`) — new standalone pages
- **Settings — Modules tab**: new default tab in Settings showing all registered `IAgentConnectorModule`, `ITestingModule`, and `IMonitoringModule` implementations with their config field definitions and links to the creation wizard
- **Wizard — module-driven Step 1**: agent creation wizard Step 1 now dynamically lists all registered `IAgentConnectorModule` implementations, replacing the previous static list; Step 2 config form is generated from the module's `ConfigSchema`
- **mateModuleRegistry**: added `RegisterTestingModule()` and `GetAllTestingModules()` to support testing module discovery
- **Program.cs registry population**: module registry is now populated at application startup by resolving all `IAgentConnectorModule`, `ITestingModule`, `IJudgeProvider`, and `IMonitoringModule` services from DI — previously the registry was always empty at runtime
- **`BrandInfo.cs`** (`mate.Domain`): new static class `BrandInfo` with `BrandName = "mate"`, `BrandTagline`, `BrandCliDescription`, `LogoUrl`, and `LogoWideUrl` — single source of truth for all brand display text and assets
- **`memory.txt`** added to repo root — persistent cross-session context file for AI-assisted development
- **`.gitignore`** added; repository initialised and pushed to `https://github.com/holgerimbery/mate` (private)

### Changed
- **Architecture naming compliance** (per `SaaS-Architecture-v2.md`): renamed 7 module projects to use correct names:
  - `mate.Modules.AgentConnectors.*` → `mate.Modules.AgentConnector.*` (singular, 4 projects: AIFoundry, CopilotStudio, Generic, Parloa)
  - `mate.Modules.Authentication.*` → `mate.Modules.Auth.*` (short form, 3 projects: EntraId, Generic, OAuth)
  - Updated `mate.sln`, 3 host `.csproj` ProjectReferences, 3 `Program.cs` using directives, 9 namespace declarations, 7 module `.csproj` `<RootNamespace>` and inter-module `<ProjectReference>` entries
- **Infra folder restructure**: `docker/Dockerfile.webui` and `docker/Dockerfile.worker` moved into `infra/local/` (alongside `docker-compose.yml`) per `SaaS-Architecture-v2.md` §2; `docker/` root folder removed; `docker-compose.yml` `dockerfile:` references updated accordingly.
- **Generic modules hidden from UI**: `Settings.razor`, `Agents.razor`, and `Wizard.razor` filter `ConnectorType == "Generic"` and `ProviderType == "Generic"` from all module lists — Generic modules remain registered in DI for developer use but are not shown to end users.

### Fixed
- `Wizard.razor` `@code` block: restored all missing methods (`StepClass`, `GetConfigValue`, `SetConfigValue`, `GoStep2`, `GoStep3`, `TestConnection`, `GoStep4`, `SaveAgent`, `FinishWizard`) and added closing `}` for the `@code` block
- `Wizard.razor` `TestConnection`: corrected method to call `module.CreateConnector(connConfig).StartConversationAsync(...)` — `StartConversationAsync` is on `IAgentConnector`, not `IAgentConnectorModule`
- `Wizard.razor` `FinishWizard`: corrected to create `TestSuiteAgent` join entity instead of setting non-existent `TestSuite.AgentId`
- `Wizard.razor` Razor string literals: replaced `""` with `string.Empty` in `@onchange` handlers; added `BoolFalse` constant for boolean field onchange
- `Settings.razor` CS1501 Razor error: fixed broken `title` attribute containing unescaped double-quotes inside a Razor expression by computing value in a local variable before the element
- `AuditLog.razor` class name collision: renamed to `AuditLogPage.razor` so the Blazor-generated class is `AuditLogPage`, eliminating the collision with `mate.Domain.Entities.AuditLog`
- Module `.csproj` files: all 7 renamed module projects had stale `<RootNamespace>` values and inter-module `<ProjectReference>` paths corrected (was causing MSB9008 warnings in Docker builds)

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
