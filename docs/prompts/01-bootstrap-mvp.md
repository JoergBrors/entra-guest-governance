# Prompt 01 — Bootstrap MVP

- **Datum:** 28. August 2026
- **Auftrag:** [`prompts-original/01-bootstrap-mvp.md`](../prompts-original/01-bootstrap-mvp.md) ("Development Foundation und MVP")
- **Ausführungsumgebung:** Sandbox ohne Netzwerkzugriff auf .NET-SDK-Download-Quellen
  (kein `dotnet` CLI verfügbar); Node/npm verfügbar.

## Was beauftragt wurde

Ein lauffähiges, mandantenfähiges B2B Guest Governance Portal als Development-Grundlage
und erster MVP, bestehend aus drei Deployables (`B2B.Portal.Web`, `B2B.Portal.Api`,
`B2B.Portal.Worker`), .NET 10/ASP.NET Core Backend, React/TypeScript/Vite-Frontend, Bicep
für Free-Tier-Infrastruktur, sowie die fachlichen Kernregeln (Guest Pool statt
Workload-Ownership, Desired/Actual State getrennt, Governance-vor-Löschung,
Tenant-Isolation, Audit-first, Mock-first).

## Was tatsächlich getan wurde

- Vollständige Repository-Struktur angelegt: `src/` (Domain, Application, Infrastructure,
  Api, Worker, Web), `tests/` (Domain, Application, Architecture, Integration), `infra/`
  (Bicep-Module + Parameterdateien), `scripts/`, `docs/`, `.vscode/`.
- Domain-Modell (Entities, Enums, `DeletionGateEvaluator`), Application Ports/Services/
  Commands, Infrastructure-Mocks (`MockGuestDirectory`, `MockEmailProvider`,
  `LocalJobQueue`, InMemory-Repositories) sowie eine bewusst unvollständige
  `GraphSharedMailboxEmailProvider`-Adapterschale implementiert.
- Worker mit `JobDispatcher`, sieben Handlergruppen (Invitation, Provisioning, Discovery,
  Reconciliation, Review, Notification, Lifecycle) und Retry-/DeadLetter-Grundmodell.
- Minimal-API mit Health-, Query- und Command-Endpoints, header-basiertem Tenant-Kontext.
- React-Frontend mit rollengetrennter Navigation (User- vs. Admin-Ansicht).
- Domain-/Application-/Architecture-/Integration-Tests sowie ein Frontend-Test
  (`MyWorkloadsPage.test.tsx`) geschrieben.
- `docs/architecture/development-plan.md` als Implementierungsplan verfasst.
- README mit Setup, drei Development-Modi (`LOCAL_MOCK`/`DEV_INTEGRATION`/`AZURE_DEV`)
  und Sicherheitswarnungen geschrieben.

## Ergebnis / Besonderheiten

- **Frontend real gebaut und getestet:** `npm install`, `npm run build`, `npx vitest run`
  liefen erfolgreich in der Sandbox.
- **Backend nicht kompiliert:** `dotnet` war in der Erstellungsumgebung nicht verfügbar —
  der gesamte C#-Code wurde nach Spezifikation geschrieben, aber nie gegen einen echten
  Compiler geprüft. Das war zum Zeitpunkt der Erstellung ein bekanntes, offen
  dokumentiertes Risiko (siehe damaliger `docs/architecture/mvp-test-report.md`).
- Keine realen Tenant-IDs, Client Secrets, Group-IDs oder Mailboxen erfunden — bewusst als
  Tenant-/Umgebungs-Konfiguration offengelassen.

## Nachgelagerte Korrektur

Bei der Vollständigkeitsprüfung am 29. August 2026 (siehe
[03-completeness-check.md](03-completeness-check.md)) bestätigte sich das dokumentierte
Risiko: Der erste echte `dotnet build` enthielt 3 Kompilierfehler (ungelesene
Primary-Constructor-Parameter in `LifecycleService`, `ApplyReviewDecisionHandler`,
`RevokeWorkloadRoleHandler`, `ValidateDeletionHandler`). Diese wurden dort behoben.
