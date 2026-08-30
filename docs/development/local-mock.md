# Local Mock

Stand: 2026-08-30 (aktualisiert: Identity Provider + JWT-Login)

`LOCAL_MOCK` bleibt der Default fuer lokale Entwicklung.

## Identity Provider

Der aktive Identity Provider wird backend-seitig konfiguriert (`IDENTITY_PROVIDER`
env var / `.env.local`), nicht mehr client-frei ueber Header:

- `EntraIdMock` (Default unter `LOCAL_MOCK`) — Login ueber `POST /api/auth/mock/login`
  mit `{ mail }`, kein Passwort. Backend prueft die Existenz im Mock-Entra-Stamm
  (`MockEntraDirectoryStore`), liest Rollen und Tenant des gewaehlten Benutzers und stellt
  ein JWT aus (`B2B.Portal.Infrastructure.Auth.MockJwtIssuer`).
- `EntraId` — Platzhalter fuer echtes OIDC gegen einen Entra-Tenant. `integration pending`,
  siehe `docs/architecture/graph-integration.md` fuer das etablierte Muster.

Signing-Key: `JWT_SIGNING_KEY` (env var). Ohne gesetzten Wert erzeugt der Prozess einen
zufaelligen Dev-Ephemeral-Key (Warnung beim Start, niemals als echtes Secret verwenden —
Tokens werden bei jedem Neustart automatisch ungueltig). Token-Laufzeit: 8 Stunden, kein
Refresh-Flow.

Claims im Token: `sub` (Mock-ObjectId), `email`, `role` (mehrfach, ein Claim je Rolle),
`platformTenantId` (aus dem gewaehlten Mock-User abgeleitet, nicht separat waehlbar),
`scenarioManagerWorkloadId` (mehrfach, serverseitig aus `WorkloadScenario.ScenarioManagers`
abgeleitet — ersetzt den frueheren freien `X-Scenario-Manager-Workload-Ids`-Header, der nie
tatsaechlich vom Client gesetzt wurde).

`POST /api/auth/mock/logout` existiert als No-op — JWT ist zustandslos, es gibt serverseitig
nichts zu invalidieren. Sign-out ist rein clientseitig (Token aus `sessionStorage` loeschen).

Alle Endpunkte ausser `/health`, `/api/auth/mock/login`, `/api/ui/configuration` und
`/api/dev/mock-entra/login-users` verlangen ein gueltiges Bearer-Token
(`Authorization: Bearer <token>`), erzwungen ueber eine ASP.NET-Core-`FallbackPolicy` in
`Program.cs`. Die bestehenden In-Handler-Rollenpruefungen (`IsGovernanceAdmin` etc.) bleiben
unveraendert — nur die Authentifizierungsschicht davor wurde ersetzt.

`X-Portal-Theme-Id` bleibt ein freier Header (reine UI-Praeferenz, kein Auth-/Identitaetsbezug).

Produktive Werte: `configuration required`.

## Mock Entra Directory

`LOCAL_MOCK` enthaelt einen lokalen Entra-ID-Mock in `MockEntraDirectoryStore`.

Der Mock-Stamm enthaelt:

- Gastbenutzer mit Object ID, UPN, Mail, DisplayName, GivenName, Surname, CompanyName, Department, JobTitle, Sponsor, AccountEnabled und UserType.
- Gruppen mit Object ID, DisplayName, MailNickname, Description, GroupTypes, MailEnabled, SecurityEnabled und ResourceProvisioningOptions.
- Gruppenmitgliedschaften zwischen Benutzern und Gruppen.
- Applications mit Object ID, App ID, DisplayName und App-Rollen (`MockEntraApplication`/`MockEntraApplicationRole`).
- Application-Sign-Ins (`MockEntraApplicationSignIn`) je Application/Benutzer mit letztem Login-Zeitpunkt.

Worker-Verhalten:

- Discovery liest Benutzer und Gruppenmitgliedschaften aus dem Mock.
- Invite legt neue Mock-Gastbenutzer an, falls sie noch nicht existieren.
- DeployScenario kann Mock-Gruppen anlegen.
- GrantWorkloadRole weist den Benutzer den in der Workload-Rolle gemappten Gruppen zu.
- RevokeWorkloadRole entfernt diese Gruppenmitgliedschaften.
- Der Large-Workload-Seed schreibt seine Gastbenutzer, Zielgruppen und aktiven Mitgliedschaften ebenfalls in den Mock-Stamm.
- `ApplicationSignInSyncWorker` (BackgroundService, alle 10 Minuten) erzeugt fuer aktive/genehmigte/angefragte Zuweisungen auf Workloads mit `ApplicationExternalId` passende Mock-Sign-In-Eintraege mit zufaellig zurueckdatiertem Login-Zeitpunkt (0–90 Tage) — simuliert Entra-Sign-In-Logs.
- `SyncWorkloadPatternResourcesHandler` (Job `SyncWorkloadPatternResources`) matcht Mock-Gruppen gegen die `ResourceNamePatterns` eines Workload (Glob `*`/`?` oder `regex:`/`/.../`-Syntax) und haengt Treffer automatisch als Ressource an (Team/M365Group/SecurityGroup je nach Gruppentyp).

Der Mock fuehrt keine externen Graph-Schreibzugriffe aus.

## Mock Entra Portal

Die Web-GUI enthaelt im Development-Modus die Route `/dev/mock-entra`.

Die Seite zeigt:

- Benutzerstamm
- Gruppenstamm
- Applications inkl. App-Rollen
- Gruppenmitgliedschaften
- Pflegeformulare fuer Benutzer, Gruppen, Applications und Mitgliedschaften (inkl. Einzel-/Bulk-Entfernen und "alle Mitglieder entfernen")

Die Daten kommen aus:

- `GET|POST /api/dev/mock-entra/users`
- `PUT|DELETE /api/dev/mock-entra/users/{objectId}`
- `GET /api/dev/mock-entra/login-users`
- `GET|POST /api/dev/mock-entra/groups`
- `PUT|DELETE /api/dev/mock-entra/groups/{objectId}`
- `DELETE /api/dev/mock-entra/groups/{groupId}/members`
- `GET|POST /api/dev/mock-entra/applications`
- `PUT|DELETE /api/dev/mock-entra/applications/{objectId}`
- `GET|POST|DELETE /api/dev/mock-entra/memberships`
- `GET /api/dev/mock-entra/application-signins?appId=`

Die Endpoints werden nur unter `LOCAL_MOCK` registriert und verlangen `GovernanceAdmin`.

## Login-Screen

`LoginPage` (`src/B2B.Portal.Web/src/pages/LoginPage.tsx`) ersetzt den frueheren
Dev-User-Switcher. Ohne gueltiges Token in `sessionStorage` zeigt die App eine echte
Anmeldeseite: sie listet Mock-Benutzer aus `GET /api/dev/mock-entra/login-users` (bewusst
ohne Auth-Zwang, damit sie vor dem Login erreichbar ist), ein Klick ruft
`POST /api/auth/mock/login` auf und speichert das zurueckgegebene JWT unter dem Key
`portal-jwt` in `sessionStorage` (bewusst nicht `localStorage` — Schliessen des Tabs beendet
die Session). `src/B2B.Portal.Web/src/auth/token.ts` decodiert den Token-Payload
clientseitig (kein Library-Bedarf, keine Signaturpruefung noetig, die passiert ohnehin
serverseitig) fuer `userMail`/`roles`/`platformTenantId` in `AppLayout`.

Sign-out (`AppLayout`) loescht das Token aus `sessionStorage` und ruft
`POST /api/auth/mock/logout` (No-op) — die App faellt danach auf die Login-Seite zurueck,
`client.ts` sendet ohne Token keinen `Authorization`-Header mehr (kein stiller Re-Login auf
einen Default-User mehr, das war der urspruengliche Bug).
