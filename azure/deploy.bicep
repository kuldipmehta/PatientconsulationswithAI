@description('Base name for resources (e.g. "patientmgmt")')
param appName string = 'patientmgmt-${uniqueString(resourceGroup().id)}'

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('SQL Server administrator username')
param sqlAdminUsername string = 'patientadmin'

@description('SQL Server administrator password')
@secure()
param sqlAdminPassword string

@description('App Service pricing tier')
param appServiceSku string = 'B1'

var storageAccountName = 'st${uniqueString(resourceGroup().id)}'
var appServicePlanName = 'asp-${appName}'
var webAppName = 'app-${appName}'
var sqlServerName = 'sql-${appName}'
var sqlDbName = 'PatientManagementDb'
var containerName = 'patient-pictures'

// 1. Storage Account for Azure Blob Storage
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
    allowBlobPublicAccess: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

resource picturesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'Blob'
  }
}

// 2. Azure SQL Server & Database
resource sqlServer 'Microsoft.Sql/servers@2022-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminUsername
    administratorLoginPassword: sqlAdminPassword
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2022-05-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

// Allow Azure Services to access SQL Server
resource sqlFirewallRule 'Microsoft.Sql/servers/firewallRules@2022-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// 3. App Service Plan (Linux)
resource appServicePlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: appServiceSku
  }
  properties: {
    reserved: true
  }
}

// Storage connection string helper
var storageKey = storageAccount.listKeys().keys[0].value
var storageConnString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageKey};EndpointSuffix=core.windows.net'
var sqlConnString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDbName};Persist Security Info=False;User ID=${sqlAdminUsername};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

// 4. Azure App Service Web App with Swagger enabled
resource webApp 'Microsoft.Web/sites@2022-09-01' = {
  name: webAppName
  location: location
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: sqlConnString
        }
        {
          name: 'AzureBlobStorage__ConnectionString'
          value: storageConnString
        }
        {
          name: 'AzureBlobStorage__ContainerName'
          value: containerName
        }
      ]
    }
    httpsOnly: true
  }
}

output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output swaggerUrl string = 'https://${webApp.properties.defaultHostName}/swagger'
output storageAccountName string = storageAccount.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
