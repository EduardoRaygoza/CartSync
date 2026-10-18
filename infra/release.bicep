targetScope = 'resourceGroup'

param environment string
param location string = resourceGroup().location
param containerEnvironmentName string
param registryName string
param sqlServerName string
param apiIdentityName string
param migrationIdentityName string
param pullIdentityName string
param apiImage string
param migratorImage string
param pwaOrigin string
param externalIdInstance string
param externalIdTenantId string
param externalIdApiClientId string
param foundationExternalSubject string
param foundationAccountId string
param foundationHouseholdId string
param foundationMemberId string
param buildVersion string
param appInsightsConnectionString string

resource containerEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' existing = { name: containerEnvironmentName }
resource registry 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = { name: registryName }
resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: apiIdentityName }
resource migrationIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: migrationIdentityName }
resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = { name: pullIdentityName }
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' existing = { name: sqlServerName }

var apiConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=cartsync;Authentication=Active Directory Managed Identity;User Id=${apiIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
var migrationConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=cartsync;Authentication=Active Directory Managed Identity;User Id=${migrationIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

resource sqlFirewall 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'ContainerAppsEnvironment'
  properties: {
    startIpAddress: containerEnvironment.properties.staticIp
    endIpAddress: containerEnvironment.properties.staticIp
  }
}

resource migrationJob 'Microsoft.App/jobs@2025-01-01' = {
  name: 'job-cartsync-${environment}-migrate'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migrationIdentity.id}': {}
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerEnvironment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 900
      replicaRetryLimit: 1
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: [{ server: registry.properties.loginServer, identity: pullIdentity.id }]
    }
    template: {
      containers: [{
        name: 'migrator'
        image: migratorImage
        env: [
          { name: 'ConnectionStrings__CartSync', value: migrationConnectionString }
          { name: 'AZURE_CLIENT_ID', value: migrationIdentity.properties.clientId }
          { name: 'CARTSYNC_API_IDENTITY_NAME', value: apiIdentity.name }
          { name: 'CARTSYNC_API_IDENTITY_CLIENT_ID', value: apiIdentity.properties.clientId }
          { name: 'FOUNDATION_EXTERNAL_SUBJECT', value: foundationExternalSubject }
          { name: 'FOUNDATION_ACCOUNT_ID', value: foundationAccountId }
          { name: 'FOUNDATION_HOUSEHOLD_ID', value: foundationHouseholdId }
          { name: 'FOUNDATION_MEMBER_ID', value: foundationMemberId }
        ]
        resources: { cpu: json('0.5'), memory: '1Gi' }
      }]
    }
  }
  dependsOn: [sqlFirewall]
}

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-cartsync-${environment}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentity.id}': {}
      '${pullIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
      registries: [{ server: registry.properties.loginServer, identity: pullIdentity.id }]
    }
    template: {
      containers: [{
        name: 'api'
        image: apiImage
        env: [
          { name: 'ConnectionStrings__CartSync', value: apiConnectionString }
          { name: 'AZURE_CLIENT_ID', value: apiIdentity.properties.clientId }
          { name: 'Cors__AllowedOrigin', value: pwaOrigin }
          { name: 'ExternalId__Instance', value: externalIdInstance }
          { name: 'ExternalId__TenantId', value: externalIdTenantId }
          { name: 'ExternalId__ClientId', value: externalIdApiClientId }
          { name: 'Build__Version', value: buildVersion }
          { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        ]
        probes: [
          { type: 'Liveness', httpGet: { path: '/health/live', port: 8080, scheme: 'HTTP' }, initialDelaySeconds: 5, periodSeconds: 30 }
          { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080, scheme: 'HTTP' }, initialDelaySeconds: 10, periodSeconds: 30 }
        ]
        resources: { cpu: json('0.5'), memory: '1Gi' }
      }]
      scale: { minReplicas: 0, maxReplicas: 2, rules: [{ name: 'http', http: { metadata: { concurrentRequests: '25' } } }] }
    }
  }
}

output migrationJobName string = migrationJob.name
output apiOrigin string = 'https://${api.properties.configuration.ingress.fqdn}'
output apiRevision string = api.properties.latestRevisionName
