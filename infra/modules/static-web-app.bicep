// Azure Static Web Apps — Free SKU (Blueprint 19.3: 100GB Bandbreite, 2 Custom Domains,
// 0.5GB Storage, kein SLA). Hostet Web UI + kleine integrierte APIs.

@description('Name der Static Web App.')
param name string

param location string
param tags object

@description('Optionale GitHub-Repository-URL; kann nach Deployment im Portal verknüpft werden.')
param repositoryUrl string = ''

resource staticWebApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    repositoryUrl: empty(repositoryUrl) ? null : repositoryUrl
    branch: empty(repositoryUrl) ? null : 'main'
    buildProperties: {
      appLocation: 'src/B2B.Portal.Web'
      outputLocation: 'dist'
    }
  }
}

output defaultHostname string = staticWebApp.properties.defaultHostname
output resourceId string = staticWebApp.id
