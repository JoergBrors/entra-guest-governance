# Prompt-Dokumentation

Pro ausgeführtem Auftrag (Prompt) an einen Coding-Agenten entsteht hier eine
Zusammenfassung: was wurde beauftragt, was wurde tatsächlich getan, welche Ergebnisse und
offenen Punkte gab es. Die ursprünglichen, wortgleich übernommenen Codex-Prompts liegen in
[`../prompts-original/`](../prompts-original/) — diese Zusammenfassungen hier sind das
Ergebnis-Protokoll je Auftrag, nicht der Auftrag selbst.

| # | Datum | Prompt | Zusammenfassung |
| --- | --- | --- | --- |
| 01 | 2026-08-28 | [`prompts-original/01-bootstrap-mvp.md`](../prompts-original/01-bootstrap-mvp.md) | [01-bootstrap-mvp.md](01-bootstrap-mvp.md) |
| 02 | 2026-08-28 | [`prompts-original/02-test-mvp.md`](../prompts-original/02-test-mvp.md) | [02-test-mvp.md](02-test-mvp.md) |
| 03 | 2026-08-29 | Vollständigkeitsprüfung + Entra/Bicep-Automatisierung (Chat-Auftrag) | [03-completeness-check.md](03-completeness-check.md) |
| 04 | 2026-08-29 | Web-Port ändern + LOCAL_MOCK End-to-End-Prüfung (Chat-Auftrag) | [04-port-change-and-e2e-check.md](04-port-change-and-e2e-check.md) |
| 05 | 2026-08-29 | `requirements.ps1`: Voraussetzungen, Ports, Cosmos DB Emulator (Chat-Auftrag) | [05-requirements-script.md](05-requirements-script.md) |
| 06 | 2026-08-29 | `requirements.ps1` Fehleranalyse, fehlende Connection Strings, Azurite (Chat-Auftrag) | [06-requirements-fix-and-storage-emulator.md](06-requirements-fix-and-storage-emulator.md) |
| 07 | 2026-08-29 | `launch.json` mit `requirements.ps1` synchronisieren (Chat-Auftrag) | [07-launch-json-sync.md](07-launch-json-sync.md) |
| 08 | 2026-08-29 | Mockdaten: Workload mit 500 Gästen (Chat-Auftrag) | [08-large-workload-mockdata.md](08-large-workload-mockdata.md) |
| 09 | 2026-08-29 | Cosmos-DB-Migration (Phase 1 des Plans) + Cosmos als LOCAL_MOCK-Default (Plan-Modus + Chat-Auftrag) | [09-cosmos-migration-and-default.md](09-cosmos-migration-and-default.md) |
| 10 | 2026-08-29 | Workload-Szenarien + JSONLogic-Bedingungen (Phase 2 des Plans, Chat-Auftrag) | [10-scenario-model-and-jsonlogic.md](10-scenario-model-and-jsonlogic.md) |
| 11 | 2026-08-29 | Szenario-Modell-Redesign: freies Template mit Ressourcen-Regeln (Plan-Modus + Chat-Auftrag) | [11-scenario-template-redesign.md](11-scenario-template-redesign.md) |
| 12 | 2026-08-29 | Workloads/Szenarien editier- und löschbar machen (Chat-Auftrag) | [12-workload-scenario-edit-delete.md](12-workload-scenario-edit-delete.md) |
| 13 | 2026-08-29 | Nutzerzahlen, Workload-Hart-Löschen, Guest-Pool-Unassign, Szenario-Cleanup (Chat-Auftrag) | [13-workload-user-counts-hard-delete.md](13-workload-user-counts-hard-delete.md) |
| 14 | 2026-08-29 | Excel-Gäste-Import mit konfigurierbarem Spalten-Mapping (Phase 4 des Plans, Plan-Modus + Chat-Auftrag) | [14-guest-excel-import.md](14-guest-excel-import.md) |
| 15 | 2026-08-29 | Challenge + GUI-/Theme-/Doku-Erweiterung (Chat-Auftrag) | [15-challenge-and-gui-extensions.md](15-challenge-and-gui-extensions.md) |
