targetScope = 'subscription'

@allowed(['staging', 'production'])
param environment string
param location string = 'canadacentral'
param staticWebAppLocation string = 'canadacentral'
param notificationEmail string
param budgetStartDate string
param githubOwner string = 'EduardoRaygoza'
param githubRepository string = 'CartSync'

var resourceGroupName = 'rg-cartsync-${environment}'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: resourceGroupName
}

module platform 'modules/environment-platform.bicep' = {
  name: 'cartsync-${environment}-platform'
  scope: resourceGroup
  params: {
    environment: environment
    location: location
    staticWebAppLocation: staticWebAppLocation
    notificationEmail: notificationEmail
    budgetStartDate: budgetStartDate
    githubOwner: githubOwner
    githubRepository: githubRepository
  }
}

output resources object = platform.outputs.resources
output identities object = platform.outputs.identities
output origins object = platform.outputs.origins
