# SaaS Architecture Guide — {BrandName}

> **Status:** Active engineering blueprint — February 2026
> **Audience:** Engineering leads / architects
> **Approach:** Fresh codebase on the `SaaS` branch. The algorithms and domain logic from v1.1.0 are ported into the new modular architecture. No v1.x data is migrated. No existing code is lifted verbatim — every file is rewritten to fit the new structure.

---

> **Two names, one codebase.** This document uses two distinct placeholders:
>
> | Placeholder | Where it appears | How to set it |
> |---|---|---|
> | **`{BrandName}`** | UI page titles, the app's `<title>` tag, email subjects, README headings, marketing copy, the document you are reading | Replace with the customer-facing product name. Only UI/content files need touching. |
> | **`mate`** | C# namespaces, project file names, DI extension method names (`AddmateCore()`), the DbContext class name (`mateDbContext`), the API key prefix string, IaC resource name prefixes in `infra/` | Replace with a short, stable, PascalCase code identifier (3–8 chars, e.g. `Acme`, `QaBot`, `CstrTest`). Choose once and keep stable — changing it later requires a project-wide rename. |
>
> **Before starting implementation:** 1) decide both names, 2) project-wide find-and-replace `mate` → `<YourCodePrefix>` across all `.cs`, `.csproj`, `.bicep`, and `docker-compose` files, 3) find-and-replace `{BrandName}` → `<YourBrandName>` in `.razor`, `.md`, and `appsettings.json` display fields only.
>
> The two names are intentionally independent — the product can be rebranded at any time without touching a single line of C#.

---

## 1. Design Principles

1. **Modular by default.** Any capability that has a credible alternative implementation lives behind a typed contract. Contracts are defined in the Domain project. Core orchestrates; modules deliver. No module depends on another module. No module depends on Core.
2. **Multi-tenant from day one.** `TenantId` is present on every data entity from the first commit. EF global query filters enforce row-level isolation automatically. There is no single-tenant code path.
3. **Infrastructure-agnostic.** The cloud provider is a deployment decision, not an architecture decision. Azure, AWS, GCP, and bare-metal (self-hosted Docker / VMs) are equal first-class hosting targets. The entire cloud surface is hidden behind four contracts (`IBlobStorageService`, `ISecretService`, `IMessageQueue`, `IBackupService`). Switching clouds is a DI registration change in the host startup only — no Core or Module code changes.

---

## 2. Solution Structure

```
src/
├── Core/
│   ├── mate.Domain/                  # Entities, contracts, value objects, enums
│   ├── mate.Data/                    # EF Core DbContext, migrations, seed data
│   └── mate.Core/                    # Orchestration services, tenant layer, module registry
│
├── Modules/
│   ├── AgentConnectors/
│   │   ├── mate.Modules.AgentConnector.Generic/        # Developer skeleton + README  ← extend here
│   │   ├── mate.Modules.AgentConnector.CopilotStudio/  # Microsoft Copilot Studio — Direct Line v3
│   │   ├── mate.Modules.AgentConnector.AIFoundry/      # Microsoft AI Foundry — Agent Framework SDK
│   │   └── mate.Modules.AgentConnector.Parloa/         # Parloa conversational AI platform
│   ├── Authentication/
│   │   ├── mate.Modules.Auth.Generic/                  # Developer skeleton + README  ← extend here
│   │   ├── mate.Modules.Auth.EntraId/                  # Microsoft Entra ID — Phase 1 default
│   │   └── mate.Modules.Auth.OAuth/                    # Generic OIDC / OAuth2 — Phase 2 option
│   ├── Testing/
│   │   ├── mate.Modules.Testing.Generic/               # Developer skeleton + README  ← extend here
│   │   ├── mate.Modules.Testing.ModelAsJudge/          # LLM judge + AI question generation
│   │   ├── mate.Modules.Testing.RubricsJudge/          # Deterministic rubrics-based evaluation
│   │   └── mate.Modules.Testing.HybridJudge/           # ModelAsJudge + Rubrics combined
│   └── Monitoring/
│       ├── mate.Modules.Monitoring.Generic/            # Developer skeleton + README  ← extend here
│       ├── mate.Modules.Monitoring.ApplicationInsights/ # Azure Application Insights
│       └── mate.Modules.Monitoring.OpenTelemetry/      # OpenTelemetry — cloud-neutral
│
├── Infrastructure/
│   ├── mate.Infrastructure.Local/   # Phase 1 — filesystem, env-vars, in-process / RabbitMQ
│   └── mate.Infrastructure.Azure/   # Phase 2 — Blob Storage, Key Vault, Service Bus
│
└── Host/
    ├── mate.WebUI/                   # Blazor Server + Minimal API (ASP.NET Core 9)
    ├── mate.Worker/                  # Background worker (.NET Generic Host)
    └── mate.CLI/                     # Command-line interface

tests/
├── mate.Tests.Unit/
├── mate.Tests.Integration/
└── mate.Tests.EndToEnd/

infra/
├── local/   # Docker Compose — multi-container, local infra (Phase 1)
└── azure/       # Bicep — Azure Container Apps + managed services (Phase 2)
```

### Dependency rule

```
Host  →  Core + Modules + Infrastructure
Modules  →  Domain (contracts only)
Infrastructure  →  Domain (contracts only)
Core  →  Domain
Domain  →  (nothing)
```

---

## 3. Core vs Module Boundary

| Always in Core (`mate.Domain` / `mate.Core`) | Always a Module |
|---|---|
| `Tenant`, `TenantSubscription`, `TenantUser` entities | Agent protocol (Copilot Studio / AI Foundry Agent Framework / Parloa / custom) |
| `TestSuite`, `TestCase`, `Run`, `Result`, `TranscriptMessage`, `AuditLog` | Identity provider (Entra ID / OAuth2 / custom) |
| `TestExecutionService` — orchestration loop, multi-agent coordinator | Evaluation strategy (ModelAsJudge / Rubrics / Hybrid / custom) |
| `DocumentIngestor` / `DocumentChunker` pipeline | Question generation backend |
| `AgentConfigurationService`, `ModuleRegistry` | Storage backend (local disk / Azure Blob / S3 / GCS) |
| Quota enforcement (`IQuotaService`), audit logging | Message queue backend (RabbitMQ / in-process / Service Bus / SQS / Pub/Sub) |
| EF DbContext + global tenant query filters | Secret store (env vars / Key Vault / Secrets Manager / Vault) |
| API key management (hash + prefix, `X-Api-Key` middleware) | Monitoring / observability (Application Insights / OpenTelemetry / custom) |
| `IBackupService` contract | Backup implementation (database-engine-specific) |
| `IPowerPlatformDiscoveryService` contract | Power Platform discovery (provider-specific API calls) |
| `Chunk.Embedding` storage (the vector field exists; computation is a module concern) | |

---

## 4. Module Contracts (in `mate.Domain/Contracts/`)

### 4.1 Agent Connector

```csharp
public interface IAgentConnector
{
    string ConnectorType { get; }
    Task<ConversationSession> StartConversationAsync(AgentConnectionConfig config, CancellationToken ct);
    Task<AgentResponse> SendMessageAsync(ConversationSession session, string message, CancellationToken ct);
    Task EndConversationAsync(ConversationSession session, CancellationToken ct);
}

public interface IAgentConnectorModule
{
    string ConnectorType { get; }
    string DisplayName { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    IEnumerable<ConfigFieldDefinition> GetConfigDefinition();  // drives dynamic UI form
    ValidationResult ValidateConfig(string configJson);
}
```

`ConversationSession`: `SessionId`, `ConversationId`, `ConnectorType`, `Metadata` (dictionary — carries Direct Line watermarks or AI Foundry thread IDs between calls).

`AgentResponse`: `Text`, `Attachments`, `RawActivityJson` (nullable — preserves the full Bot Framework Activity for the transcript), `LatencyMs`.

### 4.2 Authentication

```csharp
public interface IAuthModule
{
    string SchemeName { get; }
    string DisplayName { get; }
    bool SupportsDevelopmentBypass { get; }
    void ConfigureAuthentication(AuthenticationBuilder builder, IConfiguration config);
    void ConfigureAuthorization(AuthorizationOptions options, IConfiguration config);
    Task<ClaimsPrincipal> TransformClaimsAsync(ClaimsPrincipal external);
}
```

Internal role claim values: `"Admin"`, `"Tester"`, `"Viewer"`.
Named authorization policies (defined once in Core): `AdminOnly`, `TesterOrAbove`, `AnyAuthenticated`, `PlatformAdmin`.

### 4.3 Testing / Judge

```csharp
public interface IJudgeProvider
{
    string ProviderType { get; }
    Task<JudgeVerdict> EvaluateAsync(JudgeRequest request, CancellationToken ct);
}

public interface IQuestionGenerationProvider
{
    string ProviderType { get; }
    Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(QuestionGenerationRequest request, CancellationToken ct);
}

public interface ITestingModule
{
    string ProviderType { get; }
    string DisplayName { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    IEnumerable<ConfigFieldDefinition> GetJudgeConfigDefinition();
}
```

`JudgeRequest`: `UserInput[]` (multi-turn), `BotResponse`, `AcceptanceCriteria`, `ReferenceAnswer?`, `JudgeSettingSnapshot` (weights, thresholds, model params).

`JudgeVerdict`: `TaskSuccessScore`, `IntentMatchScore`, `FactualityScore`, `HelpfulnessScore`, `SafetyScore`, `OverallScore` (weighted), `Rationale`, `Citations[]`.

`GeneratedQuestion`: `Question`, `ExpectedAnswer`, `ExpectedIntent`, `ExpectedEntities`, `Context`, `Rationale` — identical shape to v1.1.0 `GeneratedQuestion`.

`QuestionGenerationRequest`: `DocumentContent`, `NumberOfQuestions`, `Domain?`, `ExistingQuestions?`, `SystemPromptOverride?` — identical to v1.1.0.

### 4.4 Infrastructure Contracts (in `mate.Domain/Contracts/Infrastructure/`)

```csharp
public interface IBlobStorageService
{
    Task<string> UploadAsync(string container, string blobName, Stream content, string contentType);
    Task<Stream> DownloadAsync(string container, string blobName);
    Task DeleteAsync(string container, string blobName);
}

public interface ISecretService
{
    Task<string> GetSecretAsync(string name);
    Task SetSecretAsync(string name, string value);
    Task DeleteSecretAsync(string name);
}

public interface IMessageQueue
{
    Task EnqueueAsync<T>(string queueName, T message, CancellationToken ct);
    IAsyncEnumerable<QueueMessage<T>> ConsumeAsync<T>(string queueName, CancellationToken ct);
}

public interface IBackupService
{
    Task<(Stream stream, string fileName)> CreateBackupStreamAsync();
    Task RestoreAsync(Stream backupStream);
}
```

### 4.5 Monitoring Contracts (in `mate.Domain/Contracts/Monitoring/`)

```csharp
public interface IMonitoringService
{
    void TrackRunStarted(Guid tenantId, Guid runId, Guid agentId, Guid suiteId);
    void TrackTestCaseResult(Guid tenantId, Guid runId, Guid resultId, bool passed, double overallScore, long latencyMs);
    void TrackRunCompleted(Guid tenantId, Guid runId, int total, int passed, int failed, double passRate);
    void TrackLlmUsage(Guid tenantId, string provider, string model, int promptTokens, int completionTokens);
    void TrackException(Guid tenantId, Exception ex, string? context = null);
    void TrackCustomEvent(Guid tenantId, string eventName, IDictionary<string, string>? properties = null);
}

public interface IMonitoringModule
{
    string ProviderType { get; }
    string DisplayName { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
}
```

The `IMonitoringService` is registered as a singleton. All Core services and module providers call it at key lifecycle points. Multiple monitoring modules can be registered simultaneously (fan-out via a `CompositeMonitoringService` wrapper registered in Core).

---

## 5. Domain Model

### 5.1 Tenancy entities (new in v2)

```csharp
public class Tenant
{
    public Guid Id { get; set; }
    public string ExternalTenantId { get; set; } = string.Empty; // 'tid' JWT claim or OAuth sub
    public string DisplayName { get; set; } = string.Empty;
    public string Plan { get; set; } = "trial";       // 'trial','starter','professional','enterprise'
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class TenantSubscription
{
    public Guid TenantId { get; set; }
    public string Plan { get; set; } = "trial";
    public int MaxAgents { get; set; } = 5;
    public int MaxTestSuites { get; set; } = 20;
    public int MaxTestCasesPerSuite { get; set; } = 100;
    public int MonthlyRunQuota { get; set; } = 500;
    public int MonthlyRunsUsed { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string? ExternalSubscriptionId { get; set; } // Stripe / Azure Marketplace
}
```

### 5.2 TenantId on all data entities

Every entity that owns tenant data carries `public Guid TenantId { get; set; }`:

`Agent`, `AgentConnectorConfig`, `TestSuite`, `TestCase`, `Run`, `Result`, `TranscriptMessage`, `Document`, `Chunk`, `ApiKey`, `AuditLog`, `TenantUser`, `JudgeSetting`, `GlobalQuestionGenerationSetting`.

### 5.3 Agent and AgentConnectorConfig

v1.1.0 embedded all DirectLine configuration as flat columns on `Agent` (`DirectLineBotId`, `DirectLineSecret`, `DirectLineUseWebChannelSecret`, `DirectLineUseWebSocket`, `DirectLineReplyTimeoutSeconds`, `DirectLineMaxRetries`, `DirectLineBackoffSeconds`) and inline judge config (`JudgeEndpoint`, `JudgeApiKey`, `JudgeModel`, `JudgeTemperature`, `JudgeTopP`, `JudgePassThreshold`, `JudgeMaxOutputTokens`) plus optional question-gen overrides (`QuestionGenEndpoint`, `QuestionGenApiKey`, `QuestionGenModel`, `QuestionGenSystemPrompt`).

In v2, connector config is extracted to a separate entity:

```csharp
public class Agent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Environment { get; set; } = "production";   // 'dev','test','staging','production'
    public string[] Tags { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public Guid? JudgeSettingId { get; set; }    // per-agent judge override; null = global
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public ICollection<AgentConnectorConfig> ConnectorConfigs { get; set; } = [];
    public ICollection<TestSuiteAgent> TestSuiteAgents { get; set; } = [];
    public ICollection<Run> Runs { get; set; } = [];
}

public class AgentConnectorConfig
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Guid TenantId { get; set; }
    public string ConnectorType { get; set; } = string.Empty;  // "CopilotStudio" | "AIFoundry" | "Parloa"
    public string ConfigJson { get; set; } = "{}";             // typed per connector; secrets = secret store ref names (cloud-independent)
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
```

### 5.4 JudgeSetting — add ProviderType

```csharp
public class JudgeSetting
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "ModelAsJudge";  // "ModelAsJudge" | "Rubrics" | "Hybrid" — resolves IJudgeProvider
    public string PromptTemplate { get; set; } = string.Empty;
    // Scoring weights — same five dimensions as v1.1.0
    public double TaskSuccessWeight { get; set; } = 0.3;
    public double IntentMatchWeight { get; set; } = 0.2;
    public double FactualityWeight { get; set; } = 0.2;
    public double HelpfulnessWeight { get; set; } = 0.15;
    public double SafetyWeight { get; set; } = 0.15;
    public double PassThreshold { get; set; } = 0.7;
    public bool UseReferenceAnswer { get; set; } = false;
    public string? Model { get; set; }           // null = inherit global
    public double Temperature { get; set; } = 0.2;
    public double TopP { get; set; } = 0.9;
    public int MaxOutputTokens { get; set; } = 800;
    public string? EndpointRef { get; set; }     // secret store ref name; null = global
    public string? ApiKeyRef { get; set; }       // secret store ref name; null = global
    public bool IsDefault { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 5.5 GlobalQuestionGenerationSetting

Identical shape to v1.1.0, plus `TenantId`:

```csharp
public class GlobalQuestionGenerationSetting
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Endpoint { get; set; } = string.Empty;   // secret store ref in production
    public string ApiKey { get; set; } = string.Empty;     // secret store ref in production
    public string Model { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 1.0;
    public int MaxOutputTokens { get; set; } = 1000;
    public string? SystemPrompt { get; set; }              // null = built-in default prompt
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}
```

### 5.6 Chunk — embedding field preserved

```csharp
public class Chunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid TenantId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public int ChunkIndex { get; set; }
    public double? StartChapter { get; set; }
    public double? EndChapter { get; set; }
    public string? Category { get; set; }
    public byte[]? Embedding { get; set; }   // optional semantic search vector
}
```

### 5.7 RubricSet and RubricCriteria (new in v2 — supports Rubrics and Hybrid testing modules)

```csharp
public class RubricSet
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid JudgeSettingId { get; set; }     // FK — links rubric to a JudgeSetting with ProviderType="Rubrics" or "Hybrid"
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool RequireAllMandatory { get; set; } = true;  // if any mandatory criterion fails, overall = fail regardless of score
    public DateTime CreatedAt { get; set; }
    public ICollection<RubricCriteria> Criteria { get; set; } = [];
}

public class RubricCriteria
{
    public Guid Id { get; set; }
    public Guid RubricSetId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;             // e.g. "Mentions return policy"
    public string EvaluationType { get; set; } = "Contains";    // "Contains" | "NotContains" | "Regex" | "Custom"
    public string Pattern { get; set; } = string.Empty;         // text pattern or regex
    public double Weight { get; set; } = 1.0;                   // relative weight in overall rubric score
    public bool IsMandatory { get; set; } = false;              // mandatory failure overrides weighted score
    public int SortOrder { get; set; }
}
```

`RubricSet` and `RubricCriteria` carry `TenantId` and are covered by EF global query filters.

---

## 6. Tenant Layer (in `mate.Core/Tenancy/`)

### 6.1 Interfaces

```csharp
public interface ITenantContext
{
    Guid TenantId { get; }
    string ExternalTenantId { get; }
}
```

Two implementations:
- `HttpContextTenantContext` — reads `HttpContext.Items["TenantId"]` (WebUI)
- `MessageTenantContext` — reads `TenantId` from the deserialized job message (Worker)

### 6.2 TenantResolutionMiddleware

Runs after `UseAuthentication()` / `UseAuthorization()`. Reads the external tenant identifier from the active auth module's claim (configurable; default: `tid` for Entra ID). Looks up the active `Tenant` row. Writes `Guid` to `HttpContext.Items["TenantId"]`. Returns HTTP 403 if the tenant is not found or `IsActive = false`.

### 6.3 EF Global Query Filters

```csharp
public class mateDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    public mateDbContext(DbContextOptions<mateDbContext> options, ITenantContext? tenantContext = null)
        : base(options) => _tenantContext = tenantContext;

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Agent>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<TestSuite>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<Run>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<Result>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<Document>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<Chunk>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<ApiKey>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<AuditLog>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<TenantUser>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<JudgeSetting>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<GlobalQuestionGenerationSetting>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
        mb.Entity<AgentConnectorConfig>().HasQueryFilter(e => _tenantContext == null || e.TenantId == _tenantContext.TenantId);
    }
}
```

The `_tenantContext == null` guard allows migrations, CLI tooling, and platform-admin endpoints to bypass filters.

### 6.4 Quota Enforcement

```csharp
public interface IQuotaService
{
    Task EnforceAgentLimitAsync(Guid tenantId, CancellationToken ct);
    Task EnforceTestSuiteLimitAsync(Guid tenantId, CancellationToken ct);
    Task EnforceRunQuotaAsync(Guid tenantId, CancellationToken ct);
    Task IncrementRunUsageAsync(Guid tenantId, CancellationToken ct);
}
```

All enforce methods throw `QuotaExceededException` (mapped to HTTP 429 in the API layer). `EnforceRunQuotaAsync` is called before enqueuing a `TestRunJob`. `IncrementRunUsageAsync` uses optimistic concurrency to prevent bypass from concurrent requests.

---

## 7. Module Registry (in `mate.Core/Services/`)

```csharp
public class ModuleRegistry
{
    public void Register(IAgentConnectorModule m);
    public void Register(IAuthModule m);
    public void Register(ITestingModule m);
    public void Register(IMonitoringModule m);

    public IAgentConnector ResolveConnector(string connectorType);
    public IJudgeProvider ResolveJudgeProvider(string providerType);
    public IQuestionGenerationProvider ResolveQuestionGenProvider(string providerType);
    public IEnumerable<IAgentConnectorModule> AllConnectors { get; }
    public IEnumerable<IAuthModule> AllAuthModules { get; }
    public IEnumerable<ITestingModule> AllTestingModules { get; }
    public IEnumerable<IMonitoringModule> AllMonitoringModules { get; }
}
```

Host startup wiring via extension methods:

```csharp
builder.Services.AddmateCore(builder.Configuration);

// Authentication — Entra ID is the Phase 1 default
builder.Services.AddmateAuthModule<EntraIdAuthModule>(builder.Configuration);
// OAuth added in Phase 2 / future:
// builder.Services.AddmateAuthModule<OAuthModule>(builder.Configuration);

// Agent connectors — register whichever are needed
builder.Services.AddmateConnectorModule<CopilotStudioConnectorModule>(builder.Configuration);
builder.Services.AddmateConnectorModule<AIFoundryConnectorModule>(builder.Configuration);
builder.Services.AddmateConnectorModule<ParloaConnectorModule>(builder.Configuration);

// Testing / judge modules — register one or more; each suite picks via JudgeSetting.ProviderType
builder.Services.AddmateTestingModule<ModelAsJudgeModule>(builder.Configuration);
builder.Services.AddmateTestingModule<RubricsJudgeModule>(builder.Configuration);
builder.Services.AddmateTestingModule<HybridJudgeModule>(builder.Configuration);

// Monitoring — multiple can be active simultaneously (fan-out)
builder.Services.AddmateMonitoringModule<OpenTelemetryMonitoringModule>(builder.Configuration);
// In Phase 2 on Azure:
// builder.Services.AddmateMonitoringModule<ApplicationInsightsMonitoringModule>(builder.Configuration);

// Infrastructure — Phase 1: local; Phase 2: Azure
builder.Services.AddmateLocalInfrastructure(builder.Configuration);   // Phase 1
// builder.Services.AddmateAzureInfrastructure(builder.Configuration); // Phase 2
```

Each `AddmateXxxModule<T>` call: instantiates the module, calls `module.RegisterServices()`, registers the module in `ModuleRegistry`.

---

## 8. Module Implementations

> Every module type has an empty `Generic` skeleton project. This is the starting point for any third-party or custom module development. The `README.md` in each skeleton project explains the contract, the config shape, and the expected behaviour of every method.

### 8.1 `mate.Modules.AgentConnector.Generic` — developer skeleton

```
GenericAgentConnector.cs       IAgentConnector — NotImplementedException with inline guidance on each method
GenericConnectorModule.cs      IAgentConnectorModule
GenericAgentConfig.cs          typed config POCO — extend with connector-specific fields
README.md                      step-by-step guide: contract, config, retry strategy, transcript requirements
```

### 8.2 `mate.Modules.AgentConnector.CopilotStudio`

Re-implements the algorithm from v1.1.0 `DirectLineClient.cs`:

- HTTP-based Direct Line v3 protocol: start conversation, send activity, poll with watermark, end conversation
- Retry with exponential backoff: `MaxRetries = 2`, `BackoffSeconds = 4` (same defaults as v1.1.0)
- `UseWebSocket` flag — WebSocket polling path implemented
- `UseWebChannelSecret` flag preserved
- Full Bot Framework Activity JSON stored in `ConversationSession.Metadata` per turn → forwarded to `TranscriptMessage.RawActivityJson`

Typed config (`CopilotStudioConnectorConfig`):

| Field | v1.1.0 Agent property |
|---|---|
| `BotId` | `DirectLineBotId` |
| `DirectLineSecretRef` | `DirectLineSecret` (now secret store ref name) |
| `UseWebChannelSecret` | `DirectLineUseWebChannelSecret` |
| `UseWebSocket` | `DirectLineUseWebSocket` |
| `ReplyTimeoutSeconds` | `DirectLineReplyTimeoutSeconds` |
| `MaxRetries` | `DirectLineMaxRetries` |
| `BackoffSeconds` | `DirectLineBackoffSeconds` |

### 8.3 `mate.Modules.AgentConnector.AIFoundry`

Uses the **Microsoft AI Foundry Agent Framework SDK** (`Azure.AI.Projects`):

- Thread-based conversation model: `StartConversationAsync` creates an AI Foundry thread, `SendMessageAsync` creates a run on that thread and polls for completion, `EndConversationAsync` optionally deletes the thread
- Polling interval: `PollingIntervalMs` (default 1000)
- Supports tool invocations — tool call results are captured in `TranscriptMessage.RawActivityJson` for auditability
- `RequiredActions` (function tool calls) are serialised and stored; human review flag set on the result if a tool call is present

Typed config: `ProjectEndpoint`, `ApiKeyRef`, `AgentId`, `PollingIntervalMs`, `ReplyTimeoutSeconds`, `MaxPollingAttempts`.

### 8.4 `mate.Modules.AgentConnector.Parloa`

New in v2 — connects to the **Parloa** conversational AI platform via its REST API:

- `StartConversationAsync`: `POST /v1/sessions` → returns `sessionId`; stored in `ConversationSession.Metadata`
- `SendMessageAsync`: `POST /v1/sessions/{sessionId}/messages` with `{"text": "..."}` body; response text extracted from JSON reply
- `EndConversationAsync`: `DELETE /v1/sessions/{sessionId}` (best-effort; failure does not fail the test)
- Full response JSON stored in `ConversationSession.Metadata` per turn → forwarded to `TranscriptMessage.RawActivityJson`
- Retry logic: same exponential backoff contract as all connectors

Typed config (`ParloaConnectorConfig`):

| Field | Description |
|---|---|
| `ApiBaseUrl` | Parloa API base URL, e.g. `https://api.parloa.com` |
| `ApiKeyRef` | Secret store ref name for the API key |
| `BotId` | Parloa bot identifier |
| `ChannelId` | Parloa channel identifier (e.g. `web`, `voice`) |
| `ReplyTimeoutSeconds` | Default 30 |
| `MaxRetries` | Default 2 |
| `BackoffSeconds` | Default 4 |

### 8.5 `mate.Modules.Auth.Generic` — developer skeleton

```
GenericAuthModule.cs          IAuthModule — NotImplementedException with inline guidance
GenericClaimsTransformer.cs   maps provider claims → internal mate claims
README.md                     contract description, claim mapping rules, how to register
```

### 8.6 `mate.Modules.Auth.EntraId` — Phase 1 default

Wraps `Microsoft.Identity.Web`. Multi-tenant by default (`TenantId = "common"`).

- `DevelopmentAuthHandler` (ported from v1.1.0) — registered **only** when `app.Environment.IsDevelopment() && Authentication:Enabled == false`. Cannot register in non-Development environments.
- `EntraIdClaimsTransformer`: maps `tid` → `ExternalTenantId`, `oid` → `UserId`, app roles → internal `Role` claim
- `OnTokenValidated` hook: resolves `Tenant` from `tid`; calls `ctx.Fail()` if tenant not provisioned
- App registration must have `signInAudience: AzureADMultipleOrgs` and `TenantId: common`

API key middleware (Core, not Entra-specific):
- Reads `X-Api-Key` header
- SHA-256 hashes the value, looks up against `ApiKeys.KeyHash`
- Updates `ApiKey.LastUsedAt`
- Constructs `ClaimsPrincipal` with `Tester` role
- Raw key prefixed `mcskey_` for visual identification
- Key only returned once on creation; only prefix and hash are stored server-side
- Algorithm identical to v1.1.0

### 8.7 `mate.Modules.Auth.OAuth` — future option (Phase 2)

Generic OIDC / OAuth2 via `AddOpenIdConnect`. Configurable `Authority`, `ClientId`, `ClientSecret`, `Scopes`. `OAuthClaimsTransformer` with configurable claim-name mappings for `ExternalTenantId`, `UserId`, `Role`. Activating this module in Phase 2 requires zero changes to Core or any other module.

### 8.8 `mate.Modules.Testing.Generic` — developer skeleton

```
GenericJudgeProvider.cs                IJudgeProvider — NotImplementedException with scoring dimension guidance
GenericQuestionGenerationProvider.cs   IQuestionGenerationProvider
GenericTestingModule.cs
README.md                              scoring contract, verdict shape, question generation contract
```

### 8.9 `mate.Modules.Testing.ModelAsJudge`

Re-implements the algorithms from v1.1.0 `AzureAIFoundryJudgeService.cs` and `AzureOpenAIQuestionGenerationService.cs`.

**Judge (`ModelAsJudgeProvider : IJudgeProvider`):**

- Constructs a structured prompt from `JudgeSetting.PromptTemplate`, user input turns, bot response, acceptance criteria, and optional reference answer (`UseReferenceAnswer` flag)
- LLM backend is resolved via `ILlmClient` — one abstraction, multiple provider implementations:
  - `AzureOpenAILlmClient` — `AzureOpenAIClient` SDK (Azure OpenAI deployments and AI Foundry)
  - `OpenAILlmClient` — direct OpenAI API (GPT-4o, GPT-4.1 etc.)
  - `BedrockLlmClient` — AWS Bedrock (Anthropic Claude, Amazon Nova etc.)
  - `VertexAILlmClient` — GCP Vertex AI (Gemini etc.)
  - `OllamaLlmClient` — local self-hosted models via the Ollama REST API
- Default model params: configurable per `JudgeSetting`; suggested defaults: `gpt-4o-mini`, `Temperature = 0.2`, `TopP = 0.9`, `MaxOutputTokens = 800`
- Five scoring dimensions: TaskSuccess (0.3), IntentMatch (0.2), Factuality (0.2), Helpfulness (0.15), Safety (0.15)
- Weighted overall score computed from `JudgeSetting` weights
- Parses structured JSON response from the LLM

**Question generation (`ModelAsJudgeQuestionGenerationProvider : IQuestionGenerationProvider`):**

- Takes document chunks from `DocumentChunker`, generates per-chunk questions via `ILlmClient`
- Same provider matrix as the judge — question generation and judge can use different providers/models
- System prompt is configurable via `QuestionGenerationRequest.SystemPromptOverride`; built-in default prompt (from v1.1.0) used when null
- Returns `GeneratedQuestion[]` with `Question`, `ExpectedAnswer`, `ExpectedIntent`, `ExpectedEntities`, `Context`, `Rationale`
- Per-agent `SystemPromptOverride` (from `GlobalQuestionGenerationSetting`) respected

**Config resolution** (mirrors v1.1.0 `AgentConfigurationService.GetQuestionGenerationConfigAsync`):

1. Agent-level overrides (`QuestionGenEndpoint`, `QuestionGenApiKey`, `QuestionGenModel`) if set on the agent
2. `GlobalQuestionGenerationSetting` for the tenant
3. Exception if neither is configured

`Endpoint` stores a provider-neutral URL or service identifier. Whether it points to Azure OpenAI, OpenAI, Bedrock, or a local Ollama instance is determined by the `ILlmClient` implementation registered at startup — the stored value is interpreted by the client, not by Core.

### 8.10 `mate.Modules.Testing.RubricsJudge`

**Deterministic rubrics-based evaluation (`RubricsJudgeProvider : IJudgeProvider`):**

- Loads `RubricSet` for the `JudgeSetting` (via `RubricsJudgeConfig.RubricSetId`)
- For each `RubricCriteria` in the set, evaluates the bot response using the specified `EvaluationType`:
  - `Contains` — case-insensitive substring match
  - `NotContains` — absence check
  - `Regex` — `Regex.IsMatch(response, pattern, RegexOptions.IgnoreCase)`
  - `Custom` — delegates to a named `IRubricEvaluator` registered in DI (extensible)
- Computes weighted score: `sum(weight * pass ? 1.0 : 0.0) / sum(weight)`
- If any `IsMandatory = true` criterion fails, `OverallScore = 0` and `Verdict = Fail` regardless of weight
- No LLM calls — entirely deterministic, instant, zero token cost
- `JudgeVerdict.Rationale` lists each criterion name with pass/fail result

This module does **not** implement `IQuestionGenerationProvider`. Question generation remains the responsibility of `ModelAsJudge`.

### 8.11 `mate.Modules.Testing.HybridJudge`

**Combined evaluation (`HybridJudgeProvider : IJudgeProvider`):**

- Runs rubrics evaluation first (fast, zero cost): delegates to the registered `RubricsJudgeProvider`
- If `HybridConfig.SkipLlmOnRubricsFailure = true` and mandatory rubric fails → returns immediately with fail verdict (saves LLM cost)
- Otherwise runs LLM evaluation: delegates to the registered `ModelAsJudgeProvider`
- Combines scores: `OverallScore = (RubricsScore * RubricsWeight) + (LlmScore * LlmWeight)`; weights configured per `JudgeSetting`, must sum to 1.0
- Both sub-verdicts are included in `JudgeVerdict.Rationale` for full traceability

Typed config (`HybridJudgeConfig`): `RubricsWeight` (default 0.4), `LlmWeight` (default 0.6), `SkipLlmOnRubricsFailure` (default true).

### 8.12 `mate.Modules.Monitoring.Generic` — developer skeleton

```
GenericMonitoringService.cs   IMonitoringService — no-op stubs with inline guidance on each method
GenericMonitoringModule.cs    IMonitoringModule
README.md                     events contract, recommended dimensions, flush/dispose pattern
```

### 8.13 `mate.Modules.Monitoring.OpenTelemetry`

**OpenTelemetry — cloud-neutral, Phase 1 default:**

- `OpenTelemetryMonitoringService : IMonitoringService`
- Uses `System.Diagnostics.ActivitySource` for traces and `System.Diagnostics.Metrics.Meter` for metrics
- Traces: one `Activity` per test run; child spans per test case and LLM call
- Metrics (all with `tenant_id` dimension):
  - `test_run.duration` (histogram, ms)
  - `test_case.pass_rate` (gauge)
  - `llm.tokens` (counter, split by `prompt`/`completion`/`provider`/`model`)
  - `agent.latency` (histogram, ms, split by `connector_type`)
- Exporters configured via standard `OTEL_EXPORTER_OTLP_ENDPOINT` env var — works with Jaeger, Grafana Tempo, Azure Monitor, AWS X-Ray, GCP Cloud Trace
- Phase 1: export to local Jaeger container (included in `docker-compose.yml`)
- Phase 2: swap exporter endpoint to Azure Monitor / cloud equivalent via env var — no code change

### 8.14 `mate.Modules.Monitoring.ApplicationInsights`

**Azure Application Insights — Phase 2 (Azure deployment):**

- `ApplicationInsightsMonitoringService : IMonitoringService`
- Uses `Microsoft.ApplicationInsights` SDK (`TelemetryClient`)
- Maps `TrackRunStarted` → custom event `TestRunStarted` with run/agent/suite dimensions
- Maps `TrackTestCaseResult` → custom event `TestCaseResult` + metric `test_case.overall_score`
- Maps `TrackLlmUsage` → custom metric `llm.tokens` with `provider` and `model` properties
- Maps `TrackException` → `TelemetryClient.TrackException()`
- Connection string resolved via `ISecretService` (Key Vault ref in production)
- Can be active alongside `OpenTelemetryMonitoringModule` — fan-out via `CompositeMonitoringService`

---

## 9. Infrastructure Implementations

All four infrastructure contracts have one implementation per hosting target. The secret naming convention is provider-neutral — names follow the same pattern regardless of where secrets are stored:

```
connector-{tenantId}-{agentId}-directline-secret
connector-{tenantId}-{agentId}-aifoundry-apikey
judge-{tenantId}-{agentId}-apikey
questiongen-{tenantId}-{agentId}-apikey
```

### 9.1 `mate.Infrastructure.Azure`

| Contract | Implementation | Notes |
|---|---|---|
| `IBlobStorageService` | `AzureBlobStorageService` | `BlobServiceClient` + `DefaultAzureCredential` (Managed Identity preferred) |
| `ISecretService` | `AzureKeyVaultSecretService` | `SecretClient` + 15-minute in-memory TTL cache |
| `IMessageQueue` | `AzureServiceBusMessageQueue` | `ServiceBusClient`, dead-letter, `maxDeliveryCount = 5`, `lockDuration = PT5M` |
| `IBackupService` | `AzureSqlBackupService` | Azure SQL export via EF or bacpac |

### 9.2 `mate.Infrastructure.Aws`

| Contract | Implementation | Notes |
|---|---|---|
| `IBlobStorageService` | `S3BlobStorageService` | `AmazonS3Client` + IAM role credentials (no static keys) |
| `ISecretService` | `AwsSecretsManagerSecretService` | `AmazonSecretsManagerClient` + 15-minute TTL cache |
| `IMessageQueue` | `SqsMessageQueue` | `AmazonSQSClient`, standard or FIFO queue, visibility timeout = 300s, DLQ after `maxReceiveCount = 5` |
| `IBackupService` | `RdsSnapshotBackupService` | RDS automated snapshot or `pg_dump` / `mysqldump` stream |

### 9.3 `mate.Infrastructure.Gcp`

| Contract | Implementation | Notes |
|---|---|---|
| `IBlobStorageService` | `GcsBlobStorageService` | `StorageClient` + Workload Identity (no service account key files) |
| `ISecretService` | `GcpSecretManagerSecretService` | `SecretManagerServiceClient` + 15-minute TTL cache |
| `IMessageQueue` | `PubSubMessageQueue` | `PublisherClient` / `SubscriberClient`; dead-letter topic after `max_delivery_attempts = 5` |
| `IBackupService` | `CloudSqlBackupService` | Cloud SQL automated backup or `pg_dump` / Cloud SQL export API |

### 9.4 `mate.Infrastructure.Local` (bare-metal / containers)

| Contract | Implementation | Notes |
|---|---|---|
| `IBlobStorageService` | `LocalDiskBlobStorageService` | writes to `./data/uploads/`; same SHA-256 content-hash logic as v1.1.0 |
| `ISecretService` | `EnvironmentVariableSecretService` | reads `env:SECRET_NAME`; `SetSecret`/`DeleteSecret` log a warning and no-op |
| `IMessageQueue` | `InProcessMessageQueue` | `System.Threading.Channels.Channel<T>`; single-instance only |
| `IMessageQueue` | `RabbitMqMessageQueue` | `RabbitMQ.Client`; activated via `Messaging:Provider=RabbitMQ` |
| `IBackupService` | `SqliteBackupService` | `SqliteConnection.BackupDatabase()` — same algorithm as v1.1.0 `BackupService` |

Note: `LocalDiskBlobStorageService` also works for S3-compatible storage (MinIO, Cloudflare R2) if an S3-compatible SDK is used instead of the local filesystem implementation.

---

## 10. Core Services — Ported Algorithms

### 10.1 `TestExecutionService`

Orchestration loop (from v1.1.0 `TestExecutionService.cs`):

1. Resolve `IAgentConnector` from `ModuleRegistry` by `AgentConnectorConfig.ConnectorType`
2. Retrieve connector secrets via `ISecretService`
3. For each active `TestCase` in the suite:
   - `connector.StartConversationAsync()`
   - For each `UserInput` turn: `connector.SendMessageAsync()` — accumulate `AgentResponse` list with per-turn latency
   - `connector.EndConversationAsync()`
   - Build `JudgeRequest` from accumulated conversation and test case definition
   - Resolve `IJudgeProvider` from `ModuleRegistry` by `JudgeSetting.ProviderType`
   - `judgeProvider.EvaluateAsync()` → `JudgeVerdict`
   - Persist `Result` + `TranscriptMessage[]` (including `RawActivityJson` per turn)
4. Compute run statistics: `PassedCount`, `FailedCount`, `SkippedCount`, `AverageLatencyMs`, `MedianLatencyMs`, `P95LatencyMs` — same calculation logic as v1.1.0
5. Set `Run.Status` to `"completed"` or `"failed"`

Concurrency: `ExecutionSettings.MaxConcurrency` (default 5), `RateLimitPerMinute` (default 20) — same defaults as v1.1.0.

### 10.2 `MultiAgentExecutionCoordinator`

Ported from v1.1.0 — iterates the agents assigned to a suite, creates one `Run` per agent, delegates to `TestExecutionService`, returns the list of completed `Run` objects.

### 10.3 `AgentConfigurationService`

Ported from v1.1.0. Provides:
- `GetDirectLineConfig(agent)` — now resolved from `AgentConnectorConfig.ConfigJson` for `ConnectorType = "CopilotStudio"`
- `GetJudgeConfig(agent)` — resolves per-agent `JudgeSetting` or falls back to global default
- `GetQuestionGenerationConfigAsync(agent, db)` — checks agent-level overrides, then `GlobalQuestionGenerationSetting` for the tenant
- `GetOrCreateGlobalQuestionGenSettingAsync(db)` — creates a default global setting if none exists for the tenant

### 10.4 `DocumentIngestor`

Ported from v1.1.0:
- PDF text extraction via `PdfPig`
- DOCX extraction via `DocumentFormat.OpenXml`
- Plain TXT pass-through
- Returns `(string text, int pageCount)`
- File bytes written via `IBlobStorageService.UploadAsync()` instead of `System.IO.File`

### 10.5 `DocumentChunker`

Ported from v1.1.0:
- Splits text into overlapping token-estimated chunks
- Returns `ChunkResult[]` with `Text`, `TokenEstimate`, `Index`
- `ContentHash` (SHA-256 of full text) computed here and stored on `Document`
- `Chunk.Category`, `Chunk.StartChapter`, `Chunk.EndChapter` fields preserved
- `Chunk.Embedding` field present in schema; population is deferred to a future semantic-search module

### 10.6 Audit Logging (`AuditExtensions`)

Updated signature — `tenantId` is now required:

```csharp
public static void AddAudit(
    this mateDbContext db,
    string action,
    string entityType,
    Guid tenantId,
    Guid? entityId = null,
    string? userId = null,
    string? details = null,
    string? oldValue = null,
    string? newValue = null)
```

Every mutating API endpoint and service method writes an audit row, identical coverage to v1.1.0.

### 10.7 `BackupService`

Contract `IBackupService` lives in Core. Active-run guard (`db.Runs.AnyAsync(r => r.Status == "running")`) is enforced in the API layer before calling the service, same as v1.1.0.

### 10.8 `PowerPlatformDiscoveryService`

`IPowerPlatformDiscoveryService` contract in Core. Implementation ported from v1.1.0 — discovers Power Platform environments and bots via the admin API, maps results to `AgentConnectorConfig` suggestions (`ConnectorType = "CopilotStudio"`). Scoped to the signed-in user's Entra ID tenant context.

### 10.9 `TestDataSeeder`

Ported from v1.1.0. Seeds sample agents (with `AgentConnectorConfig` entries instead of flat columns), test suites, and test cases for the seed tenant. Skips seeding if the tenant already has data. Accepts `Guid tenantId` parameter.

---

## 11. REST API (in `mate.WebUI`)

All v1.1.0 endpoints are preserved unchanged (paths, HTTP methods, auth policies, request/response shapes).

### Preserved endpoints

| Method | Path | Auth Policy | v1.1.0 status |
|---|---|---|---|
| `POST` | `/api/test-connection` | TesterOrAbove | Preserved — delegates to CopilotStudio connector |
| `GET` | `/api/testsuites` | AnyAuthenticated | Preserved |
| `GET` | `/api/testsuites/{id}` | AnyAuthenticated | Preserved — includes test cases |
| `POST` | `/api/testsuites` | TesterOrAbove | Preserved |
| `PUT` | `/api/testsuites/{id}` | TesterOrAbove | Preserved |
| `DELETE` | `/api/testsuites/{id}` | AdminOnly | Preserved |
| `GET` | `/api/testsuites/{id}/testcases` | AnyAuthenticated | Preserved |
| `POST` | `/api/testsuites/{id}/testcases` | TesterOrAbove | Preserved |
| `PUT` | `/api/testcases/{id}` | TesterOrAbove | Preserved |
| `DELETE` | `/api/testcases/{id}` | TesterOrAbove | Preserved |
| `GET` | `/api/runs` | AnyAuthenticated | Preserved |
| `GET` | `/api/runs/{id}` | AnyAuthenticated | Preserved |
| `POST` | `/api/runs` | TesterOrAbove | Changed: returns 202 + `runId` (async via Worker) |
| `GET` | `/api/runs/{id}/results` | AnyAuthenticated | Preserved |
| `DELETE` | `/api/runs/{id}` | AdminOnly | Preserved |
| `GET` | `/api/results/{id}/transcript` | AnyAuthenticated | Preserved |
| `POST` | `/api/results/{id}/human-verdict` | TesterOrAbove | Preserved — pass/fail override with note |
| `GET` | `/api/documents` | AnyAuthenticated | Preserved |
| `POST` | `/api/documents` | TesterOrAbove | Preserved — upload, extract, chunk |
| `DELETE` | `/api/documents/{id}` | AdminOnly | Preserved |
| `GET` | `/api/metrics/summary` | AnyAuthenticated | Preserved — totalRuns, passRate, avgLatency, medianLatency |
| `GET` | `/api/agents` | AnyAuthenticated | Preserved |
| `GET` | `/api/agents/{id}` | AnyAuthenticated | Preserved |
| `GET` | `/api/admin/backup` | AdminOnly | Preserved — streams backup via `IBackupService` |
| `POST` | `/api/admin/restore` | AdminOnly | Preserved — restores via `IBackupService`; active-run guard |
| `GET` | `/api/admin/api-keys` | AdminOnly | Preserved |
| `POST` | `/api/admin/api-keys` | AdminOnly | Preserved — raw key returned once, `mcskey_` prefix |
| `DELETE` | `/api/admin/api-keys/{id}` | AdminOnly | Preserved — revoke (sets `IsActive = false`) |

### New endpoints (v2)

| Method | Path | Auth Policy | Notes |
|---|---|---|---|
| `GET` | `/api/runs/{id}/status` | AnyAuthenticated | Polling endpoint for async run jobs |
| `POST` | `/api/agents/{id}/connectors` | TesterOrAbove | Add `AgentConnectorConfig` to agent |
| `PUT` | `/api/agents/{id}/connectors/{cid}` | TesterOrAbove | Update connector config |
| `DELETE` | `/api/agents/{id}/connectors/{cid}` | AdminOnly | |
| `GET` | `/api/billing/usage` | AnyAuthenticated | Current period quota meters |
| `GET` | `/api/admin/tenants` | PlatformAdmin | Platform-operator tenant list |
| `POST` | `/api/admin/tenants/{id}/activate` | PlatformAdmin | Provision / reactivate tenant |

### OpenAPI + Scalar UI

Preserved from v1.1.0:
- `/openapi/v1.json` — public, documentation only
- Scalar interactive UI at `/scalar` — gated to `AdminOnly`
- `ApiKey` + `Bearer` security schemes documented
- All endpoints tagged and summarized

### Health check

`GET /health` — anonymous, preserved from v1.1.0.

---

## 12. Blazor WebUI Pages

All 16 pages from v1.1.0 are ported. UX behaviour and navigation structure are preserved.

| Page | Route | v1.1.0 file | Notes |
|---|---|---|---|
| Home | `/` | `Home.razor` | Redirects to Dashboard or Welcome |
| Welcome | `/welcome` | `WelcomePage.razor` | First-run landing |
| Dashboard | `/dashboard` | `DashboardPage.razor` | Pass/fail metrics, latest runs |
| Agents | `/agents` | `AgentsPage.razor` | CRUD + connector config UI (new multi-config support) |
| Test Suites | `/testsuites` | `TestSuitesPage.razor` | CRUD, agent assignment, run trigger |
| Test Run Report | `/runs/{id}` | `TestRunReportPage.razor` | Per-result breakdown, transcript viewer, human verdict override |
| Documents | `/documents` | `DocumentsPage.razor` | Upload, chunk preview, question generation trigger |
| Judge Rubrics | `/judge-rubrics` | `JudgeRubricsPage.razor` | Create/edit `JudgeSetting` records + `ProviderType` selector |
| Settings | `/settings` | `SettingsPage.razor` | Global judge + question-gen settings (`GlobalQuestionGenerationSetting`) |
| API Keys | `/api-keys` | `ApiKeysPage.razor` | Create/revoke API keys — admin only |
| Audit Log | `/audit` | `AuditLogPage.razor` | Filterable event log |
| Environment Discovery | `/discovery` | `EnvironmentDiscoveryPage.razor` | Power Platform environment + bot discovery |
| Setup Wizard | `/setup` | `SetupWizard.razor` | Guided first-run setup — adapted for tenant onboarding |
| Help | `/help` | `Help.razor` | Documentation links, keyboard shortcuts |
| Login | `/login` | `Login.razor` | Auth flow |
| Access Denied | `/access-denied` | `AccessDenied.razor` | |

New pages (v2 multi-tenant SaaS):

| Page | Route | Notes |
|---|---|---|
| Register | `/register` | Company name, plan; Entra tenant auto-detected from JWT |
| Onboarding | `/onboarding` | Re-uses Setup Wizard flow post-registration |
| Billing | `/billing` | Current plan, quota meters, upgrade/downgrade |
| Admin — Tenants | `/admin/tenants` | Platform-operator view; requires `PlatformAdmin` claim |

---

## 13. CLI (`mate.CLI`)

All five v1.1.0 commands preserved. Command names and flag signatures unchanged.

| Command | v1.1.0 behaviour | v2 notes |
|---|---|---|
| `run --suite <name\|id>` | Execute test suite synchronously, write results | Enqueues `TestRunJob`, polls `GET /api/runs/{id}/status` until complete |
| `list` | List test suites | Unchanged |
| `agents` | List configured agents with connector info | Shows `ConnectorType` per agent |
| `report --run <id> --format json\|csv` | Export run results to file | JSON and CSV formats preserved |
| `generate --document <path> --suite <name\|id> --count <n>` | Generate test questions from document | Delegates to `IQuestionGenerationProvider` via module |

New flags:
- `--tenant <id>` — required in multi-tenant deployments; may be omitted if `CLI:DefaultTenantId` is configured
- `--connector <type>` — override connector type for `run` (default: first active `AgentConnectorConfig` on the agent)

---

## 14. Background Execution (`mate.Worker`)

### TestRunJob message contract

```csharp
public record TestRunJob(
    Guid JobId,
    Guid TenantId,
    Guid RunId,
    Guid SuiteId,
    Guid AgentId,
    string RequestedBy,
    IEnumerable<Guid>? TestCaseIds,  // null = all active cases in suite
    int DelayBetweenTestsMs          // default: 2000
);
```

### Worker startup

```csharp
Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddmateCore(config);
        services.AddmateConnectorModule<CopilotStudioConnectorModule>(config);
        services.AddmateConnectorModule<AIFoundryConnectorModule>(config);
        services.AddmateTestingModule<ModelAsJudgeModule>(config);
        services.AddmateAzureInfrastructure(config);  // or Aws / Gcp / Local
        services.AddHostedService<TestRunWorker>();
    });
```

### TestRunWorker

```csharp
public class TestRunWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in _queue.ConsumeAsync<TestRunJob>("test-run-jobs", stoppingToken))
        {
            using var scope = _services.CreateScope();
            // bind MessageTenantContext(msg.Payload.TenantId) in scope's DI
            var svc = scope.ServiceProvider.GetRequiredService<ITestExecutionService>();
            await svc.ExecuteRunAsync(msg.Payload, stoppingToken);
            await msg.CompleteAsync();
        }
    }
}
```

Job failures go to dead-letter after `maxDeliveryCount` (Azure Service Bus) or are NACK'd (RabbitMQ). `Run.Status` is set to `"failed"` on unhandled exception so polling returns a terminal state.

---

## 15. Test Coverage

### v1.1.0 test structure to preserve and expand

| Test file | Type | Coverage | v2 status |
|---|---|---|---|
| `AgentConfigurationServiceTests.cs` | Unit | `GetDirectLineConfig`, `GetJudgeConfig`, `GetQuestionGenerationConfigAsync` with agent overrides and global fallback | Reimplement for new config shape (`AgentConnectorConfig`) |
| `MultiAgentDatabaseIntegrationTests.cs` | Integration | EF relationships — agents, suites, runs; in-memory DB | Reimplement; add `TenantId` assertions |
| `MultiAgentEndToEndTests.cs` | End-to-end | Create agent + suite → run → verify result stored | Reimplement; add tenant isolation assertions |

### Additional tests required for v2

| Test | Type | Purpose |
|---|---|---|
| Cross-tenant data isolation | Integration | Assert that a query with `TenantId = A` never returns rows owned by `TenantId = B`; run on every build |
| `QuotaExceededException` on run limit | Unit | Assert `IQuotaService.EnforceRunQuotaAsync` throws at `MonthlyRunsUsed == MonthlyRunQuota` |
| API key auth middleware | Integration | Assert `X-Api-Key` lookups return correct role; revoked keys return 401 |
| `DevelopmentAuthHandler` environment gate | Unit | Assert handler cannot register outside `Development` environment |
| Module resolution by `ConnectorType` | Unit | Assert `ModuleRegistry.ResolveConnector("CopilotStudio")` returns correct implementation |
| `TestExecutionService` with mock connector | Unit | Assert `Run` statistics (pass count, latency p95) computed correctly |
| Backup/restore round-trip | Integration | Assert restore produces identical data |
| Tenant resolution middleware | Integration | Assert 403 for unknown tenant; assert `TenantId` written to `HttpContext.Items` |

---

## 16. Build Sequence

Delivery is split into two phases. **Phase 1** produces a fully functional system deployable on bare-metal/Docker Compose using `mate.Infrastructure.Local`. Each step in Phase 1 is coded and manually verified without any cloud account. **Phase 2** adds Azure hosting, premium connectors, and cloud monitoring.

### Phase 1 — Bare-metal + Multi-container (Infrastructure.Local)

All steps below use SQLite (or PostgreSQL via Docker Compose), RabbitMQ (or in-process queue), and the local filesystem. No Azure / cloud SDK is referenced in Phase 1 code.

| P1 Step | Deliverable | Key tasks |
|---|---|---|
| 1 | `mate.Domain` | All entities with `TenantId`; `RubricSet`, `RubricCriteria`; all module + infrastructure contracts; `TestRunJob` record |
| 2 | `mate.Data` | `mateDbContext` with global query filters; EF migrations (SQLite + PostgreSQL); RubricSet/Criteria migrations; `TestDataSeeder` |
| 3 | `mate.Core` — tenant layer | `ITenantContext` (both implementations); `TenantResolutionMiddleware`; `IQuotaService` contract; `ModuleRegistry` (all four `Register()` overloads); cross-tenant isolation tests |
| 4 | `mate.Core` — services | `TestExecutionService`, `MultiAgentExecutionCoordinator`, `DocumentIngestor`, `DocumentChunker`, `AgentConfigurationService`, `AuditExtensions`, `IBackupService` + `IPowerPlatformDiscoveryService` contracts |
| 5 | `mate.Infrastructure.Local` | SQLite backup, local filesystem, in-process queue, env-var secrets; Docker Compose multi-container stack (`docker-compose.yml`) with RabbitMQ and local Jaeger |
| 6 | `mate.Modules.Auth.EntraId` | Entra ID auth module; dev bypass handler; `OnTokenValidated` tenant hook; API key middleware |
| 7 | `mate.Modules.AgentConnector.CopilotStudio` | Direct Line v3 connector; connection-test endpoint |
| 8 | `mate.Modules.Testing.ModelAsJudge` | LLM judge + question generation |
| 9 | `mate.Modules.Testing.RubricsJudge` | Rubrics-based deterministic judge; `RubricSet` CRUD in WebUI |
| 10 | `mate.Modules.Testing.HybridJudge` | Combined ModelAsJudge + Rubrics; configurable weights |
| 11 | `mate.Modules.Monitoring.Generic` | No-op monitoring skeleton; wired into `CompositeMonitoringService` |
| 12 | `mate.Modules.Monitoring.OpenTelemetry` | OTel traces + metrics; Phase 1 target: local Jaeger in compose stack |
| 13 | `mate.WebUI` | All ported pages + rubric management pages + full REST API surface; OpenAPI + Scalar UI |
| 14 | `mate.CLI` | All commands |
| 15 | `mate.Worker` | Background worker host; `TestRunWorker` |
| 16 | Generic skeleton modules (×4) | `AgentConnector.Generic`, `Auth.Generic`, `Testing.Generic`, `Monitoring.Generic`; each with `README.md` |
| 17 | IaC — Phase 1 bare-metal | `infra/local/` Docker Compose variants (SQLite single-node, PostgreSQL+RabbitMQ multi-container) |

At the end of Phase 1 the system is fully operational on any machine with Docker installed.

### Phase 2 — Azure + Container Apps (Infrastructure.Azure)

Phase 2 adds cloud hosting and optional premium connectors. Container images are **identical** to Phase 1 — only startup DI registrations and environment variables change.

| P2 Step | Deliverable | Key tasks |
|---|---|---|
| 18 | `mate.Infrastructure.Azure` | Azure Blob, Key Vault, Service Bus, Azure SQL implementations; swap into startup via `AddmateAzureInfrastructure()` |
| 19 | `mate.Modules.AgentConnector.AIFoundry` | AI Foundry / MS Agent Framework connector |
| 20 | `mate.Modules.AgentConnector.Parloa` | Parloa REST API connector |
| 21 | `mate.Modules.Auth.OAuth` | Generic OIDC / OAuth2 module (future auth option) |
| 22 | `mate.Modules.Monitoring.ApplicationInsights` | Azure Application Insights module; works alongside OpenTelemetry module |
| 23 | Billing / quota enforcement | `TenantSubscription`, `IQuotaService` implementation, billing UI pages |
| 24 | IaC — Phase 2 Azure | `infra/azure/` Bicep modules; Container Apps, Azure SQL, Key Vault, Service Bus, Application Insights |

Phase 2 steps are independent; any step can be activated by registering the corresponding module in startup — no Core or Domain changes required.

---

## 17. Functionality Parity Checklist

Use this table to confirm every v1.1.0 capability has an explicit home in v2 before closing each build step.

| Feature | v1.1.0 location | v2 location | Build step |
|---|---|---|---|
| Direct Line conversation (start / poll / end) | `Core/DirectLine/DirectLineClient.cs` | `mate.Modules.AgentConnector.CopilotStudio` | 7 |
| Multi-turn execution loop + transcript recording | `Core/Execution/TestExecutionService.cs` | `mate.Core` — `TestExecutionService` | 4 |
| Multi-agent coordinator | `Core/Execution/MultiAgentExecutionCoordinator.cs` | `mate.Core` — `MultiAgentExecutionCoordinator` | 4 |
| LLM judge — 5-dimension scoring | `Core/Evaluation/JudgeService.cs` | `mate.Modules.Testing.ModelAsJudge` | 8 |
| AI question generation from chunks | `Core/Services/QuestionGenerationService.cs` | `mate.Modules.Testing.ModelAsJudge` | 8 |
| Question generation config resolution (agent → global) | `Core/Services/AgentConfigurationService.cs` | `mate.Core` — `AgentConfigurationService` | 4 |
| `GlobalQuestionGenerationSetting` CRUD | `WebUI/Program.cs` + DB entity | `mate.Core` + `mate.WebUI` | 4 / 9 |
| Per-agent judge config override | `Agent.JudgeEndpoint/ApiKey/Model/…` | `JudgeSetting` with `TenantId`; FK on `Agent` | 1 / 4 |
| PDF / DOCX / TXT text extraction | `Core/DocumentProcessing/DocumentIngestor.cs` | `mate.Core` — `DocumentIngestor` | 4 |
| Text chunking + token estimate | `Core/DocumentProcessing/DocumentChunker.cs` | `mate.Core` — `DocumentChunker` | 4 |
| `Chunk.Embedding` field | `Domain/Entities/Chunk.cs` | `mate.Domain` — `Chunk` (population deferred) | 1 |
| `Chunk.Category`, `StartChapter`, `EndChapter` | `Domain/Entities/Chunk.cs` | `mate.Domain` — `Chunk` | 1 |
| Agent CRUD | `Core/Services/AgentConfigurationService.cs` | `mate.Core` + `mate.WebUI` | 4 / 9 |
| Power Platform discovery | `WebUI/Services/PowerPlatformDiscoveryService.cs` | `mate.Core` contract + `mate.WebUI` implementation | 4 / 9 |
| Database backup / restore | `WebUI/Services/BackupService.cs` | `IBackupService` contract; `SqliteBackupService` (Local) / `AzureSqlBackupService` (Azure) | 4 / 5 / 12 |
| Active-run guard on backup/restore | `WebUI/Program.cs` | `mate.WebUI` — API layer | 9 |
| Entra ID / OIDC authentication | `WebUI/Program.cs` + `DevelopmentAuthHandler.cs` | `mate.Modules.Auth.EntraId` | 6 |
| Development auth bypass | `WebUI/Authentication/DevelopmentAuthHandler.cs` | `mate.Modules.Auth.EntraId` — environment-gated | 6 |
| API key auth (`X-Api-Key`) | `WebUI/Program.cs` inline middleware | `mate.Core` — middleware | 6 |
| API key CRUD (create / revoke / list) | `WebUI/Program.cs` | `mate.WebUI` + `mate.Core` | 9 |
| Role-based authorization (Admin / Tester / Viewer) | `WebUI/Program.cs` inline policies | `mate.Core` — built-in policies | 3 |
| Human verdict override (pass/fail/clear + note) | `WebUI/Program.cs` — `SetHumanVerdict` | `mate.WebUI` — API endpoint | 9 |
| `Result.HumanVerdict`, `HumanVerdictNote`, `HumanVerdictAt` | `Domain/Entities/Result.cs` | `mate.Domain` — `Result` | 1 |
| Audit logging on all mutations | `Data/AuditExtensions.cs` | `mate.Data` — `AuditExtensions` (tenantId required) | 2 |
| Run statistics (pass, fail, skip, avg/median/p95 latency) | `Core/Execution/TestExecutionService.cs` | `mate.Core` — `TestExecutionService` | 4 |
| `JudgeSetting` / rubric management | Domain + `JudgeRubricsPage.razor` | `mate.Domain` + `mate.WebUI` | 1 / 9 |
| `TestSuiteAgent` many-to-many | `Domain/Entities/TestSuiteAgent.cs` | `mate.Domain` — `TestSuiteAgent` | 1 |
| `TestCase.UserInput[]` multi-turn | `Domain/Entities/TestCase.cs` | `mate.Domain` — `TestCase` | 1 |
| `TestCase.SourceDocumentId` via `TestCaseDocument` | `Domain/Entities/TestCase.cs` | `mate.Domain` | 1 |
| `TestSuite.PassThreshold` override | `Domain/Entities/TestSuite.cs` | `mate.Domain` — `TestSuite` | 1 |
| `TestSuite.JudgeSettingId` per-suite override | `Domain/Entities/TestSuite.cs` | `mate.Domain` — `TestSuite` | 1 |
| Test data seeder | `Core/Execution/TestDataSeeder.cs` | `mate.Data` — `TestDataSeeder` (tenantId param) | 2 |
| OpenAPI spec (`/openapi/v1.json`) | `WebUI/Program.cs` | `mate.WebUI` | 9 |
| Scalar interactive API UI | `WebUI/Program.cs` | `mate.WebUI` — admin-gated | 9 |
| Health check (`/health`) | `WebUI/Program.cs` | `mate.WebUI` | 9 |
| Serilog structured logging | `WebUI/Program.cs` | `mate.WebUI` + `mate.Worker` — add AppInsights sink in Azure | 9 / 12 |
| All 16 Blazor pages | `WebUI/Components/Pages/` | `mate.WebUI/Components/Pages/` | 9 |
| CLI `run`, `list`, `agents`, `report`, `generate` | `CLI/Program.cs` | `mate.CLI` | 10 |
| Unit tests — `AgentConfigurationService` | `Tests/Unit/` | `mate.Tests.Unit` | 4 |
| Integration tests — multi-agent DB | `Tests/Integration/` | `mate.Tests.Integration` | 2 |
| End-to-end tests — create agent + run | `Tests/EndToEnd/` | `mate.Tests.EndToEnd` | 9 |
| **New: tenant isolation** | — | `mate.Core` + `mate.Data` | P1-3 |
| **New: background Worker** | — | `mate.Worker` | P1-15 |
| **New: AIFoundry (MS Agent Framework) connector** | — | `mate.Modules.AgentConnector.AIFoundry` | P2-19 |
| **New: Parloa connector** | — | `mate.Modules.AgentConnector.Parloa` | P2-20 |
| **New: OAuth auth module (future)** | — | `mate.Modules.Auth.OAuth` | P2-21 |
| **New: four Generic skeleton modules** | — | `*.Generic` projects (connector, auth, testing, monitoring) | P1-16 |
| **New: `RubricSet` + `RubricCriteria` entities** | — | `mate.Domain` | P1-1 |
| **New: Rubrics-based judge** | — | `mate.Modules.Testing.RubricsJudge` | P1-9 |
| **New: Hybrid judge (Rubrics + ModelAsJudge)** | — | `mate.Modules.Testing.HybridJudge` | P1-10 |
| **New: monitoring contract + fan-out** | — | `IMonitoringService`, `IMonitoringModule`, `CompositeMonitoringService` | P1-3 |
| **New: OpenTelemetry monitoring module** | — | `mate.Modules.Monitoring.OpenTelemetry` | P1-12 |
| **New: Application Insights monitoring module** | — | `mate.Modules.Monitoring.ApplicationInsights` | P2-22 |
| **New: Azure infrastructure** | — | `mate.Infrastructure.Azure` | P2-18 |
| **New: billing / quota enforcement** | — | `mate.Core` + `mate.WebUI` | P2-23 |
| **New: cross-tenant isolation tests** | — | `mate.Tests.Integration` | P1-3 |

---

## 18. Hosting

Containers are the universal unit of deployment across all targets. All four hosts (WebUI, Worker, and any sidecars) are OCI-compliant container images. The infrastructure module determines **where** they run and **which backing services** they use — the container image never changes between phases.

Deployment follows the same two-phase progression as the build sequence:

1. **Phase 1 — Bare-metal / Docker Compose** (`mate.Infrastructure.Local`) — zero cloud dependency
2. **Phase 2 — Azure Container Apps** (`mate.Infrastructure.Azure`) — swap startup registrations + env vars

### Phase 1 — Bare-metal / self-hosted (Docker Compose)

```
infra/local/
├── docker-compose.yml               WebUI + Worker + PostgreSQL + RabbitMQ + Jaeger
├── docker-compose.sqlite.yml        Single-node: WebUI + Worker, SQLite + in-process queue
└── nginx/nginx.conf                 TLS termination reverse proxy
```

Env vars that control infrastructure selection:

| Variable | `docker-compose.sqlite.yml` | `docker-compose.yml` |
|---|---|---|
| `Messaging__Provider` | `InProcess` | `RabbitMQ` |
| `Storage__Provider` | `LocalDisk` | `LocalDisk` |
| `Secrets__Provider` | `EnvironmentVariables` | `EnvironmentVariables` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | _(unset — no spans)_ | `http://jaeger:4317` |

- No Azure SDK packages are referenced in Phase 1 container images
- PostgreSQL variant: add `DATABASE_URL` pointing at the compose PostgreSQL service
- MinIO can replace local disk if S3-compatible object storage is needed on bare-metal: set `Storage__Provider=MinIO` and `Storage__MinioEndpoint`
- Monitoring: `mate.Modules.Monitoring.OpenTelemetry` exports to local Jaeger (included in `docker-compose.yml`)

### Phase 2 — Azure Container Apps

```
infra/azure/
├── main.bicep
└── modules/
    ├── containerApps.bicep     WebUI (external ingress) + Worker (scale-to-zero, KEDA Service Bus scaler)
    ├── sql.bicep               Azure SQL General Purpose Serverless
    ├── storage.bicep           Blob Storage + containers (uploads, reports)
    ├── keyvault.bicep          Key Vault + Managed Identity access policies
    ├── serviceBus.bicep        Standard tier, test-run-jobs queue (maxDeliveryCount=5, lockDuration=PT5M)
    ├── appInsights.bicep       Application Insights + Log Analytics workspace
    └── diagnostics.bicep       Container Apps diagnostic settings → Log Analytics
```

- WebUI: `minReplicas: 1`, `maxReplicas: 10`, external ingress, port 8080
- Worker: `minReplicas: 0`, `maxReplicas: 5`, no ingress — scale-to-zero reduces cost
- Auth: Managed Identity for all Azure SDK calls — no stored credentials
- Swapping from Phase 1: change `AddmateLocalInfrastructure()` → `AddmateAzureInfrastructure()` in startup; update env vars pointing at Azure resources
- Monitoring: both `OpenTelemetry` (exporting to Azure Monitor) and `ApplicationInsights` modules can run simultaneously via `CompositeMonitoringService`
- IaC: Bicep; deployable via `az deployment group create` or GitHub Actions

### Other cloud targets (reference)

These are not primary delivery targets but the architecture supports them. Any team can add a new `mate.Infrastructure.*` implementation and register it in startup.

**AWS (ECS Fargate / EKS):** S3 + Secrets Manager + SQS + RDS PostgreSQL; IaC via AWS CDK (TypeScript).

**GCP (Cloud Run):** GCS + Secret Manager + Pub/Sub + Cloud SQL PostgreSQL; IaC via Terraform.

**Kubernetes (cloud-neutral Kustomize overlays):** `infra/k8s/` with base manifests + overlays for `azure-aks`, `aws-eks`, `gcp-gke`, and `baremetal`; KEDA ScaledJob for Worker.

---

## 19. Infrastructure Cost Estimation

All prices are indicative list prices, February 2026. Actual costs depend on region, commitment discounts, and data egress. Use the provider calculators for accurate sizing: [Azure](https://azure.microsoft.com/en-us/pricing/calculator/), [AWS](https://calculator.aws/), [GCP](https://cloud.google.com/products/calculator).

### Azure

| Service | Small (5–10 tenants) | Medium (50–100 tenants) | Large (500+ tenants) |
|---|---|---|---|
| Azure SQL Serverless GP | €25–60 | €80–200 | €350–900 |
| Container Apps (WebUI + Worker) | €15–40 | €60–180 | €400–1,200 |
| Blob Storage | €2–5 | €10–30 | €80–200 |
| Key Vault | €1–3 | €5–15 | €40–100 |
| Service Bus Standard | €8–10 | €8–12 | €12–30 |
| App Insights / Log Analytics | €3–8 | €20–50 | €200–500 |
| AI Search *(optional)* | €65 | €220 | €880 |
| **Total (without AI Search)** | **€54–126** | **€183–487** | **€1,082–2,930** |

### AWS

| Service | Small | Medium | Large |
|---|---|---|---|
| RDS PostgreSQL (db.t4g.micro–small) / Aurora Serverless | $30–80 | $100–300 | $500–1,500 |
| ECS Fargate (WebUI + Worker) | $20–50 | $80–250 | $500–1,500 |
| S3 | $2–5 | $8–25 | $60–180 |
| Secrets Manager | $1–3 | $5–15 | $40–100 |
| SQS Standard | $0–1 | $1–5 | $5–20 |
| CloudWatch Logs + Container Insights | $5–15 | $30–80 | $250–600 |
| **Total** | **$58–154** | **$224–675** | **$1,355–3,900** |

### GCP

| Service | Small | Medium | Large |
|---|---|---|---|
| Cloud SQL PostgreSQL (shared core–standard) / AlloyDB | $25–70 | $80–250 | $400–1,200 |
| Cloud Run (WebUI + Worker) | $10–30 | $50–180 | $350–1,100 |
| GCS | $2–5 | $8–25 | $55–160 |
| Secret Manager | $0–1 | $1–5 | $10–40 |
| Pub/Sub | $0–2 | $2–8 | $10–40 |
| Cloud Logging + Monitoring | $4–10 | $20–60 | $180–450 |
| **Total** | **$41–118** | **$161–528** | **$1,005–2,990** |

### Bare-metal / self-hosted

No cloud spend. Costs are VM / bare-metal server rental + operator time. A minimal two-server setup (app + DB) on a VPS provider (Hetzner, OVH, etc.) runs €15–60/month for small deployments.

### LLM consumption (all clouds — not infrastructure)

| Provider + Model | Cost per 1,000 test cases judged |
|---|---|
| Azure OpenAI `gpt-4o-mini` | ~€0.12 |
| Azure OpenAI `gpt-4o` | ~€2.50 |
| OpenAI `gpt-4o-mini` | ~$0.13 |
| AWS Bedrock `claude-3-5-haiku` | ~$0.09 |
| AWS Bedrock `claude-3-7-sonnet` | ~$1.80 |
| GCP Vertex AI `gemini-2.0-flash` | ~$0.04 |
| GCP Vertex AI `gemini-2.0-pro` | ~$1.50 |
| Self-hosted Ollama (`llama3.3-70b`) | GPU server cost only |

> Start with SQL full-text search for document retrieval. Add a vector search service (Azure AI Search / OpenSearch / Vertex AI Search / pgvector) only if semantic search becomes a requirement.

---

## 20. Key Technical Decisions

| Decision | Options | Recommendation |
|---|---|---|
| **Cloud provider** | Azure / AWS / GCP / bare-metal / mixed | No recommendation — all are first-class. Choose based on existing organisational agreements, team familiarity, and regional data-residency requirements. The contracts make it reversible. |
| Multi-tenancy model | Shared DB vs. per-tenant DB | **Shared DB** — lower cost at startup; per-tenant only if enterprise compliance requires DB-level isolation |
| Full-text search | Cloud AI Search (Azure AI Search / OpenSearch / Vertex AI Search) vs. SQL FTS vs. `pg_trgm` + pgvector | **SQL FTS / pgvector first**; add managed vector search only when query quality requires it |
| LLM provider for judge | Azure OpenAI / OpenAI / AWS Bedrock / GCP Vertex AI / Ollama | Start with whichever the tenant is already paying for. `ILlmClient` makes switching a registration change. |
| Billing provider | Stripe vs. cloud marketplace vs. manual | **Manual MVP → Stripe**; cloud marketplace (Azure / AWS / GCP) only if targeting enterprise procurement |
| Worker isolation | In-process vs. separate container | **Separate container** — independent scaling, isolated failure domain, works on all targets |
| Auth scope | Entra ID vs. OAuth2 / OIDC vs. custom | Provide the module matching the customer's identity provider. Entra ID for Microsoft-centric customers; OAuth2 module for everyone else. |
| Secret storage | Cloud secret manager vs. env vars vs. Vault | Cloud secret manager on any managed cloud; env vars on bare-metal / local; HashiCorp Vault if multi-cloud secret centralisation is needed — all implement `ISecretService` |
| Background queue | Cloud queue (Service Bus / SQS / Pub/Sub) vs. RabbitMQ vs. in-process | Cloud-managed queue on cloud deployments; RabbitMQ on multi-instance bare-metal; in-process for single-node development |
| Container orchestration | Container Apps / ECS Fargate / Cloud Run / Kubernetes / Docker Compose | Prefer managed serverless containers (Container Apps / Fargate / Cloud Run) to avoid cluster operations overhead. Use Kubernetes only if you already operate one. |

---

## 21. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Global query filter bypass → tenant data leak | Low | Critical | Automated integration tests asserting cross-tenant isolation on every build (Step 3) |
| Module contract changes break multiple modules | Medium | High | Contracts are versioned; breaking changes require a Domain major version bump |
| Cloud vendor lock-in | Low | High | All cloud dependencies are behind the four infrastructure contracts. Switching clouds requires implementing one `IInfrastructure` package — Core, Modules, and Host code do not change. Lock-in is intentionally minimised. |
| AI SDK instability (AI Foundry / Bedrock / Vertex AI) | Medium | Medium | Version-pin SDKs; integration tests use recorded/mocked HTTP responses |
| LLM API regional availability | Medium | Medium | `ILlmClient` maps to `Endpoint` — if a model is unavailable in a region, point to another endpoint or provider. No code change required. |
| Run quota bypass via concurrent requests | Medium | Medium | Atomic increment with optimistic concurrency on `TenantSubscription.MonthlyRunsUsed` |
| Secret store cold-start latency | Low | Low | 15-minute in-memory TTL cache on all `ISecretService` implementations |
| Bare-metal in-process queue loses jobs on restart | High | Low | Documented clearly; RabbitMQ recommended for any production bare-metal deployment |
| Data residency / sovereignty compliance | Medium | High | `TenantId` is the only isolation mechanism — if data must be in a specific region or cloud, deploy a dedicated regional instance; the codebase is identical |

---

*Last updated: February 2026 — v2.0.0 fresh-start blueprint — cloud-neutral — set `mate` (stable code identifier, used in namespaces and project names) and `{BrandName}` (customer-facing product name, used in UI and docs) before first commit*
