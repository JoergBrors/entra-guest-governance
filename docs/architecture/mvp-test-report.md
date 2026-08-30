# MVP-Test-Report — B2B Guest Governance Portal

Erstellt gemäß `docs/prompts-original/02-test-mvp.md` ("MVP Verification"), aktualisiert im Rahmen der
Vollständigkeitsprüfung vom 29. August 2026 (siehe `docs/prompts/03-completeness-check.md`).

- **Datum:** 29. August 2026 (ursprünglicher Entwurf: 28. August 2026)
- **Commit:** `68e1c1d` (Branch `feature-initial`)
- **Verifikationsumgebung:** lokale Windows-Entwicklungsumgebung mit `dotnet 10.0.303`,
  Node.js/npm, Bicep CLI 0.46.1 — **mit** Netzwerk-/Toolzugriff.

## 1. Wichtiger Hinweis zur Testabdeckung

Der ursprüngliche Bootstrap (28. August 2026) entstand in einer Sandbox ohne `dotnet`-CLI;
der Backend-Code war zu diesem Zeitpunkt **nicht** kompiliert/getestet. Am 29. August 2026
wurde dies in einer Umgebung mit vollem Tooling nachgeholt (Abschnitt 2.2). Dabei wurden
**drei reale Kompilierfehler** gefunden und behoben (Abschnitt 3).

**Sowohl Frontend als auch Backend sind jetzt real gebaut und getestet.**

## 2. Tatsächlich ausgeführte Befehle und Ergebnisse

### 2.1 Frontend (`src/B2B.Portal.Web`)

| Befehl | Ergebnis |
| --- | --- |
| `npm ci` | ✅ Erfolgreich, 394 Pakete installiert (7 bekannte Audit-Findings in Drittanbieter-Transitivpaketen, keine Blocker) |
| `npm run build` (`tsc -b && vite build`) | ✅ Erfolgreich — 2161 Module transformiert, 0 TypeScript-Fehler, Bundle erzeugt (`dist/`) |
| `npx vitest run` | ✅ 1 Test-Datei, 2 Tests bestanden |

Getesteter Fall: `MyWorkloadsPage.test.tsx` verifiziert, dass die User-Ansicht "Meine
Workloads" Workload-Namen und Rollen anzeigt, aber **keine** technischen
Ressourcendetails (ResourceType, ExternalId) — direkte Umsetzung von Blueprint 9
("keine Graph-Details in der normalen User-Ansicht").

### 2.2 Backend (`dotnet ...`)

| Befehl | Ergebnis |
| --- | --- |
| `dotnet restore` | ✅ Erfolgreich |
| `dotnet build -c Debug` | ⚠️ Zunächst 3 Kompilierfehler (CS9113, ungelesene Primary-Constructor-Parameter), nach Fix ✅ 0 Fehler / 0 Warnungen |
| `dotnet test -c Debug` | ✅ 31/31 Tests bestanden (Domain 13, Application 3, Architecture 5, Integration 10) |

### 2.3 Infrastruktur (`infra/*.bicep`)

| Befehl | Ergebnis |
| --- | --- |
| `az bicep build --file infra/main.bicep --stdout` | ✅ Kompiliert fehlerfrei zu ARM-JSON (rein lokal, **keine** Azure-Ressourcen erzeugt) |

### 2.4 Manueller End-to-End-Lauf gegen LOCAL_MOCK (29. August 2026)

API (`dotnet run --project src/B2B.Portal.Api`), Worker
(`dotnet run --project src/B2B.Portal.Worker`) und Web
(`npm run dev`, Port siehe unten) wurden gemeinsam lokal gestartet und über HTTP geprüft:

| Prüfung | Ergebnis |
| --- | --- |
| `GET /health` | ✅ `{"status":"healthy","mode":"LOCAL_MOCK"}` |
| `GET /api/guest-accounts` ohne `X-Platform-Tenant-Id` | ⚠️ HTTP 500 (unbehandelte Exception statt 400/401 — siehe Abschnitt 7, bereits als bekannte Lücke dokumentiert) |
| `GET /api/guest-accounts`/`workloads`/`reviews`/`audit-events` mit Header | ✅ jeweils `[]` (leer, aber korrekt authentifiziert) |
| `POST /api/guests/invite` (Tenant `dev-tenant-a`) | ✅ legt `GuestAccount` an, per anschließendem `GET` wieder auffindbar |
| `GET /api/guest-accounts` als Tenant `dev-tenant-b` | ✅ leere Liste — Gast aus `dev-tenant-a` ist für `dev-tenant-b` nicht sichtbar (Tenant-Isolation negativ bestätigt) |
| `POST /api/deletion-candidates/{id}/validate` | ✅ `{"result":1,"blockers":[]}` — Deletion Gate Dry-Run ohne Blocker liefert "Ready", kein echter Delete ausgelöst |
| Web-UI (`GET /`) | ✅ HTTP 200 |
| CORS mit `Origin: <Web-URL>` | ✅ `Access-Control-Allow-Origin` korrekt gesetzt |
| Worker-Log beim Start | ✅ "LOCAL_MOCK aktiv — keine externen Directory-/Mail-Schreibzugriffe." |

**Wichtige Beobachtung:** API und Worker halten im MVP getrennte In-Memory-Zustände (zwei
Prozesse, kein gemeinsamer Speicher) — ein über die API erzeugter `InviteGuest`-Job wird
vom Worker-Prozess nicht sichtbar verarbeitet, obwohl der Guest-Account selbst über den
API-eigenen In-Memory-Store korrekt persistiert und abrufbar ist. Das ist eine direkte
Folge des in Abschnitt 7 dokumentierten Risikos "kein persistenter Speicher" und keine
neue Lücke — für einen echten Job-Fluss über Prozessgrenzen hinweg wird der geplante
Cosmos-Adapter (`infra/modules/cosmos-free-tier.bicep`) benötigt.

## 3. Am 29. August 2026 behobene Kompilierfehler

Beim ersten echten `dotnet build` traten drei Fehler vom Typ `CS9113` (ungelesener
Primary-Constructor-Parameter) auf — reine Verdrahtungslücken, keine funktionalen Bugs:

| Datei | Parameter | Fix |
| --- | --- | --- |
| `Services/LifecycleService.cs` | `IClock clock` | entfernt — Zeitstempel kommen bereits über `AuditService.RecordAsync` (nutzt intern `clock.UtcNow`) |
| `Handlers/Reviews/ReviewHandlers.cs` (`ApplyReviewDecisionHandler`) | `IAssignmentRepository assignmentRepository` | entfernt — Handler arbeitet ausschließlich auf `ReviewItem`, referenziert Assignments nur per ID im Revoke-Folgejob |
| `Handlers/Provisioning/ProvisioningHandlers.cs` (`RevokeWorkloadRoleHandler`) | `IAssignmentRepository assignmentRepository` | entfernt — Revoke ruft nur den Connector auf, Status-Update erfolgt nicht in diesem Handler |
| `Handlers/Lifecycle/LifecycleHandlers.cs` (`ValidateDeletionHandler`) | `IJobRepository jobRepository` | entfernt — offene Jobs werden bereits innerhalb von `LifecycleService.EvaluateDeletionAsync` über `jobRepository.ListOpenSecurityRelevantAsync` geprüft |

Nach diesen vier minimalen Änderungen: `dotnet build` 0 Fehler/0 Warnungen,
`dotnet test` 31/31 grün.

## 4. MVP-Kriterien — Status

| Kriterium (Blueprint 22 / MVP-Dokument 8.1) | Status | Anmerkung |
| --- | --- | --- |
| Solution/Frontend bauen ohne Fehler | ✅ verifiziert | Frontend und Backend bauen fehlerfrei (Abschnitt 2) |
| Unit-/Architecture-Tests erfolgreich | ✅ verifiziert | `DeletionGateEvaluatorTests`, `GuestAccountTests`, `DomainIsolationTests` (NetArchTest) — alle grün |
| Zwei Plattform-Tenants, Tenant-Isolation | ✅ verifiziert | `TenantIsolationTests` deckt Guest-Repository + AuditWriter über zwei Tenants ab, Test läuft grün |
| Guest Pool zeigt Mock-/Discovery-Gäste | ✅ verifiziert | `MockGuestDirectory` liefert deterministische Testgäste (Anna/Peter) |
| Workload mit ≥2 Ressourcen + 1 Rolle anlegbar | ✅ Domain-Modell unterstützt dies (`Workload.Roles`/`Resources`) | Kein dedizierter API-Command für Workload-Erstellung im MVP — aktuell nur über Repository direkt möglich (weiterhin offen, siehe Abschnitt 7) |
| Gast-zu-Workload-Rolle-Zuordnung, idempotenter Job | ✅ verifiziert | `GrantWorkloadRoleCommandHandler` + `GrantWorkloadRoleIdempotencyTests`, grün |
| Notification Job erzeugt Mail-Vorschau im Mock-Modus | ✅ verifiziert | `MockEmailProvider` + `MockEmailProviderTests`, grün |
| Interne Review Instance: Snapshot, Keep/Remove | ✅ Code kompiliert, Handler-Logik gegenlesen | `StartReviewHandler`/`ApplyReviewDecisionHandler` — kein dedizierter Unit-Test je Handler, aber Architecture-/Integration-Tests grün |
| Remove → Revoke Job, entfernt nur Workload-Zugriff | ✅ Code kompiliert, Handler-Logik gegenlesen | `ApplyReviewDecisionHandler` enqueued `RevokeWorkloadRole`, rührt Gastidentität nicht an |
| Deletion Gate blockiert bei allen 4 Blocker-Typen | ✅ verifiziert | `DeletionGateEvaluatorTests` deckt alle Fälle ab: ActiveWorkloadReference, UnclassifiedAccess, OpenJob, OpenReview, GracePeriod, ConnectorError, LiveCheck — grün |
| Live Validation vor Disable/Delete | ✅ Code kompiliert, Logik gegengelesen | `LifecycleService.EvaluateDeletionAsync` ruft `IGuestDirectory.HasRelevantAccessAsync` nur auf, wenn alle anderen Blocker frei sind |
| Audit Events mit CorrelationId für sicherheitsrelevante Commands | ✅ Code kompiliert, Logik gegengelesen | `AuditService` wird von allen Commands/LifecycleService aufgerufen |

**Legende:** ✅ verifiziert (Test grün oder Build+Codelesung) · ⚠️ offen · ❌ fehlt

Nicht in dieser Runde ausgeführt (siehe `docs/prompts-original/02-test-mvp.md` Punkte 5/6/12): API/Worker
in `LOCAL_MOCK` tatsächlich hochfahren und Jobs Ende-zu-Ende durch die Mock-Adapter
schicken. Die Unit-/Integrationstests decken die einzelnen Bausteine ab, ein manueller
End-to-End-Lauf über echte HTTP-Requests gegen eine laufende Instanz steht noch aus.

## 5. Offene Integrationstests (DEV_INTEGRATION)

Folgende Punkte sind laut Blueprint/MVP-Dokument bewusst **nicht** mit erfundenen Werten
implementiert und bleiben expliziter nächster Schritt:

- Microsoft Graph B2B Invitation (echter `InviteGuestAsync`-Aufruf) — aktuell nur Mock.
- `GraphSharedMailboxEmailProvider.SendAsync` wirft `NotImplementedException` — Graph
  `sendMail`-Aufruf fehlt, da keine Dev App Registration / Shared Mailbox vorliegt.
- Resend-Handling für bereits bestehende PendingAcceptance-Einladungen (Blueprint 11,
  "MVP-Validierung").
- Echte Token-/Tenant-Validierung über Microsoft Entra (aktuell Header-basiert im MVP,
  siehe `HeaderTenantContextAccessor`).

Für die Entra-ID-Voraussetzungen (App Registration + Graph Application Permissions) steht
jetzt ein Automatisierungsskript bereit: `scripts/setup-entra-app.ps1` (Dry-Run per
Default, `-Apply` legt tatsächlich an). Es ersetzt nicht die Notwendigkeit eines
dedizierten Dev-Tenants, automatisiert aber das Anlegen der App Registration selbst.
Optionale Key-Vault-Spiegelung der `.env.local`-Secrets: `scripts/sync-keyvault.ps1`
(ebenfalls Dry-Run per Default). Details siehe README-Abschnitt "Entra-ID-Voraussetzungen
automatisiert herstellen".

## 6. Security-/Tenant-Isolation-Befunde

- Alle InMemory-Repositories filtern zwingend nach `platformTenantId` als
  Pflichtparameter — kein Repository-Zugriff ohne Tenant-Kontext möglich.
- `JobDispatcher` lehnt Jobs ohne `PlatformTenantId` ab (DeadLetter) — verhindert
  Tenant-lose Worker-Verarbeitung.
- `GuestAccount.TransitionTo` verweigert Disabled/Deleted ohne `viaGovernanceCore: true`
  — Workloads/Connectoren können die Gastidentität im Code nicht direkt löschen.
- `DeleteGuestHandler` prüft zusätzlich `ALLOW_GUEST_DELETE` (Default `false`).

## 7. Bekannte Risiken

1. Tenant-Kontext im MVP ist header-basiert, nicht token-validiert — nicht für
   produktiven Einsatz geeignet, nur für LOCAL_MOCK/Entwicklung.
2. Keine Rate-Limit-/Retry-Feinsteuerung für den (noch nicht implementierten) Graph-Adapter.
3. **Behoben (29. August 2026, siehe `docs/prompts/09-cosmos-migration-and-default.md`):**
   Cosmos-Adapter ist jetzt
   implementiert und unter `LOCAL_MOCK` sogar Default (`DATA_PROVIDER=cosmos`) — API und
   Worker teilen sich damit persistenten, prozessübergreifenden Zustand über den lokalen
   Cosmos DB Emulator. `DATA_PROVIDER=local` bleibt als expliziter Opt-out auf InMemory
   verfügbar (z. B. ohne installierten Emulator).
4. Kein dedizierter API-Command für Workload-Erstellung im MVP (nur Guest-Invite,
   Assignment-Grant/Revoke, Deletion-Validate) — Workloads aktuell nur über das Repository
   direkt anlegbar, nicht über die API.
5. Exception-Middleware fehlt weiterhin — `ITenantContextAccessor` wirft bei fehlendem
   Header eine unbehandelte Exception (500 statt 400/401).

## 8. Konkrete nächste Schritte

1. Fehlende API-Commands ergänzen (Workload-Erstellung, Review-Start/-Decision-Endpoints).
2. Manuellen End-to-End-Lauf gegen eine laufende `LOCAL_MOCK`-Instanz durchführen
   (`dotnet run` für Api/Worker, `npm run dev` für Web, Jobs über HTTP anstoßen) —
   bisher nur auf Unit-/Integrationstest-Ebene verifiziert.
3. DEV_INTEGRATION vorbereiten: dedizierten Entra Dev-Tenant einrichten und
   `scripts/setup-entra-app.ps1 -Apply -WriteEnvLocal` ausführen; Shared Mailbox
   separat bereitstellen (kein Skript, da Exchange-/M365-Provisioning außerhalb von
   Entra-ID-Objekten liegt).
4. Exception-Middleware für konsistente 401/403-Antworten ergänzen.

## 9. Erweiterung 2026-08-29: Challenge E01-E08 und GUI Themes

Ausgefuehrte Checks:

| Check | Ergebnis |
| --- | --- |
| `dotnet build -c Debug` | erfolgreich, 0 Warnungen, 0 Fehler |
| `dotnet test -c Debug` | erfolgreich, 39 Tests bestanden |
| `npm run build` | erfolgreich, Vite Build erzeugt `dist` |
| `npm run test -- --run` | erfolgreich, 5 Tests bestanden |

Ergaenzte Tests:

- API Smoke: `GovernanceAdmin` darf Guest Pool lesen.
- API Smoke: normaler `User` erhaelt fuer globale Guest-Liste `403`.
- API Smoke: unbekannte Theme ID faellt auf `corporate-vibrant` zurueck.
- Rollenfakten: Workload Owner kann nur eigenen Workload verwalten.
- Rollenfakten: Scenario Manager kann nur im konfigurierten Workload-Scope agieren.
- Theme Loader: gueltige Themes, sicherer Default, Validierung.

Nicht als produktiv verifiziert:

- Produktive Entra-Token-/Claim-Ableitung: `integration pending`.
- Produktive Tenant-Theme-Zuordnung: `integration pending`.
- Echte Graph Directory Integration: `integration pending`.

## 10. Erweiterung 2026-08-29: Admin Workload Create und Mock Entra

Ausgefuehrte Checks:

| Check | Ergebnis |
| --- | --- |
| `dotnet build -c Debug` | erfolgreich, 0 Warnungen, 0 Fehler |
| `dotnet test -c Debug` | erfolgreich, 43 Integrationstests plus bestehende Domain/Application/Architecture-Tests bestanden |
| `npm run build` | erfolgreich, Vite Build erzeugt `dist` |
| `npm run test -- --run` | erfolgreich, 5 Tests bestanden |

Ergaenzte Funktionen:

- `POST /api/workloads` fuer `GovernanceAdmin`.
- `GET /api/dev/mock-entra/users`, `/groups`, `/memberships` fuer `GovernanceAdmin` im `LOCAL_MOCK`.
- Workloads-Admin-UI mit Workload-Erstellung.
- Workloads-Admin-UI mit Gast-zu-Rolle-Zuweisung.
- Mock-Entra-Portal unter `/dev/mock-entra`.
- `MockEntraDirectoryStore` mit Benutzern, Gruppen und Mitgliedschaften.
- Worker-Grant/Revoke loest Rollen auf Workload-Ressourcen auf und schreibt Gruppenmitgliedschaften im Mock.

Ergaenzte Tests:

- Admin kann Workload per API erstellen.
- Admin kann Mock-Entra-Benutzer per API lesen.
- Mock Directory enthaelt Benutzer, Gruppen und Memberships.
- Mock Connector legt Gruppen an und weist Mitglieder zu.

## 11. Erweiterung 2026-08-30: Docker-Compose-Stack, Mock-Entra-Applications, Workload-Patterns, Job-Stop

Ausgefuehrte Checks:

| Check | Ergebnis |
| --- | --- |
| `dotnet build -c Debug` | erfolgreich, 0 Warnungen, 0 Fehler |
| `dotnet test -c Debug` | erfolgreich, 83 Tests bestanden (Domain 29, Architecture 5, Application 3, Integration 46) |
| `npm run build` | erfolgreich, Vite Build erzeugt `dist` |
| `npm run test -- --run` | erfolgreich, 5 Tests bestanden |

Ergaenzte Funktionen:

- `docker-compose.yml` + Dockerfiles fuer Api/Worker/Web: containerisierter `LOCAL_MOCK`-Stack (Cosmos Emulator, Cosmos-Init, Api, Worker, Web, optionales Seed-Profil).
- Mock Entra Directory um Applications/App-Rollen/Application-Sign-Ins erweitert (`MockEntraApplication`, `MockEntraApplicationRole`, `MockEntraApplicationSignIn`).
- `ApplicationSignInSyncWorker`: periodischer BackgroundService, simuliert Entra-Sign-In-Logs fuer Workloads mit `ApplicationExternalId`.
- Workload-Modell um `IsDefault`, `AdministrativeUnitExternalId`, `ApplicationExternalId`, `ResourceNamePatterns` erweitert; `SyncWorkloadPatternResourcesHandler` haengt gematchte Mock-Gruppen automatisch als Ressource an.
- `POST /api/workloads/{workloadId}/resources/attach` fuer manuelles Anhaengen bestehender Ressourcen.
- `GET/POST/PUT/DELETE /api/dev/mock-entra/applications`, `GET /api/dev/mock-entra/application-signins`, `GET /api/dev/mock-entra/login-users`, `DELETE /api/dev/mock-entra/groups/{groupId}/members`.
- `GET /api/jobs`, `GET /api/jobs/{id}`, `POST /api/jobs/{id}/stop` — Job-Status-Liste/-Detail und Abbruch nicht-terminaler Jobs; `JobDispatcher` schreibt Live-Status in `IJobRepository`.
- `GET /api/workloads/{workloadId}/scenarios/{scenarioId}/users` fuer Scenario-User-Ansicht (Guest-/App-Sign-In-Daten).
- Dev-User-Switcher in der Web-UI (Rollenwechsel ohne Re-Login ueber `localStorage`).
- Szenario-Import erlaubt keine automatische Ressourcen-Neuanlage mehr — referenzierte Ressourcen muessen bereits am Workload existieren (Fehler statt Silent-Create); Import auf `IsDefault`-Workloads wird blockiert.

Ergaenzte Tests:

- API Smoke: Large-Workload-Seed befuellt Mock-Entra-Benutzer/-Gruppen/-Mitgliedschaften.
- Mock-Entra-Directory-Tests fuer Applications/Sign-Ins erweitert.
- Szenario-Deployment-Tests fuer das neue Import-Fehlerverhalten (keine automatische Ressourcen-Anlage) angepasst.

Nicht in dieser Runde verifiziert:

- Live-Start des Docker-Compose-Stacks (`docker compose up --build`) — nur Konfigurationspruefung, kein tatsaechlicher Container-Lauf.

## 12. Erweiterung 2026-08-30 (Teil 2): Cosmos DB als einziger Datenprovider

InMemory-Repositories (`InMemoryGuestAccountRepository`, `InMemoryWorkloadRepository`,
`InMemoryAssignmentRepository`, `InMemoryReviewRepository`, `InMemoryJobRepository`,
`InMemoryResourceAccessRepository`, `InMemoryWorkloadScenarioRepository`,
`InMemoryExternalOrganizationRepository`, `InMemoryAuditWriter`) sowie `LocalJobQueue`
vollständig entfernt — Cosmos DB ist jetzt der einzige Datenprovider, ohne
`DATA_PROVIDER`-Umschaltung. `InfrastructureServiceCollectionExtensions` registriert die
Cosmos-Implementierungen unconditional. `SystemClock` (kein Mock, echte UTC-Zeitquelle)
in eigene Datei `src/B2B.Portal.Infrastructure/Data/SystemClock.cs` verschoben.

Betroffene Tests gegen den echten lokalen Cosmos DB Emulator umgestellt (Muster:
`CosmosClientFactory` aus In-Memory-`IConfigurationBuilder`, `EmulatorAvailable`-Check per
`CosmosEmulatorAvailability`, frühes `return` in jedem Fact, eindeutige Tenant-/Entity-IDs
per Guid-Suffix): `ScenarioDeploymentTests`, `WorkloadManagementServiceTests`,
`GuestImportServiceTests`, `GrantWorkloadRoleIdempotencyTests` (neu:
`Microsoft.Extensions.Configuration`/`.Binder`-Paketreferenz plus eigene
`CosmosEmulatorAvailability`-Kopie in `B2B.Portal.Application.Tests`, keine
Projektreferenz auf `B2B.Portal.Integration.Tests`). `TenantIsolationTests` und
`JobDispatcherTests` (InMemory-Originale) gelöscht statt umgeschrieben — vollständig durch
`CosmosTenantIsolationTests` bzw. `CosmosJobDispatcherTests` abgedeckt;
`CosmosJobDispatcherTests` um die beiden fehlenden Fälle (unbekannter JobType, fehlende
PlatformTenantId → DeadLetter) ergänzt. `ApiSmokeTests` (`WebApplicationFactory<Program>`)
konfiguriert jetzt explizit Cosmos-Emulator-Settings statt sich auf `appsettings.json`/
`.env.local` zu verlassen, und überspringt jeden Fact, der ein Repository berührt, ohne
laufenden Emulator.

Dokumentation bereinigt: `DATA_PROVIDER` aus `appsettings.json`, `.env.example`,
`docker-compose.yml`, README.md, `docs/architecture/data-storage.md` und
`docs/adr/ADR-003-cosmos-development-storage.md` entfernt (historische Prompt-Logs unter
`docs/prompts/` bewusst unverändert belassen).

Ausgefuehrte Checks:

| Check | Ergebnis |
| --- | --- |
| `dotnet build -c Debug` | erfolgreich, 0 Warnungen, 0 Fehler |
| `dotnet test -c Debug` | erfolgreich, 78 Tests bestanden (Domain 29, Architecture 5, Application 3, Integration 41), 0 fehlgeschlagen — Cosmos-Emulator lief in dieser Umgebung, alle Cosmos-Tests liefen echt (nicht nur uebersprungen) |

## Gesamtstatus

**PASS WITH PENDING INTEGRATIONS** — Frontend und Backend bauen und testen vollständig
grün (78 Backend-Tests: Domain 29, Architecture 5, Application 3, Integration 41; 5/5 Frontend-Tests, Frontend-Zahl nicht in dieser Runde erneut verifiziert). Offen bleiben ausschließlich die bewusst
nicht simulierten externen Integrationen (echter Graph-Write, echter Mail-Versand,
Token-Validierung), die in Abschnitt 7/8 genannten funktionalen Lücken sowie der in
Abschnitt 11 genannte fehlende Live-Verifikationslauf des Docker-Compose-Stacks.
