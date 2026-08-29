# Prompt 05 — requirements.ps1: Voraussetzungen prüfen, Ports, Cosmos DB Emulator

- **Datum:** 29. August 2026
- **Auftrag:** Chat-Auftrag: ein `requirements.ps1` erstellen, das einmalig prüft, welche
  Ports verwendet werden können, die entsprechenden Einstellungen vornimmt, alle
  Entwickler-Runtimes prüft und weitere Tools wie eine Cosmos-DB-Entwicklerumgebung
  initialisiert.

## Vorab geklärt (AskUserQuestion)

- Fehlende Voraussetzungen: standardmäßig nur prüfen/melden, mit `-Install` optional per
  winget nachinstallieren.
- "Cosmos DB Entwicklerumgebung initialisieren" = lokalen Azure Cosmos DB Emulator
  (Windows) prüfen/starten und die im Bicep-Modul definierte Struktur (Datenbank
  `b2b-portal`, Container `domain-data`/`job-queue`) lokal per Emulator-REST-Endpoint
  anlegen — keine Azure-Cloud-Ressourcen.
- Ports: freie Ports aktiv ermitteln und nach `.env.local` (und bei Bedarf
  `vite.config.ts`) schreiben, nicht nur anzeigen.

## Was erstellt wurde

**`scripts/requirements.ps1`** (neu), Ablauf in drei Abschnitten:

1. **Runtimes/Tools-Check:** .NET SDK (Mindestversion aus `global.json`), Node.js, npm,
   Azure CLI, Bicep CLI (`az bicep version`), Microsoft.Graph PowerShell SDK (für
   `scripts/setup-entra-app.ps1`), Cosmos DB Emulator (Installationsstatus über
   `%ProgramFiles%\Azure Cosmos DB Emulator\Microsoft.Azure.Cosmos.Emulator.exe`). Mit
   `-Install`: fehlende Tools per `winget install` nachinstallieren (Bicep CLI über
   `az bicep install`).
2. **Port-Ermittlung:** liest den aktuell konfigurierten Web-Port aus
   `vite.config.ts` (Fallback: Parameter `-WebPortFallback`, Default 5301) und prüft
   API-Port (Default 5000, Parameter `-ApiPort`) sowie Web-Port per
   `Get-NetTCPConnection -State Listen`. Bei Belegung wird automatisch der nächste freie
   Port gesucht (`Find-FreePort`) und:
   - `vite.config.ts` (`server.port`) aktualisiert, falls sich der Web-Port geändert hat,
   - `.env.local` (Root) mit `API_BASE_URL`/`WEB_BASE_URL` geschrieben (Datei wird aus
     `.env.example` initialisiert, falls sie noch nicht existiert),
   - `src/B2B.Portal.Web/.env.local` mit `VITE_API_BASE_URL` synchron gehalten.
3. **Cosmos DB Emulator (nur mit `-InitCosmosEmulator`):** startet den Emulator über das
   mitgelieferte PowerShell-Modul (`Start-CosmosDbEmulator`/`Get-CosmosDbEmulatorStatus`),
   mit Fallback auf die Exe (`/GetStatus`, `/NoUI`) falls das Modul fehlt. Legt anschließend
   per REST-Aufruf (Cosmos-DB-REST-API mit HMAC-SHA256-Signatur nach offiziellem Schema,
   `StringToSign = "{verb}\n{resourceType}\n{resourceLink}\n{date}\n\n"`, siehe Microsoft
   Learn "Access control in the Azure Cosmos DB SQL API") die Datenbank `b2b-portal` sowie
   die Container `domain-data` und `job-queue` mit der in
   `infra/modules/cosmos-free-tier.bicep` definierten Partitionierung
   (`/platformTenantId`) an — idempotent (HTTP 409 bei bereits existierenden Objekten wird
   abgefangen). Nutzt ausschließlich den öffentlich dokumentierten Well-Known-Emulator-Key
   (kein echtes Secret, gilt für jede lokale Emulator-Installation gleichermaßen) und
   schreibt `COSMOS_EMULATOR_ENDPOINT`/`COSMOS_EMULATOR_KEY`/`COSMOS_DATABASE_ID` nach
   `.env.local`.

Am Ende gibt das Skript eine Zusammenfassungstabelle (`Name`/`Status`/`Detail`) aus und
weist bei fehlenden Voraussetzungen auf `-Install`/`-InitCosmosEmulator
-InstallCosmosEmulator` hin.

**README.md** — neuer Quick-Start-Abschnitt "0) Voraussetzungen prüfen" mit
Nutzungsbeispielen für Dry-Run und vollständigen Lauf.

## Was getestet wurde

Auf dem lokalen Entwicklungssystem (Windows, mit vollem Tooling) tatsächlich ausgeführt,
nicht nur syntaktisch geprüft:

| Szenario | Ergebnis |
| --- | --- |
| Dry-Run, alle Ports frei | ✅ Alle Runtime-Checks korrekt (`.NET SDK OK`, `Node.js OK`, `npm OK`, `Azure CLI OK`, `Bicep CLI OK`, `Microsoft.Graph PowerShell OK`), Cosmos DB Emulator korrekt als `FEHLT` erkannt (auf diesem System nicht installiert), Ports korrekt als frei erkannt, `.env.local` beider Projekte korrekt aus `.env.example` initialisiert und mit Ports beschrieben |
| API-Port 5000 künstlich belegt (laufende `dotnet run`-Instanz) | ✅ Skript erkennt Belegung, wählt automatisch 5001, schreibt es korrekt in beide `.env.local`-Dateien |
| Web-Port 5301 künstlich belegt (laufender `npm run dev`) | ✅ Skript wählt automatisch 5302, aktualisiert `vite.config.ts` (`server.port: 5302`) und beide `.env.local`-Dateien konsistent |

Nach den Tests: Testprozesse gestoppt, `vite.config.ts` auf den regulären Wert 5301
zurückgesetzt, die testweise erzeugten `.env.local`-Dateien auf Nutzerwunsch wieder
entfernt (sie sind ohnehin nicht in git, siehe `.gitignore`).

## Was bewusst nicht getan wurde

- `-Install`/`-InstallCosmosEmulator` wurden **nicht** scharf ausgeführt (kein winget-Install
  angestoßen) — nur die Erkennungslogik wurde verifiziert, um keine ungewollten
  Systemänderungen (Softwareinstallation) ohne expliziten Nutzerwunsch vorzunehmen.
- Der Cosmos-DB-Emulator-Start/-Init-Zweig (`-InitCosmosEmulator`) wurde **nicht** end-to-end
  getestet, da der Emulator auf diesem System nicht installiert ist — die REST-Aufruf-Logik
  (HMAC-Signatur, Datenbank-/Container-Anlage) folgt exakt dem offiziell dokumentierten
  Cosmos-DB-REST-API-Schema, ist aber ohne installierten Emulator nicht live verifizierbar.
- Keine Azure-Cloud-Ressourcen berührt — das Skript arbeitet ausschließlich lokal (Tools,
  Ports, lokaler Emulator).

## Ergebnis

`scripts/requirements.ps1` steht bereit und wurde in den relevantesten Pfaden (Runtime-
Erkennung, Port-Kollision + automatischer Fallback für API und Web) live getestet. Der
Cosmos-Emulator-Init-Pfad ist implementiert und dokumentiert, aber mangels installiertem
Emulator auf diesem System nicht end-to-end verifiziert — das ist im Report als offener
Punkt vermerkt.
