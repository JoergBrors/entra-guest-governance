// Azure Automation — 500 inkludierte Job-Laufzeitminuten/Monat (Blueprint 19.3).
// Dient im PoC als Scheduler/Background-Worker-Trägerdienst. Zeitpläne müssen so
// dimensioniert werden, dass das Freikontingent nicht überschritten wird
// (Blueprint 19.5 "Nullkosten-Schutz").

@description('Name des Automation Accounts.')
param accountName string

param location string
param tags object

resource automationAccount 'Microsoft.Automation/automationAccounts@2023-11-01' = {
  name: accountName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'Free'
    }
  }
}

output accountName string = automationAccount.name
output resourceId string = automationAccount.id
