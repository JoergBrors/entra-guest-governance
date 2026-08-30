# ADR-003: Cosmos Development Storage

Datum: 2026-08-29

## Status

Accepted

## Entscheidung

`LOCAL_MOCK` laeuft gegen Cosmos DB Emulator. Cosmos DB ist der einzige Datenprovider —
die InMemory-Option wurde entfernt (Erweiterung 2026-08-30 (Teil 2), siehe
`docs/architecture/mvp-test-report.md`).

