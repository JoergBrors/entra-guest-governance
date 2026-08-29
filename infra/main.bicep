// main.bicep — Nullkosten-PoC-Zielarchitektur (Blueprint Abschnitt 19.2/19.3)
// Verwendet ausschließlich Free-SKUs: Azure Static Web Apps (Free), Azure Cosmos DB
// (Free Tier), Azure Automation (500 inkludierte Job-Minuten/Monat). Kein Dienst mit
// notwendiger monatlicher Grundgebühr. Konkrete Namen/IDs bewusst NICHT hart codiert —
// siehe dev.bicepparam / poc.bicepparam.

targetScope = 'resourceGroup'

@description('Kurzes, eindeutiges Präfix für Ressourcennamen, z.B. "b2bpoc".')
@minLength(3)
@maxLength(11)
param namePrefix string

@description('Azure-Region für alle Ressourcen.')
param location string = resourceGroup().location

@description('Umgebungskennzeichen: dev oder poc. Rein informativ für Tags.')
@allowed(['dev', 'poc'])
param environmentName string = 'poc'

@description('GitHub-Repository-URL für Azure Static Web Apps Deployment (optional, kann leer bleiben und später verknüpft werden).')
param staticWebAppRepositoryUrl string = ''

var tags = {
  project: 'b2b-guest-governance-portal'
  environment: environmentName
  costTier: 'free'
}

module staticWebApp 'modules/static-web-app.bicep' = {
  name: 'staticWebAppDeployment'
  params: {
    name: '${namePrefix}-web'
    location: location
    tags: tags
    repositoryUrl: staticWebAppRepositoryUrl
  }
}

module cosmos 'modules/cosmos-free-tier.bicep' = {
  name: 'cosmosDeployment'
  params: {
    accountName: '${namePrefix}-cosmos'
    location: location
    tags: tags
  }
}

module automation 'modules/automation.bicep' = {
  name: 'automationDeployment'
  params: {
    accountName: '${namePrefix}-automation'
    location: location
    tags: tags
  }
}

output staticWebAppDefaultHostname string = staticWebApp.outputs.defaultHostname
output cosmosAccountEndpoint string = cosmos.outputs.documentEndpoint
output automationAccountName string = automation.outputs.accountName
