targetScope = 'resourceGroup'

@description('Region for regional resources. The verification environment uses Japan East.')
@allowed([
  'japaneast'
])
param location string = 'japaneast'

@description('The Entra External ID API application (client) ID. Easy Auth accepts tokens whose audience matches this ID.')
@minLength(1)
param externalIdApiClientId string

@description('The URL of the Entra External ID OpenID Connect metadata document.')
@minLength(1)
param externalIdMetadataUrl string

@description('The .NET isolated runtime version for this net10.0 application. The Functions host runtime remains v4.')
@allowed([
  '10'
])
param functionRuntimeVersion string = '10'

@description('The maximum scale-out instance count. Flex Consumption requires at least 40.')
@minValue(40)
@maxValue(1000)
param maximumInstanceCount int = 40

@description('Memory allocated to each Flex Consumption instance.')
@allowed([
  2048
  4096
])
param instanceMemoryMB int = 2048

@description('Additional exact browser origins allowed to call the Functions API, such as local or staging frontends.')
param additionalCorsOrigins array = []

@description('Verified ACS Email MailFrom address. Configure this after the Azure-managed domain is provisioned.')
param emailSenderAddress string = ''

var resourceToken = toLower(uniqueString(subscription().id, resourceGroup().id, location))
var deploymentStorageContainerName = 'app-package-${take(resourceToken, 13)}'
var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageQueueDataContributorRoleId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'
var communicationEmailSenderRoleId = '0ec3c718-2035-4ae6-84b2-c23a254ec10a'

resource staticWebApp 'Microsoft.Web/staticSites@2022-09-01' = {
  name: 'swa-${resourceToken}'
  location: 'eastasia'
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${resourceToken}'
  location: location
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
    features: {
      searchVersion: 1
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${resourceToken}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${resourceToken}'
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deploymentStorageContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource emailQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: 'email-jobs'
}

resource emailPoisonQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: 'email-jobs-poison'
}

resource functionIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'uai-${resourceToken}'
  location: location
}

resource storageBlobRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionIdentity.id, storageBlobDataOwnerRoleId)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: functionIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageQueueRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionIdentity.id, storageQueueDataContributorRoleId)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributorRoleId)
    principalId: functionIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageTableRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionIdentity.id, storageTableDataContributorRoleId)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorRoleId)
    principalId: functionIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2022-05-15' = {
  name: 'cosmos-${resourceToken}'
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    enableFreeTier: true
    enableAutomaticFailover: false
    enableMultipleWriteLocations: false
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2022-05-15' = {
  parent: cosmosAccount
  name: 'cfp'
  properties: {
    resource: {
      id: 'cfp'
    }
    options: {
      throughput: 1000
    }
  }
}

resource conferenceDataContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2022-05-15' = {
  parent: cosmosDatabase
  name: 'conferenceData'
  properties: {
    resource: {
      id: 'conferenceData'
      partitionKey: {
        paths: [
          '/conferenceId'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource userProfilesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2022-05-15' = {
  parent: cosmosDatabase
  name: 'userProfiles'
  properties: {
    resource: {
      id: 'userProfiles'
      partitionKey: {
        paths: [
          '/userId'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource conferenceDirectoryContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2022-05-15' = {
  parent: cosmosDatabase
  name: 'conferenceDirectory'
  properties: {
    resource: {
      id: 'conferenceDirectory'
      partitionKey: {
        paths: [
          '/slug'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource identityDirectoryContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2022-05-15' = {
  parent: cosmosDatabase
  name: 'identityDirectory'
  properties: {
    resource: {
      id: 'identityDirectory'
      partitionKey: {
        paths: [
          '/identityKey'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource emailDeliveryDirectoryContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2022-05-15' = {
  parent: cosmosDatabase
  name: 'emailDeliveryDirectory'
  properties: {
    resource: {
      id: 'emailDeliveryDirectory'
      partitionKey: {
        paths: [
          '/providerMessageId'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource functionLeasesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2022-05-15' = {
  parent: cosmosDatabase
  name: 'functionLeases'
  properties: {
    resource: {
      id: 'functionLeases'
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource cosmosDataContributorRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleDefinitions@2024-05-15' existing = {
  parent: cosmosAccount
  name: cosmosDataContributorRoleId
}

resource cosmosDataRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, cosmosDatabase.name, functionIdentity.id, cosmosDataContributorRoleId)
  properties: {
    principalId: functionIdentity.properties.principalId
    roleDefinitionId: cosmosDataContributorRole.id
    scope: '/dbs/${cosmosDatabase.name}'
  }
}

resource emailService 'Microsoft.Communication/emailServices@2023-03-31' = {
  name: 'email-${resourceToken}'
  location: 'global'
  properties: {
    dataLocation: 'Japan'
  }
}

resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-03-31' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-03-31' = {
  name: 'acs-${resourceToken}'
  location: 'global'
  properties: {
    dataLocation: 'Japan'
    linkedDomains: [
      emailDomain.id
    ]
  }
}

resource communicationEmailSenderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(communicationService.id, functionIdentity.id, communicationEmailSenderRoleId)
  scope: communicationService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', communicationEmailSenderRoleId)
    principalId: functionIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-${resourceToken}'
  location: location
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-${resourceToken}'
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${functionIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      cors: {
        allowedOrigins: concat([
          'https://${staticWebApp.properties.defaultHostname}'
        ], additionalCorsOrigins)
        supportCredentials: false
      }
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentStorageContainerName}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: functionIdentity.id
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount
        instanceMemoryMB: instanceMemoryMB
      }
      runtime: {
        name: 'dotnet-isolated'
        version: functionRuntimeVersion
      }
    }
  }
}

resource functionAppSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    FUNCTIONS_EXTENSION_VERSION: '~4'
    FUNCTIONS_WORKER_RUNTIME: 'dotnet-isolated'
    AZURE_CLIENT_ID: functionIdentity.properties.clientId
    AzureWebJobsStorage__accountName: storage.name
    AzureWebJobsStorage__credential: 'managedidentity'
    AzureWebJobsStorage__clientId: functionIdentity.properties.clientId
    APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsights.properties.ConnectionString
    Cosmos__Endpoint: 'https://${cosmosAccount.name}.documents.azure.com:443/'
    Cosmos__DatabaseName: cosmosDatabase.name
    COSMOS_DATABASE_NAME: cosmosDatabase.name
    CosmosConnection__accountEndpoint: 'https://${cosmosAccount.name}.documents.azure.com:443/'
    CosmosConnection__credential: 'managedidentity'
    CosmosConnection__clientId: functionIdentity.properties.clientId
    Communication__Endpoint: 'https://${communicationService.name}.communication.azure.com/'
    Communication__ResourceId: communicationService.id
    Communication__SenderAddress: emailSenderAddress
    Communication__EmailServiceResourceId: emailService.id
    Communication__EmailDomainResourceId: emailDomain.id
  }
}

resource functionAuthSettings 'Microsoft.Web/sites/config@2022-09-01' = {
  parent: functionApp
  name: 'authsettingsV2'
  properties: {
    platform: {
      enabled: true
    }
    globalValidation: {
      requireAuthentication: false
      unauthenticatedClientAction: 'AllowAnonymous'
    }
    httpSettings: {
      requireHttps: true
    }
    identityProviders: {
      customOpenIdConnectProviders: {
        externalid: {
          enabled: true
          registration: {
            clientId: externalIdApiClientId
            openIdConnectConfiguration: {
              wellKnownOpenIdConfiguration: externalIdMetadataUrl
            }
          }
          login: {
            scopes: [
              'openid'
              'profile'
              'email'
            ]
          }
        }
      }
    }
    login: {
      tokenStore: {
        enabled: false
      }
    }
  }
}

output staticWebAppUrl string = 'https://${staticWebApp.properties.defaultHostname}'
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output functionAppName string = functionApp.name
output cosmosAccountName string = cosmosAccount.name
output cosmosDatabaseName string = cosmosDatabase.name
output cosmosEndpoint string = 'https://${cosmosAccount.name}.documents.azure.com:443/'
output functionIdentityPrincipalId string = functionIdentity.properties.principalId
output communicationServiceName string = communicationService.name
output communicationServiceId string = communicationService.id
output emailServiceName string = emailService.name
output emailDomainResourceId string = emailDomain.id
