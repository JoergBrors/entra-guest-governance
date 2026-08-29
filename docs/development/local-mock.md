# Local Mock

Stand: 2026-08-29

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
- Gruppen mit Object ID, DisplayName, MailNickname, Description, GroupType, SecurityEnabled und optionalem WorkloadName.
- Gruppenmitgliedschaften zwischen Benutzern und Gruppen.

Worker-Verhalten:

- Discovery liest Benutzer und Gruppenmitgliedschaften aus dem Mock.
- Invite legt neue Mock-Gastbenutzer an, falls sie noch nicht existieren.
- DeployScenario kann Mock-Gruppen anlegen.
- GrantWorkloadRole weist den Benutzer den in der Workload-Rolle gemappten Gruppen zu.
- RevokeWorkloadRole entfernt diese Gruppenmitgliedschaften.

Der Mock fuehrt keine externen Graph-Schreibzugriffe aus.

## Mock Entra Portal

Die Web-GUI enthaelt im Development-Modus die Route `/dev/mock-entra`.

Die Seite zeigt:

- Benutzerstamm
- Gruppenstamm
- Gruppenmitgliedschaften

Die Daten kommen aus:

- `GET /api/dev/mock-entra/users`
- `GET /api/dev/mock-entra/groups`
- `GET /api/dev/mock-entra/memberships`

Die Endpoints werden nur unter `LOCAL_MOCK` registriert und verlangen `GovernanceAdmin`.
