# Security Invariants

Stand: 2026-08-29

Bestehende und ergaenzte Sicherheitsregeln:

- Ein Gast kann mehreren Workloads zugeordnet sein.
- Workloads entfernen nur Zugriff, nicht die Gastidentitaet.
- `DeleteGuest` bleibt blockiert, wenn noch Workload-Zuordnungen existieren.
- Deletion Gate prueft Workload-Zuordnungen, Unclassified Access, offene Jobs, offene Reviews, Grace Period, Connector Error und Live Check.
- Normale Benutzer erhalten keine globale Guest-/Workload-Liste.
- Theme Branding erlaubt keine freie CSS- oder JavaScript-Injektion.

