# Admin View

Stand: 2026-08-30

Governance/Admin-Funktionen setzen die Rolle `GovernanceAdmin` voraus.

Admin-Funktionen:

- Guest Pool lesen
- Gaeste einladen
- Guest Import Preview/Commit
- Workload erstellen (inkl. Administrative Unit, Application, Gruppen-Namenspatterns)
- bestehende Mock-Ressource an Workload anhaengen
- Gast einer Workload-Rolle zuweisen
- Reviews lesen/entscheiden
- Audit lesen
- globale Workload-Liste lesen
- Jobs lesen und nicht-terminale Jobs stoppen
- Mock Entra Directory pflegen (Benutzer, Gruppen, Applications, Mitgliedschaften) unter `LOCAL_MOCK`

Workload Owner:

- darf den eigenen Workload modifizieren.
- darf Rollen und Ressourcen des eigenen Workload modifizieren.
- darf Szenarien des eigenen Workload importieren, exportieren, deployen und loeschen.

Scenario Manager:

- darf innerhalb des konfigurierten Workload-/Szenario-Scopes agieren.
