targetScope = 'resourceGroup'

@description('Name of the Azure Communication Services resource created by main.bicep.')
param communicationServiceName string

@description('Function App resource name created by main.bicep.')
param functionAppName string

@description('Event Grid subscription name.')
param eventSubscriptionName string = 'email-delivery-reports'

resource communicationService 'Microsoft.Communication/communicationServices@2023-03-31' existing = {
  name: communicationServiceName
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' existing = {
  name: functionAppName
}

resource emailDeliverySubscription 'Microsoft.EventGrid/eventSubscriptions@2022-06-15' = {
  name: eventSubscriptionName
  scope: communicationService
  properties: {
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: '${functionApp.id}/functions/EmailDeliveryReportFunction'
        maxEventsPerBatch: 1
        preferredBatchSizeInKilobytes: 64
      }
    }
    filter: {
      includedEventTypes: [
        'Microsoft.Communication.EmailDeliveryReportReceived'
      ]
      isSubjectCaseSensitive: false
    }
    eventDeliverySchema: 'EventGridSchema'
    retryPolicy: {
      maxDeliveryAttempts: 30
      eventTimeToLiveInMinutes: 1440
    }
  }
}

output eventSubscriptionId string = emailDeliverySubscription.id
