param location string
param baseName string
param imageTag string
param workspaceId string
param appInsightsConnectionString string
param serviceBusNamespaceName string
param queueName string
param keyVaultName string
param keyVaultUri string
param aadClientId string
param aadTenantId string
param workerMinReplicas int
param workerMaxReplicas int
param webMinReplicas int
param webMaxReplicas int
param webCpu string
param webMemory string
param workerCpu string
param workerMemory string
param queueActivationThreshold int
param brandingBrandName string
param brandingBrandTagline string
param brandingBrandCliDescription string
param brandingLogoUrl string
param brandingLogoWideUrl string
param brandingApiKeyPrefix string
var caeName = take('${baseName}-cae', 32)
var webAppName = take('${baseName}-webui', 32)
var workerAppName = take('${baseName}-worker', 32)
var ghcrBase = 'ghcr.io/holgerimbery'
var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource cae 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: caeName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(workspaceId, '2023-09-01').customerId
        sharedKey: listKeys(workspaceId, '2023-09-01').primarySharedKey
      }
    }
  }
}

resource webIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: take('${baseName}-web-mi', 128)
  location: location
}

resource workerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: take('${baseName}-worker-mi', 128)
  location: location
}

resource webIdentityKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, 'KeyVaultSecretsUser', webIdentity.name)
  scope: keyVault
  properties: {
    principalId: webIdentity.properties.principalId
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
    principalType: 'ServicePrincipal'
  }
}

resource workerIdentityKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, 'KeyVaultSecretsUser', workerIdentity.name)
  scope: keyVault
  properties: {
    principalId: workerIdentity.properties.principalId
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
    principalType: 'ServicePrincipal'
  }
}

resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: webAppName
  location: location
  dependsOn: [
    webIdentityKvRole
  ]
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${webIdentity.id}': {}
    }
  }
  properties: {
    environmentId: cae.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      activeRevisionsMode: 'Single'
      secrets: [
        {
          name: 'azuread-client-secret'
          identity: webIdentity.id
          keyVaultUrl: '${keyVaultUri}secrets/azuread-client-secret'
        }
        {
          name: 'blob-connection-string'
          identity: webIdentity.id
          keyVaultUrl: '${keyVaultUri}secrets/blob-connection-string'
        }
        {
          name: 'postgres-connection-string'
          identity: webIdentity.id
          keyVaultUrl: '${keyVaultUri}secrets/postgres-connection-string'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'webui'
          image: '${ghcrBase}/mate-webui:${imageTag}'
          resources: {
            cpu: json(webCpu)
            memory: webMemory
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
              value: 'true'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'Authentication__Scheme'
              value: 'EntraId'
            }
            {
              name: 'AzureAd__TenantId'
              value: aadTenantId
            }
            {
              name: 'AzureAd__ClientId'
              value: aadClientId
            }
            {
              name: 'AzureAd__Instance'
                value: environment().authentication.loginEndpoint
            }
            {
              name: 'AzureAd__ClientSecret'
              secretRef: 'azuread-client-secret'
            }
            {
              name: 'AzureAd__CallbackPath'
              value: '/signin-oidc'
            }
            {
              name: 'AzureAd__SignedOutCallbackPath'
              value: '/signout-callback-oidc'
            }
            {
              name: 'Monitoring__Provider'
              value: 'ApplicationInsights'
            }
            {
              name: 'Branding__BrandName'
              value: brandingBrandName
            }
            {
              name: 'Branding__BrandTagline'
              value: brandingBrandTagline
            }
            {
              name: 'Branding__BrandCliDescription'
              value: brandingBrandCliDescription
            }
            {
              name: 'Branding__LogoUrl'
              value: brandingLogoUrl
            }
            {
              name: 'Branding__LogoWideUrl'
              value: brandingLogoWideUrl
            }
            {
              name: 'Branding__ApiKeyPrefix'
              value: brandingApiKeyPrefix
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
            {
              name: 'Infrastructure__Provider'
              value: 'Azure'
            }
            {
              name: 'AzureInfrastructure__UseKeyVaultForSecrets'
              value: 'true'
            }
            {
              name: 'AzureInfrastructure__KeyVaultUri'
              value: keyVaultUri
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: webIdentity.properties.clientId
            }
            {
              name: 'RedmondMode'
              value: 'false'
            }
            {
              name: 'AzureInfrastructure__BlobContainerName'
              value: 'mate-blobs'
            }
            {
              name: 'AzureInfrastructure__BlobConnectionString'
              secretRef: 'blob-connection-string'
            }
            {
              name: 'ConnectionStrings__Default'
              secretRef: 'postgres-connection-string'
            }
          ]
        }
      ]
      scale: {
        minReplicas: webMinReplicas
        maxReplicas: webMaxReplicas
      }
    }
  }
}

resource webAppAuthConfig 'Microsoft.App/containerApps/authConfigs@2024-03-01' = {
  parent: webApp
  name: 'current'
  properties: {
    platform: {
      enabled: false
    }
  }
}

resource workerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: workerAppName
  location: location
  dependsOn: [
    workerIdentityKvRole
  ]
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workerIdentity.id}': {}
    }
  }
  properties: {
    environmentId: cae.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: [
        {
          name: 'blob-connection-string'
          identity: workerIdentity.id
          keyVaultUrl: '${keyVaultUri}secrets/blob-connection-string'
        }
        {
          name: 'postgres-connection-string'
          identity: workerIdentity.id
          keyVaultUrl: '${keyVaultUri}secrets/postgres-connection-string'
        }
        {
          name: 'servicebus-connection-string'
          identity: workerIdentity.id
          keyVaultUrl: '${keyVaultUri}secrets/servicebus-connection-string'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${ghcrBase}/mate-worker:${imageTag}'
          resources: {
            cpu: json(workerCpu)
            memory: workerMemory
          }
          env: [
            {
              name: 'DOTNET_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'Infrastructure__Provider'
              value: 'Azure'
            }
            {
              name: 'AzureInfrastructure__UseKeyVaultForSecrets'
              value: 'true'
            }
            {
              name: 'AzureInfrastructure__KeyVaultUri'
              value: keyVaultUri
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: workerIdentity.properties.clientId
            }
            {
              name: 'RedmondMode'
              value: 'false'
            }
            {
              name: 'AzureInfrastructure__BlobContainerName'
              value: 'mate-blobs'
            }
            {
              name: 'AzureInfrastructure__BlobConnectionString'
              secretRef: 'blob-connection-string'
            }
            {
              name: 'ConnectionStrings__Default'
              secretRef: 'postgres-connection-string'
            }
          ]
        }
      ]
      scale: {
        minReplicas: workerMinReplicas
        maxReplicas: workerMaxReplicas
        rules: [
          {
            name: 'servicebus-queue-rule'
            custom: {
              type: 'azure-servicebus'
              metadata: {
                namespace: serviceBusNamespaceName
                queueName: queueName
                messageCount: string(queueActivationThreshold)
              }
              auth: [
                {
                  secretRef: 'servicebus-connection-string'
                  triggerParameter: 'connection'
                }
              ]
            }
          }
        ]
      }
    }
  }
}

output containerAppEnvironmentName string = cae.name
output webUiUrl string = 'https://${webApp.properties.configuration.ingress.fqdn}'
output webIdentityPrincipalId string = webIdentity.properties.principalId
output workerIdentityPrincipalId string = workerIdentity.properties.principalId
