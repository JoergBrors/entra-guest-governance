# Screens

Stand: 2026-08-29

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

## Access Request

Quelle: `GET /api/me/workloads`.
Policy-/Approver-Ergebnis: `integration pending`.

## Reviews

Quelle: `GET /api/reviews`.
Entscheidung: `POST /api/reviews/{reviewInstanceId}/items/{reviewItemId}/decision`.

## Jobs, Compliance, Discovery

Seiten existieren. Detaillierte produktive APIs: `integration pending`.

