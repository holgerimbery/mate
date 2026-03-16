# mate — Changelog

All notable changes to this project will be documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Bulk test case operations (E4-32)** — Expanded suite tables now support multi-select test case checkboxes with select-all and suite-scoped bulk actions: `Activate Selected`, `Deactivate Selected`, and `Delete Selected` (with confirmation), including audit log entries and post-action status messages.
- **Test case cloning (E4-33)** — Test Suites now includes a one-click `Clone` action for test cases. Cloned cases stay in the same suite, copy the original content and active state, receive a new ID, append a safe copy suffix to the name, and are inserted at the end of the suite with audit logging.
- **Test suite cloning (E4-44)** — Test Suites now includes a one-click `Clone` action for suites. Cloned suites stay in the same tenant, copy suite metadata, duplicate all contained test cases with new IDs and fresh timestamps, use collision-safe copy names, and write an audit log entry.
- **Agent cloning (E4-45)** — Agents now includes a one-click `Clone` action. Cloned agents stay in the same tenant, copy agent metadata and connector configs with new IDs, keep stored secret references (no resolved secret duplication), use collision-safe copy names, and write an audit log entry.
- **Test suite JSON import/export (E4-30)** — Test Suites page now supports exporting any suite (with all test cases) to a JSON file via a per-row Export button, importing a JSON file to create a new suite via an Import button in the header, and downloading a ready-to-use template (`suite-import-template.json`) with two annotated sample test cases.
- **Agent environment filter on run history (E4-29)** — Dashboard run history table now includes an `Environment` dropdown filter (Development / Test / Staging / Production / Any) that filters runs by the environment of the associated agent.
- **System status badges (E4-28)** — Dashboard now shows a `System Status` section with at-a-glance health badges for DB (live `CanConnectAsync` check), DirectLine (active CopilotStudio connector config presence), and AI Judge (provider registration and settings presence); states are `Healthy`, `Pending`, or `Down`.
- **Top-5 failing test cases widget (E4-27)** — Dashboard now shows a `Top Failing Test Cases` section that aggregates the five most frequently failing test cases across recorded results, ranked by fail count with suite context and an empty state when no failed results exist.
- **Latency P95 sparkline trend (E4-26)** — Dashboard now shows a P95 latency trend sparkline in the `Avg Latency` KPI card, based on the last 10 completed runs, normalized to the observed min/max range; the caption reads "P95 · last N runs".
- **Dashboard pass-rate sparkline (E4-25)** — Dashboard now shows a compact pass-rate trend sparkline in the `Avg Pass Rate` KPI card, based on recent completed runs, to highlight short-term quality direction at a glance.
- **Local timezone display** — timestamps on Home, Dashboard, and Run Report pages now render in the container's local timezone (configurable via `MATE_TIMEZONE`, defaulting to `Europe/Berlin`) using a new `TimeDisplay.Local()` helper; `tzdata` added to the WebUI container image.

### Changed
- **Responsive sidebar behavior (E4-18)** — WebUI layout now uses an off-canvas sidebar on mobile (hidden by default), a topbar menu trigger, backdrop tap-to-close, and auto-close on navigation; desktop expand/collapse behavior remains intact.
- **Page-level header pattern (E4-19)** — introduced reusable `PageHeader` component (title + description + optional actions layout) and applied it across core WebUI pages for a consistent page-entry experience in list/edit/run modes, with responsive behavior on mobile.
- **Agents page parity completion (E4-06)** — completed agents CRUD experience with dynamic connector config forms, connector module-type badges in list rows, and agent health badges; also refined table containment/wrapping and mobile/desktop menu behavior to avoid overflow and desktop-only menu artifacts.
- **BREAKING: strict role model without aliasing** — authorization now accepts only `SuperAdmin`, `TenantAdmin`, `Tester`, and `Viewer`. Legacy role names (`Admin`, `PlatformAdmin`) and alias mappings are no longer accepted. API key creation and role-based access docs are updated to the new role model.

### Fixed
- **API key role escalation prevention** — `ApiKeysPage` now resolves the caller's highest effective role from all supported role claim types and enforces least-privilege assignment both in the role dropdown and in `GenerateKey()` server-side validation, preventing lower-privilege users from minting higher-privilege API keys.
- **Sign-out blocked by backdrop z-index** — topbar stacking context was below the popup-close backdrop (z-index 900 vs 1999), causing the backdrop to swallow all clicks inside the user popup; raised `mate-topbar` z-index to 2000 so the popup and Sign Out link are fully interactive.
- **Role display normalization in user popup** — added `NormalizeRole()` mapping to canonicalize legacy/typo Entra role aliases (`platformadmin` → `SuperAdmin`, `tenatadmin` → `TenantAdmin`) and suppress implicit `Viewer` when higher roles are present in the token.
- **WebUI startup 500 with Authentication:Scheme=None** — normalized runtime auth resolution to use the registered Generic handler for `None`, fixing `No default challenge scheme` failures on `/`.

---

## [v0.7.1] — 2026-03-12

### Fixed
- minor branding issue
- custom domain binding for azure container apps

---

## [v0.7.0] — 2026-03-11

### Added
- **Help page changelog link** — new "View Changelog" button in the Source & Documentation section linking to Developer-Changelog wiki for easy access to release history and feature updates (E4-26).
- **Database mode badge on Help page** — new Runtime Environment row showing detected PostgreSQL or SQLite database provider; helps developers verify deployment database configuration at a glance (E4-19).

### Changed
- **CSS gradient tokenization** — migrated 16 inline gradient styles from Razor components to reusable utility classes (`.icon-tile`, `.icon-tile-{size}`, `.icon-tile-{color}`, soft variants); reduces markup duplication across Home, Settings, Help, Agents, Wizard, and RunReport pages (E4-16).
- **Version badge normalization** — strip leading `v`/`V` prefix from version string read from VERSION file; applied consistently across Home and Help version badges for uniform display (E4-17).

### Fixed
- **Dark mode contrast issues** — explicitly set `color: var(--text-primary)` on `.section-card`, `.kpi-card`, `.mate-header`, `.mate-page` to prevent Bootstrap's `--bs-body-color` from leaking through card surfaces when `data-bs-theme` is not set (E4-15).
- **Dark mode spinner color** — fix `.spinner-border` icon color in dark mode to use theme-aware styling instead of default Bootstrap color (E4-15).
- **TestSuites expanded row styling** — replace ad-hoc `var(--bg-page,#f5f7fa)` fallback (always light) with consistent `var(--surface-2)` + explicit text colors for dark mode compatibility (E4-15).
- **Help page GitHub icon contrast** — adjust icon tile color from `#24292e` to `#2d333b`/`#555f6b` for better contrast in both light and dark modes (E4-15).
- **Health check status icons** — use theme-aware `var(--success)` and `var(--danger)` for pass/fail indicators instead of hardcoded colors (E4-15).
- **Azure quickstart package script synchronization** — ensure all helper scripts (setup-env, setup-keyvault-secrets, deploy, update-container-images, deploy-whatif, cleanup-rg, repair-runtime-secrets) are copied into the standalone quickstart package for consistent deployment experience (E25-02).

---

## [v0.6.3] — 2026-03-10

### Added
- **`repair-runtime-secrets.ps1` helper** — new recovery/maintenance script in both `infra/azure/scripts/` and `quickstart-azure/` that re-applies `postgres-conn` and `blob-conn` secrets and rewires the Container App environment to `secretref:` values (E25-01).

### Changed
- **`update-container-images.ps1` sequencing** — image updates now wait for ARM/Bicep deployment completion before applying runtime secret wiring. This removes the `--no-wait` race between template deployment and post-deployment secret updates (E25-01).
- **Quickstart/Azure documentation** — update-script guidance now reflects synchronous execution, helper-script behavior, and recovery flow instead of background `--no-wait` behavior (E25-01).

### Fixed
- **Container App update race condition** — runtime secret wiring no longer runs while the Bicep deployment is still in progress. This prevents partially applied configuration and reduces failed rollouts caused by deployment ordering conflicts (E25-01).
- **Runtime secret repair path** — secret rewiring is now reusable as a standalone repair step, making recovery deterministic when DB/blob secret references drift or must be repaired manually (E25-01).

---

## [v0.6.2] — 2026-03-10

### Added
- **`repair-runtime-secrets.ps1` helper** — new recovery/maintenance script in both `infra/azure/scripts/` and `quickstart-azure/` that re-applies `postgres-conn` and `blob-conn` secrets and rewires the Container App environment to `secretref:` values (E25-01).

### Changed
- **`update-container-images.ps1` sequencing** — image updates now wait for ARM/Bicep deployment completion before applying runtime secret wiring. This removes the `--no-wait` race between template deployment and post-deployment secret updates (E25-01).
- **Quickstart/Azure documentation** — update-script guidance now reflects synchronous execution, helper-script behavior, and recovery flow instead of background `--no-wait` behavior (E25-01).

### Fixed
- **Container App update race condition** — runtime secret wiring no longer runs while the Bicep deployment is still in progress. This prevents partially applied configuration and reduces failed rollouts caused by deployment ordering conflicts (E25-01).
- **Runtime secret repair path** — secret rewiring is now reusable as a standalone repair step, making recovery deterministic when DB/blob secret references drift or must be repaired manually (E25-01).

---

## [v0.6.1] — 2026-03-09

### Added
- **Azure deployment automation** — Post-deployment automation in `deploy.ps1` for PostgreSQL firewall rule creation, blob storage secret injection, and connection string binding; idempotent for both WebUI and Worker containers (E24-01).
- **Standalone Azure quickstart package** — `quickstart-azure/` directory with complete deployment guide, configuration template, and all 6 PowerShell scripts; enables one-command deployment without git clone (E24-02).
- **`quickstart-azure/README.md`** — Comprehensive Azure deployment guide with prerequisites, 5-step workflow, profiles (xs/s/m/l), cost estimates, troubleshooting, and architecture details (E24-02).
- **`quickstart-azure/QUICKSTART.md`** — Quick reference for rapid deployment (3-5 minutes); includes scripts overview, deployment options, and cleanup instructions (E24-02).
- **`quickstart-azure/DEPLOYMENT.md`** — Technical reference documentation for post-deployment workflow phases and verification (E24-02).
- **Manual prerelease workflows** — GitHub Actions `workflow_dispatch` trigger enabling prerelease creation from any branch; supports dynamic prerelease versioning (v0.6.0-branch.runNumber or custom suffix) (E24-03).
- **Dual quickstart package generation** — GitHub Actions automatically generates both `mate-quickstart-<version>.zip` (Docker Compose) and `mate-quickstart-azure-<version>.zip` (Azure deployment) during release; both attached to GitHub Release (E24-04).
- **`update-container-images.ps1` script** — PowerShell script for quick container image updates without full infrastructure redeploy; updates `.env`, maintains Bicep state, performs zero-downtime rolling updates; available in both `infra/azure/scripts/` and `quickstart-azure/` (E24-07).

### Changed
- **README.md quickstart order** — Reordered deployment options: Option B now Deploy to Azure (recommended), Option C now Build from Source (E24-05).
- **GitHub Actions release workflow** — Extended `docker-publish.yml` with `workflow_dispatch` input for prerelease suffix; conditional prerelease marking in GitHub Release; separate image tagging logic for stable vs. prerelease versions (E24-03).
- **Release package generation** — Post-deployment steps now handle postgres-conn, blob-conn, and all 10 quickstart-azure files; ZIP generation for both local and Azure packages (E24-04).
- **Docker image tagging** — `latest` tag only applied to stable releases from version tags; prerelease versions use versioned tags only (E24-03).
- **Matrix build parallelism** — GHCR publish workflow now uses `max-parallel: 1` to serialize image builds and reduce transient timeout failures; GHCR login retries up to 3 times with exponential backoff (E24-06).

### Fixed
- **Container app crash-loop on image updates** — `update-container-images.ps1` now configures runtime secret references (`postgres-conn`, `blob-conn`) after Bicep deployment, matching the post-deployment behavior in `deploy.ps1`. Prevents new revisions from starting with broken `USE-KEYVAULT-REFERENCE` placeholder values, ensuring zero-downtime rolling updates succeed with healthy revisions (E24-08).

---

## [v0.6.0] — 2026-03-04

### Added
- **`Run.ErrorMessage`** — nullable `string?` property on the `Run` entity; populated by `TestRunWorker` when an unhandled exception causes a run to fail; surfaced as a red error banner on the Run Report page.
- **EF migration `AddRunErrorMessage`** — adds nullable `ErrorMessage` column to the `Runs` table.
- **`AgentRateLimitException`** — new public exception type in `mate.Modules.AgentConnector.CopilotStudio`; thrown when the connected Copilot Studio agent returns an `enAIToolPlannerRateLimitReached` activity, so rate-limited cases are recorded as `skipped` rather than `failed`.
- **`SkippedCount` tracking** — `TestExecutionService` now counts `"skipped"` verdicts in a dedicated `skip` counter; `Run.SkippedCount`, `Run.TotalTestCases`, and the run completion log include skipped cases correctly.

### Changed
- **PostgreSQL-only baseline** — `Local` (SQLite) infrastructure tier removed; `Container` (PostgreSQL + Azurite) is now the default and only local development baseline.
  - `AddmateSqlite` and `SqliteBackupService` deleted; `NoOpBackupService` moved to `mate.Infrastructure.Local`.
  - `LocalInfrastructureOptions.SqliteDatabasePath` and `BackupPath` properties removed.
  - `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.Data.Sqlite` package references removed.
  - All host `Program.cs` files (`WebUI`, `Worker`, `CLI`) always register `AddmatePostgres` + `AddmateAzureInfrastructure`; no `Infrastructure__Provider` branching for database.
  - `mate.CLI.csproj` project reference updated from `mate.Infrastructure.Local` to `mate.Infrastructure.Azure`.
  - `infra/local/docker-compose.yml` fully rewritten: postgres and azurite are always-on (no `profiles:`), connection strings baked in.
  - `debug-container.ps1` simplified: `-Container` flag removed, `-DB` always uses `psql`.
- **Quickstart package updated** — `quickstart/docker-compose.yml` now includes PostgreSQL 17 and Azurite alongside webui and worker (previously SQLite-only, Azurite was absent). Named volumes changed to `mate-pgdata`, `mate-azurite`, `mate-logs` (old `mate-data` volume dropped). `quickstart/.env.template` gains an `IMAGE_TAG` pinning entry. `quickstart/README.txt` updated to describe the four-container stack, new volume layout, and correct data persistence guidance.
- **`appsettings.json` cleaned up** — `ConnectionStrings.DefaultConnection` renamed to `ConnectionStrings.Default` (matches EF key); SQLite path and `LocalInfrastructure` section removed.
- **Settings → Data Management UI** — backup/restore descriptions updated to reflect no-op behaviour in PostgreSQL mode; `.db` file type restriction removed from restore input.
- **OpenAPI Admin tag** — description updated from "SQLite backup/restore" to reflect no-op in PostgreSQL deployments.
- **`TestRunWorker.cs` XML doc** — "shared SQLite volume" comment updated to "PostgreSQL instance".
- **`AzureBlobStorageService` constructor** — now parses the connection string and uses explicit `StorageSharedKeyCredential` + `Uri(blobEndpoint)` when a `BlobEndpoint` key is present. Fixes HMAC signature mismatch (HTTP 403) when Azurite runs on a non-localhost Docker hostname with Azure.Storage.Blobs v12.21+.
- **Azurite blob connection string** — changed to `UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://azurite` in `docker-compose.yml`. This is the canonical way to target a non-localhost Azurite endpoint; the SDK uses the hardcoded development account credentials and emulator canonical-resource HMAC format regardless of hostname.
- **`ResolveJudgeSettingAsync` fallback** — platform-level default judge is now looked up by `TenantId == PlatformTenantId` (`00000000-0000-0000-0000-000000000001`) instead of the incorrect `Guid.Empty`; aligns with how `mateDbSeeder` stores the seeded default.
- **`TestRunWorker` failure handler** — saves `run.ErrorMessage = ex.Message` before marking status `failed`.
- **`RunReport.razor`** — shows a `alert-danger` banner with the error message when `Run.Status == "failed"` and `ErrorMessage` is non-null.
- **PG-native EF migrations** — all SQLite migrations deleted; new `InitialCreate` migration generated with PostgreSQL-native types: `uuid`, `text`, `timestamp with time zone`, `double precision`, `boolean`.
- **`mateDbContextFactory`** — calls `AddEnvironmentVariables()` (no prefix) before the `MATE_`-prefixed variant, so `ConnectionStrings__Default` is readable during `dotnet ef migrations` tooling.

### Fixed
- **`AzureBlobStorageService.ValidateName`** — method previously rejected `/` in blob names but `DocumentIngestor` creates names of the form `{tenantId}/{guid}.ext`; `/` is now allowed (only `\` and `..` are blocked).
- **`Run.SkippedCount` never updated** — `"skipped"` verdicts previously fell through to the `default: error++` branch; now counted separately.

---

## [v0.5.0] — 2026-03-04

### Added
- **Pass rate by tag** (E4-19) — Run Report now shows a _Pass Rate by Tag_ breakdown table grouping test cases by their tags (or `(untagged)`). Each row shows total cases, pass count, fail count, a visual pass-rate bar, and average score.
- **Tags on test cases** — `TestCase` entity now has a `Tags string[]` property (stored as JSON column `TagsJson`); EF migration `AddTestCaseTags` included.
- **Tags on test cases in UI** — Test Suites page Add/Edit Case modal now includes a Tags field (comma-separated); tags are displayed as badge chips in the test case sub-table.
- **Refine Rubric** (E4-22) — new _Refine Rubric_ button appears on the Run Report when at least one human verdict disagrees with the AI verdict. Clicking opens an in-page view listing all disagreements and allows the user to request AI-generated rubric suggestions from the configured default AI judge.
- **Draft rubric sets** — after generating AI rubric suggestions, a _Save as Draft Rubric Set_ button persists the result as a new `RubricSet` (with `IsDraft = true` and a back-reference to the originating run) including one placeholder `RubricCriteria` per suggested bullet point. Draft sets appear on the Rubrics page with a **DRAFT** badge and a **Promote** button that clears the flag.
- EF migration `AddRubricSetDraft` adds `IsDraft INTEGER NOT NULL DEFAULT 0` and `SourceRunId TEXT NULL` columns to `RubricSets`.

### Fixed
- **Refine Rubric panel never rendered** — the top-level conditional in `RunReport.razor` did not exclude `_refineRubricMode`, so the `else if (_refineRubricMode)` branch was unreachable from the main report view; fixed by adding `&& !_refineRubricMode` to the guard.
- **"No default judge setting found"** — `RunRefineRubric` previously added an explicit `TenantId == TenantCtx.TenantId` filter that excluded judge settings stored with the platform tenant id; removed the redundant filter and rely on the existing EF global query filter instead.

---

## [v0.4.0] — 2026-03-02

### Added
- **Module Tier Labels** — every module now carries a `ModuleTier` (`Free` / `Premium`) property surfaced as a badge on Settings → Modules cards.
  - `ModuleTier` enum (`Free`, `Premium`) added to `mate.Domain.Contracts.Modules`.
  - `ModuleTier Tier { get; }` property added to all five module interfaces: `IAgentConnectorModule`, `ITestingModule`, `IQuestionGenerationProvider`, `IMonitoringModule`, `IRedTeamModule`.
  - All current modules implement `ModuleTier.Free` except `GenericRedTeamModule`, which is `ModuleTier.Premium`.
  - Settings → Modules cards show the tier badge (neutral grey for Free, amber for Premium) alongside the green Active badge.
  - `badge-free` and `badge-premium` CSS classes added to `app.css` with dark-mode variants.
  - `README.md` module tables updated with a `Tier` column; `docs/wiki/Developer-Module-Development.md` gains a **Module Tiers** section documenting the enum values, current assignments, and the required `Tier` member for new modules.
- **Copyright headers** — 3-line copyright/licence notice added to all 81 hand-written `.cs` files in `src/` (excluding `obj/`, `bin/`, and `Migrations/` generated files).

### Changed
- **Going public** — repository visibility changed to public; `docs/concepts/` design documents excluded from git tracking; GHCR images configured as public packages.
- **GHCR public access** — removed Docker login / Personal Access Token instructions from `README.md`, `docs/wiki/User-Getting-Started.md`, and `quickstart/README.txt`; images are publicly accessible on GHCR.
- **Documentation consistency pass** — wiki version numbers, epic table (added E19, E20), Settings page Modules tab description (added Red Teaming + Question Generation + tier badges), Architecture module table (fixed merged row), tech-debt TD-02 marked resolved.

---

## [v0.3.2] — 2026-03-02

### Added
- **GitHub Actions workflow** (`.github/workflows/docker-publish.yml`): automated build and publish pipeline for Docker images.
  - Triggers on version tags (`v*.*.*`) only — no build on every `main` push.
  - Matrix strategy builds `mate-webui` and `mate-worker` images in parallel.
  - Pushes to **GitHub Container Registry** (`ghcr.io/holgerimbery/mate-webui`, `ghcr.io/holgerimbery/mate-worker`).
  - Multi-arch build (`linux/amd64`, `linux/arm64`) via QEMU.
  - Tags: full semver, `major.minor`, `sha-<short>`, and `latest` (stable releases only — skipped for pre-release tags like `-rc.1`).
  - VERSION file guard: build fails if the `VERSION` file does not match the pushed git tag.
  - `create-release` job: runs after images are pushed, extracts the matching section from `CHANGELOG.md`, bundles the quickstart package (with images pinned to the release tag), and creates a GitHub Release with the changelog body and zip attachment.
- **Quickstart package** (`quickstart/`): self-contained starter kit for end-users deploying from GHCR.
  - `docker-compose.yml` — references both `mate-webui` and `mate-worker` GHCR images; all environment variables pre-wired; health-check and named volumes configured.
  - `.env.template` — minimal template covering `Authentication__Scheme`, `AzureAd__*` Entra ID settings, and reverse-proxy trust configuration.
  - `README.txt` — step-by-step guide: copy `.env`, configure auth, pin image tags, `docker compose pull && up -d`, API key CI usage, backup notes, and security guidance.

---

## [v0.3.1] — 2026-03-02

### Added
- **Document Viewer page** (`/documents/{id}`) — full per-document view accessible from the Documents list via a new **View** (eye icon) button per row.
  - **Metadata panel**: content type, file size, page count, total chunks, upload date, uploader — rendered in a responsive grid with a coloured left border.
  - **Chunk browser**: all chunks rendered in sequence; each card shows the chunk index badge, token count, and optional category tag.
  - **Full-text search**: live search field filters chunks client-side as the user types; matching terms are highlighted with a yellow mark; results counter updates in real time; **Clear** button resets the filter.
  - **Pagination**: 20 chunks per page; Previous / Next controls with current page / total page indicator.
  - **Breadcrumb navigation**: Documents → _filename_ breadcrumb header with a Back button.
- **View button in Documents list** (`Documents.razor`) — each document row in `/documents` now has a `bi-eye` View link beside the Delete button, navigating to `/documents/{id}`.

### Fixed
- **`DocumentViewer.razor` RZ10008 build error**: Blazor rejects combining `@bind:event="oninput"` with a separate `@oninput` handler on the same element. Fixed by replacing `@bind` with a plain `value="@_searchQuery"` binding and updating `_searchQuery` directly inside `OnSearchChanged`.
- **Chunk text black-on-black rendering**: the global `app.css` rule `pre { background: #1e1e2e; }` applied a dark code-block background to the chunk `<pre>` element; inline `color:var(--text-primary)` then rendered dark text on that background. Fixed by replacing `<pre>` with a `<div style="white-space:pre-wrap">` so the chunk text inherits the card background and colour in both light and dark modes.

---

## [v0.3.0] — 2026-03-01

### Added
- **Red Teaming module category** — new first-class module type (`IRedTeamModule` / `IAttackProvider`) for adversarial security testing of AI agents, fully separate from the existing `ITestingModule` / `IJudgeProvider` stack.
- **Domain contracts** (`src/Core/mate.Domain/Contracts/RedTeaming/IRedTeamModule.cs`):
  - `AttackCategory` enum — 8 attack types: `PromptInjection`, `Jailbreak`, `SystemPromptLeak`, `DataExfiltration`, `HallucinationInduction`, `ToxicContent`, `PrivacyLeak`, `RoleConfusion`.
  - `RiskLevel` enum — `None`, `Low`, `Medium`, `High`, `Critical`.
  - `AttackRequest` — input to probe generators (agent description, categories filter, probe count, domain hint, resolved credentials).
  - `AttackProbe` — a single adversarial prompt with category, failure signature, and rationale.
  - `RedTeamFinding` — confirmed vulnerability: probe, agent response, risk level, rationale, reproduction steps, mitigations.
  - `RedTeamReport` — aggregated findings for one red-team run.
  - `IAttackProvider` — generates probes + evaluates agent responses; returns `null` when the agent handled the probe safely.
  - `IRedTeamModule` — module descriptor with `ModuleId`, `DisplayName`, `ProviderType`, `ConfigSchema`, `IsHealthy()`, `ValidateConfig()`, `GetCapabilities()`, `RegisterServices()`.
- **`mate.Modules.RedTeaming.Generic`** (`src/Modules/RedTeaming/mate.Modules.RedTeaming.Generic/`):
  - `GenericAttackProvider` — 10 built-in adversarial probes covering all 8 attack categories; heuristic refusal-keyword detection to flag compliant (vulnerable) responses; severity-mapped `RedTeamFinding` output; no external LLM required — suitable for local dev and CI.
  - `GenericRedTeamModule` — zero-config descriptor; `GetCapabilities()` returns all 7 attack-category strings.
  - `AddmateGenericRedTeaming()` DI extension.
- **`mateModuleRegistry`** (`src/Core/mate.Core/mateModuleRegistry.cs`) — new `_redTeamModules` dictionary, `RegisterRedTeamModule()`, `GetRedTeamModule()`, `GetAllRedTeamModules()`.
- **WebUI registration** (`Program.cs`) — `AddmateGenericRedTeaming()` call; `foreach (var m in GetServices<IRedTeamModule>()) registry.RegisterRedTeamModule(m)` population loop at startup.
- **Settings UI — Red Teaming Modules section** (`Settings.razor`) — new card section in the Modules tab after Question Generation: shows `DisplayName`, `ProviderType` badge, Active status, and all attack-category capability chips; red/purple gradient icon (`bi-shield-exclamation`).
- **Project wiring** — `mate.WebUI.csproj` project reference; `mate.sln` project entry and all 6 build configuration entries.

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
- **UI identity assets — logo**: `mate-logo.png` and `mate-logo-wide.png` added to `wwwroot`; sidebar now renders the logo image instead of the initial letter; `App.razor` `<head>` includes a `<link rel="icon">` favicon.
- **`CopilotStudioConnectorModule.GetConfigDefinition()`** updated: `EnvironmentId` is now optional; `UseWebChannelSecret` boolean field added (default: false); `ReplyTimeoutSeconds` default value set to "30".
- **WebUI pages**: `Discover.razor` (`/discover`), `Rubrics.razor` (`/rubrics`), `Help.razor` (`/help`), `AuditLogPage.razor` (`/audit-log`) — new standalone pages.
- **Settings — Modules tab**: new default tab showing all registered `IAgentConnectorModule`, `ITestingModule`, and `IMonitoringModule` implementations with their config field definitions and links to the creation wizard.
- **Wizard — module-driven Step 1**: agent creation wizard Step 1 dynamically lists all registered `IAgentConnectorModule` implementations; Step 2 config form generated from the module's `ConfigSchema`.
- **`mateModuleRegistry`**: added `RegisterTestingModule()` and `GetAllTestingModules()` to support testing module discovery.
- **Program.cs registry population**: module registry populated at application startup by resolving all `IAgentConnectorModule`, `ITestingModule`, `IJudgeProvider`, and `IMonitoringModule` services from DI.
- **`BrandInfo.cs`** (`mate.Domain`): central UI identity metadata (`BrandName`, `BrandTagline`, `BrandCliDescription`, `LogoUrl`, `LogoWideUrl`) used by host components.
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
