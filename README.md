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
| Web UI | http://localhost:5173 |
| Portal API | http://localhost:5000 |
| Health | http://localhost:5000/health |
| Worker | Hintergrundprozess / Konsolenlog |

## Sicherheitswarnung (Definition of Safe Local Development)

> Ein frisches Checkout darf nach Restore/Install/Start **keine** externen Directory- oder
> Mail-Schreiboperationen ausführen. Erst eine explizite `DEV_INTEGRATION`-Konfiguration
> (separate App Registration, dedizierter Dev-Tenant, dedizierte Shared Mailbox) schaltet
> reale Adapter frei. Secrets werden nie in `.env`-Dateien committed — nutze User Secrets /
> Key Vault / Managed Identity.

## Codex-Prompts

`prompts/01-bootstrap-mvp.md` und `prompts/02-test-mvp.md` sind die im Development-Dokument
festgelegten, wortgleich übernommenen Aufträge für Codex CLI (`codex exec -`). Sie dienen
als wiederholbare Grundlage, um dieses Repository (weiter) zu bauen bzw. gegen die
MVP-Anforderungen zu prüfen.

## MVP-Testreport

Siehe `docs/architecture/mvp-test-report.md` für den aktuellen Status, offene
Integrationstests und nächste Schritte.

## Nicht festgelegt / bewusst offen gelassen

Konkrete Entra Tenant IDs, App Registrations, Graph Permission Sets, Shared-Mailbox-Adresse,
Gruppennamenskonventionen, Review-Intervalle und Lifecycle-Fristen sind **absichtlich nicht
erfunden**. Sie bleiben Tenant-/Umgebungs-Konfiguration (siehe Blueprint Abschnitt 23.2).

---
Version 0.1.0-mvp · basierend auf Blueprint Version 1.0, Stand 28. August 2026
