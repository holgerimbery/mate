![version](https://img.shields.io/github/v/release/holgerimbery/mate)
[![License: CC BY-NC 4.0](https://img.shields.io/badge/License-CC%20BY--NC%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc/4.0/)
# **mate** — a multi-agent test environment for AI agents.
<p align="center">
  <img src="src/Host/mate.WebUI/wwwroot/mate-logo.png" width="220" alt="Multi-Agent Assessment & Judgement Logo">
</p>

mate connects to multiple AI agents, runs automated evaluation suites against them, tracks quality over time, and red-teams them for adversarial vulnerabilities. It supports Microsoft Copilot Studio, Azure AI Foundry*, generic HTTP agents*, and Parloa* out of the box — and is extensible with custom connector, judge, and red-team modules.   
> *backlog item   

Current version: **v0.9.0**  



> Mate is still in *active development*, so please keep in mind that some parts may not be fully documented yet, or the documentation may still be catching up. You might encounter a few rough edges along the way, so expect a slightly bumpy ride here and there.

---

## Product Demo (movie)

<p align="center">
  <a href="https://www.youtube.com/watch?v=iQjUIwJGqfs">
    <img src="https://img.youtube.com/vi/iQjUIwJGqfs/hqdefault.jpg" 
         alt="Watch the video" 
         style="border-radius:10px;">
  </a>
</p>


---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full release history.

## Backlog

See [BACKLOG.md](BACKLOG.md) for planned epics and work items.

---

## Features

- **Multi-agent** — connect and evaluate any number of AI agents in a single test suite
- **Pluggable judge modules** — ModelAsJudge (LLM scoring), RubricsJudge (deterministic), HybridJudge, CopilotStudioJudge, or write your own
- **Red teaming*** — adversarial probe generation and vulnerability assessment across 8 attack categories (prompt injection, jailbreak, system-prompt leak, data exfiltration, hallucination induction, toxic content, privacy leak, role confusion); pluggable `IRedTeamModule` / `IAttackProvider` architecture 
- **Blazor Server UI** — full-featured web interface for managing agents, test suites, documents, rubrics, and results
- **REST API** — full Minimal API with OpenAPI spec, Scalar explorer, and API key authentication
- **Multi-tenant** — EF Core global query filters isolate all data per tenant
- **Auth-flexible** — runs anonymous in local dev (`None`); Microsoft Entra ID (Azure AD) OIDC in production
- **Docker-first** — single `docker compose up` for the full local stack
- **Azure Containers Apps** - deployable with interactice deployment secripts

*not yet in the code, see premium modules

---

## Quick Start

### Option A — Quickstart package (no clone required)

Download the latest `mate-quickstart-<version>.zip` from the [GitHub Releases page](https://github.com/holgerimbery/mate/releases/latest) and unzip it.

Start the stack:

**Windows (PowerShell)**
```powershell
copy .env.template .env
# Edit .env — set Authentication__Scheme and optionally Entra ID values
docker compose pull
docker compose up -d
```

**macOS / Linux**
```bash
cp .env.template .env
# Edit .env — set Authentication__Scheme and optionally Entra ID values
docker compose pull
docker compose up -d
```

Open **<http://localhost:5000>**. Images are pulled from GitHub Container Registry (`ghcr.io/holgerimbery/mate-webui`, `ghcr.io/holgerimbery/mate-worker`) — no build step required.

> **Tip:** Pin a specific version by replacing `:latest` with the version tag in `docker-compose.yml`, e.g. `:0.3.2`.

### Option B — Deploy to Azure

Download the `mate-quickstart-azure-<version>.zip` from [GitHub Releases](https://github.com/holgerimbery/mate/releases/latest) or use the scripts in `infra/azure/scripts/`:

**Windows (PowerShell)**
```powershell
cd infra/azure/scripts
pwsh ./check-prerequisites.ps1      # Validate tools
pwsh ./setup-env.ps1                # Configure Azure credentials
pwsh ./deploy-whatif.ps1            # Preview changes (recommended)
pwsh ./deploy.ps1                   # Deploy infrastructure
pwsh ./setup-keyvault-secrets.ps1   # Configure secrets & RBAC
```

See [quickstart-azure/README.md](quickstart-azure/README.md) for full deployment guide, troubleshooting, architecture details, and cost estimates.

> **Prerequisites:** Azure CLI, PowerShell 7+, Bicep CLI. Estimated deployment time: 3–5 minutes.

### Option C — Build from source

**Prerequisites:** [.NET 9 SDK](https://dotnet.microsoft.com/download) · [Docker Desktop](https://www.docker.com/products/docker-desktop/)

**Windows (PowerShell)**
```powershell
git clone https://github.com/holgerimbery/mate.git
cd mate

copy infra\local\.env.template infra\local\.env
# Edit infra\local\.env if needed — defaults work for local development

cd infra\local
docker compose up --build
```

**macOS / Linux**
```bash
git clone https://github.com/holgerimbery/mate.git
cd mate

cp infra/local/.env.template infra/local/.env
# Edit infra/local/.env if needed — defaults work for local development

cd infra/local
docker compose up --build
```

Open **<http://localhost:5000>**. No login required in the default `Generic` auth mode.

> **PostgreSQL + Azurite** are always started alongside webui and worker — no extra flags required. The default `.env.template` values work out of the box for local development.


---

## Project Structure

```
mate.sln
├── src/
│   ├── Core/               — module registry, execution engine, domain entities
│   ├── Host/               — WebUI (Blazor + API), Worker, CLI
│   ├── Infrastructure/     — Azure Blob Storage and local filesystem implementations
│   └── Modules/            — agent connectors, judge modules, auth, monitoring, red teaming
├── tests/                  — Unit, Integration, EndToEnd
├── infra/local/            — docker-compose.yml, Dockerfiles, .env.template
├── infra/azure/            — Azure Deployment Scripts
└── docs/
    ├── wiki/               — full user and developer documentation
    └── concepts/           — architecture blueprints
```

## Module Tiers

Each module carries a **tier** label shown on the Settings → Modules card:

| Tier | Meaning |
|------|---------|
| **Free** | Included in every installation — no additional licence required |
| **Premium** | Requires a premium subscription; fully functional without these modules, but use requires a valid licence |

> Currently the **Generic Red Teaming** module is the only **Premium** module (in development). All agent connector, judge, question-generation, and monitoring modules are **Free**.

---

## Agent Connector Modules

| Module | Protocol | Tier |
|--------|----------|------|
| `mate.Modules.AgentConnector.CopilotStudio` | Direct Line v3 / Web Channel Security | Free |
| `mate.Modules.AgentConnector.AIFoundry`* | Azure AI Agents SDK | Free |
| `mate.Modules.AgentConnector.Generic`* | Generic HTTP POST | Free |
| `mate.Modules.AgentConnector.Parloa`* | Parloa Conversation API | Free |

## Judge (Testing) Modules

| Module | Approach | Tier |
|--------|----------|------|
| `mate.Modules.Testing.ModelAsJudge` | LLM 5-dimension scoring | Free |
| `mate.Modules.Testing.RubricsJudge` | Deterministic Contains / NotContains / Regex | Free |
| `mate.Modules.Testing.HybridJudge` | Rubrics gate + LLM blend | Free |
| `mate.Modules.Testing.CopilotStudioJudge` | Citation-aware LLM, Copilot Studio defaults | Free |
| `mate.Modules.Testing.Generic`* | Keyword/regex, zero cost | Free |

## Red Teaming Modules

| Module | Approach | Tier |
|--------|----------|------|
| `mate.Modules.RedTeaming.Generic`* | Built-in static probe library (10 probes, 8 attack categories), heuristic response evaluation, works offline | **Premium** |

> \* backlog item — not yet implemented

## Documentation

Full documentation is in [`docs/wiki/`](docs/wiki/Home.md):

- **[User Documentation](docs/wiki/Home.md#user-documentation)** — getting started, agents, test suites, dashboard, settings, API keys, audit log
- **[Developer Documentation](docs/wiki/Home.md#developer-documentation)** — architecture, local setup, module development, API reference, contributing

---

## Running Tests

```bash
dotnet test mate.sln
```

---

## Configuration

All settings are controlled via environment variables (or `appsettings.json`):

| Variable | Default | Description |
|----------|---------|-------------|
| `Authentication__Scheme` | `None` | `None` (dev) or `EntraId` (prod) |
| `Infrastructure__Provider` | `Container` | `Container` (PostgreSQL + Azurite, default) · `Azure` (Azure Database for PostgreSQL + Azure Blob Storage) |
| `ConnectionStrings__Default` | *(PostgreSQL)*  | PostgreSQL connection string — set automatically by docker-compose; override for custom deployments |
| `AzureInfrastructure__BlobConnectionString` | — | Required when `Infrastructure__Provider` is `Container` or `Azure` |
| `AzureInfrastructure__UseKeyVaultForSecrets` | `false` | Core secrets mode switch: `false` = database-backed secrets, `true` = single Azure Key Vault |
| `AzureInfrastructure__KeyVaultUri` | — | Required when `AzureInfrastructure__UseKeyVaultForSecrets=true` |
| `AZURE_CLIENT_ID` | — | Required in Docker single-vault mode (service principal client ID) |
| `AZURE_CLIENT_SECRET` | — | Required in Docker single-vault mode (service principal secret) |
| `AZURE_TENANT_ID` | — | Required in Docker single-vault mode (tenant/directory ID) |
| `AzureAd__TenantId` | — | Required for EntraId auth |
| `AzureAd__ClientId` | — | Required for EntraId auth |
| `AzureAd__ClientSecret` | — | Required for EntraId auth |
| `JudgeSettings__Endpoint` | — | Azure OpenAI endpoint for LLM judge |
| `JudgeSettings__ApiKey` | — | API key for LLM judge |

See `infra/local/.env.template` for the full list.

### Core Secrets Modes (Database vs Single Vault)

Core deployments support two secret storage modes:

- **Database mode (default)**
  - `AzureInfrastructure__UseKeyVaultForSecrets=false`
  - Judge/QGen/Agent secrets are stored in PostgreSQL (`AppSecrets` table).
- **Single-vault mode**
  - `AzureInfrastructure__UseKeyVaultForSecrets=true`
  - `AzureInfrastructure__KeyVaultUri=https://<your-vault>.vault.azure.net/`
  - `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID` must be set for container auth.

Switch modes by updating `infra/local/.env` and rebuilding:

```powershell
./debug-container.ps1 -Stop
./debug-container.ps1 -Source build -Rebuild
```

---

## API

- **Interactive Explorer:** `/scalar/v1`
- **OpenAPI spec:** `/openapi/v1.json`

---

## License

© Holger Imbery. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the “Software”), to use
the Software for personal, educational, or research purposes only, subject to
the following conditions:

**Commercial use of the Software, in whole or in part, is strictly prohibited
without prior written permission from the copyright holder**
