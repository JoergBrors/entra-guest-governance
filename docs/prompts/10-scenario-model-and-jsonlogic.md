# Prompt 10 — Workload-Szenarien + JSONLogic-Bedingungen (Phase 2 des Plans)

- **Datum:** 29. August 2026
- **Auftrag:** "bitte phase 2 umsetzen" — Fortsetzung des im Plan-Modus erarbeiteten
  5-Phasen-Plans (siehe `docs/prompts/09-cosmos-migration-and-default.md` für Phase 1 und
  den Plankontext). Phase 2: strukturierte Workload-Szenario-Entities, JSONLogic-
  Bedingungsauswertung, Job-Typ + Worker-Handler für Szenario-Deployment, JSON-Im-/Export,
  API-Endpoints, Web-UI.

## Was umgesetzt wurde

### Domain-Modell
- `src/B2B.Portal.Domain/Enums/StatusEnums.cs` — neues Enum `ScenarioEnvironment { Test, Prod }`.
- `src/B2B.Portal.Domain/Entities/WorkloadScenario.cs` — neue Entities `WorkloadScenario`
  (Id, PlatformTenantId, WorkloadId, ExternalOrganizationId, Name, Environment,
  `List<Guid> ResourceIds` — dieselbe Referenz-Konvention wie `WorkloadRole.ResourceMappings`,
  `List<ScenarioCondition> Conditions`) und `ScenarioCondition` (Name + rohes
  JSONLogic-`JsonElement` als `Expression`).

### JSONLogic-Evaluator (selbst implementiert, wie besprochen)
`src/B2B.Portal.Domain/Services/JsonLogicEvaluator.cs` — reiner, statischer Evaluator nach
exakt demselben Muster wie `DeletionGateEvaluator` (typisierter `ScenarioEvaluationContext`
als Input, pure Funktion). Unterstützter Operator-Satz: `and`, `or`, `not`, `==`, `!=`, `<`,
`<=`, `>`, `>=`, `in`, `var`. `Validate(JsonElement)` prüft Operatoren ohne echten Kontext
(für Editor-/Import-Zeit-Feedback). Nicht unterstützte Operatoren werfen
`NotSupportedException` mit sprechender Meldung statt eine nie zutreffende Bedingung zu
erzeugen. 15 neue Unit-Tests (`tests/B2B.Portal.Domain.Tests/JsonLogicEvaluatorTests.cs`) —
ein Test pro Operator plus verschachtelte Ausdrücke plus Validate-Fälle.

**Ein Test-Fehler beim ersten Lauf gefunden**: `Evaluate_NestedAndOrNot_ReflectsComplexCondition`
hatte eine falsche Erwartung (Testfehler, nicht Evaluator-Fehler) — per Handrechnung
nachvollzogen und die Testerwartung korrigiert.

### Neue Repositories
`IWorkloadScenarioRepository` und `IExternalOrganizationRepository` (letzteres fehlte
komplett, obwohl `ExternalOrganization` als Entity bereits existierte) in `CorePorts.cs`,
mit InMemory- und Cosmos-Implementierungen nach dem in Phase 1 etablierten Muster
(`CosmosWorkloadScenarioRepository`, `CosmosExternalOrganizationRepository` — inkl.
`GetByNameAsync` für die Namensauflösung beim Import).

### Job-Typ + Worker-Handler
- `JobTypes.DeployScenario` neu in `src/B2B.Portal.Domain/Entities/Job.cs`.
- `src/B2B.Portal.Application/Commands/DeployScenarioCommand.cs` — gleiche Form wie
  `GrantWorkloadRoleCommand.cs`, enqueued nur einen Job, führt keine synchrone
  Graph-/Connector-Operation aus.
- `src/B2B.Portal.Worker/Handlers/Provisioning/DeployScenarioHandler.cs` — in der
  **bestehenden** Provisioning-Handlergruppe (ADR-0001-konform, keine neue
  Ausführungsschiene). Wertet jede `ScenarioCondition` per `JsonLogicEvaluator` aus, bevor
  Ressourcen via `IResourceConnector.CreateResourceAsync` deployt werden — nicht via
  `GrantAccessAsync` (das würde einen einzelnen Gast voraussetzen; ein Szenario ist an
  Firma+Umgebung gebunden, nicht an einen Gast). Ruft **niemals**
  `GuestAccount.TransitionTo` — Governance-Core-Invariante gewahrt.

  **Designentscheidung während der Implementierung**: der ursprünglich geplante
  `ScenarioEvaluationContext`-Aufbau sollte `ActiveAssignmentCount` aus
  `IAssignmentRepository.ListActiveByGuestAsync` befüllen — das ist aber gast-, nicht
  workload-bezogen und für ein Szenario (nicht an einen Gast gebunden) nicht sinnvoll
  auflösbar. Korrigiert: Kontext wird direkt aus dem Szenario selbst gebaut
  (Environment, ExternalOrganizationId), `GuestAccountState`/`ActiveAssignmentCount`
  bleiben im MVP leer statt erfundene Werte zu liefern — dokumentiert als bewusste
  MVP-Lücke, nicht stillschweigend implementiert.

### JSON-Import/Export
`src/B2B.Portal.Application/Scenarios/ScenarioImportExportDtos.cs` (menschenlesbare DTOs:
Namen statt GUIDs, `ResourceIndexes` statt `ResourceIds`) und
`ScenarioImportExportService.cs` (löst `WorkloadName`/`ExternalOrganizationName`/
Ressourcen-Referenzen zu IDs auf, validiert JSONLogic-Ausdrücke vorab, sammelt Fehler pro
Zeile statt beim ersten Fehler abzubrechen).

### API-Endpoints
`GET/POST /api/workloads/{id}/scenarios`, `PUT /api/scenarios/{id}`,
`POST /api/scenarios/{id}/deploy`, `POST /api/scenarios/import`,
`GET /api/workloads/{id}/scenarios/export` — alle in `src/B2B.Portal.Api/Program.cs`,
unconditional registriert (nicht LOCAL_MOCK-gated, wie die bestehenden Workload-Endpoints).

### Web-UI
- `types/domain.ts` — `WorkloadScenario`, `ScenarioCondition`, Im-/Export-DTOs. Wichtiger
  Fund: das Backend serialisiert `ScenarioEnvironment` als numerischen Enum-Index (System.Text.Json-
  Default), nicht als String — `SCENARIO_ENVIRONMENT_LABELS`-Mapping ergänzt, live per curl
  bestätigt (`"environment":0` für Test).
- `api/client.ts` — `listScenarios`, `createScenario`, `updateScenario`, `deployScenario`,
  `importScenarios`, `exportScenarios`.
- Neue Seite `src/B2B.Portal.Web/src/pages/ScenariosPage.tsx` — Liste bestehender
  Szenarien mit Deploy-Button, strukturiertes Anlage-Formular (Name, ExternalOrganizationId,
  Environment-Dropdown, Ressourcen-IDs, Bedingungen als JSON-Textarea), sowie ein
  JSON-Import/-Export-Panel. Bewusst **kein** visueller JSONLogic-Builder (wie im Plan
  vorgesehen — raw JSON reicht für v1, keine neue Abhängigkeit).
- `WorkloadsAdminPage.tsx` — "Scenarios"-Button pro Workload-Karte, navigiert zu
  `/workloads/:workloadId/scenarios`. Ein erster Versuch mit Fluent-`Button as={Link}`
  scheiterte an einem TypeScript-Typkonflikt (Fluent UIs `as`-Prop akzeptiert in dieser
  Version keinen beliebigen Component-Typ) — behoben über `useNavigate()` +
  `onClick`-Handler statt `as`-Polymorphismus.
- `App.tsx` — neue Route `/workloads/:workloadId/scenarios`.

## Live-Verifikation

**Backend, alles gegen den echten Cosmos DB Emulator** (API+Worker mit `DATA_PROVIDER=cosmos`,
kein manuelles Setup nötig dank Phase 1):
- Workload mit 5 Ressourcen per Dev-Seed angelegt.
- Szenario `Fabrikam-Test` (Environment=Test, 1 Ressource, 1 Bedingung `Environment==Test`)
  per `POST /api/workloads/{id}/scenarios` angelegt — echter Cosmos-Roundtrip bestätigt.
- `POST /api/scenarios/{id}/deploy` ausgelöst → Worker-Log zeigt: Job korrekt geroutet,
  Bedingung ausgewertet (erfüllt), 1 Ressource via Connector deployt, Audit-Event
  `DeployScenario`/`Accepted` protokolliert.
- `GET .../scenarios/export` liefert korrektes menschenlesbares JSON.
- `POST /api/scenarios/import` mit unbekanntem Firmennamen → korrekter Fehler im
  `errors`-Array statt Serverfehler (`HTTP 200` mit leerer `upsertedScenarioIds`-Liste).
- `POST /api/workloads/{id}/scenarios` mit nicht unterstütztem JSONLogic-Operator
  (`{"map": [...]}`) → `HTTP 400` mit sprechender Fehlermeldung.

**Zusätzlich zwei dauerhafte End-to-End-Tests** (`tests/B2B.Portal.Integration.Tests/ScenarioDeploymentTests.cs`)
gegen InMemory: kompletter Fluss Command → Job → Dispatcher → Handler → Connector, einmal
mit erfüllter Bedingung (Ressource wird deployt) und einmal mit nicht erfüllter Bedingung
(keine Deployment-Aktion) — beide grün.

**Frontend**: `npm run build`/`vitest run` grün, Route `/workloads/:id/scenarios` liefert
HTTP 200 über den laufenden Dev-Server. Keine visuelle Browser-Prüfung möglich (kein
Browser-Tool in dieser Umgebung, wie in früheren Sessions dokumentiert) — der
API-Response-Vertrag ist aber vollständig live verifiziert und die UI-Komponente kompiliert
gegen exakt dieselben Typen.

**Gesamt-Testergebnis: 54/54 Tests grün** (28 Domain + 5 Architecture + 3 Application + 18
Integration, davon 15 neue JsonLogic-Tests + 2 neue End-to-End-Szenario-Tests + 1 neuer
`CosmosTenantIsolationTests`-artiger Zuwachs aus Phase 1 bereits enthalten).

## Was bewusst nicht getan wurde

- Kein visueller JSONLogic-Builder — raw JSON in einer Textarea, mit Backend-Validate für
  Feedback, wie im Plan als bewusste Scope-Reduktion für v1 festgehalten.
- Kein API-Endpoint zum Anlegen/Verwalten von `ExternalOrganization` selbst — Phase 2 hat
  nur die Repository-Ebene gebraucht; ein Create-Endpoint war nicht Teil des Plans für
  diese Phase (würde vermutlich mit Phase 4/Excel-Import sinnvoll zusammen entstehen, da
  der Import bereits Organisationen automatisch anlegt).
- `ScenarioEvaluationContext.GuestAccountState`/`ActiveAssignmentCount` bleiben im MVP ohne
  echte Datenquelle (siehe Designentscheidung oben) — nicht erfunden, sondern leer/neutral
  belassen und als Lücke dokumentiert.
- Kein Nav-Link für Szenarien in `AppLayout.tsx` — Szenarien sind bewusst nur über die
  zugehörige Workload-Karte erreichbar (pro-Workload gescoped), nicht als eigenständiger
  Menüpunkt.

## Ergebnis

Workload-Szenarien mit Firma+Umgebung+Ressourcen+JSONLogic-Bedingungen sind vollständig
implementiert, live gegen Cosmos verifiziert (Anlegen, Deployen, Import/Export,
Validierungsfehler), und in der Web-UI bedienbar. Bereit für Phase 3 wäre eigentlich schon
mit-erledigt (GUI ist Teil dieser Phase-2-Umsetzung gewesen, da Backend und Frontend
zusammen sinnvoll waren) — als nächstes stünde laut Plan Phase 4 (Excel-Import-Backend) an.
