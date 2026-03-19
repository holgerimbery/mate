// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License.
//
// Assigns Key Vault Secrets User role to both Container App managed identities.
// Principal IDs are passed from the containerApps module outputs, which ARM
// resolves only after that module completes — guaranteeing correct ordering.

param keyVaultName string
param webIdentityPrincipalId string
param workerIdentityPrincipalId string

// Key Vault Secrets User — built-in role definition ID (stable across all tenants)
var kvSecretsUserRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e0'
)

resource kvRef 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource webIdentityKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultName, webIdentityPrincipalId, kvSecretsUserRoleId)
  scope: kvRef
  properties: {
    roleDefinitionId: kvSecretsUserRoleId
    principalId: webIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource workerIdentityKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultName, workerIdentityPrincipalId, kvSecretsUserRoleId)
  scope: kvRef
  properties: {
    roleDefinitionId: kvSecretsUserRoleId
    principalId: workerIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}
