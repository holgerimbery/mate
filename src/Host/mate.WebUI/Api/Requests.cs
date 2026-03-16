// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.WebUI.Api;

// ── Agents ───────────────────────────────────────────────────────────────────

public record CreateAgentRequest(
    string Name,
    string Description,
    string Environment,
    string[] Tags,
    string ConnectorType,
    string ConfigJson,
    Guid? JudgeSettingId);

public record UpdateAgentRequest(
    string Name,
    string Description,
    string Environment,
    string[] Tags,
    bool IsActive,
    Guid? JudgeSettingId);

// ── ConnectorConfig ──────────────────────────────────────────────────────────

public record UpdateConnectorConfigRequest(
    string ConnectorType,
    string ConfigJson,
    bool IsActive);

// ── TestSuites ───────────────────────────────────────────────────────────────

public record CreateTestSuiteRequest(
    string Name,
    string Description,
    double PassThreshold,
    Guid? JudgeSettingId);

public record UpdateTestSuiteRequest(
    string Name,
    string Description,
    double PassThreshold,
    bool IsActive,
    Guid? JudgeSettingId);

// ── TestCases ────────────────────────────────────────────────────────────────

public record CreateTestCaseRequest(
    string Name,
    string Description,
    string[] UserInput,
    string AcceptanceCriteria,
    string? ExpectedIntent,
    string[] ExpectedEntities,
    string? ReferenceAnswer,
    int SortOrder);

public record UpdateTestCaseRequest(
    string Name,
    string Description,
    string[] UserInput,
    string AcceptanceCriteria,
    string? ExpectedIntent,
    string[] ExpectedEntities,
    string? ReferenceAnswer,
    bool IsActive,
    int SortOrder);

// ── Runs ─────────────────────────────────────────────────────────────────────

public record StartRunRequest(
    Guid SuiteId,
    Guid AgentId,
    string RequestedBy,
    IEnumerable<Guid>? TestCaseIds = null);

// ── Results / Human Verdict ──────────────────────────────────────────────────

public record SetHumanVerdictRequest(string Verdict, string? Note, string SetBy);

// ── JudgeSettings ────────────────────────────────────────────────────────────

public record CreateJudgeSettingRequest(
    string Name,
    string ProviderType,
    string? PromptTemplate,
    double TaskSuccessWeight,
    double IntentMatchWeight,
    double FactualityWeight,
    double HelpfulnessWeight,
    double SafetyWeight,
    double PassThreshold,
    bool UseReferenceAnswer,
    string? Model,
    double Temperature,
    double TopP,
    int MaxOutputTokens,
    string? EndpointRef,
    string? ApiKeyRef,
    bool IsDefault);

public record UpdateJudgeSettingRequest(
    string Name,
    string ProviderType,
    string? PromptTemplate,
    double TaskSuccessWeight,
    double IntentMatchWeight,
    double FactualityWeight,
    double HelpfulnessWeight,
    double SafetyWeight,
    double PassThreshold,
    bool UseReferenceAnswer,
    string? Model,
    double Temperature,
    double TopP,
    int MaxOutputTokens,
    string? EndpointRef,
    string? ApiKeyRef,
    bool IsDefault);

// ── ApiKeys ──────────────────────────────────────────────────────────────────

public record CreateApiKeyRequest(string Name, string Role);

// ── Documents ────────────────────────────────────────────────────────────────

public record ImportDocumentFromUrlRequest(string Url, string? FileName);
