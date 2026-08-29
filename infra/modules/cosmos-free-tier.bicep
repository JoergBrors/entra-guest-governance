// Azure Cosmos DB — Free Tier (Blueprint 19.3: 1.000 RU/s + 25GB Storage für die
// Lebensdauer des Free-Tier-Kontos). Max. ein Free-Tier-Konto je Subscription —
// bei erneutem Deployment ggf. enableFreeTier=false setzen.

@description('Name des Cosmos DB Kontos (global eindeutig).')
param accountName string

param location string
param tags object

@description('Free Tier aktivieren. Nur EIN Free-Tier-Konto pro Subscription möglich.')
param enableFreeTier bool = true

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-08-15' = {
  name: accountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: enableFreeTier
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
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-08-15' = {
  parent: cosmosAccount
  name: 'b2b-portal'
  properties: {
    resource: {
      id: 'b2b-portal'
    }
  }
}

// Domain-Daten (Guests, Workloads, Assignments, Reviews, Audit) — Partitionierung
// nach platformTenantId gemäß Blueprint 8.1 "Shared DB / Shared Schema" (MVP-Empfehlung).
resource domainContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-08-15' = {
  parent: database
  name: 'domain-data'
  properties: {
    resource: {
      id: 'domain-data'
      partitionKey: {
        paths: ['/platformTenantId']
        kind: 'Hash'
      }
    }
  }
}

// PoC-Jobqueue als Cosmos-Container statt Service Bus (Blueprint 19.4).
resource jobQueueContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-08-15' = {
  parent: database
  name: 'job-queue'
  properties: {
    resource: {
      id: 'job-queue'
      partitionKey: {
        paths: ['/platformTenantId']
        kind: 'Hash'
      }
      defaultTtl: -1
    }
  }
}

output documentEndpoint string = cosmosAccount.properties.documentEndpoint
output accountName string = cosmosAccount.name
