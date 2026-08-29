# Prompt 14 — Excel-Gäste-Import (Phase 4 des ursprünglichen 5-Phasen-Plans)

- **Datum:** 29. August 2026
- **Auftrag:** "bitte nun den nächsten plan umsetzen" — letzte offene Phase des im
  Plan-Modus aus Prompt 09 erarbeiteten 5-Phasen-Plans: Excel-Gäste-Import. Da sich das
  Szenario-Modell seit dem ursprünglichen Plan grundlegend geändert hatte (freies
  `Fields`-Dictionary statt fixer Felder, siehe Prompts 11-13), wurde vor der Umsetzung
  erneut in den Plan-Modus gewechselt, um das Import-Design an das aktuelle Modell
  anzupassen und mit dem Nutzer zu klären.

## Geklärte Anforderungen

- **Konfigurierbares Spalten-Mapping** statt fixer Spaltennamen: Sheet-Auswahl, Start-Zeile/
  -Spalte, und eine UI, die Excel-Kopfzeilen auf Zielschlüssel abbildet.
- **Zwei-Schritt-Ablauf**: Preview (Dry-Run, keine Schreibzugriffe) vor Commit, damit Fehler
  vor dem echten Import in der Excel-Datei korrigiert werden können.
- **E-Mail als eindeutiger Gast-Schlüssel**. Ändert sich bei bekannter Mail ein anderes
  Feld, wird überschrieben (auditiert), UND für jede bestehende aktive Zuweisung des Gasts
  in einem ANDEREN Workload wird ein Review markiert — innerhalb des gerade importierten
  Workload ist das unkritisch. Alles im Audit-Trail.
- Firma bleibt bewusst getrennt vom freien Fields-Matching.

## Was umgesetzt wurde

### Architektur-Entscheidung: ClosedXML bleibt in Infrastructure gekapselt
Ein erster Entwurf hätte `ClosedXML.Excel` direkt in `GuestImportService.cs` (Application)
referenziert — das hätte gegen die etablierte Konvention verstoßen, dass Application
paketfrei bleibt (Domain/Application referenzieren keine externen NuGet-Pakete, nur
Infrastructure/Api tun das). Gefixt durch eine neue schmale Abstraktion
`ISpreadsheetReader` (`CorePorts.cs`) mit `GetSheetNames`/`ReadHeaderRow`/`ReadDataRows` —
Application kennt nur Rohwert-Zeilen, `ClosedXmlSpreadsheetReader`
(`src/B2B.Portal.Infrastructure/Import/`) ist die einzige Stelle, die ClosedXML kennt.

### Domain
- `ReviewItem` bekommt ein neues optionales `string? Reason` — einzige Schema-Änderung,
  rückwärtskompatibel. Trägt den Erklärungstext für Fremd-Workload-Reviews.

### Application (`src/B2B.Portal.Application/Import/`)
- `GuestImportDtos.cs`: `GuestImportColumnMapping`, `GuestImportRowResult`,
  `GuestImportForeignWorkloadImpact`, `GuestImportResult`. `GuestImportReservedFields`
  definiert die vier reservierten Zielschlüssel (Mail/DisplayName/Workload/Szenario) —
  jeder andere Mapping-Wert wird zum freien `ScenarioResourceRule.Fields`-Schlüssel.
- `GuestImportService.cs`: `Inspect`/`PreviewAsync`/`CommitAsync` teilen sich denselben
  Matching-Code (`ProcessRowAsync`), um Preview/Commit-Drift auszuschließen — "gather facts,
  then evaluate", dasselbe Muster wie `LifecycleService.EvaluateDeletionAsync`. Regel-
  Matching: pro Zeile und Regel prüfen, ob alle im Mapping abgedeckten
  `rule.Fields`-Einträge exakt mit den Zeilenwerten übereinstimmen (Regel-Schlüssel ohne
  Mapping-Entsprechung werden ignoriert — "kann mehrere Ressourcen/Regeln treffen"), plus
  optionale `JsonLogicEvaluator`-Auswertung der Regel-Condition. Ziel-Rolle wird über
  `WorkloadRole.ResourceMappings` aufgelöst. Commit nutzt den bestehenden
  `GrantWorkloadRoleCommandHandler` (Wiederverwendung, bringt dessen Idempotenz gratis mit).

### Ports/Infrastructure
- `IGuestAccountRepository.GetByMailAsync` (case-insensitive) — InMemory + Cosmos
  (Cosmos: `UPPER(c.mail) = UPPER(@mail)`-Query, serverseitig gefiltert).
- `ISpreadsheetReader`/`ClosedXmlSpreadsheetReader` — DI-Registrierung unconditional (nicht
  Data-Provider-abhängig).
- `CosmosReviewRepository`: `ReviewItemDocument.Reason` ergänzt.

### API (`Program.cs`)
Erster echter Datei-Upload-Endpoint im Projekt (`multipart/form-data`, `IFormFile`):
`POST /api/guest-import/inspect` (Sheet-/Spaltennamen ermitteln),
`POST /api/guest-import/preview`, `POST /api/guest-import/commit` (Mapping als JSON-String
im Formularfeld "mapping", da Minimal APIs kein natives multipart-JSON-Binding kennen).

### Web-UI
Neue Seite `GuestImportPage.tsx`: Datei-Upload → Sheet/Startzeile/-spalte → "Datei
einlesen" (inspect) → Spalten-Mapping-Tabelle (Datalist mit den vier reservierten
Schlüsseln, freie Texteingabe für zusätzliche Fields) → "Vorschau" → Ergebnis-Tabelle
(neu/aktualisiert, Zuweisungen, Warnungen, Fremd-Workload-Review-Hinweise) → "Import
ausführen". `client.ts` bekommt einen `requestForm()`-Helper (FormData statt JSON, kein
Content-Type-Header — der Browser setzt die multipart-Boundary selbst). `ReviewsPage.tsx`
zeigt jetzt `reason`, falls vorhanden. Route `/guest-import` + Nav-Eintrag "Gäste-Import".

## Kritischer Bug gefunden und gefixt: Cosmos-Enum-Serialisierung (String vs. Zahl)

Live-Verifikation des Fremd-Workload-Review-Flows zeigte: trotz vorhandener aktiver
Zuweisung in einem anderen Workload blieb `foreignWorkloadImpacts` leer und kein
`ReviewItem` wurde erzeugt. Ursache: `CosmosClientOptions.SerializerOptions`
(`CosmosSerializationOptions`) kennt nur `PropertyNamingPolicy`, keinen Weg, Enums als
String statt als Zahl zu serialisieren — alle Enum-Properties auf Cosmos-Dokumenten
(`AssignmentStatus`, `GovernanceProvider`, etc.) wurden daher als numerischer Index
gespeichert. Repository-Queries wie `CosmosAssignmentRepository.ListActiveByGuestAsync`
filtern aber `c.status IN (@active, @approved, @requested)` mit den **String-Namen** als
Parameter — ein stiller String-vs-Zahl-Mismatch, der diese Methode (und
`ListByWorkloadAsync`-mit-Statusfilter, sofern vorhanden) gegen echtes Cosmos **seit ihrer
Einführung** immer leer zurückliefern ließ, ohne dass es bisher auffiel (InMemory-Tests
nutzen keinen echten Cosmos-Query-Pfad, decken das also nicht ab).

**Fix**: `CosmosClientFactory.cs` — `SerializerOptions` (inkompatibel mit der Alternative)
ersetzt durch `CosmosClientOptions.UseSystemTextJsonSerializerWithOptions` (SDK 3.32+,
bestätigt per Reflection gegen die installierte 3.46.0-DLL), mit `JsonSerializerOptions {
PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new
JsonStringEnumConverter() } }`. Betrifft alle Enum-Properties auf allen Cosmos-Dokumenten
(nicht nur `AssignmentStatus`) — bestehende Testdaten aus früheren Sessions im lokalen
Emulator sind dadurch mit altem (numerischem) Format inkonsistent zum neuen Code, wurde
bewusst nicht migriert (disposable Dev-Daten, per Neuanlage ersetzbar, wie bei ähnlichen
Fällen in dieser Session).

## Live-Verifikation (gegen echten Cosmos-Emulator, nach dem Enum-Fix)

1. `inspect`: echte `.xlsx`-Datei hochgeladen → Sheet-/Spaltennamen korrekt erkannt.
2. `preview`: zwei Zeilen (eine mit Regel-Treffer, eine mit unbekanntem Rollenwert) →
   korrekte Zuweisungs-/Warnungs-Vorschau, **keine** Guest-Accounts angelegt (bestätigt
   Dry-Run-Reinheit).
3. `commit`: beide Gäste angelegt, Zuweisung für die Treffer-Zeile korrekt erzeugt.
4. Zweiter Import derselben Mail mit geändertem DisplayName, nachdem der Gast zusätzlich
   manuell einem zweiten (fremden) Workload zugewiesen wurde → `foreignWorkloadImpacts`
   korrekt befüllt, `GET /api/reviews` zeigt das neue `ReviewItem` mit `Reason` und
   `assignmentId` auf die Fremd-Workload-Zuweisung — **keine** Review-Markierung auf der
   Zuweisung im gerade importierten Workload selbst (bestätigt "innerhalb desselben
   Workloads unkritisch").

## Tests

Neue `tests/B2B.Portal.Integration.Tests/GuestImportServiceTests.cs` (5 Tests, InMemory +
ClosedXML für in-memory generierte Test-`.xlsx`-Dateien): Regel-Treffer über freie Fields,
Preview schreibt nichts, Commit legt Gast+Zuweisung an, kein Regel-Treffer legt Gast trotzdem
mit Warnung an, Commit ist bei zweimaligem Lauf idempotent (keine doppelten Zuweisungen,
dank `GrantWorkloadRoleCommandHandler`), geänderte Daten erzeugen ein `ReviewItem` mit
`Reason` NUR für Fremd-Workload-Zuweisungen.

**Gesamt-Testergebnis: 72/72 grün** (29 Domain + 5 Architecture + 3 Application + 35
Integration, davon 5 neu). `dotnet build` fehlerfrei über alle Schichten inkl. neuem
ClosedXML-Paketverweis in Infrastructure. `npm run build`/`vitest run` im Web-Projekt grün.

## Was bewusst nicht getan wurde

- Keine Migration bestehender (vor dem Enum-Fix geschriebener) Cosmos-Testdaten — lokale
  Dev-Daten, per Neuanlage ersetzbar.
- Keine automatische Entziehung von Fremd-Workload-Zuweisungen bei geänderten Gast-Daten —
  bewusst nur eine Review-Markierung, das Entziehen bleibt Governance-Core/LifecycleService
  vorbehalten (Anhang A Regel 3), exakt wie vom Nutzer vorgegeben.
- Keine Kaskaden-Logik für mehrfach passende Regeln über eine Zeile hinweg — jede passende
  Regel liefert ihre Ziel-Rolle(n) unabhängig, wie in der ursprünglichen Anforderung
  "kann mehrere Ressourcen/Regeln treffen" vorgesehen.
- Keine Migration der reservierten Feld-Namen (Mail/DisplayName/Workload/Szenario) zu
  lokalisierbaren/konfigurierbaren Strings — im MVP-Rahmen fest im Code (`GuestImportReservedFields`).
