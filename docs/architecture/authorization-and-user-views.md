# Authorization And User Views

Stand: 2026-08-29

## Fachliche Fakten aus der Freigabe

- Rollen und Scopes gelten auf Workgroup/Workload- und Szenario-Ebene.
- Der Workload Owner darf Szenarien anlegen, loeschen und editieren.
- Der Workload Owner darf den Workload modifizieren.
- Der Scenario Manager darf innerhalb seines Szenario-/Workload-Scopes agieren.
- Der GuestAccount ist die Person, die sich anmeldet.
- Der Sponsor ist die Person, die den GuestAccount verantwortet.
- Bei mehreren Workloads werden Aenderungen ueber Review-Prozesse gefuehrt.
- `DeleteGuest` darf nur durchgefuehrt werden, wenn kein Workload mehr zugeordnet ist.

## Umsetzung

- `src/B2B.Portal.Api/Auth/PortalUserContext.cs` definiert Rollen und Kontext.
- `GET /api/me/workloads` liefert nur Workloads, die dem angemeldeten GuestAccount aktiv zugeordnet sind.
- `GET /api/workloads/{id}` gibt fuer normale Benutzer nur die eigene Rolle und abgeleitete Ressourcen zurueck.
- Governance/Admin-Endpunkte pruefen `GovernanceAdmin`.
- Workload-Aenderungen pruefen `WorkloadOwner` gegen `Workload.Owner` oder `GovernanceAdmin`.
- Szenario-Aktionen pruefen `WorkloadOwner`, `ScenarioManager` oder `GovernanceAdmin`.

## LOCAL_MOCK

Der MVP nutzt Header als Dev-Kontext:

- `X-Portal-User-Mail`
- `X-Portal-Roles`
- `X-Scenario-Manager-Workload-Ids`

Produktive Token-/Claim-Ableitung: `integration pending`.

