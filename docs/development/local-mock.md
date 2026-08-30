# Local Mock

Stand: 2026-08-30 (aktualisiert: Identity Provider + JWT-Login; Mock-Entra-User-Persistenz + Startup-Hydration; Worker/Trigger-Uebersicht + Job-Restart; Invitation Reminder Worker + Erinnerungs-Policy + Mail Monitor)

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

## Worker/Trigger-Uebersicht, Job-Restart (Erweiterung 2026-08-30)

Neue Seite "Worker" (`/worker`, neben "Jobs" in der Governance-Nav) zeigt Jobs aggregiert pro
JobType (Anzahl/Erfolg/Fehler/Wartend). `RunDiscovery` und `RunReconciliation` — die einzigen
Job-Typen ohne fachlichen Kontext-Parameter — haben dort einen "Jetzt ausfuehren"-Button
(`POST /api/jobs/trigger/discovery` bzw. `.../trigger/reconciliation`, Governance-Admin-only).
Alle anderen Job-Typen entstehen weiterhin nur kontextuell (Workloads-Admin, Gast-Einladung
etc.) und erscheinen in der Worker-Uebersicht read-only.

Jeder fehlgeschlagene Job (`Failed`/`DeadLetter`) hat einen "Restart"-Button — sowohl in
`JobsPage` als auch per Klick auf die Fehlerzahl in der Worker-Uebersicht (springt gefiltert
in `JobsPage`, `?jobType=X&status=Failed`). Restart legt einen NEUEN Job mit identischem
Payload an (`POST /api/jobs/{id}/restart`) — der urspruengliche fehlgeschlagene Datensatz
bleibt als Historie erhalten. Voraussetzung: der Job wurde nach Einfuehrung von
`DirectoryOperation.PayloadJson` erzeugt (aeltere Jobs ohne gespeicherten Payload koennen
nicht neu gestartet werden, 400 mit entsprechender Fehlermeldung).

Produktive Werte: `configuration required`.

## Invitation Reminder Worker, Erinnerungs-Policy, Mail Monitor (Erweiterung 2026-08-30)

Neuer periodischer `BackgroundService` `InvitationReminderWorker` (Worker-Host, nur unter
`LOCAL_MOCK` registriert, gleiches 10-Minuten-Intervall wie `ApplicationSignInSyncWorker`):
scannt Gaeste im Zustand `Invited`, deren Einladung (`GuestAccount.CreatedAt`) laenger
zurueckliegt als die naechste faellige Stufe der tenant-weiten `ReminderPolicy`
(`GET/PUT /api/reminder-policy`, Governance-Admin-only, Admin-UI unter `/reminder-policy`).
Ohne konfigurierte Policy (keine Stufen) passiert nichts — es gibt keine hartkodierten
Default-Stufen. Idempotenz ueber `GuestAccount.LastReminderStageSent`/`LastReminderSentAt`:
eine Stufe wird pro Gast genau einmal ausgeloest, nie uebersprungen, nie doppelt gesendet.

Der eigentliche Versand laeuft ueber einen neuen `IJobHandler` fuer
`JobTypes.InvitationReminder` (`InvitationReminderHandler`, in derselben Datei wie
`InvitationHandler`/`ResendInvitationHandler`) — einfache String-Platzhalter-Ersetzung in
Betreff/Text (`{{DisplayName}}`, `{{WorkloadName}}`, `{{DaysSinceInvite}}`,
`{{RedemptionLink}}`), kein Templating-Framework.

**Mock-Redemption-Link:** `GuestAccount.InvitationRedemptionLink` wird deterministisch beim
Einladen gesetzt (`InvitationHandler.HandleAsync`, Format
`https://mock-invite.local/redeem/{guestId}`). Das ist **kein echter Entra-Redemption-Link** —
ein echter `DEV_INTEGRATION`-Pfad wuerde stattdessen die von Microsoft Graph beim Invite
zurueckgegebene `inviteRedeemUrl` verwenden (`integration pending`, siehe
`docs/architecture/graph-integration.md`).

**Guest Pool Filter:** `GET /api/guest-accounts` akzeptiert jetzt optionale Query-Parameter
`workloadId`, `scenarioId`, `accountState`, `invitationStatus` (`accepted`/`pending`,
abgeleitet aus `GuestAccountState` — `Invited` = pending, alles andere = accepted). Filterung
laeuft serverseitig ueber `GuestWorkloadAssignment`/`WorkloadScenario`.

**Scoped Visibility fuer Workload-/Scenario-Owner:** neuer Endpoint
`GET /api/me/managed-guests` (dieselbe Scoping-Logik wie `GET /api/me/workloads` — kein
Governance-Admin noetig, nur `CanManageWorkload`/`ScenarioManagerWorkloadIds`) liefert
dieselbe gefilterte Gaesteliste, beschraenkt auf die selbst verwalteten Workloads. Erscheint
als eigener Abschnitt ueber "Meine Workloads" auf `MyWorkloadsPage.tsx`, nur fuer
WorkloadOwner/ScenarioManager/GovernanceAdmin sichtbar.

**Mail Monitor:** `MockEmailProvider.Sink` (bisher nirgends erreichbar) ist jetzt ueber
`GET /api/dev/mail-sink` abrufbar (LOCAL_MOCK-only, Governance-Admin-only, wie alle
`/api/dev/*`-Endpunkte) und in der neuen Admin-Seite `/mail-monitor` sichtbar (Polling wie
`JobsPage`, neueste zuerst).

## Mock Entra Directory

`LOCAL_MOCK` enthaelt einen lokalen Entra-ID-Mock in `MockEntraDirectoryStore`.

**Persistenz (Erweiterung 2026-08-30, Teil 3):** Benutzer inkl. `PortalRoles` werden ueber
`IMockEntraUserRepository`/`CosmosMockEntraUserRepository` im Cosmos-Container `discovery`
persistiert (`entityType: "MockEntraUser"`, disambiguiert wie `CosmosResourceAccessRepository`
im selben Container). Vorher lebten Rollenzuweisungen (z.B. `GovernanceAdmin`) nur im
In-Memory-Singleton und gingen bei jedem API-Neustart verloren. Gruppen, Applications und
Mitgliedschaften bleiben weiterhin reines In-Memory-State (nicht persistiert) — nur
Benutzer/Rollen sind jetzt Cosmos-gestuetzt, da sie die einzige Quelle fuer den Login
(`POST /api/auth/mock/login`) sind.

**Startup-Hydration:** Beim API-Start (nur `LOCAL_MOCK`, siehe `Program.cs`) laedt
`MockEntraDirectoryStore.HydrateFromRepositoryAsync` alle in Cosmos bekannten Benutzer in den
In-Memory-Store, *bevor* der erste Request bedient wird. Das loest das fruehere
Henne-Ei-Problem: `POST /api/auth/mock/login` fragt ausschliesslich den In-Memory-Store,
und `GET /api/dev/mock-entra/login-users` re-hydrierte vorher nur Tenants, die im Store
bereits bekannt waren — bei einem leeren Store (frischer Prozess nach Cosmos-Reset) also
keine. Jetzt ist der Login sofort nach `dotnet run` nutzbar, sobald mindestens ein
Mock-Entra-Benutzer in Cosmos existiert (siehe Abschnitt "Reset & Seed" unten).
`GET /api/dev/mock-entra/login-users` bleibt als ergaenzender Refresh bestehen (synct
weiterhin Gast-/Workload-Daten aus dem Tenant in den Mock-Stamm), ist aber nicht mehr der
einzige Weg, den Store initial zu befuellen.

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

## Reset & Seed (Erweiterung 2026-08-30, Teil 3)

Die Seed-Skripte (`scripts/seed-dev-data.ps1`, `scripts/seed-large-workload.ps1`) senden
keine freien `X-Platform-Tenant-Id`/`X-Portal-*`-Header mehr — jeder Request braucht ein
JWT, der Tenant kommt aus dem Token-Claim (`ClaimsTenantContextAccessor`). Beide Skripte
loggen sich daher zuerst per `POST /api/auth/mock/login` als `admin@platform.example` ein
und haengen `Authorization: Bearer <token>` an alle folgenden Aufrufe.

Empfohlener Ablauf nach einem Cosmos-Reset:

1. `./scripts/reset-cosmos-dev-data.ps1` — legt die Container neu an **und** schreibt einen
   `GovernanceAdmin`-Mock-Benutzer (`admin@platform.example`, Tenant `dev-tenant-a`) direkt
   als Dokument in den Container `discovery` (per REST, gleiches Schema wie
   `CosmosMockEntraUserRepository`). Ohne diesen Schritt gaebe es nach einem Reset keinen
   Weg, sich ueberhaupt einzuloggen (leerer Mock-Stamm, kein Login moeglich, kein Seed
   moeglich).
2. Portal API (neu) starten — nur beim Start hydriert `MockEntraDirectoryStore` aus Cosmos
   (siehe "Startup-Hydration" oben). Ein bereits laufender Prozess sieht den frisch
   geschriebenen Admin-Benutzer nicht automatisch.
3. `./scripts/seed-dev-data.ps1` bzw. `./scripts/seed-large-workload.ps1 -GuestCount <n>` —
   loggen sich selbst ein und melden einen klaren Fehler ("... im Mock-Entra-Store nicht
   gefunden ...") mit Hinweis auf Schritt 1/2, falls der Login fehlschlaegt.

`scripts/seed-large-workload.ps1` behaelt den Parameter `-PlatformTenantId` aus
Kompatibilitaetsgruenden, er ist aber nur noch informationell — der tatsaechliche Tenant
kommt aus dem JWT des `-AdminMail`-Logins.
