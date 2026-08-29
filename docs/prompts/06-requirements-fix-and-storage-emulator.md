# Prompt 06 — requirements.ps1 Fehleranalyse, fehlende Connection Strings, Azure Storage Emulator

- **Datum:** 29. August 2026
- **Auftrag:** Chat-Auftrag: der Nutzer meldete einen Fehler beim Start des Web-Dev-Servers
  ("Port 5304 is already in use", obwohl `requirements.ps1` zuvor gelaufen war). Prüfen ob
  `requirements.ps1` alles richtig macht, klären warum der Cosmos-DB-Emulator-Connection-
  String fehlte, und bei Bedarf Azure Storage Emulator (Azurite) ergänzen — inklusive
  Speicherung der Connection Strings.

## Root-Cause-Analyse des gemeldeten Fehlers

Der Nutzer hatte bereits einen eigenen `npm run dev`-Prozess aktiv laufen (PID 40668,
gebunden an Port 5304). `requirements.ps1` hatte diesen Port zuvor als belegt erkannt und
korrekt einen neuen Port in `vite.config.ts` eingetragen — aber der bereits laufende
Vite-Prozess folgte per Hot-Reload jeder `vite.config.ts`-Änderung und band sich beim
nächsten internen Neustart wieder an den zuletzt eingetragenen Port. Ein weiterer Start
(zweiter `npm run dev` oder ein erneuter Skriptlauf mit anschließendem manuellem Start)
kollidierte dann mit dem noch laufenden ersten Prozess. Live am System reproduziert und
bestätigt (siehe Abschnitt "Was getestet wurde").

**Kernbefund:** `requirements.ps1` hat die Portbelegung korrekt erkannt, aber bis zu dieser
Version nicht angezeigt, *wodurch* ein Port belegt ist — der Nutzer konnte also nicht
erkennen, dass es sich um seinen eigenen, weiterhin aktiven Dev-Server handelte statt um
einen verwaisten/fremden Prozess.

## Was behoben/ergänzt wurde

### 1. Prozess-Erkennung bei Portkonflikten (Kernfix)

`scripts/requirements.ps1` — neue Funktion `Get-PortOwnerInfo` ermittelt bei einem
belegten Port den haltenden Prozess (PID, Name, Kommandozeile über
`Get-CimInstance Win32_Process`) und schätzt heuristisch, ob es sich um einen eigenen
Prozess dieses Repos handelt (Kommandozeile enthält Repo-Pfad, `B2B.Portal` oder `vite`).
Die neue Funktion `Resolve-Port` nutzt das und gibt bei Belegung eine `Write-Warning` mit
PID + Kommandozeile aus, bevor sie auf den nächsten freien Port ausweicht — killt aber
bewusst nichts automatisch (siehe Antwort auf die entsprechende Rückfrage: "Erkennen +
warnen, nicht killen").

### 2. Fehlende Cosmos-Connection-String-Struktur

Vorher wurden `COSMOS_EMULATOR_ENDPOINT`/`COSMOS_EMULATOR_KEY`/`COSMOS_DATABASE_ID` nur
geschrieben, wenn `-InitCosmosEmulator` tatsächlich lief — ohne dieses Flag fehlten sie in
`.env.local` komplett, ihr Zweck war aus der Datei nicht ersichtlich. Jetzt: Platzhalter
(leer) werden bei **jedem** Lauf angelegt, Werte nur bei `-InitCosmosEmulator` scharf
gesetzt. Gleiches Muster für `AZURE_STORAGE_CONNECTION_STRING`. `.env.example` dokumentiert
beide Blöcke inklusive Ursprung (Well-Known-Emulator-Keys, keine echten Secrets, mit
Verweis auf die jeweilige Microsoft-Dokumentation).

### 3. Azure Storage Emulator (Azurite) ergänzt

Auf Nutzerentscheidung: Azurite (aktueller, offiziell empfohlener Nachfolger des
klassischen Windows Storage Emulators, npm-Paket) statt des deprecateten MSI-Tools.

- **Tool-Check:** `Get-Command azurite` — Status OK/FEHLT, mit `-Install`/
  `-InstallStorageEmulator` automatische Installation per `npm install -g azurite`.
- **`-InitStorageEmulator`:** startet Azurite im Hintergrund (Datenverzeichnis
  `.azurite/`, neu in `.gitignore`), wartet bis Port 10000 (Blob) reagiert, schreibt den
  vollständigen Well-Known-Connection-String (Account `devstoreaccount1`, öffentlich
  dokumentierter Key, Endpoints für Blob/Queue/Table auf 10000/10001/10002) nach
  `.env.local` als `AZURE_STORAGE_CONNECTION_STRING`.

**Während der Implementierung gefundener und behobener Zusatzfehler:** Der erste Ansatz
(`Start-Process -FilePath "azurite" -ArgumentList ...`) startete den Prozess nicht
zuverlässig — `azurite` ist unter Windows ein npm-generierter `.cmd`-Shim, den
`Start-Process` mit direktem `-FilePath` und komplexer `-ArgumentList`-Quotierung nicht
zuverlässig ausführt (kein Fehler, aber kein laufender Prozess, kein Log). Live
reproduziert (leeres `.azurite/`-Verzeichnis, kein `debug.log`, Port 10000 blieb frei) und
behoben durch Umweg über `cmd.exe /c azurite ...`, was zuverlässig funktioniert.

## Was getestet wurde (live, nicht nur Codelesung)

| Test | Ergebnis |
| --- | --- |
| `requirements.ps1` gegen den vom Nutzer gemeldeten, tatsächlich noch laufenden blockierenden Prozess (PID 40668 auf Port 5304) | ✅ Warnung korrekt ausgegeben: "PID 40668 (node.exe) — sieht nach einem laufenden Prozess DIESES Repos aus", wich korrekt auf nächsten freien Port aus |
| `-InitCosmosEmulator` scharf (Cosmos DB Emulator war beim Nutzer bereits installiert) | ✅ Emulator gestartet, Datenbank `b2b-portal` + Container `domain-data`/`job-queue` per REST/HMAC angelegt (bestätigt: die HMAC-Signaturimplementierung ist korrekt), Connection-Werte korrekt in `.env.local` |
| `-InitStorageEmulator` scharf, erster Versuch | ❌ Fehlgeschlagen ("Azurite konnte nicht gestartet werden") — Ursache gefunden (`.cmd`-Shim-Problem, siehe oben) |
| `-InitStorageEmulator` scharf, nach Fix | ✅ Azurite gestartet, Blob-Endpoint (Port 10000) antwortete auf HTTP-Request, Connection String korrekt in `.env.local` |
| Erneuter Dry-Run nach allen Fixes | ✅ Alle Checks weiterhin korrekt, Portkonflikt-Warnung weiterhin korrekt |

Nach den Tests: Azurite- und Cosmos-Testprozesse gestoppt, `.azurite/`-Testverzeichnis
entfernt, `vite.config.ts` wieder auf den vom Nutzer aktiv genutzten Port 5301 gesetzt
(sein weiterhin laufender Dev-Server folgte den Config-Änderungen automatisch per
Hot-Reload und band sich jedes Mal korrekt neu — live bestätigt), `.env.local` konsistent
zum tatsächlich laufenden Port belassen (nicht entfernt, da der Nutzer aktiv damit
arbeitet).

## Was bewusst nicht getan wurde

- Kein automatisches Beenden fremder oder eigener laufender Prozesse — auf ausdrücklichen
  Nutzerwunsch nur Erkennung + Warnung, kein `-KillStaleProcesses` o. ä.
- Kein klassischer (deprecateter) Windows Storage Emulator ergänzt — bewusst Azurite
  gewählt, siehe Rückfrage-Antwort.
- Keine Azure-Cloud-Ressourcen berührt — beide Emulatoren (Cosmos, Azurite) laufen
  ausschließlich lokal.

## Ergebnis

`requirements.ps1` erkennt jetzt zuverlässig, *wodurch* ein Port blockiert ist, statt nur
*dass* er blockiert ist — das schließt die Lücke, die zum ursprünglich gemeldeten Fehler
geführt hat (der Nutzer wusste nicht, dass sein eigener Dev-Server noch aktiv war). Cosmos-
DB-Emulator- und neu Azurite-Connection-Strings werden zuverlässig ermittelt und in
`.env.local` gespeichert, mit vollständiger Ursprungsdokumentation in `.env.example`. Beide
Emulator-Init-Pfade wurden live getestet (nicht nur syntaktisch geprüft), inklusive eines
während der Tests gefundenen und behobenen echten Bugs im Azurite-Start.
