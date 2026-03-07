targetScope = 'resourceGroup'

@description('Environment name, e.g. dev/staging/prod')
param environmentName string = 'dev'

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Container image tag to deploy')
param imageTag string = 'latest'

@description('Worker min replicas (scale-to-zero enabled with 0)')
param workerMinReplicas int = 0

@description('Worker max replicas')
param workerMaxReplicas int = 5

@description('WebUI min replicas')
param webMinReplicas int = 1

@description('WebUI max replicas')
param webMaxReplicas int = 3

@description('WebUI CPU cores')
param webCpu string = '0.5'

@description('WebUI memory')
param webMemory string = '1Gi'

@description('Worker CPU cores')
param workerCpu string = '0.5'

@description('Worker memory')
param workerMemory string = '1Gi'

@description('Service Bus queue activation threshold for worker scaling')
param queueActivationThreshold int = 1

@description('If true, create PostgreSQL resources. Keep false until secure admin values are provided.')
param deployPostgres bool = false

@description('PostgreSQL admin login (required only when deployPostgres=true)')
param postgresAdminLogin string = ''

@secure()
@description('PostgreSQL admin password (required only when deployPostgres=true)')
param postgresAdminPassword string = ''

@description('Entra ID application (client) ID for WebUI Easy Auth authentication')
param aadClientId string

@description('Entra ID tenant ID for authentication issuer')
param aadTenantId string = subscription().tenantId

@description('Azure Blob Storage container name for documents')
param blobContainerName string = 'mate-blobs'

@description('Service Bus queue name for test runs')
param serviceBusQueueName string = 'test-runs'

@description('PostgreSQL database name')
param postgresDatabaseName string = 'mate'

var env = toLower(environmentName)
var baseName = env

module monitoring './modules/monitoring.bicep' = {
  name: 'monitoring-${env}'
  params: {
    location: location
    baseName: baseName
  }
}

module storage './modules/storage.bicep' = {
  name: 'storage-${env}'
  params: {
    location: location
    baseName: baseName
    containerName: blobContainerName
  }
}

module serviceBus './modules/servicebus.bicep' = {
  name: 'servicebus-${env}'
  params: {
    location: location
    baseName: baseName
    queueName: serviceBusQueueName
  }
}

module keyVault './modules/keyvault.bicep' = {
  name: 'keyvault-${env}'
  params: {
    location: location
    baseName: baseName
  }
}

module postgres './modules/postgres.bicep' = if (deployPostgres) {
  name: 'postgres-${env}'
  params: {
    location: location
    baseName: baseName
    administratorLogin: postgresAdminLogin
    administratorPassword: postgresAdminPassword
    databaseName: postgresDatabaseName
  }
}

module containerApps './modules/container-apps.bicep' = {
  name: 'aca-${env}'
  params: {
    location: location
    baseName: baseName
    imageTag: imageTag
    workspaceId: monitoring.outputs.workspaceId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    queueName: serviceBus.outputs.queueName
    keyVaultName: keyVault.outputs.keyVaultName
    aadClientId: aadClientId
    aadTenantId: aadTenantId
    workerMinReplicas: workerMinReplicas
    workerMaxReplicas: workerMaxReplicas
    webMinReplicas: webMinReplicas
    webMaxReplicas: webMaxReplicas
    webCpu: webCpu
    webMemory: webMemory
    workerCpu: workerCpu
    workerMemory: workerMemory
    queueActivationThreshold: queueActivationThreshold
    postgresServerName: deployPostgres ? postgres!.outputs.serverName : ''
    postgresDatabaseName: deployPostgres ? postgres!.outputs.databaseName : ''
    postgresAdminLogin: postgresAdminLogin
    postgresEnabled: deployPostgres
  }
}

output containerAppEnvironmentName string = containerApps.outputs.containerAppEnvironmentName
output webUiUrl string = containerApps.outputs.webUiUrl
output keyVaultName string = keyVault.outputs.keyVaultName
output serviceBusNamespace string = serviceBus.outputs.namespaceName
output storageAccountName string = storage.outputs.accountName
