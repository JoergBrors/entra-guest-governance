# Development-Plan — B2B Guest Governance Portal

Erstellt gemäß Codex-Bootstrap-Prompt (`prompts/01-bootstrap-mvp.md`), Schritt 2
("Erstelle einen kurzen Implementierungsplan").

## 1. Ausgangslage

Frisches Repository, keine vorhandenen Quelldateien. Grundlage sind die beiden
bereitgestellten Projektdokumente (Blueprint + Development-/MVP-Leitfaden).

## 2. Vorgehen

Umsetzung entlang der Schichtung Domain → Application → Infrastructure → Worker/Api → Web,
damit jede Schicht nur auf die darunterliegende referenziert (siehe Blueprint Abschnitt 3.1
"Projektabhängigkeiten"):

1. **Domain** — Entities, Enums, ValueObjects, reine Fachlogik (`DeletionGateEvaluator`).
   Keine Pakete außer dem .NET-BCL.
2. **Application** — Ports (Interfaces) für Directory, Connector, JobQueue, EmailProvider,
   AuditWriter, Repositories, Clock. Application Services (`ProvisioningService`,
   `LifecycleService`, `AuditService`) und Commands (`InviteGuest`, `GrantWorkloadRole`,
   `RevokeWorkloadRole`).
3. **Infrastructure** — Mock-Implementierungen aller Ports (`MockGuestDirectory`,
   `MockResourceConnector`, `MockEmailProvider`, `LocalJobQueue`,
   `InMemory*Repository`), plus eine bewusst unvollständige `GraphSharedMailboxEmailProvider`-
   Adapterschale, die erst mit einer echten Dev-App-Registration aktiviert wird.
4. **Worker** — `JobEnvelope`, `IJobHandler`, `JobDispatcher` mit Tenant-Validierung und
   Retry/DeadLetter, sieben Handlergruppen (Invitation, Provisioning, Discovery,
   Reconciliation, Review, Notification, Lifecycle), `PollingWorker` als BackgroundService.
5. **Api** — Minimal API mit Health-, Query- und Command-Endpoints, zentraler
   `ITenantContextAccessor`.
6. **Web** — React/TypeScript/Vite mit Fluent UI, rollengetrennte Navigation (User-Ansicht
   "Meine Workloads" vs. Admin/Governance-Bereich).
7. **Tests** — Domain (Deletion Gate, GuestAccount-Invariante), Architecture
   (NetArchTest: Domain/Application dürfen Infrastructure/Graph/Azure nicht referenzieren),
   Application (Idempotenz, Notification-Mock), Integration (Tenant-Isolation,
   Worker-Dispatcher, API-Smoke via `WebApplicationFactory`), Frontend (Vitest).
8. **Infra** — Bicep-Module für Free-Tier-Ressourcen (Static Web Apps, Cosmos DB Free
   Tier, Azure Automation), sichere Parameter-Templates ohne echte IDs.

## 3. Bewusste Vereinfachungen im MVP

- Ein gemeinsamer Worker-Host statt sieben getrennter Deployments (siehe MVP-Dokument,
  "Kernentscheidung").
- Tenant-Kontext im MVP über einen HTTP-Header (`X-Platform-Tenant-Id`) statt vollständiger
  Entra-Token-Validierung — als expliziter nächster Schritt markiert (siehe unten).
- `GraphSharedMailboxEmailProvider` wirft bewusst `NotImplementedException`, solange kein
  Dev-Tenant vorliegt — es werden keine Tenant-/Client-IDs erfunden.
- Review-Zuweisung an Reviewer sowie Workload-Template-Provisionierung sind im Domain-
  Modell vorbereitet, aber im MVP-Command-Set noch nicht vollständig verdrahtet.

## 4. Bekannte nächste Schritte (vor Pilotbetrieb)

1. Echte Token-Validierung (Microsoft.Identity.Web) statt Header-basiertem Tenant-Kontext.
2. Microsoft.Graph SDK einbinden, sobald eine Dev App Registration mit
   `Mail.Send`/`User.Invite.All`/`Group.ReadWrite.All`-Rechten vorliegt (DEV_INTEGRATION).
3. Cosmos DB Adapter für Repositories (aktuell InMemory) gemäß `infra/modules/cosmos-free-tier.bicep`.
4. Web-UI: Review-Entscheidungen (Keep/Remove) und Assignment-Erstellung aus der
   Workloads-Admin-Ansicht heraus (aktuell nur über die API direkt möglich).
5. Exception-Middleware für konsistente 401/403-Antworten (aktuell wirft
   `ITenantContextAccessor` bei fehlendem Header eine Exception, die als 500 sichtbar wird).

Siehe auch `docs/architecture/mvp-test-report.md` für den aktuellen Verifikationsstatus.
