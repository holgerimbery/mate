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
| E15 | Multi-Agent Execution & Comparison | High |
| E16 | Notifications & Scheduled Runs | Medium |
| E17 | Extended Document Sources | Medium |
| E18 | Dynamic Binary Module System (Plugins) | High |
| E19 | Red Teaming Module Category | High |
| E19 | Red Teaming Module Category | High |

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
- [ ] **E1-12** Sample data seeder — auto-create a sample agent, test suite, and 5 test cases on first startup when the database is empty (matches MaaJforMCS `TestDataSeeder`)
- [ ] **E1-13** Azure Container Apps IaC — Bicep + `azd` template for one-command production deployment; includes WebUI + Worker containers, managed identity, Azure SQL, Service Bus

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
- [ ] **E2-12** User management page — list tenant users, assign Admin/Tester/Viewer roles, activate/deactivate; mirrors MaaJforMCS `User` entity with Role/Email/IsActive/LastActiveAt
- [ ] **E2-13** Role-based suite ownership — assign a test suite to specific users/groups; restrict run execution and edit rights to owners

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
- [ ] **E3-07** Power Platform Discovery Service — BAP API (`api.bap.microsoft.com`) environment enumeration + Dataverse `botcomponent` agent listing + one-click import-to-DB; supports pre-fetched access token or `TokenCredential` (`AzureCliCredential`) for auth

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
- [x] **E4-13** **API Keys page** (admin) — generate, list, revoke keys; show scopes
- [x] **E4-14** **Settings page** (admin) — tabs: AI Judge config, Question Generation, Modules, Tenant

### v0.1.0 — Visual Design

- [ ] **E4-15** Match MaaJforMCS color palette: `#f5f6f8` background, `#ffffff` card/sidebar, `#1f1f23` text, Bootstrap Icons
- [ ] **E4-16** Responsive layout — mobile-friendly collapsed sidebar state
- [ ] **E4-17** Page-level title + description header (matches MaaJforMCS header pattern)

### v0.1.0 — Run Report parity (MaaJforMCS `TestRunReportPage.razor`)

- [ ] **E4-18** Regression detection panel — amber warning panel when a test case result changes from pass → fail vs. the previous run; side-by-side rationale diff
- [ ] **E4-19** Pass rate by category/tag breakdown table on Run Report
- [ ] **E4-20** Per-result confidence score trend — sparkline of last 6 runs + delta indicator per test case row
- [ ] **E4-21** CSV export of run results from Run Report page
- [ ] **E4-22** Refine Rubric button — sends all human-override disagreements (human ≠ AI verdict) to LLM and returns proposed rubric update; one-click on Run Report

### v0.1.0 — Dashboard parity (MaaJforMCS `Home.razor` + `DashboardPage.razor`)

- [ ] **E4-23** Pass rate sparkline trend (last 10 runs) on Dashboard/Home page
- [ ] **E4-24** Latency P95 sparkline trend (last 10 runs) on Dashboard/Home page
- [ ] **E4-25** Top-5 most frequently failing test cases widget on Dashboard
- [ ] **E4-26** System status badges on Dashboard — shows DB / AI judge / active connector health at a glance (DB/DirectLine/AI Judge)
- [ ] **E4-27** Agent environment filter on run history table (dev/test/staging/production)

### v0.1.0 — Test Suites page parity (MaaJforMCS `TestSuitesPage.razor`)

- [ ] **E4-28** Import / export test suite as JSON — download whole suite + test cases as JSON; import from JSON file
- [ ] **E4-29** Test case bulk import via CSV/Excel file (matches MaaJforMCS `EvaluationTemplate.csv` format and Excel import)
- [ ] **E4-30** Bulk test case operations — multi-select checkboxes → bulk delete / activate / deactivate
- [ ] **E4-31** Clone test case — copy a test case within the same suite with a single click
- [ ] **E4-32** Keyword search + active/inactive filter for test cases within a suite
- [ ] **E4-33** Re-run failed — re-execute only the failed test cases from a previous run (passes selective `TestCaseIds[]` to the run API)

### v0.1.0 — Documents page parity (MaaJforMCS `DocumentsPage.razor`)

- [ ] **E4-34** HTTP/HTTPS URL paste import — user pastes a public URL; server-side fetch with SSRF protection (block loopback/private CIDRs); 30-second timeout; content chunked same as file uploads
- [ ] **E4-35** Generate test cases from document — per-document button in Documents page that triggers the `ModelQGen` module and optionally assigns generated cases to a selected suite

### v0.1.0 — Settings page parity (MaaJforMCS `SettingsPage.razor`)

- [ ] **E4-36** Run history pruning — retention threshold in days + "Prune Now" button in Settings; deletes runs (and their results/transcripts) older than the threshold

### v0.1.0 — Auth-enabled pages

- [ ] **E4-37** `WelcomePage.razor` (`/welcome`) — new-user landing page shown before onboarding; `AccessDenied.razor` (`/access-denied`) — 403 page for insufficient role; both needed when `Authentication:Scheme != None`

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
- [ ] **E5-10** `mate report --run <id> [--format json|csv] [--output <dir>]` — export a completed run's results as JSON (default) or CSV; mirrors MaaJforMCS `report` command
- [ ] **E5-11** `mate list [--config <path>]` — list all test suites with name and test case count (mirrors MaaJforMCS `list` command)
- [ ] **E5-12** `mate generate --document <path> [--suite <name|id>] [--count <n>]` — generate test cases from a local document file; saves to named suite when `--suite` is specified, prints to console otherwise

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
- [ ] **E6-13** Full-text search over document chunks — Lucene.NET index at `Storage:LuceneIndexPath`; index updated on ingest/delete; `GET /api/documents/search?q=` endpoint; surfaced in Documents page search box
- [ ] **E6-14** Run report PDF export — generate a formatted PDF from a completed run's results and verdict summary (in addition to the existing CSV export in E4-21)

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
- [ ] **E7-17** WebSocket streaming in CopilotStudio connector — add `UseWebSocket` config field (bool, default false); implement `StreamActivitiesWebSocketAsync` as an alternative to the current polling path; activated via `UseWebSocket=true` in connector config

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

#### Deterministic Evaluation (cost-effective LLM-free alternatives)
- [ ] **E8-17** ExactMatch evaluation type — pass only if bot response equals expected answer exactly (case-insensitive option); zero AI cost
- [ ] **E8-18** KeywordMatch evaluation type — pass if all required keywords are present in the response; configurable required/forbidden keyword lists
- [ ] **E8-19** TextSimilarity evaluation type — cosine similarity or edit-distance against `ReferenceAnswer`; configurable threshold; no LLM required

#### Attachment / Adaptive Card Assertion
- [ ] **E8-20** Attachment presence assertion in RubricsJudge — new `EvaluationType.AttachmentPresent` criteria: asserts that the bot response includes an attachment of the specified `contentType` (e.g. `application/vnd.microsoft.card.adaptive`); mandatory-gate capable

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

## E15 — Multi-Agent Execution & Comparison

### v0.2.0

- [ ] **E15-01** `MultiAgentExecutionCoordinator` — run the same test suite against N agents simultaneously; creates one `Run` per agent; returns `List<Run>`; mirrors MaaJforMCS `MultiAgentExecutionCoordinator.ExecuteForMultipleAgentsAsync`
- [ ] **E15-02** `POST /api/runs` — accept optional `agentIds[]` array to start a multi-agent run in a single API call
- [ ] **E15-03** Side-by-side agent comparison page — select two or more completed runs against the same suite; compare per-test-case verdicts and scores in a diff-style table; highlight regressions and improvements

---

## E16 — Notifications & Scheduled Runs

### v0.2.0

- [ ] **E16-01** Webhook notification — configurable outbound HTTP POST to a user-supplied URL on run completion or regression detection; payload: run ID, suite, agent, pass rate, verdict summary; mirrors MaaJforMCS backlog item
- [ ] **E16-02** Microsoft Teams notification — post an Adaptive Card to a Teams incoming webhook URL on run completion / regression; configurable per suite or globally in Settings
- [ ] **E16-03** Email notification — SMTP run summary email on completion; configurable To/From/SMTP host/port/credentials in Settings; basic HTML template with pass/fail counts and top failing tests
- [ ] **E16-04** Scheduled runs — cron expression config per test suite for automatic execution (Quartz.NET or `IHostedService`-based timer); configurable in Settings; run history shows `scheduler` as executionUser

---

## E17 — Extended Document Sources

### v0.2.0

- [ ] **E17-01** SharePoint document import — Microsoft Graph API file picker integration; browse site document libraries; select PDF/DOCX/TXT; ingest via existing `DocumentIngestor`
- [ ] **E17-02** OneDrive document import — Microsoft Graph API browse + select + ingest; reuses SharePoint Graph client
- [ ] **E17-03** Azure Blob Storage document import — connection string + container browser UI; list blobs → select → stream + ingest
- [ ] **E17-04** Azure Data Lake Storage (ADLS Gen2) document import — SAS-token or managed-identity browse; select files → ingest
- [ ] **E17-05** Web page scrape import — user pastes an HTTP/HTTPS URL; server-side headless HTML fetch (no JS rendering) → strip HTML → plain text → chunk; SSRF protection identical to E4-34; surfaced in Documents page alongside file upload and URL import

---

## E18 — Dynamic Binary Module System (Plugins)

> Concept document: `dynamic-modules.md` at repo root.
> Scope: Agent Connectors and Testing Modules only. Auth and Monitoring modules remain host-compiled.

### v0.3.0 — Contracts & Infrastructure

- [ ] **E18-01** Add `IModulePlugin` interface to `mate.Domain` (`Contracts/Modules/IModulePlugin.cs`) — `PluginId`, `PluginVersion`, `PluginType`, `void Register(IServiceCollection, IConfiguration)`
- [ ] **E18-02** Add `MatePluginLoadContext` to `mate.Core` — extends `AssemblyLoadContext` (`isCollectible: true`); resolves `mate.Domain` against host's already-loaded copy to prevent duplicate interface types
- [ ] **E18-03** Add `PluginLoader` to `mate.Core` — scans configured directory for `*.dll`, loads each into `MatePluginLoadContext`, finds types implementing `IModulePlugin`, validates `PluginType` allow-list, calls `plugin.Register(services, config)`
- [ ] **E18-04** Wire `PluginLoader.DiscoverAndLoad(builder.Services, config)` into `Program.cs` before the `mateModuleRegistry` population loop; existing `AddmateXxxModule(...)` calls remain unchanged
- [ ] **E18-05** Add `Plugins` config section to `appsettings.json` — `Path` (default `/app/plugins`), `Enabled` (default `false`), `SkipSignatureCheck` (default `false`, dev only)
- [ ] **E18-06** Add `MATE_PLUGIN_PATH` environment variable override for plugin directory path
- [ ] **E18-07** Add audit logging for plugin load/reject events — `AuditHelper.Log(db, ...)` with entity type `"Plugin"`, action `"PluginLoaded"` or `"PluginRejected"`, details containing plugin id, version, and DLL path

### v0.3.0 — Security

- [ ] **E18-08** Implement Authenticode signature verification in `PluginLoader` — reject unsigned DLLs when `Plugins:SkipSignatureCheck=false`; read allowed publisher thumbprints from `Plugins:AllowedThumbprints[]` config
- [ ] **E18-09** SSRF protection for plugin-declared HTTP base URLs — validate connector `ConfigSchema` URL fields against existing SSRF block-list before secrets are passed to connectors at runtime
- [ ] **E18-10** Dev-mode signature bypass — `Plugins:SkipSignatureCheck=true` logs a prominent warning but does not hard-fail; blocked when `ASPNETCORE_ENVIRONMENT=Production`

### v0.3.0 — Plugin Discovery Modes

- [ ] **E18-11** Mode 1 — Folder drop: Docker Compose `./plugins:/app/plugins:ro` volume mount; documented in `infra/local/docker-compose.yml` comments
- [ ] **E18-12** Mode 2 — NuGet-based: `mate plugin add <package>` CLI command (`E18-CLI`) that `dotnet restore`s the package into the plugins folder; host only sees final DLLs, no NuGet restore at runtime
- [ ] **E18-13** Mode 3 — Allow-list: `MATE_ENABLED_CONNECTORS` and `MATE_ENABLED_JUDGES` env vars; `PluginLoader` skips plugins whose `PluginId` is absent; loads all if list is absent

### v0.3.0 — UI & Settings

- [ ] **E18-14** Settings — Modules tab: add "Source" column showing `Built-in` vs `Plugin (v{version})` for each registered module
- [ ] **E18-15** Settings — Plugins tab (new): list all loaded plugin DLLs with name, version, type, load status (`Loaded` / `Rejected`), and reject reason if applicable
- [ ] **E18-16** Plugins tab: "Reload Plugins" button — triggers `PluginLoader` rescan without host restart (requires `isCollectible` unload support; blocked until OD-1 decided)

### v0.3.0 — Worker Parity

- [ ] **E18-17** `mate.Worker` `Program.cs`: add same `PluginLoader.DiscoverAndLoad(...)` call so execution engine picks up the same connector and judge plugins as the WebUI
- [ ] **E18-18** Shared plugins volume: update `docker-compose.yml` to mount the same `./plugins` directory into both `mate-webui` and `mate-worker` containers

### v0.3.0 — SDK & Documentation

- [ ] **E18-19** Create `mate.PluginSDK` NuGet package — ships `mate.Domain` contracts + `IModulePlugin` interface + a project template (`dotnet new mate-plugin`) with the correct `<PackageReference>` and a stub `IModulePlugin` implementation
- [ ] **E18-20** `docs/modules/plugin-authoring.md` — step-by-step guide: create project, implement `IModulePlugin`, implement `IAgentConnectorModule`/`ITestingModule`, sign the DLL, drop into plugins folder
- [ ] **E18-21** `docs/modules/plugin-security.md` — signing requirements, thumbprint allow-list config, SSRF rules, audit log entries

### Open Decisions (must resolve before implementation)

- [ ] **E18-OD1** Decide: hot-reload (unload + reload without restart) vs startup-only — *recommendation: startup-only for v1*
- [ ] **E18-OD2** Decide: Worker plugin discovery — separate directory or shared volume — *recommendation: shared volume*
- [ ] **E18-OD3** Decide: plugin config namespacing — flat root vs `Plugins:{PluginId}:{Key}` — *recommendation: namespaced*
- [ ] **E18-OD4** Decide: minimum plugin .NET TFM — `net9.0` only vs `netstandard2.1` — *recommendation: `net9.0` only for v1*
- [ ] **E18-OD5** Decide: signing enforcement — dev skippable, prod always enforced — *recommendation: yes, guard with `ASPNETCORE_ENVIRONMENT` check*

---

## E14 — HTTPS on localhost (dev cert for Docker)

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

---

## E19 — Red Teaming Module Category

> **Goal:** give operators a structured way to probe an AI agent for adversarial vulnerabilities (prompt injection, jailbreaks, data exfiltration, etc.) and receive actionable, risk-rated findings — separate from functional quality testing in E6/E8.
>
> **Regulatory context:** supports EU AI Act Art. 9 risk-management obligations and NIST AI RMF GOVERN/MAP/MEASURE cycles.

### v0.3.0 — Contracts & Generic Module ✅ Done

- [x] **E19-01** New domain contract file `src/Core/mate.Domain/Contracts/RedTeaming/IRedTeamModule.cs`:
  - `AttackCategory` enum (8 values: PromptInjection, Jailbreak, SystemPromptLeak, DataExfiltration, HallucinationInduction, ToxicContent, PrivacyLeak, RoleConfusion)
  - `RiskLevel` enum (None / Low / Medium / High / Critical)
  - `AttackRequest`, `AttackProbe`, `RedTeamFinding`, `RedTeamReport` DTOs
  - `IAttackProvider` interface — `GenerateProbesAsync` + `EvaluateResponseAsync`
  - `IRedTeamModule` interface — module descriptor
- [x] **E19-02** `mate.Modules.RedTeaming.Generic` — `GenericAttackProvider` with 10 built-in probes, heuristic refusal detection, severity-mapped findings; zero external dependencies
- [x] **E19-03** `mateModuleRegistry` extended: `RegisterRedTeamModule`, `GetRedTeamModule`, `GetAllRedTeamModules`
- [x] **E19-04** WebUI wiring: `AddmateGenericRedTeaming()` in `Program.cs`; registry population loop
- [x] **E19-05** Settings UI: Red Teaming Modules section in the Modules tab (card per module, capability chips, `bi-shield-exclamation` icon)
- [x] **E19-06** `mate.WebUI.csproj` and `mate.sln` updated with new project references and configuration entries

### v0.3.0 — Red Team Run Execution

- [ ] **E19-07** `RedTeamRun` entity — `Id`, `TenantId`, `AgentId`, `ModuleId`, `Status`, `StartedAt`, `CompletedAt`, `TotalProbes`, `FindingCount`, `HighestRisk`; EF migration
- [ ] **E19-08** `RedTeamFindingRecord` entity — persisted finding: `Id`, `RunId`, `TenantId`, `ProbeMessage`, `AgentResponse`, `Category`, `Risk`, `Rationale`, navigation to run; EF migration
- [ ] **E19-09** `RedTeamExecutionCoordinator` in `mate.Core` — selects probe provider, calls `GenerateProbesAsync`, sends each probe to the agent via `IAgentConnector`, calls `EvaluateResponseAsync`, persists findings; honours `CancellationToken`
- [ ] **E19-10** `IMessageQueue` message type `StartRedTeamRunMessage` — `RunId`, `AgentId`, `ModuleId`, `Categories[]`, `NumberOfProbes`
- [ ] **E19-11** `TestRunWorker` extended (or separate `RedTeamWorker`) to consume `StartRedTeamRunMessage` and call `RedTeamExecutionCoordinator`
- [ ] **E19-12** `POST /api/redteam/runs` API endpoint — start a red-team run; body: `agentId`, `providerType`, `categories[]`, `numberOfProbes`; returns `{ runId }`
- [ ] **E19-13** `GET /api/redteam/runs/{id}` — run status + summary
- [ ] **E19-14** `GET /api/redteam/runs/{id}/findings` — paginated list of findings for a run

### v0.3.0 — Red Team UI

- [ ] **E19-15** Red Team page (`/redteam`) — list past red-team runs per agent with highest risk badge, finding count, date; "Start New Run" button opening a config panel
- [ ] **E19-16** New Run panel — select agent, select provider module, choose attack categories (multi-select chips), set probe count; submit triggers `POST /api/redteam/runs`
- [ ] **E19-17** Red Team Run Report page (`/redteam/{runId}`) — findings table with risk badge, category, probe message, agent response preview, rationale, reproduction steps, mitigations; expandable row for full detail
- [ ] **E19-18** Risk summary bar — Critical / High / Medium / Low / None count tiles at top of report
- [ ] **E19-19** Export findings as CSV / JSON from the run report page
- [ ] **E19-20** Red Team run history accessible from the Agent detail card (link from Agents page)
- [ ] **E19-21** Navigation sidebar entry — "Red Team" link (below Testing section, `bi-shield-exclamation` icon)

### v0.3.0 — Audit Logging

- [ ] **E19-22** `AuditHelper.Log` calls for `RedTeamRunStarted` and `RedTeamRunCompleted` entity type actions
- [ ] **E19-23** Audit Log page shows red-team events alongside test-run events

### v0.3.0 — CLI Integration

- [ ] **E19-24** `mate redteam run <agentId> [--categories <list>] [--probes <n>]` — start a run, stream finding summary to console
- [ ] **E19-25** `mate redteam report <runId> [--format json|csv]` — export findings

### v0.3.0 — Health & Monitoring

- [ ] **E19-26** `/health/modules` extended — includes `IRedTeamModule.IsHealthy()` per registered provider
- [ ] **E19-27** Settings Modules tab: red-team modules show their health status inline (same pattern as connectors)

### v0.4.0 — AI-Powered Red Team Modules

- [ ] **E19-28** `mate.Modules.RedTeaming.ModelAsAttacker` — uses an LLM (Azure OpenAI / AI Foundry) to generate context-aware, domain-specific adversarial probes; config fields: Endpoint, ApiKey, Model, Temperature; implements `IAttackProvider`
- [ ] **E19-29** `mate.Modules.RedTeaming.ModelAsEvaluator` — uses an LLM to evaluate whether an agent response constitutes a vulnerability (replaces heuristic refusal detection); combine with any `IAttackProvider`
- [ ] **E19-30** Multi-turn attack chains — `AttackProbe` extended with `FollowUpMessages[]`; `RedTeamExecutionCoordinator` sends a multi-turn conversation; captures full transcript in `RedTeamFindingRecord`
- [ ] **E19-31** Jailbreak catalogue — curated, versioned YAML library of known jailbreak patterns (`docs/redteaming/jailbreaks.yml`); `GenericAttackProvider` loads from catalogue at startup; community-updatable

### v0.4.0 — Reporting & Compliance

- [ ] **E19-32** CVSS-style risk score per finding — exploitability + impact sub-scores composited into `RiskScore` (0.0–10.0); shown on report
- [ ] **E19-33** Trend analysis — compare finding counts and highest risk across multiple red-team runs for the same agent; trend chart on Red Team page
- [ ] **E19-34** PDF report export — formatted red-team report with executive summary, finding table, and remediation guidance; suitable for compliance reviews
- [ ] **E19-35** EU AI Act Art. 9 compliance annotation — tag findings with relevant AI Act obligation; PDF report section maps findings to Art. 9 sub-requirements
| TD-05 | `AddMicrosoftIdentityWebApp(AuthenticationBuilder)` overload does not set DefaultScheme/DefaultChallengeScheme — must be set explicitly before calling it | Note |
| TD-06 | Azure AD `response_mode=form_post` requires `SameSite=Unspecified` on OIDC correlation and nonce cookies | Note |
| TD-07 | ~~ASP.NET Core Data Protection keys must be persisted to a volume in Docker~~ — **RESOLVED**: in-memory keys are correct; persisted keys caused decrypt failures when container restarts with new keys | Resolved |
| TD-08 | `IHttpContextAccessor.HttpContext` is null in Blazor Server SignalR circuits — always use `AuthenticationStateProvider` fallback for tenant/user resolution | Note |
| TD-09 | ~~EntraId login requires HTTPS on localhost~~ — **RESOLVED**: production deployment at `https://maaj.imbery.de` via nginx fully resolves the Mixed Content issue; localhost HTTP remains unsupported | Resolved |
| TD-10 | `Run` entity is missing CI/CD traceability fields present in MaaJforMCS: `GitSha`, `ModelVersion`, `PromptVersion` — add these nullable string columns to `Run` + EF migration | Medium |
| TD-11 | `TestCase.SourceDocumentId` is a single optional FK; MaaJforMCS uses a `TestCaseDocument` many-to-many join entity — evaluate whether M2M is needed for test cases that reference multiple source chunks | Low |
| TD-12 | Missing `Execution:*` config surface: `MaxConcurrency`, `RateLimitPerMinute`, `Retries`, `BackoffSeconds` — add to `appsettings.json` and honour in `TestExecutionService` to avoid throttling when running large suites | Medium |

---

*Last updated: 2026-03-01, Session 14 (parity audit) — v0.2.0 released*
