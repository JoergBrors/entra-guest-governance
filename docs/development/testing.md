# Testing

Stand: 2026-08-30

Ausgefuehrte Checks fuer die Erweiterung (Docker-Stack, Mock-Entra-Applications/Sign-Ins, Workload-Patterns, Job-Stop):

- `dotnet build -c Debug`
- `npm run build`
- `dotnet test -c Debug`
- `npm run test -- --run`

Ergebnisse:

- .NET: 83 Tests bestanden (Domain 29, Architecture 5, Application 3, Integration 46).
- Frontend: 5 Tests bestanden, `npm run build` erfolgreich (Vite meldet einen Chunk >500 kB — reine Bundle-Groessen-Warnung, kein Fehler).

Hinweis: `npm` meldete beim Start eine lokale Zugriffswarnung auf `C:\Users\JoergBrors\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js`; Build und Tests liefen trotzdem erfolgreich.

Nicht ausgefuehrt in dieser Runde: `docker compose up --build` (kompletter Container-Stack) — nur statisch gegen `docker-compose.yml`/Dockerfiles geprueft, kein Live-Start verifiziert.
