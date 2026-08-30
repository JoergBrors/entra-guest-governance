# Screens

Stand: 2026-08-30

## Dashboard

Quelle: `api.health`, `api.listGuests`, `api.listWorkloads`, `api.listOpenReviews`.
Admin-Kontext erforderlich fuer globale Daten.

## Meine Workloads

Quelle: `GET /api/me/workloads`.
Zeigt nur serverseitig zugeordnete Workloads, Rollen und abgeleitete Ressourcen.

## Guest Pool

Quelle: `GET /api/guest-accounts`.
Governance/Admin-Funktion.

## Gast Detail

Quelle: `GET /api/guest-accounts/{id}` und `GET /api/guest-accounts/{id}/assignments`.
Technische Details sind fuer Governance/Admin sichtbar.

## Workload Detail

Quelle: `GET /api/workloads/{id}`.
Normale Benutzer erhalten nur die eigene Rolle und eigene Ressourcen.

## Workloads Admin

Quelle: `GET /api/workloads`, `POST /api/workloads`, `POST /api/workloads/{workloadId}/assignments`, `POST /api/workloads/{workloadId}/resources/attach`.
Governance/Admin kann Workloads erstellen und Gaeste Rollen zuweisen. Workload-Erstellung/-Bearbeitung umfasst zusaetzlich Administrative Unit, Application (App-Rollen-Mapping fuer Rollen) und Gruppen-Namenspatterns (Glob/Regex) mit Validierungs-Vorschau; Treffer werden per Job (`SyncWorkloadPatternResources`) automatisch als Ressource angehaengt. Bestehende Mock-Gruppen koennen auch direkt als Ressource angehaengt werden. Der Worker setzt die Zuweisung technisch ueber Gruppenmitgliedschaften im Mock um.

## Access Request

Quelle: `GET /api/me/workloads`.
Policy-/Approver-Ergebnis: `integration pending`.

## Reviews

Quelle: `GET /api/reviews`.
Entscheidung: `POST /api/reviews/{reviewInstanceId}/items/{reviewItemId}/decision`.

## Jobs

Quelle: `GET /api/jobs`, `GET /api/jobs/{id}`, `POST /api/jobs/{id}/stop`.
Zeigt laufende/abgeschlossene Jobs (tenant-/rollenscoped) inkl. Ausloeser (`TriggeredBy`) und verknuepftem Workload; nicht-terminale Jobs koennen ueber "Stop" abgebrochen werden.

## Scenario Users

Quelle: `GET /api/workloads/{workloadId}/scenarios/{scenarioId}/users`.
Zeigt pro Szenario zugewiesene Gaeste inkl. Login-/App-Sign-In-Zeitpunkt; zugaenglich fuer Workload Owner, Scenario Manager und GovernanceAdmin.

## Compliance, Discovery

Seiten existieren. Detaillierte produktive APIs: `integration pending`.
