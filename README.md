![version](https://img.shields.io/github/v/release/holgerimbery/mate)
[![License: CC BY-NC 4.0](https://img.shields.io/badge/License-CC%20BY--NC%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc/4.0/)
# **mate** — a multi-agent test environment for AI agents.
<p align="center">
  <img src="src/Host/mate.WebUI/wwwroot/mate-logo.png" width="220" alt="Multi-Agent Assessment & Judgement Logo">
</p>

mate connects to multiple AI agents, runs automated evaluation suites against them, tracks quality over time, and red-teams them for adversarial vulnerabilities. It supports Microsoft Copilot Studio, Azure AI Foundry, generic HTTP agents, and Parloa out of the box — and is extensible with custom connector, judge, and red-team modules.

Current version: **v0.3.2**  


> Mate is still in an active development phase, so please keep in mind that some parts may not be fully documented yet, or the documentation may still be catching up. You might encounter a few rough edges along the way, so expect a slightly bumpy ride here and there.

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

### Option B — Build from source

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

### Option C — Run without Docker

**Windows (PowerShell)**
```powershell
cd src\Host\mate.WebUI
dotnet run
```

**macOS / Linux**
```bash
cd src/Host/mate.WebUI
dotnet run
```

---

## Project Structure

```
mate.sln
├── src/
│   ├── Core/               — module registry, execution engine, domain entities
│   ├── Host/               — WebUI (Blazor + API), Worker, CLI
│   ├── Infrastructure/     — SQLite/Azure storage implementations
│   └── Modules/            — agent connectors, judge modules, auth, monitoring, red teaming
├── tests/                  — Unit, Integration, EndToEnd
├── infra/local/            — docker-compose.yml, Dockerfiles, .env.template
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
| `mate.Modules.AgentConnector.AIFoundry` | Azure AI Agents SDK | Free |
| `mate.Modules.AgentConnector.Generic` | Generic HTTP POST | Free |
| `mate.Modules.AgentConnector.Parloa` | Parloa Conversation API | Free |

## Judge (Testing) Modules

| Module | Approach | Tier |
|--------|----------|------|
| `mate.Modules.Testing.ModelAsJudge` | LLM 5-dimension scoring | Free |
| `mate.Modules.Testing.RubricsJudge` | Deterministic Contains / NotContains / Regex | Free |
| `mate.Modules.Testing.HybridJudge` | Rubrics gate + LLM blend | Free |
| `mate.Modules.Testing.CopilotStudioJudge` | Citation-aware LLM, Copilot Studio defaults | Free |
| `mate.Modules.Testing.Generic` | Keyword/regex, zero cost | Free |

## Red Teaming Modules

| Module | Approach | Tier |
|--------|----------|------|
| `mate.Modules.RedTeaming.Generic` | Built-in static probe library (10 probes, 8 attack categories), heuristic response evaluation, works offline | **Premium** |
---

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
| `MATE_DB_PROVIDER` | `sqlite` | `sqlite` or `postgres` |
| `AzureAd__TenantId` | — | Required for EntraId auth |
| `AzureAd__ClientId` | — | Required for EntraId auth |
| `AzureAd__ClientSecret` | — | Required for EntraId auth |
| `JudgeSettings__Endpoint` | — | Azure OpenAI endpoint for LLM judge |
| `JudgeSettings__ApiKey` | — | API key for LLM judge |

See `infra/local/.env.template` for the full list.

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
