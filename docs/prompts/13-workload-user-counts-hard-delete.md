# Prompt 13 — Nutzerzahlen, Workload-Hart-Löschen, Guest-Pool-Unassign, Szenario-Cleanup

- **Datum:** 29. August 2026
- **Auftrag:** "wennn der workload keine user mehr hat kann er auch gelöscht werden,
  bitte ergänze auch die anzeige das man siegt wie viele user der Workload hat, welche
  anzahl aktiv ist und welche nicht, wenn keine anzahl mehr aktiv ist dann kann gelöscht
  werden es ist daher auch notwendig das man im guetpool den workload sieht und auch
  unasignen kann, wichtig es kann mehrer workloads pro user geben, wenn ein scenarion
  gelöscht werden müssen auch die zugehörigkeiten zu den resourcen bei dem user gelöscht
  werden"

## Geklärte Anforderungen

- **Szenario-Cleanup**: Ein Szenario-Deploy weist aktuell keinem einzelnen Gast direkten
  Zugriff zu (das passiert ausschließlich über die separate GuestWorkloadAssignment/
  GrantWorkloadRole-Funktion) — "Zugehörigkeiten bei dem User löschen" bezieht sich also
  auf die vom Szenario automatisch angelegten `WorkloadResource`s, nicht auf
  Gast-Zuweisungen. Geklärt: beim Szenario-Löschen werden **nur** die Ressourcen entfernt,
  die AUSSCHLIESSLICH von diesem Szenario referenziert wurden.
- **Workload-Hart-Löschen**: Zusätzlich zum bestehenden Soft-Delete (Prompt 12) ein neuer
  "Endgültig löschen"-Button, nur aktiv bei 0 aktiven Zuweisungen.
- **Nutzerzahl-Anzeige**: Getrennt als "Aktiv X / Inaktiv Y", nicht nur eine Gesamtzahl.

## Was umgesetzt wurde

### Backend
- `IWorkloadRepository.DeleteAsync` (Hart-Löschen, InMemory + Cosmos).
- `IAssignmentRepository.GetAsync` (fehlte komplett — siehe Bugfix unten) und `DeleteAsync`
  (Hart-Löschen für historische Assignments beim Workload-Hart-Löschen).
- `WorkloadManagementService`:
  - `GetAssignmentCountsAsync` — liefert `{Active, Inactive}` für einen Workload.
  - `DeleteWorkloadAsync` — blockiert (409) bei aktiven Zuweisungen; entfernt sonst alle
    Szenarien des Workload und alle historischen Assignments mit, dann den Workload selbst.
- `ScenarioImportExportService.DeleteAsync` erweitert: ermittelt beim Löschen alle
  `ResourceId`s der Szenario-Regeln, die **nicht** mehr von einer `WorkloadRole.ResourceMappings`
  oder einer Regel eines **anderen** Szenarios desselben Workload referenziert werden, und
  entfernt genau diese verwaisten `WorkloadResource`s mit.
- Neue Endpoints: `DELETE /api/workloads/{id}/permanent` (Hart-Löschen, 409 bei aktiven
  Zuweisungen), `GET /api/workloads/{id}/assignment-counts`,
  `GET /api/guest-accounts/{id}/assignments` (alle Zuweisungen eines Gastes — ein Gast kann
  mehrere Workloads haben, daher Liste statt Einzelwert).

### Kritischer Bugfix: `POST /api/assignments/{id}/revoke`
Der bestehende Endpoint behandelte `{id}` fälschlich als **GuestId** statt als
**AssignmentId** (`assignmentRepo.ListByGuestAsync(tenantCtx.Current, id, ct).FirstOrDefault()`)
— ein Revoke-Aufruf für Assignment X konnte dadurch eine völlig andere Zuweisung desselben
(zufällig gleich benannten) Guids treffen, bzw. schlug grundsätzlich fehl, sobald die
route-id tatsächlich eine AssignmentId statt GuestId war. Gefunden beim Bau des neuen
Guest-Pool-Unassign-Flows, der diesen Endpoint korrekt adressiert braucht. Gefixt durch
Ergänzung von `IAssignmentRepository.GetAsync` und Umstellung des Endpoints darauf.

### Kritischer Bugfix: Enum-Serialisierung
Live-Verifikation zeigte `"status":4` statt `"status":"Expired"` in der API-Antwort —
**alle** Enums (`AssignmentStatus`, `GuestAccountState`, etc.) wurden bislang als
numerischer Index serialisiert, obwohl sämtliche TypeScript-Typen im Frontend (inkl. der
bereits bestehenden `GuestPoolPage.tsx` `stateColor[g.accountState]`-Lookup) durchgängig
String-Werte erwarten. Das war ein **bereits vorher bestehender, stiller Bug** — der
Status-Badge fiel wegen des `?? 'informative'`-Fallbacks nie sichtbar auf, tauchte aber
beim neuen Assignment-Status-Lookup als leeres Ergebnis auf. Gefixt zentral in
`Program.cs`: `builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()))`
— behebt beide Lookups gleichzeitig, ohne Änderungen an den TypeScript-Typen nötig zu
machen (die waren immer schon korrekt).

### Web-UI
- `WorkloadsAdminPage.tsx`: zeigt "Nutzer: Aktiv X / Inaktiv Y" pro Workload-Karte (per
  `GET .../assignment-counts`), neuer "Endgültig löschen"-Button (disabled solange
  `active > 0`, mit Bestätigungsdialog, der auf Szenario-/Historie-Mitlöschung hinweist).
- `GuestPoolPage.tsx`: neue Spalte "Workloads" — zeigt pro Gast alle Zuweisungen
  (Workload-Name · Rollenname · Status als Badge), mit "Unassign"-Button für jede
  aktive/anstehende Zuweisung (`revokeAssignment`, jetzt korrekt pro AssignmentId). Ein Gast
  kann mehrere Workloads gleichzeitig haben — alle werden aufgelistet, nicht nur eines.

## Live-Verifikation (gegen echten Cosmos-Emulator)

1. Workload mit 3 Gästen geseedet → `assignment-counts` zeigt `{active:1, inactive:2}`.
2. Hart-Löschen-Versuch mit aktiver Zuweisung → `409` mit Klartext-Fehlermeldung.
3. Aktive Zuweisung über den (gefixten) `POST /api/assignments/{id}/revoke`-Endpoint
   revoked → `assignment-counts` fällt auf `{active:0, inactive:3}`.
4. Hart-Löschen erneut versucht → `204`, Workload verschwindet aus `GET /api/workloads`.
5. Szenario mit einer automatisch angelegten Ressource importiert, Szenario gelöscht →
   Ressource verschwindet aus dem Workload, alle vorher bestehenden Ressourcen bleiben
   erhalten.
6. Enum-Fix bestätigt: `GET .../assignments` liefert jetzt `"status":"Requested"` statt
   `"status":0`.

## Tests

- `WorkloadManagementServiceTests.cs`: 3 neue Tests (`DeleteWorkload_WithActiveAssignment_Throws`,
  `DeleteWorkload_NoActiveAssignments_RemovesWorkloadScenariosAndHistoricalAssignments`,
  `GetAssignmentCounts_SeparatesActiveFromInactive`).
- `ScenarioDeploymentTests.cs`: 1 neuer Test
  (`DeleteScenario_RemovesOrphanedResources_ButKeepsResourcesStillReferenced`) — deckt sowohl
  den Lösch-Fall als auch den "wird noch von einer Rolle referenziert, bleibt erhalten"-Fall
  in einem Test ab.

**Gesamt-Testergebnis: 66/66 grün** (29 Domain + 5 Architecture + 3 Application + 29
Integration, davon 4 neu). `dotnet build` fehlerfrei, `npm run build`/`vitest run` im
Web-Projekt grün (ein TS-Typfehler in `GuestPoolPage.tsx` — Griffel akzeptiert `marginBottom: 4`
nicht als bare number, nur als String/px — während der Implementierung gefunden und behoben).

## Was bewusst nicht getan wurde

- Keine automatische Bereinigung von `ResourceAccess`-Einträgen beim Szenario-Löschen (per
  Nutzerantwort explizit nicht gewünscht — nur die WorkloadResources selbst).
- Kein Kaskaden-Hart-Löschen von Rollen/Ressourcen beim Workload-Hart-Löschen über das
  Nötigste hinaus — Szenarien und historische Assignments werden mitgelöscht (Teil des
  Workload-Datensatzes selbst), Rollen/Ressourcen sind ohnehin Teil des
  `Workload`-Dokuments und verschwinden automatisch mit dem gesamten Dokument.
- Keine UI-Anzeige/Filterung der 505 im Laufe der Session angesammelten Test-Gäste im Guest
  Pool — außerhalb des Auftragsumfangs.
