targetScope = 'subscription'

@allowed(['staging', 'production'])
param environment string
param location string = 'canadacentral'
param githubOwner string = 'EduardoRaygoza'
param githubOwnerId string = '39197906'
param githubRepository string = 'CartSync'
param githubRepositoryId string = '1382501218'

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
    githubOwnerId: githubOwnerId
    githubRepository: githubRepository
    githubRepositoryId: githubRepositoryId
  }
}

output resourceGroup string = resourceGroup.name
output deploymentClientId string = environmentBootstrap.outputs.deploymentClientId
output tenantId string = tenant().tenantId
output subscriptionId string = subscription().subscriptionId
