param resourceLocation string
param prefix string
param tags object = {}

param msiPrincipalID string

var uniqueSuffix = substring(uniqueString(subscription().id, resourceGroup().id), 1, 3) 
var sqlServerName = '${prefix}-sql-${uniqueSuffix}'
var sqlDBName = '${prefix}-db-${uniqueSuffix}'


resource sqlServer 'Microsoft.Sql/servers@2022-05-01-preview' = {
  name: sqlServerName
  location: resourceLocation
  tags: tags
  properties: {
    administrators: {
      azureADOnlyAuthentication: true
      principalType: 'Application'
      administratorType: 'ActiveDirectory'
      login: msiPrincipalID
      sid: msiPrincipalID
      tenantId: tenant().tenantId
    }
  }

  resource fw 'firewallRules' = {
    name: 'default-fw'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }
}

resource sqlDB 'Microsoft.Sql/servers/databases@2022-05-01-preview' = {
  parent: sqlServer
  name: sqlDBName
  location: resourceLocation
  properties: {
    sampleName: 'AdventureWorksLT'
  }
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

output sqlServer string = sqlServer.id
output sqlDB string = sqlDB.id
output sqlConnectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDB.name};Persist Security Info=False;Authentication=Active Directory MSI;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
