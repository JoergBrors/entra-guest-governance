# Data Storage

Stand: 2026-08-30

Persistenz erfolgt ueber Repository-Ports in `src/B2B.Portal.Application/Ports/CorePorts.cs`.

Implementierung: Cosmos DB (`src/B2B.Portal.Infrastructure/Data/Cosmos/*`) — der einzige
Datenprovider (InMemory-Repositories wurden am 2026-08-30 entfernt, siehe
`docs/architecture/mvp-test-report.md`). Ein laufender Cosmos DB Emulator ist damit fuer
`LOCAL_MOCK` erforderlich (siehe `scripts/requirements.ps1 -InitCosmosEmulator`).

Tenant-Isolation nutzt `TenantContext` und `platformTenantId` als Partition-/Filterkontext.

