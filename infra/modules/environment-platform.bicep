param environment string
param location string
param staticWebAppLocation string
param notificationEmail string
param budgetStartDate string
param githubOwner string
param githubRepository string

var suffix = toLower(uniqueString(resourceGroup().id))
var apiIdentityName = 'id-cartsync-${environment}-api'
var migrationIdentityName = 'id-cartsync-${environment}-migrate'
var pullIdentityName = 'id-cartsync-${environment}-pull'
var registryName = 'crcartsync${suffix}${environment}'
var sqlServerName = take('sql-cartsync-${environment}-${suffix}', 63)
var workspaceName = 'log-cartsync-${environment}'
var appInsightsName = 'appi-cartsync-${environment}'
var containerEnvironmentName = 'cae-cartsync-${environment}'
var staticWebAppName = 'swa-cartsync-${environment}-${suffix}'

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: apiIdentityName
  location: location
}

resource migrationIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: migrationIdentityName
  location: location
}

resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: pullIdentityName
  location: location
}

resource deploymentIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: 'id-cartsync-${environment}-deploy'
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: registryName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
    dataEndpointEnabled: false
    publicNetworkAccess: 'Enabled'
    policies: {
      exportPolicy: { status: 'enabled' }
      quarantinePolicy: { status: 'disabled' }
      retentionPolicy: { days: 7, status: 'enabled' }
      trustPolicy: { type: 'Notary', status: 'disabled' }
    }
  }
}

resource pullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pullIdentity.id, 'acr-pull')
  scope: registry
  properties: {
    principalId: pullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource deploymentPushRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, deploymentIdentity.id, 'acr-push')
  scope: registry
  properties: {
    principalId: deploymentIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')
  }
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  properties: {
    retentionInDays: 30
    features: { enableLogAccessUsingOnlyResourcePermissions: true }
    sku: { name: 'PerGB2018' }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    DisableIpMasking: false
    IngestionMode: 'LogAnalytics'
    RetentionInDays: 30
  }
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: containerEnvironmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${migrationIdentity.id}': {} }
  }
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: migrationIdentity.name
      principalType: 'Application'
      sid: migrationIdentity.properties.principalId
      tenantId: tenant().tenantId
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    primaryUserAssignedIdentityId: migrationIdentity.id
    restrictOutboundNetworkAccess: 'Enabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'cartsync'
  location: location
  sku: { name: 'GP_S_Gen5', tier: 'GeneralPurpose', family: 'Gen5', capacity: 1 }
  properties: {
    autoPauseDelay: 60
    availabilityZone: 'NoPreference'
    freeLimitExhaustionBehavior: 'BillOverUsage'
    minCapacity: json('0.5')
    readScale: 'Disabled'
    requestedBackupStorageRedundancy: 'Zone'
  }
}

resource denyAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'DenyBroadAzureServices'
  properties: { startIpAddress: '0.0.0.1', endIpAddress: '0.0.0.1' }
}

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: staticWebAppLocation
  sku: { name: 'Free', tier: 'Free' }
  properties: {
    allowConfigFileUpdates: true
    branch: environment == 'production' ? 'main' : 'staging'
    buildProperties: { skipGithubActionWorkflowGeneration: true }
    enterpriseGradeCdnStatus: 'Disabled'
    provider: 'GitHub'
    repositoryUrl: 'https://github.com/${githubOwner}/${githubRepository}'
    stagingEnvironmentPolicy: 'Disabled'
  }
}

resource sqlDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'cartsync-sql-diagnostics'
  scope: sqlDatabase
  properties: {
    workspaceId: workspace.id
    logs: [
      { category: 'Errors', enabled: true }
      { category: 'Timeouts', enabled: true }
      { category: 'Blocks', enabled: true }
      { category: 'Deadlocks', enabled: true }
    ]
    metrics: [{ category: 'Basic', enabled: true }]
  }
}

resource workbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  name: guid(resourceGroup().id, 'operations-workbook')
  location: location
  kind: 'shared'
  properties: {
    displayName: 'CartSync ${environment} operations'
    category: 'workbook'
    sourceId: resourceGroup().id
    serializedData: string({
      version: 'Notebook/1.0'
      items: [
        { type: 1, content: { json: '# CartSync ${environment}\nRedacted availability, deployment, migration, backup, usage, and cost signals.' } }
        { type: 3, content: { version: 'KqlItem/1.0', query: 'requests | summarize Requests=count(), Failures=countif(success == false), P95=percentile(duration, 95) by bin(timestamp, 15m)', size: 0, title: 'Availability and latency', queryType: 0, resourceType: 'microsoft.insights/components' } }
      ]
    })
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'cartsync-${environment}-monthly'
  properties: {
    amount: 25
    category: 'Cost'
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    filter: { dimensions: { name: 'ResourceGroupName', operator: 'In', values: [resourceGroup().name] } }
    notifications: {
      target: { enabled: true, operator: 'GreaterThan', threshold: 100, contactEmails: [notificationEmail], thresholdType: 'Actual' }
      review: { enabled: true, operator: 'GreaterThan', threshold: 200, contactEmails: [notificationEmail], thresholdType: 'Forecasted' }
    }
  }
}

output resources object = {
  registryName: registry.name
  sqlServerName: sqlServer.name
  sqlDatabaseName: sqlDatabase.name
  containerEnvironmentName: containerEnvironment.name
  staticWebAppName: staticWebApp.name
  workspaceId: workspace.id
  appInsightsConnectionString: appInsights.properties.ConnectionString
}
output identities object = {
  apiName: apiIdentity.name
  apiClientId: apiIdentity.properties.clientId
  apiResourceId: apiIdentity.id
  migrationName: migrationIdentity.name
  migrationClientId: migrationIdentity.properties.clientId
  migrationResourceId: migrationIdentity.id
  pullName: pullIdentity.name
  pullResourceId: pullIdentity.id
}
output origins object = {
  pwa: 'https://${staticWebApp.properties.defaultHostname}'
  sql: '${sqlServer.name}${az.environment().suffixes.sqlServerHostname}'
}
