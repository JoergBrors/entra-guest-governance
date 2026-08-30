# Local Data And Seeding

Stand: 2026-08-30 (aktualisiert: Login-Flow der Seed-Skripte nach JWT-Umstellung)

Vorhanden:

- `scripts/seed-dev-data.ps1`
- `scripts/seed-large-workload.ps1`
- `POST /api/dev/seed/large-workload` unter `LOCAL_MOCK`

Seed-Daten verwenden Beispiel-Domains und keine produktiven Personen- oder Tenant-Werte.

Beide Seed-Skripte loggen sich vor dem eigentlichen Seeden per `POST /api/auth/mock/login`
als `admin@platform.example` ein und senden das JWT als `Authorization: Bearer <token>`
(keine freien `X-Platform-Tenant-Id`-Header mehr, siehe `docs/development/local-mock.md`
Abschnitt "Reset & Seed"). Nach einem `scripts/reset-cosmos-dev-data.ps1` gilt daher immer:
Reset → Portal API (neu) starten → Seed-Skript ausfuehren.

