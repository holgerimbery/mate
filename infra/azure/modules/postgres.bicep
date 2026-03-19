param location string
param baseName string
param administratorLogin string
@secure()
param administratorPassword string
param databaseName string = 'mate'

var serverNameSuffix = take(uniqueString(subscription().subscriptionId, resourceGroup().name, baseName, location), 6)
var serverNameBase = take('${baseName}-pg', 56)
var serverName = '${serverNameBase}-${serverNameSuffix}'
var dbName = databaseName

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2023-06-01-preview' = {
  name: serverName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '17'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

resource db 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-06-01-preview' = {
  name: dbName
  parent: server
}

output serverName string = server.name
output databaseName string = db.name
