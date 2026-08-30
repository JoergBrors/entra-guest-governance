# B2B Guest Governance Portal

Mandantenfähige, workloadorientierte Verwaltung externer Identitäten (Microsoft Entra B2B
Guests): zentraler Gast-Pool, fachliche Workload-Zuweisungen über konfigurierbare Szenarien,
Excel-Massenimport, interne Access Reviews und ein vollständiger Audit-Trail.

Dieses Repository implementiert die im Blueprint definierte Architektur als lauffähigen,
lokal betreibbaren MVP:

- `B2B_Guest_Governance_Portal_Blueprint.docx` — Architektur- und Konzept-Blueprint
- `B2B_Guest_Governance_Development_und_MVP.docx` — Development- und MVP-Implementierungsleitfaden

## Inhalt

- [Was das Portal kann](#was-das-portal-kann)
- [Architektur im Überblick](#architektur-im-überblick)
- [Voraussetzungen](#voraussetzungen)
- [Installation & erster Start](#installation--erster-start)
- [Drei Development-Modi](#drei-development-modi)
- [LOCAL_MOCK per Docker Compose](#local_mock-per-docker-compose)
- [Verwendete Fremdsoftware](#verwendete-fremdsoftware)
- [Weiterführende Dokumentation](#weiterführende-dokumentation)
- [Sicherheitshinweise](#sicherheitshinweise)

## Was das Portal kann

### Guest Pool (zentrale Gastidentitäten)

Gäste gehören keinem einzelnen Workload — sie werden zentral verwaltet (`GuestAccount`,
Lifecycle-Status Discovered → Invited → Active → … → Deleted) und über Zuweisungen mit
Workloads verknüpft. Ein Gast kann gleichzeitig mehreren Workloads zugeordnet sein. Die
Guest-Pool-Ansicht zeigt pro Gast alle Workload-Zuweisungen inkl. Status und erlaubt das
gezielte Entziehen einzelner Zuweisungen ("Unassign"), ohne die Gastidentität selbst
anzutasten.

### Workloads, Rollen und Ressourcen

Ein *Workload* bündelt fachliche *Rollen* (`WorkloadRole`), die wiederum technische
*Ressourcen* (`WorkloadResource`, z. B. Security Groups) referenzieren. Workloads sind über
die Admin-Oberfläche vollständig verwaltbar: Anlegen, Bearbeiten (Name/Owner), Rollen/
Ressourcen hinzufügen/entfernen (mit Konsistenzprüfung — eine Rolle mit aktiven Zuweisungen
kann nicht gelöscht werden), Deaktivieren/Reaktivieren (Soft-Delete) und endgültiges Löschen
(nur möglich, wenn keine aktiven Zuweisungen mehr existieren; historische Zuweisungen und
zugehörige Szenarien werden dabei mitentfernt). Jede Workload-Karte zeigt die aktuelle Anzahl
aktiver/inaktiver Nutzer.

### Szenarien mit freiem Regel-Matching (JSONLogic)

Ein *Szenario* (`WorkloadScenario`) besteht aus beliebig vielen *Ressourcen-Regeln*
(`ScenarioResourceRule`). Jede Regel bindet eine Ressource an ein frei definierbares
Schlüssel-Wert-Set (z. B. `{"Firma": "Fabrikam", "Rolle": "Disponent"}`) und optional eine
Bedingung im [JSONLogic](https://jsonlogic.com/)-Format (selbst implementiert, siehe unten).
Szenarien werden per JSON-Template importiert/exportiert — referenzierte Ressourcen werden
beim Import automatisch angelegt, falls sie noch nicht existieren. Der Szenario-Viewer zeigt
alle Regeln, ihre freien Felder und Bedingungen; ein Deploy-Button löst die Provisionierung
der Zielressourcen über den Worker aus.

### Excel-Gäste-Import mit konfigurierbarem Spalten-Mapping

Eine Excel-Datei (`.xlsx`) kann hochgeladen werden, um Gäste in großer Zahl anzulegen/zu
aktualisieren und automatisch den passenden Workload-Rollen zuzuweisen:

1. **Inspect** — Sheet wählen, Kopfzeile/Startspalte festlegen, gefundene Spaltenköpfe
   anzeigen.
2. **Mapping** — jede Spalte auf einen Zielschlüssel abbilden: die vier reservierten Felder
   `Mail`, `DisplayName`, `Workload`, `Szenario`, oder einen beliebigen freien fachlichen
   Schlüssel (z. B. `Rolle`), der gegen die `Fields` der Szenario-Regeln gematcht wird.
3. **Preview** — echter Dry-Run: zeigt pro Zeile, welcher Gast neu/aktualisiert würde,
   welche Regeln (und damit Rollen) treffen, und Warnungen (kein Regel-Treffer, unbekannter
   Workload/Szenario) — ohne etwas zu schreiben.
4. **Commit** — führt denselben Matching-Code erneut aus, diesmal mit Schreibzugriff.
   E-Mail ist der eindeutige Gast-Schlüssel; ändern sich bei einer bereits bekannten Mail
   andere Felder, wird der Datensatz überschrieben (auditiert), und für jede bestehende
   aktive Zuweisung des Gasts in einem **anderen** Workload wird ein Review-Eintrag mit
   Begründung angelegt — der jeweilige Workload-Owner soll die weiterhin gültige Zuweisung
   manuell bestätigen, statt dass automatisch Zugriff entzogen wird.

### Interne Access Reviews

Review-Instanzen (`ReviewInstance`/`ReviewItem`) fassen zu prüfende Zuweisungen zusammen —
entstehen sowohl über den turnusmäßigen Review-Flow als auch automatisch aus dem
Excel-Import (siehe oben), jeweils mit optionaler Begründung (`Reason`).

### Governance-Kernregeln

- **Desired State ≠ Actual State** — Reconciliation und Live Validation sind explizite
  Prozesse.
- **Governance vor Löschung** — nur der Governance Core / das Lifecycle-Modul darf eine
  Gastidentität löschen. Workloads/Connectoren entziehen ausschließlich eigenen Zugriff.
- **Tenant-Isolation by Design** — jede Entität, jeder Job, jede Query trägt Tenant-Kontext
  (`TenantContext`, Partition Key `platformTenantId`).
- **Audit-first** — jede sicherheitsrelevante Aktion erzeugt ein `AuditEvent` mit
  `CorrelationId` (Container `audit`).
- **Mock-first** — `LOCAL_MOCK` ist der Default-Modus. Keine echten Graph-Schreibzugriffe
  oder E-Mails ohne explizite Integrationskonfiguration.

## Architektur im Überblick

Schichtung Domain → Application → Infrastructure → Worker/Api → Web, durchgesetzt per
Architektur-Test (`NetArchTest`, siehe `tests/B2B.Portal.Architecture.Tests`): Domain und
Application referenzieren keine externen Pakete außer der .NET-BCL — technische Adapter
(Cosmos, ClosedXML, Graph) leben ausschließlich in Infrastructure.

```text
src/B2B.Portal.Web/            React + TypeScript + Vite, Fluent UI
src/B2B.Portal.Api/            ASP.NET Core 10 Minimal API — Commands/Queries, Tenant Context
src/B2B.Portal.Application/    Use Cases, Ports (Interfaces), Commands, Services
src/B2B.Portal.Domain/         reine Fachlogik, keine Azure-/Graph-/NuGet-Referenzen
src/B2B.Portal.Infrastructure/ Cosmos-, Excel-, Mail-, Directory-Adapter (Mock + Graph-Schale)
src/B2B.Portal.Worker/         .NET 10 Worker Host mit Job-Dispatcher und Handlergruppen
tests/                         Domain / Application / Architecture / Integration Tests
infra/                         Bicep (main.bicep + Module + dev/poc Parameterdateien)
docs/                          Architektur-Doku, Prompt-Protokolle, ADRs (siehe unten)
```

Datenhaltung: Cosmos DB (lokal über den Emulator), vier Container
(`domain`/`discovery`/`jobs`/`audit`, Partition Key `/platformTenantId`, siehe
`infra/modules/cosmos-free-tier.bicep`). Job-Verarbeitung: eigener Worker-Prozess mit
Polling-Dispatcher, Lease-basiertem Claim und Retry/Dead-Letter.

## Voraussetzungen

| Werkzeug | Zweck | Automatisch prüfbar/installierbar via `requirements.ps1` |
| --- | --- | --- |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Backend (Api/Worker/Domain/…) | ✅ |
| [Node.js](https://nodejs.org/) + npm | Frontend (Vite/React) | ✅ |
| [Bicep CLI](https://learn.microsoft.com/azure/azure-resource-manager/bicep/install) | Infrastructure-as-Code (optional für lokale Entwicklung) | ✅ |
| [Azure Cosmos DB Emulator](https://learn.microsoft.com/azure/cosmos-db/emulator) (Windows) | lokale Datenhaltung unter `LOCAL_MOCK` | ✅ |
| [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) (npm-Paket) | lokaler Azure-Storage-Emulator | ✅ |
| [Azure CLI](https://learn.microsoft.com/cli/azure/) | optional, für spätere Azure-Deployments | ✅ |
| [Microsoft.Graph PowerShell SDK](https://learn.microsoft.com/powershell/microsoftgraph) | nur für `DEV_INTEGRATION` (Entra-App-Registration automatisiert anlegen) | manuell, siehe unten |
| [Docker](https://docs.docker.com/get-docker/) + Compose | optional: kompletten `LOCAL_MOCK`-Stack containerisiert starten (siehe unten) | manuell, siehe unten |

Alle Emulatoren/Tools laufen rein lokal — ein frisches Checkout benötigt **keine** Azure-
Subscription und **keine** echten Entra-Tenant-Zugänge.

## Installation & erster Start

### 0) Voraussetzungen prüfen und einrichten

```powershell
# Nur prüfen (Runtimes/Tools, freie Ports) — ändert nur .env.local/vite.config.ts, keine Cloud-Ressourcen
./scripts/requirements.ps1

# Fehlende Tools nachinstallieren + Cosmos DB Emulator + Azurite (Storage Emulator) lokal initialisieren
./scripts/requirements.ps1 -Install -InitCosmosEmulator -InstallCosmosEmulator -InitStorageEmulator -InstallStorageEmulator
```

Das Skript prüft .NET SDK, Node.js/npm, Bicep CLI, Azure CLI, Microsoft.Graph PowerShell
SDK, den lokalen Cosmos DB Emulator und Azurite; ermittelt freie Ports für API/Web (weicht
bei Belegung automatisch aus) und schreibt sie nach `.env.local` bzw. `vite.config.ts`. Mit
`-InitCosmosEmulator`/`-InitStorageEmulator` werden die jeweiligen Connection Strings
(öffentlich dokumentierte Well-Known-Emulator-Keys, keine echten Secrets) ebenfalls nach
`.env.local` geschrieben. Details siehe Kommentarkopf von `scripts/requirements.ps1`.

Der ermittelte API-Port wird zusätzlich als `ASPNETCORE_URLS` nach `.env.local`
geschrieben — `.vscode/launch.json` ("Portal API") lädt diese Datei über `envFile` und
startet damit automatisch auf demselben Port wie `dotnet run`/das Skript.

**Bekannter Fallstrick:** Läuft bereits ein eigener `npm run dev`, bindet Vite sich beim
nächsten Start wieder an den zuletzt in `vite.config.ts` eingetragenen Port — ein
paralleler zweiter Start auf demselben Port schlägt dann fehl. Das Skript zeigt an, welcher
Prozess (PID, Kommandozeile) einen Port blockiert.

### 1) Backend bauen und testen

```bash
dotnet restore
dotnet build -c Debug
dotnet test -c Debug
```

### 2) Frontend bauen und testen

```bash
cd src/B2B.Portal.Web
npm ci
npm run build
npm run test -- --run
cd ../..
```

### 3) LOCAL_MOCK starten

Drei Prozesse (separate Terminals oder VS-Code-Compound-Launch):

```bash
dotnet run --project src/B2B.Portal.Api
dotnet run --project src/B2B.Portal.Worker
npm run dev --prefix src/B2B.Portal.Web
```

Voraussetzung: der Cosmos DB Emulator muss laufen (Schritt 0). Ohne laufenden Emulator
schlägt der Start fehl — alternativ `DATA_PROVIDER=local` in `.env.local` setzen, um
stattdessen ohne Emulator gegen InMemory-Repositories zu arbeiten (z. B. für schnelle,
isolierte Tests).

| Komponente | URL |
| --- | --- |
| Web UI | <http://localhost:5301> |
| Portal API | <http://localhost:5000> |
| Health-Check | <http://localhost:5000/health> |
| Worker | Hintergrundprozess / Konsolenlog |

### 4) Aussagekräftige Mockdaten laden (optional)

Für Demos/UI-Tests mit realistischer Datenmenge — ein Workload mit mehreren Rollen und
konfigurierbar vielen Gästen über mehrere Beispielfirmen, Lifecycle-Status und Rollen
verteilt:

```powershell
./scripts/seed-large-workload.ps1
# oder mit eigener Anzahl/Name:
./scripts/seed-large-workload.ps1 -GuestCount 1500 -WorkloadName "Onboarding-Projekt Nord"
```

Ruft `POST /api/dev/seed/large-workload` auf — ein Endpoint, der **nur unter
`B2B_MODE=LOCAL_MOCK`** registriert ist (siehe `src/B2B.Portal.Api/Program.cs`) und über
dieselben Repository-Pfade wie reguläre Requests schreibt (Cosmos Emulator oder InMemory,
je nach `DATA_PROVIDER`). Ergebnis danach sichtbar in der Web-UI (Guest Pool,
Workloads-Admin-Ansicht) oder direkt über `/api/guest-accounts` bzw. `/api/workloads`.

## Drei Development-Modi

| Modus | Zweck |
| --- | --- |
| `LOCAL_MOCK` | Default. UI + API + Worker lokal, Mock Directory/Mail. Datenhaltung läuft standardmäßig gegen den lokalen Cosmos DB Emulator — keine externen Schreibzugriffe zu echten Entra-/Mail-Systemen. |
| `DEV_INTEGRATION` | Gezielte Integrationstests gegen einen dedizierten Entra Dev-Tenant + Shared Mailbox. |
| `AZURE_DEV` | End-to-End-Abnahme in Azure Dev/PoC. |

Konfiguration erfolgt über `.env.local` (Vorlage: `.env.example`). Es werden **keine**
realen Tenant-IDs, Secrets, Group-IDs oder Mailboxen im Repository hinterlegt.

### Datenhaltung in LOCAL_MOCK: Cosmos DB Emulator als Default

`DATA_PROVIDER` steuert die Repository-Implementierung und ist unter `LOCAL_MOCK`
standardmäßig **`cosmos`** — API und Worker schreiben/lesen bereits lokal gegen den Cosmos
DB Emulator (Datenbank `b2b-governance-dev`, Container `domain`/`discovery`/`jobs`/`audit`,
siehe `infra/modules/cosmos-free-tier.bicep`), nicht nur gegen InMemory. Das gilt auch für
Bulk-Läufe wie `scripts/seed-large-workload.ps1` und den Excel-Gäste-Import.

`API_BASE_URL`/`COSMOS_EMULATOR_ENDPOINT`/`COSMOS_EMULATOR_KEY`/`COSMOS_DATABASE_ID` werden
von `scripts/requirements.ps1 -InitCosmosEmulator` nach `.env.local` geschrieben und von
`B2B.Portal.Api`/`B2B.Portal.Worker` automatisch geladen (auch bei einfachem `dotnet run`,
nicht nur über den VS-Code-Debugger — siehe `DotEnvLoader` in
`src/B2B.Portal.Infrastructure`).

### DEV_INTEGRATION: Entra-ID-Voraussetzungen automatisiert herstellen

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

## LOCAL_MOCK per Docker Compose

Alternative zum manuellen Start (Schritt 0/1/2/3 oben): `docker-compose.yml` startet den
kompletten `LOCAL_MOCK`-Stack containerisiert — Cosmos DB Emulator, API, Worker und Web,
ohne lokal installiertes .NET SDK/Node.js.

```bash
docker compose up --build
```

Dienste:

| Service | Beschreibung |
| --- | --- |
| `cosmos` | `azure-cosmos-emulator` (linux/amd64), Ports `8081` + `10250-10255`, persistentes Volume `cosmos-data` |
| `cosmos-init` | Einmaliger Init-Container (`docker/cosmos-init.ps1`), legt Datenbank `b2b-governance-dev` und Container `domain`/`discovery`/`jobs`/`audit` an, wartet auf gesunden `cosmos`-Healthcheck |
| `api` | Baut `src/B2B.Portal.Api/Dockerfile`, Port `5000:8080`, Healthcheck auf `/health` |
| `worker` | Baut `src/B2B.Portal.Worker/Dockerfile`, kein exponierter Port, wartet auf `cosmos-init` |
| `web` | Baut `src/B2B.Portal.Web/Dockerfile` (Vite-Build → nginx), Port `5301:80`, wartet auf gesunde `api` |

Gemeinsame Umgebung (`x-portal-env`): `B2B_MODE=LOCAL_MOCK`, `DIRECTORY_PROVIDER=mock`,
`EMAIL_PROVIDER=mock`, `DATA_PROVIDER=cosmos`, `JOB_QUEUE_PROVIDER=cosmos`,
`ALLOW_GRAPH_WRITES=false`, `ALLOW_GUEST_DELETE=false`, `COSMOS_DATABASE_ID=b2b-governance-dev`,
`VITE_DEV_PLATFORM_TENANT_ID=dev-tenant-a` — identische Sicherheitsdefaults wie beim
manuellen `LOCAL_MOCK`-Start (keine echten Graph-/Mail-Schreibzugriffe).

Optionales Seeding über das Compose-Profil `seed` (curl-Container, ruft
`POST /api/dev/seed/large-workload` mit `X-Platform-Tenant-Id: dev-tenant-a` auf):

```bash
docker compose --profile seed up seed
```

Lokalen Cosmos-Emulator-Datenbestand (außerhalb von Docker) zurücksetzen:

```powershell
./scripts/reset-cosmos-dev-data.ps1
```

Web UI danach unter <http://localhost:5301>, API unter <http://localhost:5000>. Die
Docker-Variante ersetzt Schritt 0–3 vollständig, ist aber unabhängig vom lokal per
`requirements.ps1` eingerichteten Cosmos DB Emulator/Azurite.

## Verwendete Fremdsoftware

Alle Abhängigkeiten sind Open-Source und werden unverändert über die jeweiligen offiziellen
Paket-Registries bezogen (NuGet/npm) — kein Code aus Drittquellen ist in dieses Repository
kopiert.

### Backend (.NET, NuGet)

| Paket | Verwendung | Lizenz |
| --- | --- | --- |
| [Microsoft.Azure.Cosmos](https://www.nuget.org/packages/Microsoft.Azure.Cosmos) | Cosmos-DB-Client (Repositories, Job-Queue) | [MIT](https://github.com/Azure/azure-cosmos-dotnet-v3/blob/master/LICENSE) |
| [ClosedXML](https://www.nuget.org/packages/ClosedXML) | Excel-Gäste-Import (`.xlsx` lesen/schreiben) | [MIT](https://github.com/ClosedXML/ClosedXML/blob/develop/LICENSE) |
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) | intern vom Cosmos SDK benötigt | [MIT](https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md) |
| [NetArchTest.Rules](https://www.nuget.org/packages/NetArchTest.Rules) | Architektur-Tests (Schichtungsregeln erzwingen) | [MIT](https://github.com/BenMorris/NetArchTest/blob/master/LICENSE) |
| [xUnit](https://www.nuget.org/packages/xunit) + [xunit.runner.visualstudio](https://www.nuget.org/packages/xunit.runner.visualstudio) | Test-Framework | [Apache-2.0](https://github.com/xunit/xunit/blob/main/LICENSE) |
| [Microsoft.AspNetCore.Mvc.Testing](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing) | API-Integrationstests (`WebApplicationFactory`) | [MIT](https://github.com/dotnet/aspnetcore/blob/main/LICENSE.txt) |
| Microsoft.Extensions.\* (Configuration/DependencyInjection/Hosting/Logging Abstractions) | .NET-Hosting-Bausteine | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |

JSONLogic-Auswertung für Szenario-Bedingungen (`JsonLogicEvaluator`) ist bewusst **selbst
implementiert statt eines NuGet-Pakets** — Domain referenziert keine externen Pakete (siehe
Architektur-Tests), und das [JSONLogic](https://jsonlogic.com/)-Format selbst ist eine
offene Spezifikation ohne Lizenzbindung an eine bestimmte Implementierung.

### Frontend (React, npm)

| Paket | Verwendung | Lizenz |
| --- | --- | --- |
| [React](https://www.npmjs.com/package/react) / [react-dom](https://www.npmjs.com/package/react-dom) | UI-Framework | [MIT](https://github.com/facebook/react/blob/main/LICENSE) |
| [react-router-dom](https://www.npmjs.com/package/react-router-dom) | Client-seitiges Routing | [MIT](https://github.com/remix-run/react-router/blob/main/LICENSE.md) |
| [@fluentui/react-components](https://www.npmjs.com/package/@fluentui/react-components) + [@fluentui/react-icons](https://www.npmjs.com/package/@fluentui/react-icons) | UI-Komponenten (Microsoft Fluent UI) | [MIT](https://github.com/microsoft/fluentui/blob/master/LICENSE) |
| [Vite](https://www.npmjs.com/package/vite) | Build-Tool/Dev-Server | [MIT](https://github.com/vitejs/vite/blob/main/LICENSE) |
| [Vitest](https://www.npmjs.com/package/vitest) + [@testing-library/react](https://www.npmjs.com/package/@testing-library/react) + [@testing-library/jest-dom](https://www.npmjs.com/package/@testing-library/jest-dom) | Test-Framework | [MIT](https://github.com/vitest-dev/vitest/blob/main/LICENSE) |
| [TypeScript](https://www.npmjs.com/package/typescript) | Sprachbasis | [Apache-2.0](https://github.com/microsoft/TypeScript/blob/main/LICENSE.txt) |
| [ESLint](https://www.npmjs.com/package/eslint) + typescript-eslint | Linting | [MIT](https://github.com/eslint/eslint/blob/main/LICENSE) |

Vollständige, versionsgenaue Liste inkl. transitiver Abhängigkeiten:
`src/B2B.Portal.Web/package-lock.json`.

### Externe Tools (lokale Entwicklung, keine Code-Abhängigkeit)

| Tool | Zweck | Lizenz/Quelle |
| --- | --- | --- |
| [Azure Cosmos DB Emulator](https://learn.microsoft.com/azure/cosmos-db/emulator) | lokale Cosmos-DB-kompatible Datenhaltung | Microsoft, kostenlos für Entwicklungszwecke |
| [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) | lokaler Azure-Storage-Emulator | [MIT](https://github.com/Azure/Azurite/blob/main/LICENSE) |
| [Microsoft.Graph PowerShell SDK](https://learn.microsoft.com/powershell/microsoftgraph) | Entra-App-Registration automatisiert anlegen (nur `DEV_INTEGRATION`) | [MIT](https://github.com/microsoftgraph/msgraph-sdk-powershell/blob/dev/LICENSE) |
| [Bicep CLI](https://learn.microsoft.com/azure/azure-resource-manager/bicep/overview) | Infrastructure-as-Code für spätere Azure-Deployments | [MIT](https://github.com/Azure/bicep/blob/main/LICENSE) |

## Weiterführende Dokumentation

Alle Dokumentation außer dieser README liegt in [`docs/`](docs/):

| Ordner | Inhalt |
| --- | --- |
| [`docs/architecture/`](docs/architecture/) | Implementierungsplan, aktueller MVP-Testreport |
| [`docs/adr/`](docs/adr/) | Architecture Decision Records |
| [`docs/prompts/`](docs/prompts/) | Zusammenfassung je ausgeführtem Auftrag (was beauftragt, was getan, Ergebnis) — [Index](docs/prompts/README.md) |
| [`docs/prompts-original/`](docs/prompts-original/) | Die ursprünglichen, wortgleich übernommenen Codex-Bootstrap-/Verifikations-Prompts |

## Sicherheitshinweise

> Ein frisches Checkout darf nach Restore/Install/Start **keine** externen Directory- oder
> Mail-Schreiboperationen ausführen. Erst eine explizite `DEV_INTEGRATION`-Konfiguration
> (separate App Registration, dedizierter Dev-Tenant, dedizierte Shared Mailbox) schaltet
> reale Adapter frei. Secrets werden nie in `.env`-Dateien committed — nutze User Secrets /
> Key Vault / Managed Identity.

Konkrete Entra-Tenant-IDs, App Registrations, Graph-Permission-Sets,
Shared-Mailbox-Adresse, Gruppennamenskonventionen, Review-Intervalle und Lifecycle-Fristen
sind **absichtlich nicht erfunden**. Sie bleiben Tenant-/Umgebungs-Konfiguration.

---
Version 0.2.0 · basierend auf Blueprint Version 1.0
