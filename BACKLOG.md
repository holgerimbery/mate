# mate — Product Backlog

> Version discipline: Items are tagged with the target version milestone. We start at **v0.1.0** and advance only on explicit instruction.
>
> Status legend: `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked

---

## Epics Overview

| # | Epic | Priority |
|---|------|----------|
| E1 | Foundation & Infrastructure | Critical |
| E2 | Tenant Onboarding | High |
| E3 | Agent Onboarding (Module-Agnostic) | High |
| E4 | WebUI — Full Feature Parity with MaaJforMCS | High |
| E5 | CLI — Full Feature Parity with MaaJforMCS | Medium |
| E6 | Testing Module — Rubrics, Test Sets, Dashboard | High |
| E7 | Agent Connector Modules (AIFoundry, CopilotStudio, Parloa, Generic) | High |
| E8 | Testing Engine Modules (HybridJudge, ModelAsJudge, RubricsJudge, Generic) | High |
| E9 | API Generation & Management | Medium |
| E10 | Self-Check & App Health Mechanisms | Medium |
| E11 | Logging System | Medium |
| E12 | Versioning, CHANGELOG, BACKLOG | Done |
| E13 | Authentication — EntraId full flow | Blocked |
| E14 | HTTPS on localhost — dev cert for Docker | High |

---

## Module Contract (Agnostic Base)

Every module — regardless of type — MUST provide the following parameters/capabilities:

```
ModuleId          : string (unique, e.g. "CopilotStudio", "AIFoundry")
DisplayName       : string
Description       : string
Version           : string (semver)
ModuleType        : enum (AgentConnector | TestingEngine | Authentication | Monitoring)
ConfigSchema      : List<ConfigFieldDefinition>
  └─ Key          : string
  └─ Label        : string
  └─ Description  : string
  └─ FieldType    : enum (Text | Secret | Number | Boolean | Select | Url)
  └─ IsRequired   : bool
  └─ DefaultValue : string?
  └─ SelectOptions: List<string>?
IsHealthy()       : Task<bool>               — self-check endpoint
ValidateConfig()  : Task<ValidationResult>   — validate config JSON
GetCapabilities() : List<string>             — declares what the module can do
```

---

## E1 — Foundation & Infrastructure

### v0.1.0

- [x] **E1-01** Resolve duplicate AgentConnector module directories (singular vs plural: `AgentConnector.*` vs `AgentConnectors.*`) — keep one set, remove the other
- [ ] **E1-02** Add `mate.Data` EF migration for multi-tenant entities (Tenants, TenantSubscriptions, TenantUsers)
- [ ] **E1-03** Add `mate.Data` EF migration for Rubrics entities (RubricSet, RubricCriteria)
- [ ] **E1-04** Create `data/` directory gitkeep in mate root so local SQLite file path works
- [x] **E1-05** Add `.env.template` file documenting all required environment variables
- [ ] **E1-06** Add `dotnet user-secrets` setup instructions to README
- [x] **E1-07** Verify `mate.sln` includes ALL projects (check for missing module projects)
- [x] **E1-08** Restructure `infra/local/` — Dockerfiles moved from `docker/` root into `infra/local/`; `docker-compose.yml` references updated; `docker/` folder removed
- [x] **E1-09** Git repository initialised; `.gitignore` added; pushed to `https://github.com/holgerimbery/mate` (private)
- [x] **E1-10** Data Protection keys persisted: `docker-compose.yml` mounts `mate-dataprotection` volume to `/root/.aspnet/DataProtection-Keys` — prevents correlation cookie failures on container restart
- [x] **E1-11** `Authentication__Scheme` in `.env` reset to `None` — app runs without auth for local development

### v0.2.0

- [ ] **E1-08** Add Azure Key Vault provider for credential storage (production path)
- [ ] **E1-09** PostgreSQL migration path — add `MATE_DB_PROVIDER` environment variable switch

---

## E2 — Tenant Onboarding

### v0.1.0

- [ ] **E2-01** Tenant entity schema: Name, Slug, Plan (Free/Pro/Enterprise), ActiveModules, CreatedAt
- [ ] **E2-02** `POST /api/admin/tenants` — create tenant
- [ ] **E2-03** `GET /api/admin/tenants` — list tenants (platform-admin only)
- [ ] **E2-04** `PUT /api/admin/tenants/{id}` — update tenant (enable/disable modules)
- [ ] **E2-05** `DELETE /api/admin/tenants/{id}` — deactivate tenant
- [ ] **E2-06** Tenant onboarding wizard — WebUI multi-step flow:
  - Step 1: Name, slug, contact email
  - Step 2: Select modules to activate
  - Step 3: Configure authentication (Entra ID or Generic)
  - Step 4: Review & confirm
- [ ] **E2-07** Tenant context middleware — reads tenant from JWT claim or API key header, injects `ITenantContext`
- [ ] **E2-08** `/api/admin/tenants/{id}/modules` — GET/PUT module activation per tenant
- [ ] **E2-09** Tenant switcher UI widget (for platform-admin users)

### v0.2.0

- [ ] **E2-10** Tenant usage quotas (max agents, max runs/month)
- [ ] **E2-11** Tenant subscription management UI

---

## E3 — Agent Onboarding (Module-Agnostic)

### v0.1.0

- [x] **E3-01** Agent onboarding wizard — module-agnostic multi-step flow:
  - Step 1: Select agent connector module (shows all registered `IAgentConnectorModule` implementations)
  - Step 2: Dynamic config form generated from `ConfigSchema` of selected module
  - Step 3: Test connection (`IsHealthy()` + `ValidateConfig()`)
  - Step 4: Assign agent to test suites (optional)
  - Step 5: Review & save
- [ ] **E3-02** `GET /api/modules/agent-connectors` — enumerate all registered connector modules with their `ConfigSchema`
- [ ] **E3-03** `POST /api/agents/{id}/test-connection` — runs `IsHealthy()` for an agent's connector
- [ ] **E3-04** Dynamic config field renderer component (renders `ConfigFieldDefinition` list as a form)
- [ ] **E3-05** Agent list page shows module type badge and health status indicator
- [ ] **E3-06** Agent edit flow uses same dynamic form as onboarding wizard

---

## E4 — WebUI Feature Parity with MaaJforMCS

### v0.1.0 — Navigation Structure

Implement the same collapsible icon sidebar as MaaJforMCS:

- [x] **E4-01** **MainLayout** — collapsible left sidebar with logo, Bootstrap Icons, branding from `BrandInfo`
- [x] **E4-02** **Home / Welcome page** — action card grid, KPI stats, Quick Run modal, Recent Runs feed, Module Status panel, Getting Started steps
- [ ] **E4-03** **Setup Wizard** — combined tenant + agent onboarding wizard (E2-06 + E3-01 merged)
- [ ] **E4-04** **Test Suites page** — CRUD, test case management, run execution button
- [ ] **E4-05** **Documents page** — upload, list, delete; displays chunk count per document
- [ ] **E4-06** **Agents page** — CRUD using dynamic config form, health badge, module type badge
- [x] **E4-07** **Discover page** — environment & agent discovery via Entra ID + Power Platform API (mirrors MaaJforMCS `EnvironmentDiscoveryPage.razor`)
- [ ] **E4-08** **Dashboard page** — KPI cards, pass rate trend chart, recent runs table, per-module breakdown
- [x] **E4-09** **Judge Rubrics page** — RubricSet CRUD, criteria editor, assign to test suite
- [ ] **E4-10** **Run Report page** — results table, transcript modal, human verdict override
- [x] **E4-11** **Help page** — documentation links, REST API reference table, OpenAPI download, Interactive API Explorer link
- [x] **E4-12** **Audit Log page** (admin) — paginated event log with filters
- [ ] **E4-13** **API Keys page** (admin) — generate, list, revoke keys; show scopes
- [x] **E4-14** **Settings page** (admin) — tabs: AI Judge config, Question Generation, Modules, Tenant

### v0.1.0 — Visual Design

- [ ] **E4-15** Match MaaJforMCS color palette: `#f5f6f8` background, `#ffffff` card/sidebar, `#1f1f23` text, Bootstrap Icons
- [ ] **E4-16** Responsive layout — mobile-friendly collapsed sidebar state
- [ ] **E4-17** Page-level title + description header (matches MaaJforMCS header pattern)

---

## E5 — CLI Feature Parity

### v0.1.0

- [ ] **E5-01** `mate run <suite-id>` — execute a test suite, stream results to console
- [ ] **E5-02** `mate agents list` — list agents with module type and status
- [ ] **E5-03** `mate agents add --module <module-id>` — guided interactive onboarding
- [ ] **E5-04** `mate tenants list` — list tenants (platform-admin)
- [ ] **E5-05** `mate tenants add` — guided tenant onboarding
- [ ] **E5-06** `mate health` — run self-check across all registered modules
- [ ] **E5-07** `mate export --run <run-id> --format csv|json` — export run report

### v0.2.0

- [ ] **E5-08** `mate documents upload <path>` — ingest document via CLI
- [ ] **E5-09** `mate generate-questions --suite <id>` — AI question generation

---

## E6 — Testing Module: Rubrics, Test Sets, Dashboard

### v0.1.0

- [ ] **E6-01** `RubricSet` entity: name, description, tenantId, criteria list
- [ ] **E6-02** `RubricCriteria` entity: name, description, weight (0-1), passMark (0-1), evalPrompt
- [ ] **E6-03** Rubrics management page — create/edit rubric sets, add/remove criteria, weight validation (must sum to 1.0)
- [ ] **E6-04** Rubric assignment to TestSuite (many-to-many)
- [ ] **E6-05** Test set import page — upload CSV (matches MaaJforMCS `EvaluationTemplate.csv` format)
- [ ] **E6-06** Test set export page — download current test cases as CSV
- [ ] **E6-07** Dashboard module breakdown widget — shows pass/fail/pending per agent connector module
- [ ] **E6-08** Dashboard rubric scores widget — per-criteria average score chart
- [ ] **E6-09** `GET /api/metrics/rubric-breakdown` — API endpoint for rubric score aggregation
- [ ] **E6-10** `GET /api/metrics/module-breakdown` — API endpoint for per-module metrics

### v0.2.0

- [ ] **E6-11** Automated question generation from documents (AI-powered, module-agnostic prompt)
- [ ] **E6-12** Trend charts — 7-day and 30-day pass rate trend line

---

## E7 — Agent Connector Modules

### Module contract (all must implement)

All connectors implement `IAgentConnectorModule` with `ModuleId`, `DisplayName`, `ConfigSchema`, `IsHealthy()`, `ValidateConfig()`, `CreateConnector()`.

### v0.1.0

#### CopilotStudio Connector
- [x] **E7-01** Implement `IAgentConnectorModule` for `mate.Modules.AgentConnector.CopilotStudio`
- [x] **E7-02** Config fields: BotId, EnvironmentId, DirectLineSecretRef, WebChannelSecretRef, ReplyTimeoutSeconds, UseWebChannelSecret
- [x] **E7-03** `CreateConnector()` → `CopilotStudioConnector` via Direct Line v3 (polling, watermark, exponential backoff); both Direct Line and Web Channel Security secret paths implemented
- [ ] **E7-04** Health check: attempt token fetch from DirectLine endpoint

#### AIFoundry Connector
- [ ] **E7-05** Implement `IAgentConnectorModule` for `mate.Modules.AgentConnectors.AIFoundry`
- [ ] **E7-06** Config fields: Endpoint URL, API Key, Agent ID, Deployment Name, API Version
- [ ] **E7-07** `CreateConnector()` → `IAgentConnector` via Microsoft Agent Framework (Azure AI Agents SDK)
- [ ] **E7-08** Health check: list agents API call with provided credentials

#### Generic HTTP Connector
- [ ] **E7-09** Implement `IAgentConnectorModule` for `mate.Modules.AgentConnectors.Generic`
- [ ] **E7-10** Config fields: Base URL, Auth Header Name, Auth Header Value, Message Path, Timeout MS
- [ ] **E7-11** `CreateConnector()` → HTTP POST send/receive connector

#### Parloa Connector
- [ ] **E7-12** Implement `IAgentConnectorModule` for `mate.Modules.AgentConnectors.Parloa`
- [ ] **E7-13** Config fields: API URL, API Key, Agent Slug, Locale
- [ ] **E7-14** `CreateConnector()` → Parloa Conversation API connector

### v0.2.0

- [ ] **E7-15** Semantic Kernel Connector module (for SK-based agents)
- [ ] **E7-16** Azure Bot Service connector module

---

## E8 — Testing Engine Modules

### v0.1.0

#### ModelAsJudge
- [x] **E8-01** Implement `ITestingModule` for `mate.Modules.Testing.ModelAsJudge`
- [x] **E8-02** Config fields: AI Endpoint, API Key, Model, System Prompt, Temperature, Max Tokens, Pass Threshold
- [x] **E8-03** LLM judge: 5-dimension scoring (TaskSuccess, IntentMatch, Factuality, Helpfulness, Safety)

#### RubricsJudge
- [x] **E8-04** Implement `ITestingModule` for `mate.Modules.Testing.RubricsJudge`
- [x] **E8-05** Deterministic rubrics: Contains/NotContains/Regex, mandatory gate, weighted scoring
- [x] **E8-06** Config: Pass Threshold (no LLM required)

#### HybridJudge
- [x] **E8-07** Implement `ITestingModule` for `mate.Modules.Testing.HybridJudge`
- [x] **E8-08** Rubrics gate (mandatory + score < 0.5) → LLM (0.4×rubrics + 0.6×LLM blend)
- [x] **E8-09** Config fields: AI Endpoint, API Key, Model, Pass Threshold

#### CopilotStudioJudge
- [x] **E8-13** Implement `ITestingModule` for `mate.Modules.Testing.CopilotStudioJudge`
- [x] **E8-14** Built-in CopilotStudio default rubrics (NonEmpty mandatory, no rejection phrase, no error surfacing)
- [x] **E8-15** Citation-aware LLM prompt: `[1]: cite:...` = grounding positive; semantic equivalence; 0.3×rubrics + 0.7×LLM blend
- [x] **E8-16** CopilotStudio-specific scoring weights: TaskSuccess=0.35, IntentMatch=0.25, Factuality=0.25, Helpfulness=0.10, Safety=0.05

#### Generic / Keyword Judge
- [ ] **E8-10** Implement `ITestingModule` for `mate.Modules.Testing.Generic`
- [ ] **E8-11** Pure keyword/regex matching — no AI dependency, zero cost
- [ ] **E8-12** Config fields: Match Mode (Contains/StartsWith/Regex/Exact), Case Sensitive

---

## E9 — API Generation & Management Page

### v0.1.0 (mirrors MaaJforMCS ApiKeysPage + Scalar UI)

- [ ] **E9-01** API Keys management page — generate named keys with scopes (Read / Write / Admin)
- [x] **E9-02** Scalar API reference UI at `/scalar/v1` — already implemented, verify working
- [ ] **E9-03** OpenAPI spec at `/openapi/v1.json` — ensure all endpoints are documented
- [x] **E9-04** API key middleware — SHA-256 hash lookup, scope enforcement per endpoint
- [ ] **E9-05** API usage statistics page — calls per key per day, error rates
- [ ] **E9-06** Swagger/OpenAPI download button on API Keys page

### v0.2.0

- [ ] **E9-07** Webhook support — outbound HTTP notifications on run completion
- [ ] **E9-08** Rate limiting per API key

---

## E10 — Self-Check & App Health Mechanisms

### v0.1.0

- [ ] **E10-01** `/health/live` — liveness probe (app is running)
- [ ] **E10-02** `/health/ready` — readiness probe (DB + all modules pass self-check)
- [ ] **E10-03** `/health/modules` — per-module health JSON (calls `IsHealthy()` on each registered module)
- [ ] **E10-04** Health dashboard section on Home page — shows module health cards
- [ ] **E10-05** Startup self-check — on app boot, log the health of each module; warn if any module is unhealthy
- [ ] **E10-06** Database connectivity check at health endpoint
- [ ] **E10-07** Blob/file storage check at health endpoint
- [ ] **E10-08** Background job queue check (worker heartbeat)
- [ ] **E10-09** `mate health` CLI command — calls `/health/modules` and renders table

---

## E11 — Logging System

### v0.1.0

- [ ] **E11-01** Structured logging via Serilog (already partially configured) — verify full configuration
- [ ] **E11-02** Log sinks: Console (dev), File (`./logs/mate-.log`, rolling daily) for all environments
- [ ] **E11-03** Correlation ID middleware — adds `X-Correlation-Id` header, enriches all log entries
- [ ] **E11-04** Request/response logging middleware (configurable — off by default in prod)
- [ ] **E11-05** Audit log entries for: tenant create/update, agent create/update/delete, run start/complete, API key create/revoke
- [ ] **E11-06** Audit Log page (WebUI) — paginated table, filter by event type, tenant, user, date range
- [ ] **E11-07** Log level override endpoint (admin only) — `POST /api/admin/log-level` — changes level at runtime
- [ ] **E11-08** Log viewer page (admin) — tail last N lines or search (only for file sink)

### v0.2.0

- [ ] **E11-09** Application Insights / Azure Monitor sink (optional, module-based)
- [ ] **E11-10** OpenTelemetry traces export

---

## E12 — Versioning, CHANGELOG, BACKLOG ✅

- [x] **E12-01** Create `BACKLOG.md` in mate root
- [x] **E12-02** Create `CHANGELOG.md` in mate root
- [x] **E12-03** Create `VERSION` file in mate root (start: `v0.1.0`)

---

## E13 — Authentication — EntraId full flow ✅ Complete

- [x] **E13-01** `EntraIdAuthModule` — `AddMicrosoftIdentityWebApp` (OIDC + session cookie) + `AddMicrosoftIdentityWebApi` (JWT Bearer `EntraId` scheme)
- [x] **E13-02** `AddMicrosoftIdentityUI` — MVC controllers for `/MicrosoftIdentity/Account/*` redirect endpoints
- [x] **E13-03** `IClaimsTransformation` on `EntraIdAuthModule` — injects `mate:externalTenantId` (`tid`), `mate:userId` (`oid`), `mate:role` (`roles`) claims
- [x] **E13-04** `Program.cs` explicit auth defaults: `DefaultScheme=Cookies`, `DefaultChallengeScheme=OpenIdConnect`, `DefaultSignInScheme=Cookies`
- [x] **E13-05** OIDC correlation cookie `SameSite=Unspecified` — fixes `Correlation failed` on Azure AD `form_post` cross-site callback
- [x] **E13-06** Tenant mapping seeder: `SeedEntraIdTenantMappingAsync` maps dev tenant `ExternalTenantId` → Azure AD `tid` GUID (idempotent)
- [x] **E13-07** `ITenantContext` factory Blazor Server fallback via `AuthenticationStateProvider` (fixes null `HttpContext` in SignalR circuits)
- [x] **E13-08** `Agents.razor`, `TestSuites.razor`, `Wizard.razor` updated to use `@inject ITenantContext TenantCtx`
- [ ] **E13-09** `infra/local/.env.template` — document `Authentication__Scheme`, `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__ClientSecret`
- [ ] **E13-10** README section: "Switching to EntraId authentication" setup guide
- [x] **E13-11** ~~BLOCKED~~ **RESOLVED** — EntraId login on HTTP localhost works via nginx reverse proxy at `https://maaj.imbery.de`; full OIDC flow confirmed working in production.
- [x] **E13-12** Fixed circular DI infinite loop: `TenantLookupService` now constructs `mateDbContext` directly with `null` tenant context — no DI involvement, loop impossible.
- [x] **E13-13** Fixed Blazor sync-context deadlock: `Task.Run()` in `ITenantContext` factory escapes `RendererSynchronizationContext`.
- [x] **E13-14** Fixed user name display: `ClaimsIdentity` constructed with `nameType:"name"` in `EntraIdAuthModule` — maps Entra `name` claim to `Identity.Name`.
- [x] **E13-15** Removed `DataProtection` key persistence — in-memory keys prevent cookie decrypt failures on container restart.

---

## E14 — HTTPS on localhost (dev cert for Docker)

### v0.1.0

- [ ] **E14-01** Generate ASP.NET Core dev certificate on host: `dotnet dev-certs https --export-path ./infra/local/certs/aspnetapp.pfx --password <pwd>`
- [ ] **E14-02** Mount cert into `mate-webui` container; set `ASPNETCORE_Kestrel__Certificates__Default__Path` + `__Password` env vars
- [ ] **E14-03** Expose port 5001 (HTTPS) in `docker-compose.yml` alongside 5000
- [ ] **E14-04** Update Azure AD app registration redirect URI to `https://localhost:5001/signin-oidc`
- [ ] **E14-05** Update `.env` `Authentication__Scheme` back to `EntraId` and test full login flow
- [ ] **E14-06** Add README section: "Running with HTTPS locally"

---

## Discovered Issues / Tech Debt

| ID | Issue | Severity |
|----|-------|----------|
| TD-01 | Duplicate module directories: `AgentConnector.*` and `AgentConnectors.*` both exist | High |
| TD-02 | No `.env.template` file documenting required environment variables | Medium |
| TD-03 | `dotnet user-secrets` not initialized for local dev | Low |
| TD-04 | Missing `Class1.cs` stub files should be replaced with real implementations | Low |
| TD-05 | `AddMicrosoftIdentityWebApp(AuthenticationBuilder)` overload does not set DefaultScheme/DefaultChallengeScheme — must be set explicitly before calling it | Note |
| TD-06 | Azure AD `response_mode=form_post` requires `SameSite=Unspecified` on OIDC correlation and nonce cookies | Note |
| TD-07 | ~~ASP.NET Core Data Protection keys must be persisted to a volume in Docker~~ — **RESOLVED**: in-memory keys are correct; persisted keys caused decrypt failures when container restarts with new keys | Resolved |
| TD-08 | `IHttpContextAccessor.HttpContext` is null in Blazor Server SignalR circuits — always use `AuthenticationStateProvider` fallback for tenant/user resolution | Note |
| TD-09 | ~~EntraId login requires HTTPS on localhost~~ — **RESOLVED**: production deployment at `https://maaj.imbery.de` via nginx fully resolves the Mixed Content issue; localhost HTTP remains unsupported | Resolved |

---

*Last updated: 2026-03-01, Session 14 — v0.2.0 released*
