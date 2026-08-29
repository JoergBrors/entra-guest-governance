# Prompt 11 — Szenario-Modell redesignt: freies Template mit Ressourcen-Regeln

- **Datum:** 29. August 2026
- **Auftrag:** Ablehnung des in Prompt 10 gebauten starren Szenario-Modells ("hmh das ist
  nich ganz was ich möchte, ich möchte am liebsten ein komplettes template hochladen") —
  Redesign auf ein frei beschriebenes JSON-Template, das Ressourcen per Name+Art selbst
  definiert und automatisch anlegt, mit Bedingungen **pro Ressourcen-Zuordnung** statt pro
  Szenario, und einem freien Key-Value-Set je Regel als Grundlage für den späteren
  Excel-Import. Details der iterativen Anforderungsklärung siehe Plan-Datei
  `bittepr-fe-diese-beschreibung-und-recursive-fairy.md`.

## Was sich geändert hat (gegenüber Prompt 10)

### Domain-Modell
`src/B2B.Portal.Domain/Entities/WorkloadScenario.cs` komplett umgebaut: `ExternalOrganizationId`,
`Environment` (`ScenarioEnvironment`-Enum, ersatzlos entfernt aus `StatusEnums.cs`),
`ResourceIds` und `Conditions` (`ScenarioCondition`) sind weg. Stattdessen eine einzige
`List<ScenarioResourceRule> Rules` — jede Regel bindet **eine** `WorkloadResource` an ein
freies `Dictionary<string,string> Fields` (z.B. `{"Firma":"Fabrikam","Rolle":"Disponent"}`)
und eine optionale eigene `JsonElement? Condition`. Eine Bedingung gilt jetzt nur noch für
ihre eigene Regel, nicht mehr fürs ganze Szenario.

### JsonLogicEvaluator
`ScenarioEvaluationContext` verliert `ExternalOrganizationId`/`Environment` als feste
Properties, bekommt stattdessen `IReadOnlyDictionary<string,string> Fields`. `{"var":"Environment"}`
wird zu `{"var":"Fields.Environment"}` — derselbe `Fields.`-Präfix-Mechanismus wie der
bereits bestehende `AdditionalFacts.`-Fallback. Operator-Satz unverändert.

### Template-Import (ScenarioImportExportService)
Neues Format: `{workloadName, scenarioName, rules: [{resourceName, resourceType, fields,
condition}]}`. Referenzierte Ressourcen werden gegen `workload.Resources` gematcht
(ResourceType+ExternalId, case-insensitive) — bei keinem Treffer wird die
`WorkloadResource` **automatisch neu angelegt** (`Managed=true`), nicht mehr nur
referenziert. Export ist symmetrisch. Fehler pro Regel werden gesammelt statt beim ersten
Fehler abzubrechen (unverändert aus Prompt 10 übernommen).

### Worker-Handler
`DeployScenarioHandler` wertet die Bedingung jetzt **pro Regel** aus (Kontext aus
`rule.Fields` gebaut), nicht mehr einmal für das ganze Szenario — nur Regeln mit erfüllter
(oder fehlender) Bedingung deployen ihre Ressource.

### API/Web
`POST/PUT /api/workloads/{id}/scenarios` (manuelles Anlage-Formular) entfällt ersatzlos —
neue Szenarien entstehen nur noch über `POST /api/scenarios/import`. Export wandert von
`GET /api/workloads/{id}/scenarios/export` zu `GET /api/scenarios/{id}/export`, weil ein
Template jetzt scenario-scoped statt workload-scoped ist (ein Workload kann mehrere
Szenarien mit unterschiedlichen Regelsätzen haben). `ScenariosPage.tsx` wird vom
Anlage-Formular zum reinen Viewer + Template-Upload-Panel: pro Szenario eine Regel-Tabelle
(Ressource, freie Fields als Badges, Bedingung als Tooltip), Export-Button pro Szenario.

## Kritischer Bug gefunden und gefixt: Fields-Casing im Cosmos-Client

Live-Verifikation gegen den Cosmos-Emulator zeigte: ein Template mit
`{"Firma":"Fabrikam","Rolle":"Disponent"}` wurde als `{"firma":"Fabrikam","rolle":"Disponent"}`
zurückgelesen — die Bedingung `{"var":"Fields.Rolle"}` fand dadurch nie einen Treffer
(case-sensitive Lookup), beide Regeln wurden beim Deploy übersprungen.

**Ursache**: `CosmosClientFactory` konfiguriert den Client global auf
`CosmosPropertyNamingPolicy.CamelCase` — das betrifft nicht nur C#-Property-Namen, sondern
auch die Schlüssel von `Dictionary<string,string>`-Properties. Für `Fields` ist das fatal,
weil die Schlüssel frei definierte fachliche Bezeichner sind (später 1:1 gegen
Excel-Spaltennamen gematcht) und ihr Original-Casing erhalten bleiben muss.

**Fix**: `ScenarioResourceRuleDocument.Fields` (natives `Dictionary<string,string>`-Property)
ersetzt durch `FieldsJson` (roher JSON-String, `System.Text.Json` statt dem
CosmosClient-eigenen Serializer) — exakt dasselbe Muster wie bereits `ConditionJson` und
`CosmosJobQueue.PayloadJson`. Zusätzlich defensiv gegen `FieldsJson == null/empty`
abgesichert (alte, vor dem Fix geschriebene Test-Dokumente im Emulator hätten sonst beim
Lesen eine `ArgumentNullException` geworfen).

Nach dem Fix erneut live verifiziert: Import mit zwei Regeln (`Rolle:Disponent` +
`Rolle:Reader`, beide Bedingungen `Fields.Rolle==Disponent`) → Export liefert exaktes
Original-Casing zurück, Deploy meldet korrekt "1 Ressource deployt, 1 durch Bedingung
übersprungen".

## Verifikation

- `dotnet build` — alle Schichten fehlerfrei.
- `dotnet test` — 56/56 grün (29 Domain + 5 Architecture + 3 Application + 19 Integration,
  inkl. neuem dritten Szenario-Test `ImportTemplate_ThenDeploy_AutoCreatesResourceAndDeploysOnlyMatchingRule`).
- Live gegen laufenden Cosmos-Emulator: Workload per Dev-Seed angelegt, Template mit zwei
  bisher unbekannten Ressourcen importiert (beide automatisch angelegt, per
  `GET /api/workloads` bestätigt), Deploy ausgelöst, Worker-Log bestätigt selektives Deployment
  nur der Regel mit erfüllter Bedingung, Export liefert das Template mit korrektem
  Original-Casing zurück.
- `npm run build` / `vitest run` im Web-Projekt — grün.

## Was bewusst nicht getan wurde

- Kein Migrationsscript für vor diesem Fix angelegte Szenario-Dokumente im Emulator — lokale
  Dev-/Test-Daten, per Neuanlage (neuer Szenario-Name) ersetzbar; die defensive
  Null-Behandlung verhindert nur einen Absturz beim Lesen, alte Dokumente bleiben mit leeren
  `Fields` bestehen.
- Excel-Import selbst (Phase 4 des ursprünglichen Plans) ist weiterhin nicht begonnen — dieses
  Redesign schafft nur die Datengrundlage (frei definierte `Fields`-Schlüssel als künftiger
  Bezugspunkt für Excel-Spaltennamen).
