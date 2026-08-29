# Solution Structure

Stand: 2026-08-29

Projektstruktur:

- `src/B2B.Portal.Domain`: Entitaeten, Enums, Value Objects, pure Domain Services.
- `src/B2B.Portal.Application`: Commands, Services, Ports.
- `src/B2B.Portal.Infrastructure`: Cosmos, InMemory, Directory, Email, Import, Queue.
- `src/B2B.Portal.Api`: ASP.NET Core Minimal API.
- `src/B2B.Portal.Worker`: Worker Host und Job Handler.
- `src/B2B.Portal.Web`: React/Vite/Fluent UI.
- `tests`: Domain-, Application-, Architecture- und Integrationstests.
- `infra`: Bicep.

