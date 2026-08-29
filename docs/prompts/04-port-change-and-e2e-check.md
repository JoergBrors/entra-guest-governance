# Prompt 04 — Anderer Web-Port + LOCAL_MOCK End-to-End-Prüfung

- **Datum:** 29. August 2026
- **Auftrag:** Chat-Auftrag: Frontend-Dev-Port von 5173 auf einen anderen Port ändern und
  prüfen, ob `LOCAL_MOCK` tatsächlich funktioniert (nicht nur Build/Unit-Tests).

## Was getan wurde

### Port-Wechsel

- `src/B2B.Portal.Web/vite.config.ts` — `server.port` von `5173` auf **`5301`** geändert,
  zusätzlich `strictPort: true` gesetzt (Vite weicht nicht mehr stillschweigend auf einen
  anderen Port aus, falls der konfigurierte belegt ist — verhindert, dass CORS/README
  unbemerkt von der tatsächlichen URL abweichen).
- Konsistent nachgezogen: `src/B2B.Portal.Api/appsettings.json` (`WEB_BASE_URL`),
  `src/B2B.Portal.Api/Program.cs` (CORS-Fallback-Origin), `.env.example` (Root),
  `README.md` (Endpunkt-Tabelle).
- Port-Wahl: 5173 (Vite-Standard) und die ursprünglich naheliegenden Alternativen 5180 und
  5273 waren auf dem Testsystem durch einen zuvor selbst gestarteten, nicht sauber
  beendeten `npm run dev`-Hintergrundprozess belegt (siehe unten). 5301 wurde vorab per
  `netstat` als frei verifiziert und nach Bereinigung dieses Prozesses bestätigt.

### LOCAL_MOCK End-to-End-Prüfung

API, Worker und Web wurden gemeinsam lokal gestartet (nicht nur `dotnet build`/`dotnet
test`, sondern echte laufende Prozesse mit HTTP-Requests dagegen):

- `GET /health` → healthy.
- Query-Endpoints (`guest-accounts`, `workloads`, `reviews`, `audit-events`) mit
  `X-Platform-Tenant-Id`-Header → funktionieren; **ohne** Header → HTTP 500 statt 400/401
  (bestätigt eine bereits in Prompt 03 dokumentierte, noch offene Lücke: fehlende
  Exception-Middleware).
- `POST /api/guests/invite` → legt Guest an, per `GET` wieder auffindbar.
- Tenant-Isolation negativ getestet: Guest aus `dev-tenant-a` ist für `dev-tenant-b` nicht
  sichtbar.
- `POST /api/deletion-candidates/{id}/validate` (Dry-Run) → liefert "Ready" ohne Blocker,
  kein echter Delete ausgelöst.
- Web-UI liefert HTTP 200 auf dem neuen Port, CORS erlaubt exakt diesen Origin.

**Neuer Befund:** API und Worker sind getrennte Prozesse mit jeweils eigenem
In-Memory-Zustand. Ein über die API erzeugter Job ist für den separat laufenden
Worker-Prozess nicht sichtbar — der Job-Fluss über Prozessgrenzen hinweg funktioniert erst
mit einem gemeinsamen persistenten Speicher (geplanter Cosmos-Adapter). Das ist keine neue
Baustelle, sondern eine konkrete, jetzt beobachtete Ausprägung des bereits dokumentierten
Risikos "kein persistenter Speicher" — bisher nur aus dem Code abgeleitet, jetzt am
laufenden System bestätigt.

Details siehe `docs/architecture/mvp-test-report.md`, Abschnitt 2.4.

## Was bewusst nicht getan wurde

- Der fremde `node.exe`-Prozess, der ursprünglich 5180 belegte, stellte sich als mein
  eigener, nicht sauber beendeter Hintergrundprozess aus einem vorherigen Startversuch
  heraus — er wurde beendet, es wurden keine anderen/fremden Nutzerprozesse angerührt.
- Die Exception-Middleware-Lücke (HTTP 500 statt 400/401) wurde **nicht** behoben — das
  war nicht Teil dieses Auftrags und bleibt der bereits dokumentierte nächste Schritt.
- Der getrennte In-Memory-Zustand zwischen API und Worker wurde **nicht** behoben (würde
  den Cosmos-Adapter oder einen anderen gemeinsamen Speicher voraussetzen) — nur
  dokumentiert.

## Ergebnis

`LOCAL_MOCK` funktioniert als Gesamtsystem: Health, Queries, Commands, Tenant-Isolation
und Deletion-Gate-Dry-Run wurden live gegen laufende Prozesse verifiziert, nicht nur über
Unit-/Integrationstests. Web-Dev-Server läuft jetzt zuverlässig auf Port 5301 statt 5173.
