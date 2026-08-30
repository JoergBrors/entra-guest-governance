# Worker Model

Stand: 2026-08-30 (aktualisiert: Invitation Reminder Worker)

Der Worker liegt in `src/B2B.Portal.Worker`.

Kernbestand:

- `Processing/JobDispatcher.cs`
- `Processing/IJobHandler.cs`
- Handlergruppen fuer Invitation, Provisioning, Discovery, Reconciliation, Reviews, Notifications, Lifecycle und Workloads (`Handlers/Workloads/SyncWorkloadPatternResourcesHandler.cs`).
- `ApplicationSignInSyncWorker.cs`: eigener `BackgroundService` (10-Minuten-`PeriodicTimer`), unabhaengig vom Job-Dispatcher.
- `InvitationReminderWorker.cs` (Erweiterung 2026-08-30): zweiter eigenstaendiger
  `BackgroundService`, gleiches 10-Minuten-`PeriodicTimer`-Muster wie
  `ApplicationSignInSyncWorker`, ebenfalls unabhaengig vom Job-Dispatcher. Scannt Gaeste im
  Zustand `Invited` gegen die tenant-weite `ReminderPolicy` (`IReminderPolicyRepository`,
  Cosmos-Container `discovery`) und enqueued pro faelliger Stufe genau einen
  `InvitationReminder`-Job ueber `ProvisioningService.EnqueueJobAsync` — der Dispatcher
  verarbeitet diesen Job danach wie jeden anderen (Retry/DeadLetter/Status-Tracking bleiben
  unveraendert). Idempotenz gegen doppelten Versand liegt NICHT im Dispatcher, sondern im
  Scanner selbst: `GuestAccount.LastReminderStageSent` verhindert, dass dieselbe Stufe fuer
  denselben Gast zweimal enqueued wird.

Der Dispatcher prueft `PlatformTenantId`, nutzt Retry/DeadLetter und delegiert an registrierte Handler. Er schreibt den Job-Status (`Running`/`Success`/`Retry`/`DeadLetter`/`Cancelled`) laufend in `IJobRepository` zurueck und prueft vor/nach der Handler-Ausfuehrung auf Cancellation (`POST /api/jobs/{id}/stop` in der API, `CancelAsync` in `CosmosJobQueue`/`LocalJobQueue`).

## Mock-Entra-Verarbeitung

Im `LOCAL_MOCK` teilen sich `MockGuestDirectory` und `MockResourceConnector` den `MockEntraDirectoryStore`.

- `DiscoveryHandler` liest Benutzer und Gruppenmitgliedschaften.
- `DeployScenarioHandler` legt Gruppen ueber `CreateResourceAsync` an.
- `GrantWorkloadRoleHandler` loest Workload-Rollen auf Ressourcen auf und fuegt den Gast zu den Gruppen hinzu.
- `RevokeWorkloadRoleHandler` entfernt die Gruppenmitgliedschaften.
- `SyncWorkloadPatternResourcesHandler` (Job `SyncWorkloadPatternResources`) matcht Mock-Gruppen gegen `Workload.ResourceNamePatterns` (Glob oder Regex) und haengt Treffer als Ressource an.
- `ApplicationSignInSyncWorker` erzeugt fuer Workloads mit `ApplicationExternalId` periodisch Mock-Sign-In-Eintraege je aktiver/genehmigter/angefragter Zuweisung.
- `InvitationReminderHandler` (Job `InvitationReminder`) versendet ueber `IEmailProvider` (im `LOCAL_MOCK` `MockEmailProvider`, sichtbar unter `GET /api/dev/mail-sink`) und schreibt `GuestAccount.LastReminderStageSent`/`LastReminderSentAt` fort.
