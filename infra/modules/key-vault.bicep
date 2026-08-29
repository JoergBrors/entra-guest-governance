// Azure Key Vault — Spiegelung der .env-Werte fuer DEV_INTEGRATION/AZURE_DEV
// (Blueprint 19.2/23.2, README "Konfiguration erfolgt ueber .env.local"). Secrets werden
// NICHT von diesem Modul befuellt - es legt nur den Vault + RBAC an. Das eigentliche
// Schreiben der Werte erfolgt separat und bewusst manuell/gesteuert ueber
// scripts/sync-keyvault.ps1, damit kein Secret-Wert in einer Bicep-Deployment-History landet.

@description('Name des Key Vaults (global eindeutig, 3-24 Zeichen).')
@minLength(3)
@maxLength(24)
param name string

param location string
param tags object

@description('Object-ID des Principals (Benutzer, Managed Identity oder Service Principal), der Secrets lesen/schreiben darf.')
param accessPrincipalId string = ''

@description('Azure AD Tenant-ID fuer den Key Vault (nicht der Entra Directory-Tenant der Gaeste - siehe README Unterscheidung Platform-/Directory-Tenant).')
param vaultTenantId string = subscription().tenantId

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: vaultTenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    enablePurgeProtection: false
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// "Key Vault Secrets Officer" — erlaubt Lesen/Schreiben von Secrets, aber keine
// Verwaltung des Vaults selbst (Prinzip minimaler Rechte).
var secretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource secretsOfficerAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(accessPrincipalId)) {
  name: guid(keyVault.id, accessPrincipalId, secretsOfficerRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsOfficerRoleId)
    principalId: accessPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output vaultUri string = keyVault.properties.vaultUri
output vaultName string = keyVault.name
