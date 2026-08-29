# Prompt 09 — Cosmos-DB-Migration (Phase 1) + Cosmos als LOCAL_MOCK-Default

- **Datum:** 29. August 2026
- **Auftrag:** Zwei zusammenhängende Chat-Aufträge:
  1. Umsetzung eines zuvor im Plan-Modus erarbeiteten, mehrteiligen Plans
     (Cosmos-Migration, Workload-Szenarien, JSONLogic-Bedingungen, Excel-Import) —
     begonnen mit **Phase 1: Cosmos-Migration**.
  2. Direkter Nachfolgeauftrag: "der mok soll auch schon komplett gegen die cosmos db
     gehen auch der load" — LOCAL_MOCK soll standardmäßig (nicht nur optional) gegen
     Cosmos laufen, inklusive Bulk-Seed (`seed-large-workload.ps1`).

## Kontext: der vorausgegangene Plan

Der Nutzer hatte ein Zieldokument zur Datenhaltung bereitgestellt
(`B2B_Guest_Governance_Dev_Datenspeicherung.txt`): Cosmos DB Emulator mit vier logisch
getrennten Containern (`domain`/`discovery`/`jobs`/`audit`) statt der bisherigen zwei
(`domain-data`/`job-queue`), Repository-Abstraktion über `TenantContext`. Im Plan-Modus
wurden zwei Explore-Agenten und ein Plan-Agent eingesetzt, die einen detaillierten
5-Phasen-Plan erarbeiteten (Cosmos-Migration → Workload-Szenarien/JSONLogic →
Szenario-GUI → Excel-Import-Backend → Excel-Import-GUI). Wichtiger Fund dabei:
`TenantContext` existierte bereits als Domain-`ValueObject`, wurde aber nur an der
API-Grenze genutzt — die Migration war daher ein mechanisches, aber großflächiges
Refactoring, kein neuer Typ.

Nutzerentscheidungen für den Gesamtplan: volle Cosmos-Migration (alle Repositories +
Job-Queue, config-schaltbar), strukturierte Szenario-Entities (kein JSON-Blob),
JSONLogic für Bedingungen (selbst implementiert, kein NuGet-Paket), kombinierter
Gäste+Zuordnung-Excel-Import. Umsetzung phasenweise mit Zwischenstopp nach jeder Phase.

## Was in Phase 1 umgesetzt wurde

### Container-/Datenbank-Umbenennung
- `infra/modules/cosmos-free-tier.bicep`: `b2b-portal`/`domain-data`+`job-queue` →
  `b2b-governance-dev`/`domain`+`discovery`+`jobs`+`audit` (je Partition Key
  `/platformTenantId`, `jobs`/`audit` mit explizitem `defaultTtl: -1`).
- `scripts/requirements.ps1` (`-InitCosmosEmulator`-Block): dieselbe Umbenennung,
  Kommentarkopf aktualisiert.

### TenantContext-Migration
Alle 7 Repository-Interfaces (`IGuestAccountRepository` etc.) plus `IAuditWriter.QueryAsync`
nehmen jetzt `TenantContext` statt nacktem `string platformTenantId` entgegen — durchgezogen
durch `InMemoryRepositories.cs`, `LifecycleService`, alle Commands, alle 7
Worker-Handler-Dateien, `Program.cs` (nutzt jetzt direkt `tenantCtx.Current` statt
`.PlatformTenantId`), sowie die bestehenden Tests.

### IJobQueue.RetryAsync — Breaking Change
Signatur geändert von `Task` auf `Task<int>` (liefert den neuen, dauerhaften
Attempt-Zähler zurück). `JobDispatcher` verlor damit seinen nicht-persistenten
In-Prozess-`_attempts`-Dictionary — der Zähler lebt jetzt in der jeweiligen
`IJobQueue`-Implementierung selbst (bei `LocalJobQueue` weiterhin in-memory, bei
`CosmosJobQueue` im Queue-Dokument), was einen Worker-Neustart oder mehrere
Worker-Instanzen übersteht.

### Sechs Cosmos-Repository-Implementierungen
`src/B2B.Portal.Infrastructure/Data/Cosmos/`: `CosmosGuestAccountRepository`,
`CosmosWorkloadRepository`, `CosmosAssignmentRepository`, `CosmosReviewRepository`,
`CosmosJobRepository`, `CosmosResourceAccessRepository`, `CosmosAuditWriter` — einheitliches
Muster: `ReadItemAsync`/`GetItemQueryIterator` mit `entityType`-Discriminator und
`platformTenantId`-Partition-Key, interne `{Entity}Document`-DTOs mit
`FromEntity`/`ToEntity`-Mappern. Plus `CosmosClientFactory` (liest
`COSMOS_EMULATOR_ENDPOINT`/`COSMOS_EMULATOR_KEY`/`COSMOS_DATABASE_ID`, Gateway-Mode mit
Zertifikats-Bypass für den Emulator).

### CosmosJobQueue — Lease-Mechanismus
`src/B2B.Portal.Infrastructure/Queue/CosmosJobQueue.cs`: Cosmos kennt kein natives
FIFO-Dequeue-mit-Lock, daher Status-Feld (`Pending`→`Leased`→`Done`/`DeadLetter`) plus
ETag-conditional Replace mit `LeaseExpiresAt` und Reclaim abgelaufener Leases. Kein Change
Feed (unverhältnismäßig für den Single-Worker-Prozess laut ADR-0001).

### DI-Switch
`InfrastructureServiceCollectionExtensions.cs`: `DATA_PROVIDER`-Variable, `"cosmos"` vs.
`"local"` (InMemory), analog zum bestehenden `DIRECTORY_PROVIDER`/`EMAIL_PROVIDER`-Muster.

### Neue Tests
`CosmosEmulatorAvailability` (TCP-Connect-Check auf Port 8081), `CosmosJobDispatcherTests`
(Routing, Retry-vor-DeadLetter, **neuer** Test für ETag-Concurrency-Verhalten, das
InMemory dank atomarem `ConcurrentQueue.TryDequeue` nicht braucht), `CosmosTenantIsolationTests`
— alle überspringen sich selbst (frühes `return`), wenn kein Emulator läuft, damit
`dotnet test` CI-sicher bleibt.

## Zwei echte Bugs gefunden und behoben (während Phase 1)

1. **`LocalJobQueue.DeadLetterAsync` fand Jobs nicht mehr** — nachdem die neue
   `JobDispatcher`-Logik zuerst `RetryAsync` aufrief (um den Zähler zu bekommen) und dann
   bei Erreichen von `MaxRetries` zusätzlich `DeadLetterAsync`, hatte `RetryAsync` den Job
   bereits aus `_inFlight` zurück nach `_pending` verschoben — `DeadLetterAsync` suchte
   aber nur in `_inFlight`. Behoben: `DeadLetterAsync` sucht jetzt in beiden möglichen
   Fundorten.
2. **Cosmos SDK braucht `Newtonsoft.Json` zur Laufzeit** trotz konfiguriertem
   `System.Text.Json`-Serializer (nicht nur ein Build-Time-Check) — `JobEnvelope.Payload`
   als `JsonElement` kam nach einem Cosmos-Roundtrip korrupt zurück
   (`GetProperty()` warf `InvalidOperationException`). Behoben: `Payload` wird in
   `CosmosJobQueue`'s Dokument-DTO als Raw-JSON-`string` gespeichert
   (`GetRawText()`/`JsonDocument.Parse()`), `Newtonsoft.Json` explizit referenziert statt
   nur den Build-Check zu umgehen.

## Live-Verifikation Phase 1

API+Worker mit `DATA_PROVIDER=cosmos` gestartet: Guest-Invite → Job-Enqueue → Worker-Dequeue
(mit Lease) → Verarbeitung → Read-Roundtrip funktionierte vollständig. Tenant-Isolation,
Deletion-Gate, Audit-Events real gegen Cosmos bestätigt. Regressionscheck: InMemory-Default
(ohne `DATA_PROVIDER`) funktionierte zu diesem Zeitpunkt noch unverändert (später durch den
zweiten Auftragsteil bewusst auf Cosmos als Default umgestellt). **37/37 Tests grün**
(31 bisherige + 6 neue Cosmos-Tests, alle real gegen den laufenden Emulator ausgeführt).

## Was im zweiten Auftragsteil umgesetzt wurde: Cosmos als LOCAL_MOCK-Default

Nach Rückfrage bestätigt: `DATA_PROVIDER` soll unter `B2B_MODE=LOCAL_MOCK` implizit
`cosmos` sein (nicht mehr `local`), und `scripts/seed-large-workload.ps1` (der "Load")
soll automatisch mitziehen.

- **`InfrastructureServiceCollectionExtensions.cs`**: `dataProviderDefault` hängt jetzt von
  `mode` ab — `LOCAL_MOCK` → `"cosmos"`, sonst `"local"`. `DATA_PROVIDER` explizit gesetzt
  überschreibt weiterhin (Opt-out auf InMemory bleibt möglich).
- **Neues Problem dabei gefunden:** Weder `B2B.Portal.Api` noch `B2B.Portal.Worker` luden
  `.env.local` automatisch — nur `AddEnvironmentVariables()`. Die Cosmos-Konfiguration kam
  bisher nur über den VS-Code-Debugger (`launch.json` `envFile`) oder manuelles Exportieren
  an. Da Cosmos jetzt Default ist, muss auch ein einfacher `dotnet run` ohne manuelles
  Setup funktionieren.
- **Neue Datei `src/B2B.Portal.Infrastructure/DotEnvLoader.cs`**: liest `.env.local` (Suche
  bis zu 5 Verzeichnisebenen nach oben ab dem aktuellen Arbeitsverzeichnis, deckt sowohl
  Aufruf aus dem Repo-Root als auch aus dem Projektordner ab) und setzt fehlende
  Prozess-Umgebungsvariablen — bereits gesetzte Werte werden nicht überschrieben. In beiden
  `Program.cs` vor `AddEnvironmentVariables()` eingebunden.
- **`.env.example`**: `DATA_PROVIDER=cosmos` als neuer Beispielwert mit Erklärung.
- **README.md**: neuer Abschnitt "Datenhaltung in LOCAL_MOCK: Cosmos DB Emulator als
  Default".

### Ein weiterer echter Bug beim Testen gefunden

Nach der Default-Umstellung verarbeitete der Worker einen frisch erzeugten Job zunächst
nicht (kein Fehler, aber auch keine Verarbeitung sichtbar) — Ursache: die lokale
`.env.local` enthielt noch `DATA_PROVIDER=local` aus einer älteren Kopie von
`.env.example` (vor der heutigen Default-Umstellung erzeugt), was den neuen impliziten
Default explizit überschrieb. Kein Code-Bug, sondern veraltete lokale Konfiguration —
behoben durch Aktualisierung von `.env.local` und `.env.example`.

## Live-Verifikation: Cosmos als Default

- API+Worker mit **nur** `B2B_MODE=LOCAL_MOCK` gestartet (kein `DATA_PROVIDER`, keine
  manuell exportierten Cosmos-Env-Vars) → Guest-Invite funktionierte sofort, Worker
  verarbeitete den Job automatisch gegen Cosmos, Read-Roundtrip bestätigt.
- **`scripts/seed-large-workload.ps1 -GuestCount 500`** gegen den jetzt automatisch
  Cosmos-gestützten Stack ausgeführt: **500 Gäste + 1 Workload in 44 Sekunden** erfolgreich
  angelegt (zum Vergleich: 80ms bei InMemory — erwartbarer Unterschied durch 500
  sequenzielle Netzwerk-Roundtrips zum Emulator statt In-Memory-Dictionary-Writes).
  Ergebnis über die API verifiziert: exakt 500 Gäste, 1 Workload korrekt gespeichert.
- Volle Test-Suite erneut ausgeführt: **37/37 Tests weiterhin grün.**

## Was bewusst nicht getan wurde

- Keine Performance-Optimierung des Bulk-Seeds (z. B. Cosmos Bulk-Executor/Parallelisierung)
  — 44 Sekunden für 500 Gäste war nicht Teil des Auftrags und die sequenzielle
  Implementierung folgt bewusst demselben Code-Pfad wie einzelne Requests (Wiederverwendung
  von `ProvisioningService`/`AuditService`, keine Sonderlogik für Bulk).
- Kein automatischer Emulator-Start beim `dotnet run` selbst — bleibt weiterhin explizit
  über `scripts/requirements.ps1 -InitCosmosEmulator` zu starten; ohne laufenden Emulator
  scheitert der Start jetzt mit einer klaren Exception (`CosmosClientFactory`) statt eines
  stillen Fallbacks.
- Phasen 2–5 des Gesamtplans (Workload-Szenarien, JSONLogic, Excel-Import) sind noch nicht
  begonnen — der Nutzer hatte Zwischenstopps nach jeder Phase gewünscht.

## Ergebnis

`LOCAL_MOCK` läuft jetzt vollständig gegen den lokalen Cosmos DB Emulator — sowohl einzelne
Requests als auch Bulk-Seeds — ohne manuelles Setup über einen einfachen `dotnet run`
hinaus (Emulator muss laufen). InMemory bleibt als expliziter, funktionierender Opt-out
verfügbar. Alle 37 Tests grün, Build fehlerfrei, Architecture-Isolation gewahrt.
