![version](https://img.shields.io/github/v/release/holgerimbery/mate)
[![License: CC BY-NC 4.0](https://img.shields.io/badge/License-CC%20BY--NC%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc/4.0/)
# **mate** — a multi-agent test environment for AI agents.
<p align="center">
  <img src="src/Host/mate.WebUI/wwwroot/mate-logo.png" width="220" alt="Multi-Agent Assessment & Judgement Logo">
</p>

mate connects to multiple AI agents, runs automated evaluation suites against them, and tracks quality over time. It supports Microsoft Copilot Studio, Azure AI Foundry, generic HTTP agents, and Parloa out of the box — and is extensible with custom connector and judge modules.

Current version: **v0.2.1**  

---

## Features

- **Multi-agent** — connect and evaluate any number of AI agents in a single test suite
- **Pluggable judge modules** — ModelAsJudge (LLM scoring), RubricsJudge (deterministic), HybridJudge, CopilotStudioJudge, or write your own
- **Blazor Server UI** — full-featured web interface for managing agents, test suites, documents, rubrics, and results
- **REST API** — full Minimal API with OpenAPI spec, Scalar explorer, and API key authentication
- **Multi-tenant** — EF Core global query filters isolate all data per tenant
- **Auth-flexible** — runs anonymous in local dev (`None`); Microsoft Entra ID (Azure AD) OIDC in production
- **Docker-first** — single `docker compose up` for the full local stack

---

## Quick Start

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

### Run locally with Docker

```bash
git clone https://github.com/holgerimbery/mate.git
cd mate

cp infra/local/.env.template infra/local/.env
# Edit infra/local/.env if needed — defaults work for local development

cd infra/local
docker compose up --build
```

Open **<http://localhost:5000>**. No login required in the default `None` auth mode.

### Run without Docker

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
│   └── Modules/            — agent connectors, judge modules, auth, monitoring
├── tests/                  — Unit, Integration, EndToEnd
├── infra/local/            — docker-compose.yml, Dockerfiles, .env.template
└── docs/
    ├── wiki/               — full user and developer documentation
    └── concepts/           — architecture blueprints
```

---

## Agent Connector Modules

| Module | Protocol |
|--------|---------|
| `mate.Modules.AgentConnector.CopilotStudio` | Direct Line v3 / Web Channel Security |
| `mate.Modules.AgentConnector.AIFoundry` | Azure AI Agents SDK |
| `mate.Modules.AgentConnector.Generic` | Generic HTTP POST |
| `mate.Modules.AgentConnector.Parloa` | Parloa Conversation API |

## Judge (Testing) Modules

| Module | Approach |
|--------|---------|
| `mate.Modules.Testing.ModelAsJudge` | LLM 5-dimension scoring |
| `mate.Modules.Testing.RubricsJudge` | Deterministic Contains / NotContains / Regex |
| `mate.Modules.Testing.HybridJudge` | Rubrics gate + LLM blend |
| `mate.Modules.Testing.CopilotStudioJudge` | Citation-aware LLM, Copilot Studio defaults |
| `mate.Modules.Testing.Generic` | Keyword/regex, zero cost |

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

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full release history.

## Backlog

See [BACKLOG.md](BACKLOG.md) for planned epics and work items.

---

## License

Private repository — © Holger Imbery. All rights reserved.
