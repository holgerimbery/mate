param location string
param baseName string
param queueName string = 'test-runs'

var namespaceName = take('sbns-${baseName}', 50)

resource namespace 'Microsoft.ServiceBus/namespaces@2023-01-01-preview' = {
  name: namespaceName
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2023-01-01-preview' = {
  name: queueName
  parent: namespace
  properties: {
    lockDuration: 'PT5M'
    maxDeliveryCount: 5
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P7D'
  }
}

output namespaceName string = namespace.name
output queueName string = queue.name
output namespaceId string = namespace.id
