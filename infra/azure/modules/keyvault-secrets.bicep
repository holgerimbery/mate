// Copyright (c) Holger Imbery. All rights reserved.
// Licensed under the mate Custom License.
//
// Populates Key Vault secrets for blob storage, PostgreSQL, and Service Bus.
// All resources referenced here are guaranteed to exist because their names
// are passed as output values from upstream modules, which ARM resolves only
// after those modules complete.

param keyVaultName string
param storageAccountName string
param serviceBusNamespaceName string
param postgresServerName string
param postgresDatabaseName string
param postgresAdminLogin string

@secure()
param postgresAdminPassword string

resource kvRef 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource storageRef 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource sbRef 'Microsoft.ServiceBus/namespaces@2023-01-01-preview' existing = {
  name: serviceBusNamespaceName
}

resource sbRuleRef 'Microsoft.ServiceBus/namespaces/authorizationRules@2023-01-01-preview' existing = {
  parent: sbRef
  name: 'RootManageSharedAccessKey'
}

resource kvBlobSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kvRef
  name: 'blob-connection-string'
  properties: {
    value: 'DefaultEndpointsProtocol=https;AccountName=${storageRef.name};AccountKey=${storageRef.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
  }
}

resource kvPostgresSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kvRef
  name: 'postgres-connection-string'
  properties: {
    value: 'Host=${postgresServerName}.postgres.database.azure.com;Database=${postgresDatabaseName};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require'
  }
}

resource kvSbSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kvRef
  name: 'servicebus-connection-string'
  properties: {
    value: sbRuleRef.listKeys().primaryConnectionString
  }
}
