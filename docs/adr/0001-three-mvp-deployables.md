# ADR-0001: Drei MVP-Deployables statt Microservices

**Status:** Akzeptiert
**Datum:** 28. August 2026

## Kontext

Der Blueprint beschreibt eine fachlich in viele Komponenten getrennte Zielarchitektur
(API, Policy Engine, Workflow/Job Layer, Graph Adapter, Provider Layer, Connector Layer).
Das Development-/MVP-Dokument legt für den MVP jedoch eine reduzierte Topologie fest.

## Entscheidung

Der MVP wird als genau drei deploybare Einheiten umgesetzt: `B2B.Portal.Web`,
`B2B.Portal.Api`, `B2B.Portal.Worker`. Der Worker registriert alle sieben Handlergruppen
(Invitation, Provisioning, Discovery, Reconciliation, Review, Notification, Lifecycle) in
einem gemeinsamen Prozess.

## Konsequenzen

- Die fachlichen Grenzen bleiben als Namespaces/Ordner (`Handlers/Invitation`,
  `Handlers/Provisioning`, ...) sichtbar, auch wenn sie im selben Prozess laufen.
- Skalierung erfolgt im MVP durch Skalieren des gesamten Worker-Prozesses, nicht durch
  gezieltes Skalieren einzelner Handlergruppen.
- Der Wechsel zu getrennten Deployments je Handlergruppe (Blueprint 20.1 Tabelle) ist ohne
  Domänenumbau möglich, da `IJobHandler`/`JobDispatcher` bereits pro JobType entkoppelt sind.
