# Data Storage

Stand: 2026-08-29

Persistenz erfolgt ueber Repository-Ports in `src/B2B.Portal.Application/Ports/CorePorts.cs`.

Implementierungen:

- InMemory: `src/B2B.Portal.Infrastructure/Data/InMemoryRepositories.cs`
- Cosmos: `src/B2B.Portal.Infrastructure/Data/Cosmos/*`

Tenant-Isolation nutzt `TenantContext` und `platformTenantId` als Partition-/Filterkontext.

