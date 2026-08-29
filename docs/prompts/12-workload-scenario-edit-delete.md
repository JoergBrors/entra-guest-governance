# Prompt 12 — Workloads und Szenarien editier- und löschbar machen

- **Datum:** 29. August 2026
- **Auftrag:** "bitte es auch ermöglichen workloads und szenarien zu editieren und zu
  löschen". Bisher gab es weder ein Anlage-/Bearbeitungs-Formular für Workloads (nur
  Dev-Seed) noch ein Lösch-Endpoint für Workloads oder Szenarien.

## Geklärte Anforderungen

- **Workload-Löschen**: Soft-Delete (`Active=false`) statt Hart-Löschen — Assignments/
  Szenarien bleiben erhalten, kein Datenverlust.
- **Workload-Editierbar**: Name+Owner **und** Rollen (Name, ResourceMappings) **und**
  Ressourcen (Type, ExternalId) — mit der expliziten Vorgabe "es muss aber die Konsistenz
  der Daten behalten werden".
- **Szenario-Löschen**: Hart-Löschen (keine Fremdreferenzen von außen auf ein Szenario).

## Was umgesetzt wurde

### Konsistenzprüfungen (Kern der Anforderung)
Neuer `WorkloadManagementService` (`src/B2B.Portal.Application/Workloads/WorkloadManagementService.cs`)
bündelt alle schreibenden Workload-Operationen inkl. der Prüfungen, die eine reine
Repository-Schicht nicht kennen kann:
- `DeleteRoleAsync` blockiert (409), solange noch aktive `GuestWorkloadAssignment`s auf die
  Rolle zeigen (`IAssignmentRepository.ListByWorkloadAsync`, neu ergänzt).
- `DeleteResourceAsync` blockiert (409), solange noch eine `WorkloadRole.ResourceMappings`
  **oder** ein `ScenarioResourceRule.ResourceId` auf die Ressource zeigt — Fehlermeldung
  nennt die blockierenden Rollen/Szenarien namentlich.
- `UpsertRoleAsync` validiert, dass alle übergebenen `ResourceMappings` auf tatsächlich
  existierende Ressourcen des Workloads zeigen (400 sonst).

### Neue/erweiterte Ports
- `IAssignmentRepository.ListByWorkloadAsync` (InMemory + Cosmos) — Grundlage für die
  Rollen-Löschprüfung.
- `IWorkloadScenarioRepository.DeleteAsync` (InMemory + Cosmos, per `DeleteItemAsync`,
  idempotent bei NotFound) — Hart-Löschen für Szenarien.

### API-Endpoints (`Program.cs`)
- `PUT /api/workloads/{id}` — Name/Owner.
- `DELETE /api/workloads/{id}` — Soft-Delete (204).
- `POST/PUT/DELETE /api/workloads/{id}/roles[/{roleId}]` — Rollen anlegen/bearbeiten/löschen.
- `POST/PUT/DELETE /api/workloads/{id}/resources[/{resourceId}]` — Ressourcen anlegen/
  bearbeiten/löschen.
- `DELETE /api/scenarios/{id}` — Hart-Löschen (204), 404 falls nicht gefunden.
- Alle Konsistenzverstöße kommen als `409 Conflict` mit `{"error": "..."}`, unbekannte
  Referenzen als `400 Bad Request` — der Frontend-Client liest `error` jetzt aus dem Body
  statt nur den Statuscode zu melden (`client.ts` `request()` erweitert).

### Web-UI
- `WorkloadsAdminPage.tsx`: Inline-Bearbeitung (Name/Owner), Deaktivieren-Button mit
  Bestätigungs-Dialog, Rolle/Ressource hinzufügen (kleine Formulare pro Karte), Rolle/
  Ressource per ×-Button auf dem Badge löschen (zeigt die 409-Fehlermeldung aus dem Backend
  im MessageBar, falls z.B. noch eine aktive Zuweisung existiert).
- `ScenariosPage.tsx`: Löschen-Button pro Szenario mit Bestätigungs-Dialog.
- Aufgeräumt: zwei tote, seit dem Szenario-Redesign (Prompt 11) ungenutzte DTOs
  (`WorkloadScenarioBody`/`ScenarioConditionBody`) in `Program.cs` durch die neuen
  Request-DTOs ersetzt.

## Live-Verifikation (gegen echten Cosmos-Emulator)

- Workload umbenannt (`PUT`) — bestätigt.
- Ressourcen-Löschung einer noch von einer Rolle referenzierten Ressource → `409` mit
  korrekter Rollen-Nennung.
- Rolle ohne aktive Zuweisung gelöscht → `204`; danach dieselbe Ressourcen-Löschung erneut
  versucht → jetzt `204` (Blockade korrekt aufgehoben).
- Workload deaktiviert (`DELETE`) → bleibt in `GET /api/workloads` mit `active:false`
  sichtbar (kein Datenverlust).
- Szenario importiert, gelöscht (`204`), zweite Löschung desselben Ids → `404`.

## Tests

Neue `tests/B2B.Portal.Integration.Tests/WorkloadManagementServiceTests.cs` (6 Tests, gegen
InMemory): Rolle mit aktiver Zuweisung nicht löschbar / ohne Zuweisung löschbar, Ressource
mit Rollen-Referenz nicht löschbar, Ressource mit Szenario-Regel-Referenz nicht löschbar,
unreferenzierte Ressource löschbar, Workload-Deaktivierung setzt `Active=false`.

**Gesamt-Testergebnis: 62/62 grün** (29 Domain + 5 Architecture + 3 Application + 25
Integration, davon 6 neu). `dotnet build` fehlerfrei über alle Schichten, `npm run build`/
`vitest run` im Web-Projekt grün.

## Was bewusst nicht getan wurde

- Kein Hart-Löschen für Workloads — mit Assignment-/Szenario-Historie wäre das riskant;
  Soft-Delete war die vom Nutzer bestätigte Wahl.
- Keine Kaskaden-Logik (z.B. automatisches Entfernen abhängiger Szenarien/Rollen beim
  Workload-Deaktivieren) — bewusst nicht gewünscht, Konsistenz wird stattdessen durch
  Blockieren statt automatisches Aufräumen sichergestellt.
- Keine UI für die Validierungs-400-Fälle (z.B. ResourceMappings auf unbekannte Ressource)
  im Detail — die Fehlermeldung aus dem Backend wird 1:1 im MessageBar angezeigt, kein
  spezielles Feld-Highlighting.
