# Navigation

Stand: 2026-08-29

Die Navigation wird in `src/B2B.Portal.Web/src/components/AppLayout.tsx` aus Rollen abgeleitet.

Normale Benutzer sehen:

- Start
- Meine Workloads
- Antraege
- Profil im Header

Reviewer sehen zusaetzlich:

- Meine Reviews

Governance/Admin sieht zusaetzlich:

- Guest Pool
- Workloads
- Gaeste-Import
- Compliance
- Ressourcen / Discovery
- Jobs
- Audit

Development Navigation:

- Theme Preview
- Mock Entra fuer Governance/Admin

Workload Owner und Scenario Manager sehen Workloads fuer ihren Scope. Die API bleibt die massgebliche Durchsetzung; die Navigation ist nur die sichtbare Auswahl.
