# Auftrag: B2B Guest Governance Portal - Development Foundation und MVP

Du arbeitest im aktuellen Git-Repository. Baue eine lauffähige Development-Grundlage und den ersten MVP für ein mandantenfähiges B2B Guest Governance Portal.

VERBINDLICHE TECHNISCHE FESTLEGUNGEN
- Backend: .NET 10 LTS, TargetFramework net10.0, ASP.NET Core 10, C#.
- Frontend: React + TypeScript + Vite; Fluent UI als UI-Basis.
- Tests: xUnit für .NET; Frontend-Testsetup passend zum Vite/React-Stack.
- IaC: Bicep.
- MVP Deployables: B2B.Portal.Web, B2B.Portal.Api, B2B.Portal.Worker.
- Keine direkte Microsoft-Graph-Nutzung in Domain/Application.
- Alle externen Systeme liegen hinter Interfaces/Adaptern.
- Default Development Mode ist LOCAL_MOCK. Keine echten Graph-Schreibzugriffe und keine echten E-Mails im Default.

FACHLICHE KERNREGELN
1. Zentraler Guest Pool; ein GuestAccount gehört nicht einem einzelnen Workload.
2. Workloads referenzieren Gäste über GuestWorkloadAssignment und eine fachliche WorkloadRole.
3. Eine WorkloadRole kann mehrere technische Ressourcen abbilden.
4. Desired State und Actual State sind getrennt.
5. Unclassified Access muss sichtbar sein und blockiert eine Gastlöschung.
6. Workloads/Connectoren dürfen niemals eine Gastidentität direkt löschen.
7. Nur Lifecycle/Governance darf Disable/Delete freigeben.
8. Vor Disable/Delete muss eine Live Validation des Actual State stattfinden.
9. Jede relevante Aktion besitzt PlatformTenantId, DirectoryTenantId (wo relevant), CorrelationId und AuditEvent.
10. DirectoryOperations und Jobs müssen idempotent und retryfähig sein.

ERSTELLE DIE REPOSITORY-STRUKTUR
- .vscode: launch.json, tasks.json, settings.json, extensions.json
- src/B2B.Portal.Web
- src/B2B.Portal.Api
- src/B2B.Portal.Application
- src/B2B.Portal.Domain
- src/B2B.Portal.Infrastructure
- src/B2B.Portal.Worker
- tests: Domain, Application, Architecture, Integration
- infra: main.bicep + modules + dev/poc Parameterdateien als sichere Templates
- prompts, scripts, docs/architecture, docs/adr
- Directory.Build.props, global.json, .editorconfig, .gitignore, .env.example, README.md

DOMAIN-MODELL MINDESTENS
- Tenant/DirectoryConnector metadata
- ExternalOrganization
- GuestAccount
- Workload
- WorkloadRole
- WorkloadResource
- GuestWorkloadAssignment
- ResourceAccess (classified/unclassified)
- ReviewDefinition, ReviewInstance, ReviewItem
- DirectoryOperation / Job
- AuditEvent

APPLICATION PORTS MINDESTENS
- IGuestDirectory / IResourceConnector
- IJobQueue
- IEmailProvider
- IAuditWriter
- Repositories für Guest/Workload/Assignment/Review/Job
- IClock für deterministische Tests

INFRASTRUCTURE PROVIDER
- MockGuestDirectory: deterministische Gäste, Gruppen und Actual Access
- MockEmailProvider: schreibt eine strukturierte E-Mail-Vorschau in Log/Test-Sink
- LocalJobQueue: thread-safe lokale Queue für Development/Tests
- InMemory/Development repositories
- Graph-Adapter nur als klar getrennte, zunächst sichere Implementierungsschale; keine unbekannten IDs erfinden
- GraphSharedMailboxEmailProvider als Adapterstruktur für Microsoft Graph sendMail; Absender aus Konfiguration

WORKER
- .NET 10 Worker Service mit BackgroundService
- JobEnvelope mit Tenant, JobType, Entity, CorrelationId, DesiredStateHash, Payload
- Dispatcher + IJobHandler
- Handlergruppen: Invitation, Provisioning, Discovery, Reconciliation, Review, Notification, Lifecycle
- Retry-/DeadLetter-Grundmodell
- Idempotenzprüfung vor technischen Writes

API
- Health Endpoint
- Query Endpoints für Guests, Workloads, Jobs, Reviews, Audit
- Command Endpoints für Workload Assignment, Invite Request, Review Start/Decision, Revoke und Deletion Dry Run
- Tenant Context serverseitig zentral kapseln; Tests müssen Tenant-Leaks verhindern

WEB MVP
- reduzierte moderne Benutzeroberfläche
- User-Ansicht zeigt ausschließlich zugeordnete Workloads und die eigene Rolle/Zugriffe
- Admin/Governance-Ansichten für Guest Pool, Workloads, Reviews, Jobs, Audit
- keine Graph-Details in der normalen User-Ansicht

TESTS / QUALITY GATES
- dotnet build muss erfolgreich sein
- dotnet test muss erfolgreich sein
- Architecture Test: Domain darf Infrastructure/Graph nicht referenzieren
- Tenant Isolation Tests mit mindestens zwei Platform Tenants
- Deletion Gate Tests für: aktive Workload Reference, Unclassified Access, offener Job, Connector Error, Live Check
- Idempotenztest für GrantWorkloadRole
- Notification Mock Test
- Worker Dispatcher Test
- API Smoke Tests
- Frontend Build und Tests

VORGEHEN
1. Untersuche zuerst den bestehenden Repository-Inhalt. Überschreibe keine wertvollen vorhandenen Dateien blind.
2. Erstelle einen kurzen Implementierungsplan in docs/architecture/development-plan.md.
3. Implementiere in kleinen Schritten.
4. Führe nach jedem größeren Schritt Build/Tests aus.
5. Behebe Build- und Testfehler, bevor du weitermachst.
6. Führe am Ende einen vollständigen lokalen Quality-Gate-Lauf aus.
7. Schreibe README mit Setup, LOCAL_MOCK Start, DEV_INTEGRATION Konzept und Sicherheitswarnungen.
8. Erstelle docs/architecture/mvp-test-report.md mit ausgeführten Befehlen, Ergebnissen und verbleibenden offenen Punkten.

WICHTIG
- Keine realen Tenant IDs, Client Secrets, Group IDs oder Mailboxen erfinden.
- Keine produktiven Graph Writes durchführen.
- Wenn echte Integration nicht möglich ist, implementiere den Adapter und teste ihn mit Mocks; markiere den echten Integrationstest als expliziten nächsten Schritt.
- Halte die Lösung einfach: zunächst drei Deployables, keine unnötigen Microservices.

Beginne jetzt mit Repository-Analyse, Plan und Implementierung.
