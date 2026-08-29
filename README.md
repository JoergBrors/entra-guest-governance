# B2B Guest Governance Portal — Development Foundation & MVP

Mandantenfähige, workloadorientierte Verwaltung externer Identitäten (Microsoft Entra B2B Guests).

Dieses Repository implementiert die Development-Grundlage und den ersten MVP gemäß:

- `B2B_Guest_Governance_Portal_Blueprint.docx` — Architektur- und Konzept-Blueprint
- `B2B_Guest_Governance_Development_und_MVP.docx` — Development- und MVP-Implementierungsleitfaden

> ⚠️ **Hinweis zur Erstellung dieses Repos:** Der C#/.NET-Teil wurde in einer Sandbox-Umgebung
> ohne Internetzugriff auf `dotnet.microsoft.com` erstellt. Der Code folgt der im Blueprint
> festgelegten Architektur, Projektstruktur und den Interfaces, konnte hier aber **nicht** mit
> `dotnet build` / `dotnet test` verifiziert werden. Führe vor dem ersten produktiven Einsatz
> unbedingt die Quality Gates in Abschnitt "Quick Start" lokal aus. Der Frontend-Teil (React/Vite)
> wurde lokal gebaut und getestet.

## Kernprinzipien (siehe Blueprint Abschnitt 3)

- **Guest Pool statt Workload-Ownership** — Gäste gehören keinem einzelnen Workload.
- **Desired State ≠ Actual State** — Reconciliation und Live Validation sind explizite Prozesse.
- **Governance vor Löschung** — nur der Governance Core / das Lifecycle-Modul darf eine
  Gastidentität löschen. Workloads/Connectoren entziehen nur Zugriff.
- **Mock-first** — `LOCAL_MOCK` ist der Default-Modus. Keine echten Graph-Schreibzugriffe oder
  E-Mails ohne explizite Integrationskonfiguration.
- **Tenant-Isolation by design** — jede Entität, jeder Job, jede Query trägt Tenant-Kontext.
- **Audit-first** — jede sicherheitsrelevante Aktion erzeugt ein AuditEvent mit CorrelationId.

## Repository-Struktur

Siehe `docs/architecture/development-plan.md` für die vollständige Struktur und den
Implementierungsplan. Kurzüberblick:

```
src/B2B.Portal.Web/            React + TypeScript + Vite (Fluent-UI-nahes Design)
src/B2B.Portal.Api/            ASP.NET Core 10 — Commands/Queries, Tenant Context, Health
src/B2B.Portal.Application/    Use Cases, Ports (Interfaces), Commands, Queries
src/B2B.Portal.Domain/         reine Fachlogik, keine Azure-/Graph-Referenzen
src/B2B.Portal.Infrastructure/ Graph-, Data-, Queue-, Mail-Adapter (Mock + Graph-Schale)
src/B2B.Portal.Worker/         .NET 10 Worker Host mit 7 Handlergruppen
tests/                         Domain / Application / Architecture / Integration Tests
infra/                         Bicep (main.bicep + Module + dev/poc Parameterdateien)
prompts/                       Codex-Prompts (Bootstrap + MVP Verification), aus dem
                                Development-Dokument unverändert übernommen
docs/architecture/             Implementierungsplan, MVP-Test-Report
```

## Drei Development-Modi

| Modus | Zweck |
| --- | --- |
| `LOCAL_MOCK` | Default. UI + API + Worker lokal, Mock Directory/Mail/Queue/Data. Keine externen Schreibzugriffe. |
| `DEV_INTEGRATION` | Gezielte Integrationstests gegen einen dedizierten Entra Dev-Tenant + Shared Mailbox. |
| `AZURE_DEV` | End-to-End-Abnahme in Azure Dev/PoC. |

Konfiguration erfolgt über `.env.local` (siehe `.env.example`). Es werden **keine** realen
Tenant-IDs, Secrets, Group-IDs oder Mailboxen im Repository hinterlegt (siehe Blueprint,
"Nicht festgelegt").

## Quick Start

### 0) Voraussetzungen prüfen (empfohlen vor dem ersten Start)

```powershell
# Nur prüfen (Runtimes/Tools, freie Ports) — ändert nur .env.local/vite.config.ts, keine Cloud-Ressourcen
./scripts/requirements.ps1

# Fehlende Tools nachinstallieren + Cosmos DB Emulator + Azurite (Storage Emulator) lokal initialisieren
./scripts/requirements.ps1 -Install -InitCosmosEmulator -InstallCosmosEmulator -InitStorageEmulator -InstallStorageEmulator
```

Prüft .NET SDK, Node.js/npm, Bicep CLI, Azure CLI, Microsoft.Graph PowerShell SDK, den
lokalen Cosmos DB Emulator und Azurite (lokaler Azure Storage Emulator); ermittelt freie
Ports für API/Web (weicht bei Belegung automatisch aus und zeigt an, welcher Prozess den
Port blockiert) und schreibt sie nach `.env.local` bzw. `vite.config.ts`. Mit
`-InitCosmosEmulator`/`-InitStorageEmulator` werden die jeweiligen Connection Strings
(Well-Known-Emulator-Keys, keine echten Secrets) ebenfalls nach `.env.local` geschrieben.
Details siehe Kommentarkopf des Skripts.

Der ermittelte API-Port wird zusätzlich als `ASPNETCORE_URLS` nach `.env.local`
geschrieben — `.vscode/launch.json` ("Portal API") lädt diese Datei über `envFile` und
startet damit automatisch auf demselben Port wie `dotnet run`/das Skript, ohne manuelle
Anpassung der Launch-Konfiguration.

**Bekannter Fallstrick:** Läuft bereits ein eigener `npm run dev`, bindet Vite sich beim
nächsten Start wieder an den zuletzt in `vite.config.ts` eingetragenen Port — ein
paralleler zweiter Start auf demselben Port schlägt dann fehl ("Port already in use"),
auch wenn `requirements.ps1` zuvor einen anderen Port ausgewichen hat. Das Skript weist ab
sofort per Warnung darauf hin, welcher Prozess (PID, Kommandozeile) einen Port blockiert.

```bash
# 1) .NET Backend
dotnet restore
dotnet build -c Debug
dotnet test -c Debug

# 2) Frontend
cd src/B2B.Portal.Web
npm ci
npm run build
npm run test -- --run
cd ../..

# 3) LOCAL_MOCK starten (drei Terminals oder VS Code Compound Launch)
dotnet run --project src/B2B.Portal.Api
dotnet run --project src/B2B.Portal.Worker
npm run dev --prefix src/B2B.Portal.Web
```

Lokale Endpunkte:

| Komponente | URL |
| --- | --- |
| Web UI | http://localhost:5301 |
| Portal API | http://localhost:5000 |
| Health | http://localhost:5000/health |
| Worker | Hintergrundprozess / Konsolenlog |

### 4) Aussagekräftige Mockdaten laden (optional)

Für Demos/UI-Tests mit realistischer Datenmenge — ein Workload mit mehreren Rollen und
500 (konfigurierbar) Gästen, verteilt über mehrere Beispielfirmen, Lifecycle-Status und
Rollen:

```powershell
./scripts/seed-large-workload.ps1
# oder mit eigener Anzahl/Name:
./scripts/seed-large-workload.ps1 -GuestCount 1500 -WorkloadName "Onboarding-Projekt Nord"
```

Ruft `POST /api/dev/seed/large-workload` auf — ein Endpoint, der **nur unter
`B2B_MODE=LOCAL_MOCK`** registriert ist (siehe `src/B2B.Portal.Api/Program.cs`) und
ausschließlich in die lokalen InMemory-Repositories schreibt. Ergebnis danach sichtbar in
der Web-UI (Guest Pool, Workloads-Admin-Ansicht) oder direkt über `/api/guest-accounts`
bzw. `/api/workloads`.

## Sicherheitswarnung (Definition of Safe Local Development)

> Ein frisches Checkout darf nach Restore/Install/Start **keine** externen Directory- oder
> Mail-Schreiboperationen ausführen. Erst eine explizite `DEV_INTEGRATION`-Konfiguration
> (separate App Registration, dedizierter Dev-Tenant, dedizierte Shared Mailbox) schaltet
> reale Adapter frei. Secrets werden nie in `.env`-Dateien committed — nutze User Secrets /
> Key Vault / Managed Identity.

## Entra-ID-Voraussetzungen automatisiert herstellen (DEV_INTEGRATION)

Für `DEV_INTEGRATION` wird eine App Registration mit Graph-Application-Permissions
(`User.Invite.All`, `Mail.Send`, `Group.ReadWrite.All`, `User.Read.All`) benötigt. Dies
kann per Microsoft Graph PowerShell automatisiert werden — es werden dabei **keine**
Azure-Compute-/Storage-Ressourcen angelegt, nur Objekte in Entra ID:

```powershell
# Dry-Run (Default) — zeigt nur an, was angelegt würde
./scripts/setup-entra-app.ps1

# Tatsächlich anlegen und Werte nach .env.local schreiben (nicht committed)
./scripts/setup-entra-app.ps1 -Apply -WriteEnvLocal
```

Voraussetzung: [Microsoft.Graph PowerShell SDK](https://learn.microsoft.com/powershell/microsoftgraph)
(`Install-Module Microsoft.Graph -Scope CurrentUser`) und ein Konto mit
`Application.ReadWrite.All` + `AppRoleAssignment.ReadWrite.All` im Ziel-Tenant (idealerweise
ein dedizierter Entra Dev-Tenant, niemals ein Produktions-Tenant).

Optional: Spiegelung der `.env.local`-Secrets in einen Azure Key Vault, sobald ein Vault via
`infra/modules/key-vault.bicep` (Parameter `deployKeyVault=true` in `main.bicep`, Default
`false`) deployt wurde:

```powershell
./scripts/sync-keyvault.ps1 -VaultName <name> -Apply
```

Beide Skripte laufen standardmäßig im Dry-Run (`-WhatIf`-Charakter) und ändern ohne
`-Apply` nichts. In der lokalen Entwicklung (`LOCAL_MOCK`) ist keines der beiden Skripte
erforderlich.

## Codex-Prompts

`prompts/01-bootstrap-mvp.md` und `prompts/02-test-mvp.md` sind die im Development-Dokument
festgelegten, wortgleich übernommenen Aufträge für Codex CLI (`codex exec -`). Sie dienen
als wiederholbare Grundlage, um dieses Repository (weiter) zu bauen bzw. gegen die
MVP-Anforderungen zu prüfen.

## MVP-Testreport

Siehe `docs/architecture/mvp-test-report.md` für den aktuellen Status, offene
Integrationstests und nächste Schritte.

## Prompt-Dokumentation

Siehe `docs/prompts/` für eine Zusammenfassung je ausgeführtem Auftrag (was beauftragt,
was getan, welches Ergebnis) — beginnend mit dem initialen Bootstrap.

## Nicht festgelegt / bewusst offen gelassen

Konkrete Entra Tenant IDs, App Registrations, Graph Permission Sets, Shared-Mailbox-Adresse,
Gruppennamenskonventionen, Review-Intervalle und Lifecycle-Fristen sind **absichtlich nicht
erfunden**. Sie bleiben Tenant-/Umgebungs-Konfiguration (siehe Blueprint Abschnitt 23.2).

---
Version 0.1.0-mvp · basierend auf Blueprint Version 1.0, Stand 28. August 2026
