# Worker Model

Stand: 2026-08-29

Der Worker liegt in `src/B2B.Portal.Worker`.

Kernbestand:

- `Processing/JobDispatcher.cs`
- `Processing/IJobHandler.cs`
- Handlergruppen fuer Invitation, Provisioning, Discovery, Reconciliation, Reviews, Notifications und Lifecycle.

Der Dispatcher prueft `PlatformTenantId`, nutzt Retry/DeadLetter und delegiert an registrierte Handler.

