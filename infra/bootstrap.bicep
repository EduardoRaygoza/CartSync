targetScope = 'subscription'

@allowed(['staging', 'production'])
param environment string
param location string = 'canadacentral'
param githubOwner string = 'EduardoRaygoza'
param githubRepository string = 'CartSync'

var resourceGroupName = 'rg-cartsync-${environment}'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

module environmentBootstrap 'modules/bootstrap-environment.bicep' = {
  name: 'cartsync-${environment}-bootstrap'
  scope: resourceGroup
  params: {
    environment: environment
    location: location
    githubOwner: githubOwner
    githubRepository: githubRepository
  }
}

output resourceGroup string = resourceGroup.name
output deploymentClientId string = environmentBootstrap.outputs.deploymentClientId
output tenantId string = tenant().tenantId
output subscriptionId string = subscription().subscriptionId
