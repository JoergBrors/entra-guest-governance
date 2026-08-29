# Testing

Stand: 2026-08-29

Ausgefuehrte Checks fuer die Erweiterung:

- `dotnet build -c Debug`
- `npm run build`
- `dotnet test -c Debug`
- `npm run test -- --run`

Ergebnisse:

- .NET: 39 Tests bestanden.
- Frontend: 5 Tests bestanden.

Hinweis: `npm` meldete beim Start eine lokale Zugriffswarnung auf `C:\Users\JoergBrors\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js`; Build und Tests liefen trotzdem erfolgreich.
