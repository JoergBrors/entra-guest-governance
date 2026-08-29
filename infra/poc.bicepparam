using 'main.bicep'

// Sicheres Template — KEINE echten Tenant-/Ressourcennamen committen.
// Vor Deployment lokal überschreiben oder als separate, nicht versionierte Datei pflegen.

param namePrefix = 'b2bpoc'
param environmentName = 'poc'
param staticWebAppRepositoryUrl = ''
