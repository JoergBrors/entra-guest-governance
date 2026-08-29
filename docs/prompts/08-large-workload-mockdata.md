# Prompt 08 — Aussagekräftige Mockdaten: Workload mit 500 Gästen

- **Datum:** 29. August 2026
- **Auftrag:** Chat-Auftrag: aussagekräftige Mockdateien erstellen, die einen Workload mit
  500 Gästen zeigen können.

## Ausgangslage geprüft

Es gab bisher keinen API-Command zum Anlegen eines Workloads (bereits in
`docs/architecture/mvp-test-report.md` als offener Punkt dokumentiert) und keinen
Bulk-Insert-Mechanismus in den InMemory-Repositories — nur einzelne `UpsertAsync`-Aufrufe.
`scripts/seed-dev-data.ps1` legte bisher genau einen Gast per `POST /api/guests/invite` an.
Für 500 Gäste realistisch und schnell wäre ein Skript mit 500 sequenziellen HTTP-Requests
nötig gewesen — funktional möglich, aber langsam und ohne Möglichkeit, überhaupt einen
Workload anzulegen, dem die Gäste zugeordnet werden könnten.

## Entscheidung (Rückfrage beantwortet)

Backend-Seed-Endpoint + PowerShell-Skript statt reiner Frontend-Mock-Fixture — damit die
Demo über echte API-/Repository-/Audit-Pfade läuft und in der bestehenden UI (Guest Pool,
Workloads-Admin) ohne Sonderlogik sichtbar wird.

## Was erstellt wurde

### `POST /api/dev/seed/large-workload` (neuer Endpoint, nur unter `LOCAL_MOCK`)

In `src/B2B.Portal.Api/Program.cs` registriert, **bedingt auf `mode == "LOCAL_MOCK"`** —
in jedem anderen Modus existiert der Endpoint nicht. Nutzt dieselben Bausteine wie die
echten Commands (`IWorkloadRepository`, `IGuestAccountRepository`,
`IAssignmentRepository`, `ProvisioningService`, `AuditService`), nur ohne den Umweg über
500 einzelne HTTP-Requests:

- Legt **einen Workload** an ("SAP S/4 Rollout — Projekt Meridian" oder per Body
  `workloadName` überschreibbar) mit 5 Ressourcen (2× SecurityGroup, M365Group, Team,
  AppRole) und 4 Rollen (Reader/Contributor/Core Team/Project Admin, mit gestaffelten
  `ResourceMappings`).
- Legt **N Gäste** an (`guestCount` im Body, Default 500, serverseitig auf 1–5000
  geclampt) über die neue statische Hilfsklasse `DevSeedData`:
  - Namen aus 26 Vor-/26 Nachnamen-Listen kombiniert (bis zu 676 eindeutige
    Namenskombinationen, plus fortlaufender Index in der Mailadresse für Eindeutigkeit
    auch darüber hinaus).
  - 8 fiktive Beispielfirmen mit `.example`-Domains (RFC 2606) — bewusst keine echten
    Firmennamen/Domains.
  - Rollenverteilung 65 % Reader / 20 % Contributor / 10 % Core Team / 5 % Project Admin
    (deterministisch über `index % 20`).
  - Lifecycle-Status-Mix: 2 % Discovered, 6 % Invited, 4 % OrphanCandidate, 88 % Active
    (deterministisch über `index % 50`).
  - Assignment-Status-Mix: überwiegend Active, vereinzelt PendingReview/Requested/Expired
    (`index % 25`).
- Legt pro Gast einen `GrantWorkloadRole`-Job über `ProvisioningService.EnqueueJobAsync`
  an — damit Job-bezogene Ansichten/Tests nicht leer sind.
- Schreibt ein `AuditEvent` (`Action=SeedLargeWorkload`) über `AuditService` — konsistent
  zur Blueprint-Regel "Audit-first".

### `scripts/seed-large-workload.ps1` (neu)

Ruft den Endpoint auf, liest `API_BASE_URL` optional aus `.env.local`, prüft vorab per
`/health`, dass die API tatsächlich im `LOCAL_MOCK`-Modus läuft (sonst aussagekräftiger
Fehler statt eines rohen 404), Parameter für `-GuestCount`, `-WorkloadName`,
`-PlatformTenantId`, `-ApiBaseUrl`.

### README.md

Neuer Abschnitt "4) Aussagekräftige Mockdaten laden (optional)" im Quick-Start-Bereich.

## Was getestet wurde (live)

| Test | Ergebnis |
| --- | --- |
| `dotnet build` nach Hinzufügen des Endpoints | ⚠️ zunächst 5 Kompilierfehler (fehlende `using`-Direktiven für `B2B.Portal.Domain.Entities`/`.Enums`, da die Top-Level-Statements-Datei sie vorher nicht brauchte) → behoben → ✅ 0 Fehler |
| `POST /api/dev/seed/large-workload` mit `guestCount:500` gegen laufende API | ✅ Antwort in 80 ms, 1 Workload mit 4 Rollen, 500 Gäste |
| `GET /api/guest-accounts` danach | ✅ genau 500 Einträge, realistische Namen/Firmen/Mailadressen, korrekte Status-Verteilung (440 Active / 30 Invited / 20 OrphanCandidate / 10 Discovered — exakt wie geplant) |
| `GET /api/workloads` danach | ✅ 1 Workload mit 4 Rollen und 5 Ressourcen, korrekt strukturiert |
| `GET /api/audit-events` danach | ✅ `SeedLargeWorkload`-Event mit `details: "500 guests seeded"` vorhanden |
| Deletion-Gate-Dry-Run gegen einen geseedeten, aktiv zugewiesenen Gast | ✅ korrekt `Blocked` mit `ActiveWorkloadReferences=1` — die erzeugten Assignments wirken sich korrekt auf den Deletion Gate aus |
| Tenant-Isolation (Tenant B sieht Tenant-A-Daten nicht) | ✅ weiterhin 0 Einträge für fremden Tenant |
| `scripts/seed-large-workload.ps1` ohne Parameter | ✅ 500 Gäste in 63 ms, verständliche Ausgabe |
| `scripts/seed-large-workload.ps1 -GuestCount 25 -WorkloadName "Test-Onboarding" -PlatformTenantId dev-tenant-b` | ✅ korrekt parametrisiert, eigener Tenant sauber getrennt |
| `dotnet test` nach allen Änderungen | ✅ weiterhin 31/31 grün, inkl. Architecture-Tests (Domain/Application referenzieren weiterhin keine Infrastructure — der neue Seed-Code liegt bewusst nur in `B2B.Portal.Api`) |
| Web-UI (`GET /`) mit befüllten Daten im Hintergrund | ✅ HTTP 200, kein serverseitiger Rendering-Fehler zu erwarten (Guest-Pool-/Workloads-Admin-Page sind einfache Tabellen/Karten ohne Paginierungslogik, die bei 500 Einträgen brechen könnte) — visuelle Kontrolle im Browser nicht möglich (kein Browser-Tool in dieser Umgebung), API-Response-Struktur aber vollständig verifiziert |

Nach den Tests: alle Testprozesse (API, Web) gestoppt, keine dauerhaften Änderungen an
`.env.local`/`vite.config.ts` (Seed-Daten leben nur im In-Memory-Zustand des jeweils
laufenden API-Prozesses und sind nach dessen Neustart wieder weg — erwartetes Verhalten,
siehe bereits dokumentiertes Risiko "kein persistenter Speicher").

## Was bewusst nicht getan wurde

- Kein Bulk-Insert-Mechanismus in den Repository-Interfaces selbst ergänzt (`IGuestAccountRepository`
  etc. bleiben unverändert) — der Seed-Endpoint ruft die bestehenden Einzel-`UpsertAsync`
  in einer Schleife auf; bei 500–5000 Einträgen ist das performant genug (< 100 ms) und
  vermeidet eine Interface-Änderung, die alle Implementierungen (auch einen künftigen
  Cosmos-Adapter) beträfe.
- Kein Endpoint zum Löschen/Zurücksetzen der Seed-Daten ergänzt — ein Neustart des
  API-Prozesses setzt den In-Memory-Zustand ohnehin zurück (`scripts/reset-local.ps1`
  bleibt für Build-Artefakte zuständig, nicht für Laufzeitdaten).
- Keine Persistenz der Seed-Daten über Prozessneustarts hinweg — folgt aus dem
  InMemory-Charakter des MVP, nicht aus dieser Änderung.

## Ergebnis

500 Gäste + 1 Workload mit differenzierten Rollen/Status lassen sich jetzt in unter
100 ms per `./scripts/seed-large-workload.ps1` erzeugen, laufen über echte API-/
Audit-/Deletion-Gate-Pfade und sind sofort in der bestehenden Web-UI sichtbar — ohne
Produktionscode oder Domain/Application-Schichten zu verändern.
