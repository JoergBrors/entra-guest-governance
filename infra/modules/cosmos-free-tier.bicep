// Azure Cosmos DB — Free Tier (Blueprint 19.3: 1.000 RU/s + 25GB Storage für die
// Lebensdauer des Free-Tier-Kontos). Max. ein Free-Tier-Konto je Subscription —
// bei erneutem Deployment ggf. enableFreeTier=false setzen.
//
// Container-Struktur gemäß Datenhaltungskonzept (Desired State / Actual State / Jobs /
// Audit getrennt, siehe docs/architecture — vier logisch getrennte Container statt eines
// gemeinsamen "domain-data"-Containers):
//   domain     — Desired State: Tenant, ExternalOrganization, GuestAccount, Workload,
//                WorkloadScenario, GuestWorkloadAssignment, ReviewDefinition/Instance
//   discovery  — Actual State: ResourceAccess
//   jobs       — DirectoryOperation + JobEnvelope (Job-Queue-Transportdokumente)
//   audit      — AuditEvent (unveränderliche Nachweise)

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
  name: 'b2b-governance-dev'
  properties: {
    resource: {
      id: 'b2b-governance-dev'
    }
  }
}

// Desired State: Tenant, ExternalOrganization, GuestAccount, Workload, WorkloadScenario,
// GuestWorkloadAssignment, ReviewDefinition/Instance — Partitionierung nach
// platformTenantId gemäß Blueprint 8.1 "Shared DB / Shared Schema" (MVP-Empfehlung).
resource domainContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-08-15' = {
  parent: database
  name: 'domain'
  properties: {
    resource: {
      id: 'domain'
      partitionKey: {
        paths: ['/platformTenantId']
        kind: 'Hash'
      }
    }
  }
}

// Actual State (Blueprint 12.2): ResourceAccess — getrennt vom Desired State, damit
// Reconciliation beide Zustände unabhängig lesen/vergleichen kann.
resource discoveryContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-08-15' = {
  parent: database
  name: 'discovery'
  properties: {
    resource: {
      id: 'discovery'
      partitionKey: {
        paths: ['/platformTenantId']
        kind: 'Hash'
      }
    }
  }
}

// Job-Queue (DirectoryOperation + JobEnvelope-Transportdokumente). Persistent — ein
// Neustart von API/Worker darf offene Jobs nicht verlieren. defaultTtl=-1 heißt "kein
// automatisches Ablaufen", explizit gesetzt statt implizit.
resource jobsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-08-15' = {
  parent: database
  name: 'jobs'
  properties: {
    resource: {
      id: 'jobs'
      partitionKey: {
        paths: ['/platformTenantId']
        kind: 'Hash'
      }
      defaultTtl: -1
    }
  }
}

// Audit Events — fachlich unveränderliche Nachweise, nie automatisch ablaufend.
resource auditContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-08-15' = {
  parent: database
  name: 'audit'
  properties: {
    resource: {
      id: 'audit'
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
