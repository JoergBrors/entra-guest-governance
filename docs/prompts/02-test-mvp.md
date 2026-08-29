# Prompt 02 — MVP Verification

- **Datum:** 28. August 2026
- **Auftrag:** [`prompts-original/02-test-mvp.md`](../prompts-original/02-test-mvp.md) ("MVP Verification")
- **Ausführungsumgebung:** dieselbe Sandbox wie Prompt 01, weiterhin ohne `dotnet` CLI.

## Was beauftragt wurde

Das bestehende Repository gegen die Development-/MVP-Anforderungen prüfen: Struktur,
`dotnet restore/build/test`, Frontend `npm ci/build/test`, API-Health und
Query/Command-Endpoints im `LOCAL_MOCK`-Modus, Worker-Jobs Ende-zu-Ende (Invite, Grant,
Notification, Review, Revoke, ValidateDeletion), Tenant-Isolation negativ testen,
Idempotenz nachweisen, Deletion Gate negativ testen (alle Blocker-Typen), Notification-Mock-
Protokollierung, Graph-Shared-Mailbox-Konfigurationstreue, Audit Events, abschließend Status
PASS / PASS WITH PENDING INTEGRATIONS / FAIL vergeben.

## Was tatsächlich getan wurde

- Repository-/Projektstruktur gegen die Anforderungen geprüft (strukturell vollständig).
- Frontend-Verifikation wiederholt: `npm install`, `npm run build`, `npx vitest run`,
  `npx tsc -b --force` — alle erfolgreich.
- Backend-Verifikation **nicht möglich** — `dotnet` weiterhin nicht verfügbar
  (`/bin/sh: 1: dotnet: not found`).
- `docs/architecture/mvp-test-report.md` mit MVP-Kriterien-Tabelle (Status je Kriterium),
  offenen Integrationstests, Security-/Tenant-Isolation-Befunden (durch Codelesung,
  nicht durch Testlauf), bekannten Risiken und nächsten Schritten erstellt.

## Ergebnis

**Gesamtstatus laut damaligem Report: PASS WITH PENDING INTEGRATIONS**, ausdrücklich unter
dem Vorbehalt "Backend-Code noch nicht lokal kompiliert/getestet". Alle Backend-Kriterien
trugen den Status ⚠️ ("Code vorhanden, nicht ausgeführt") statt ✅.

## Nachgelagerte Korrektur

Am 29. August 2026 (siehe [03-completeness-check.md](03-completeness-check.md)) wurde der
Backend-Teil erstmals real gebaut und getestet. Der `mvp-test-report.md` wurde dabei
aktualisiert: aus ⚠️ wurde für die meisten Kriterien ✅, nachdem 3 reale Kompilierfehler
behoben waren und 31/31 .NET-Tests grün liefen.
