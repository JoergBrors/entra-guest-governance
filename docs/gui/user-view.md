# User View

Stand: 2026-08-29

Benutzer sehen nur zugeordnete Workloads.

API-Regel:

- `GET /api/me/workloads` sucht den `GuestAccount` ueber die angemeldete Mailadresse.
- Danach werden aktive `GuestWorkloadAssignment`-Eintraege gelesen.
- Fuer jeden Workload wird nur die zugewiesene Rolle und deren Ressourcen projektiert.

Kein normaler Benutzer bekommt ueber diese View eine globale Workload-, Guest- oder Ressourcenliste.

Direkter Workload-Zugriff:

- `GET /api/workloads/{id}` erlaubt normalen Benutzern nur Workloads mit eigener aktiver Zuordnung.
- Ohne Zuordnung antwortet die API mit `403`.

Produktiver Auth-Provider: `integration pending`.

