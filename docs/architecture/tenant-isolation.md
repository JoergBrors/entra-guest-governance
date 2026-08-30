# Tenant Isolation

Stand: 2026-08-29

Tenant-Kontext:

- `TenantContext`
- `ITenantContextAccessor`
- `HeaderTenantContextAccessor`

Repository-Ports verlangen `TenantContext`. InMemory- und Cosmos-Repositories filtern nach `platformTenantId`.

Produktive Ableitung aus validierten Tokens: `integration pending`.

