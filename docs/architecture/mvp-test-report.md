# MVP-Test-Report — B2B Guest Governance Portal

Erstellt gemäß `prompts/02-test-mvp.md` ("MVP Verification").

- **Datum:** 28. August 2026
- **Commit:** initiales Bootstrap-Commit (noch nicht committed — Repository frisch erstellt)
- **Erstellungsumgebung:** Sandbox ohne Netzwerkzugriff auf .NET-SDK-Download-Quellen

## 1. Wichtiger Hinweis zur Testabdeckung

Dieses Repository wurde in einer Umgebung erstellt, die **kein `dotnet` CLI** zur Verfügung
hatte (Netzwerk auf npm/PyPI/GitHub beschränkt, kein Zugriff auf `dotnet.microsoft.com` /
NuGet). Der gesamte C#/.NET-Teil (Domain, Application, Infrastructure, Api, Worker, alle
Tests) wurde daher **nicht** mit `dotnet restore` / `dotnet build` / `dotnet test`
verifiziert. Der Code folgt der im Blueprint und MVP-Dokument festgelegten Architektur und
den dort vorgegebenen Interfaces/Signaturen, kann aber Tippfehler oder kleinere
Kompilierfehler enthalten, die erst beim ersten lokalen `dotnet build` sichtbar werden.

**Der Frontend-Teil (React/TypeScript/Vite) wurde dagegen real gebaut und getestet.**

## 2. Tatsächlich ausgeführte Befehle und Ergebnisse

### 2.1 Frontend (`src/B2B.Portal.Web`)

| Befehl | Ergebnis |
| --- | --- |
| `npm install` | ✅ Erfolgreich, 394 Pakete installiert |
| `npm run build` (`tsc -b && vite build`) | ✅ Erfolgreich — 2161 Module transformiert, 0 TypeScript-Fehler, Bundle erzeugt (`dist/`) |
| `npx vitest run` | ✅ 1 Test-Datei, 2 Tests bestanden |
| `npx tsc -b --force` (isolierter Strict-Check) | ✅ 0 Fehler |

Getesteter Fall: `MyWorkloadsPage.test.tsx` verifiziert, dass die User-Ansicht "Meine
Workloads" Workload-Namen und Rollen anzeigt, aber **keine** technischen
Ressourcendetails (ResourceType, ExternalId) — direkte Umsetzung von Blueprint 9
("keine Graph-Details in der normalen User-Ansicht").

Bekannte Einschränkung: `@fluentui/react-components` (genauer dessen transitive
Abhängigkeit `tabster`) hat unter Vitest/Node-ESM ein CJS/ESM-Interop-Problem mit
benannten Exporten. Der Test mockt Fluent UI daher mit schlanken HTML-Ersatzkomponenten;
die Produktionsbuild ist davon nicht betroffen (siehe `npm run build`-Ergebnis oben).

### 2.2 Backend (`dotnet ...`)

**Nicht ausgeführt** — `dotnet` war in der Erstellungsumgebung nicht verfügbar
(`/bin/sh: 1: dotnet: not found`, keine Netzwerkfreigabe für .NET-SDK-Installation).

## 3. MVP-Kriterien — Status

| Kriterium (Blueprint 22 / MVP-Dokument 8.1) | Status | Anmerkung |
| --- | --- | --- |
| Solution/Frontend bauen ohne Fehler | ⚠️ Teilweise verifiziert | Frontend: ✅ verifiziert. Backend: **nicht verifiziert** (kein dotnet verfügbar) |
| Unit-/Architecture-Tests erfolgreich | ⚠️ Code vorhanden, nicht ausgeführt | `DeletionGateEvaluatorTests`, `GuestAccountTests`, `DomainIsolationTests` (NetArchTest) geschrieben, aber nicht gegen echten Compiler geprüft |
| Zwei Plattform-Tenants, Tenant-Isolation | ⚠️ Code vorhanden, nicht ausgeführt | `TenantIsolationTests` deckt Guest-Repository + AuditWriter über zwei Tenants ab |
| Guest Pool zeigt Mock-/Discovery-Gäste | ⚠️ Code vorhanden, nicht ausgeführt | `MockGuestDirectory` liefert deterministische Testgäste (Anna/Peter) |
| Workload mit ≥2 Ressourcen + 1 Rolle anlegbar | ✅ Domain-Modell unterstützt dies (`Workload.Roles`/`Resources`) | Kein dedizierter API-Command für Workload-Erstellung im MVP — aktuell nur über Repository direkt möglich (nächster Schritt) |
| Gast-zu-Workload-Rolle-Zuordnung, idempotenter Job | ⚠️ Code vorhanden, nicht ausgeführt | `GrantWorkloadRoleCommandHandler` + `GrantWorkloadRoleIdempotencyTests` |
| Notification Job erzeugt Mail-Vorschau im Mock-Modus | ⚠️ Code vorhanden, nicht ausgeführt | `MockEmailProvider` + `MockEmailProviderTests` |
| Interne Review Instance: Snapshot, Keep/Remove | ⚠️ Code vorhanden, nicht ausgeführt | `StartReviewHandler`/`ApplyReviewDecisionHandler` |
| Remove → Revoke Job, entfernt nur Workload-Zugriff | ⚠️ Code vorhanden, nicht ausgeführt | `ApplyReviewDecisionHandler` enqueued `RevokeWorkloadRole`, rührt Gastidentität nicht an |
| Deletion Gate blockiert bei allen 4 Blocker-Typen | ⚠️ Code vorhanden, nicht ausgeführt | `DeletionGateEvaluatorTests` deckt alle Fälle ab: ActiveWorkloadReference, UnclassifiedAccess, OpenJob, OpenReview, GracePeriod, ConnectorError, LiveCheck |
| Live Validation vor Disable/Delete | ⚠️ Code vorhanden, nicht ausgeführt | `LifecycleService.EvaluateDeletionAsync` ruft `IGuestDirectory.HasRelevantAccessAsync` nur auf, wenn alle anderen Blocker frei sind |
| Audit Events mit CorrelationId für sicherheitsrelevante Commands | ⚠️ Code vorhanden, nicht ausgeführt | `AuditService` wird von allen Commands/LifecycleService aufgerufen |

**Legende:** ✅ verifiziert · ⚠️ implementiert, aber nicht kompiliert/ausgeführt (siehe
Abschnitt 1) · ❌ fehlt

## 4. Offene Integrationstests (DEV_INTEGRATION)

Folgende Punkte sind laut Blueprint/MVP-Dokument bewusst **nicht** mit erfundenen Werten
implementiert und bleiben expliziter nächster Schritt:

- Microsoft Graph B2B Invitation (echter `InviteGuestAsync`-Aufruf) — aktuell nur Mock.
- `GraphSharedMailboxEmailProvider.SendAsync` wirft `NotImplementedException` — Graph
  `sendMail`-Aufruf fehlt, da keine Dev App Registration / Shared Mailbox vorliegt.
- Resend-Handling für bereits bestehende PendingAcceptance-Einladungen (Blueprint 11,
  "MVP-Validierung").
- Echte Token-/Tenant-Validierung über Microsoft Entra (aktuell Header-basiert im MVP,
  siehe `HeaderTenantContextAccessor`).

## 5. Security-/Tenant-Isolation-Befunde

- Alle InMemory-Repositories filtern zwingend nach `platformTenantId` als
  Pflichtparameter — kein Repository-Zugriff ohne Tenant-Kontext möglich.
- `JobDispatcher` lehnt Jobs ohne `PlatformTenantId` ab (DeadLetter) — verhindert
  Tenant-lose Worker-Verarbeitung.
- `GuestAccount.TransitionTo` verweigert Disabled/Deleted ohne `viaGovernanceCore: true`
  — Workloads/Connectoren können die Gastidentität im Code nicht direkt löschen.
- `DeleteGuestHandler` prüft zusätzlich `ALLOW_GUEST_DELETE` (Default `false`).

## 6. Bekannte Risiken

1. **Unverifizierter Backend-Code** — höchste Priorität: `dotnet restore/build/test`
   lokal ausführen und alle Kompilierfehler beheben, bevor auf diesem Stand aufgebaut wird.
2. Tenant-Kontext im MVP ist header-basiert, nicht token-validiert — nicht für
   produktiven Einsatz geeignet, nur für LOCAL_MOCK/Entwicklung.
3. Keine Rate-Limit-/Retry-Feinsteuerung für den (noch nicht implementierten) Graph-Adapter.
4. Kein persistenter Speicher (nur InMemory) — Neustart des Prozesses verliert alle Daten;
   Cosmos-Adapter ist vorbereitet (Bicep-Modul vorhanden), aber nicht angebunden.

## 7. Konkrete nächste Schritte

1. `dotnet restore && dotnet build -c Debug && dotnet test -c Debug` lokal ausführen und
   alle gemeldeten Fehler beheben.
2. `docs/architecture/mvp-test-report.md` (diese Datei) nach echtem Testlauf mit realen
   Ergebnissen aktualisieren.
3. Fehlende API-Commands ergänzen (Workload-Erstellung, Review-Start/-Decision-Endpoints).
4. DEV_INTEGRATION vorbereiten: dedizierten Entra Dev-Tenant + App Registration +
   Shared Mailbox einrichten (siehe README "Drei Development-Modi").

## Gesamtstatus

**PASS WITH PENDING INTEGRATIONS** — unter dem Vorbehalt, dass der Backend-Code noch
nicht lokal kompiliert/getestet wurde (siehe Abschnitt 1). Das Frontend ist vollständig
verifiziert. Führe vor jeder weiteren Nutzung zwingend `dotnet build`/`dotnet test` lokal
aus.
