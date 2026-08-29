# GUI Architecture

Stand: 2026-08-29

Die Web-GUI liegt in `src/B2B.Portal.Web` und nutzt React, TypeScript, Vite und Fluent UI.
Die App Shell besteht aus `src/components/AppLayout.tsx`.

## Struktur

- `src/App.tsx`: Routing, UI-Konfiguration, FluentProvider.
- `src/components/AppLayout.tsx`: Navigation, Header, Development Theme Switch.
- `src/pages/*`: bestehende und ergaenzte MVP-Seiten.
- `src/themes/*`: Theme-Schema, Theme Loader und zwei Beispielthemes.
- `src/api/client.ts`: API-Client mit LOCAL_MOCK Header-Kontext.

## Screens

Vorhanden oder ergaenzt:

- Dashboard
- Meine Workloads
- Guest Pool
- Gast Detail
- Workloads
- Workload Detail
- Scenarios
- Guest Import
- Reviews
- Access Request
- Compliance
- Discovery
- Jobs
- Audit
- Development Theme Preview

Unbekannte produktive API-Quellen bleiben `integration pending`.

