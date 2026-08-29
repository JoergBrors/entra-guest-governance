using 'main.bicep'

// Sicheres Template — KEINE echten Tenant-/Ressourcennamen committen.
// Vor Deployment lokal überschreiben oder als separate, nicht versionierte Datei pflegen.

param namePrefix = 'b2bdev'
param environmentName = 'dev'
param staticWebAppRepositoryUrl = ''
