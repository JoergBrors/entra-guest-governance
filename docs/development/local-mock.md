# Local Mock

Stand: 2026-08-30 (aktualisiert: Mock Applications/Sign-Ins, Dev-User-Switcher)

`LOCAL_MOCK` bleibt der Default fuer lokale Entwicklung.

Dev-Header fuer die neue UI-/Auth-Schicht:

- `X-Platform-Tenant-Id`
- `X-Portal-User-Mail`
- `X-Portal-Roles`
- `X-Scenario-Manager-Workload-Ids`
- `X-Portal-Theme-Id`

Der Web-Client setzt lokale Defaultwerte aus Vite-Env-Variablen:

- `VITE_DEV_PLATFORM_TENANT_ID`
- `VITE_DEV_PORTAL_USER_MAIL`
- `VITE_DEV_PORTAL_ROLES`

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

## Dev-User-Switcher

`AppLayout`/`App.tsx` bieten im Development-Modus ein Dropdown, um zwischen Mock-Benutzern (aus `GET /api/dev/mock-entra/login-users`) zu wechseln, ohne sich neu anzumelden. Auswahl und Rollen landen in `localStorage` (`portal-user-mail`, `portal-user-roles`) und werden vom API-Client als Dev-Header (`X-Portal-User-Mail`, `X-Portal-Roles`) mitgesendet. Ein Sign-out-Button setzt die Auswahl zurueck.
