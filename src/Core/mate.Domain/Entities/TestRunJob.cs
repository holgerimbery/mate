// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License. See LICENSE in the project root.
// Commercial use of this file, in whole or in part, is prohibited without prior written permission.
namespace mate.Domain.Entities;

/// <summary>
/// Message envelope persisted in the queue when a run is requested.
/// The Worker deserialises this to create a MessageTenantContext and
/// then delegates to TestExecutionService.
/// </summary>
public record TestRunJob(
    Guid JobId,
    Guid TenantId,
    Guid RunId,
    Guid SuiteId,
    Guid AgentId,
    string RequestedBy,

    /// <summary>Null = run all active test cases in the suite.</summary>
    IEnumerable<Guid>? TestCaseIds,

    /// <summary>Milliseconds to wait between individual test cases. Prevents rate limiting.</summary>
    int DelayBetweenTestsMs = 2000
);
