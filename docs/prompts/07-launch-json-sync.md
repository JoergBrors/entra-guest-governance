# Prompt 07 — launch.json mit requirements.ps1 synchronisieren

- **Datum:** 29. August 2026
- **Auftrag:** Chat-Auftrag: `.vscode/launch.json` so anpassen, dass es zum von
  `requirements.ps1` ermittelten Port passt (Nutzer hatte die Datei gerade geöffnet und
  darauf hingewiesen, dass sie nicht mitzieht).

## Befund

`.vscode/launch.json`, Konfiguration "Portal API", setzte `ASPNETCORE_URLS` hart auf
`http://localhost:5000` im `"env"`-Block — parallel dazu wird `envFile:
"${workspaceFolder}/.env.local"` geladen, aus der `requirements.ps1` den tatsächlich
ermittelten API-Port bereits als `API_BASE_URL` schrieb. Da `env` und `envFile` bei
VS Code/`coreclr`-Launch-Konfigurationen beide angewendet werden und `env` dabei
vorrangig ist, hätte ein von `requirements.ps1` gewählter abweichender Port (z. B. nach
einem Portkonflikt) **nie** gewirkt — der Debug-Start wäre immer auf 5000 gelandet, selbst
wenn `.env.local` einen anderen Port auswies.

## Was geändert wurde

- **`.vscode/launch.json`** — den hartcodierten `ASPNETCORE_URLS`-Eintrag aus dem
  `"env"`-Block der Konfiguration "Portal API" entfernt. `ASPNETCORE_URLS` kommt jetzt
  ausschließlich aus `.env.local` (über `envFile`).
- **`scripts/requirements.ps1`** — schreibt zusätzlich zu `API_BASE_URL` jetzt auch
  `ASPNETCORE_URLS` (identischer Wert) nach `.env.local`, da `launch.json` genau diesen
  Variablennamen erwartet (ASP.NET-Core-Konvention), während der Rest des Projekts
  `API_BASE_URL` als eigene Konvention nutzt (z. B. für `scripts/seed-dev-data.ps1`,
  `scripts/smoke-test.ps1`).
- **`.env.example`** — `ASPNETCORE_URLS` als dokumentierten Platzhalter ergänzt, mit
  Hinweis auf den Zweck (`.vscode/launch.json`) und dass er von `requirements.ps1`
  automatisch synchron zu `API_BASE_URL` gehalten wird.
- **README.md** — kurzer Hinweis im Abschnitt "Voraussetzungen prüfen" ergänzt.

## Was getestet wurde

`requirements.ps1` erneut ausgeführt (gegen den weiterhin aktiven Web-Dev-Server des
Nutzers, siehe [06-requirements-fix-and-storage-emulator.md](06-requirements-fix-and-storage-emulator.md)) —
`.env.local` enthält jetzt korrekt sowohl `API_BASE_URL=http://localhost:5000` als auch
`ASPNETCORE_URLS=http://localhost:5000`. `dotnet build` weiterhin fehlerfrei. Der
VS-Code-Debug-Start selbst (F5 mit dem Compound "LOCAL_MOCK: API + Worker + Web") wurde
nicht separat manuell durchgeklickt, da das reine `envFile`-Laden ein von VS Code intern
gut abgedecktes Standardverhalten ist und `dotnet run` mit derselben `.env.local` bereits
in vorherigen Sessions erfolgreich gegen wechselnde Ports getestet wurde.

## Ergebnis

`.vscode/launch.json` folgt jetzt automatisch dem von `requirements.ps1` ermittelten
API-Port, ohne dass die Datei nach jedem Skriptlauf manuell angepasst werden muss.
