using B2B.Portal.Application.Ports;

namespace B2B.Portal.Worker;

/// <summary>
/// Basisklasse fuer alle periodischen Worker-BackgroundServices (Erweiterung 2026-08-31
/// "Job/Worker-Audit — Worker-Steuerung"): buendelt PeriodicTimer-Loop, Pause/Resume-Pruefung
/// vor jedem Tick, und das Fortschreiben von WorkerControlState (LastRunStartedAt/
/// -CompletedAt/-Succeeded/-Summary) fuer die Worker-Detailansicht (GET /api/dev/workers).
///
/// Vorher hatten alle 5 periodischen Worker (ApplicationSignInSyncWorker,
/// InvitationReminderWorker, WorkloadPatternSyncWorker, DiscoveryReconciliationWorker,
/// PollingWorker-JobDispatch-Loop) je eine eigene, quasi identische PeriodicTimer-Schleife
/// ohne jede Moeglichkeit, sie einzeln anzuhalten/fortzusetzen oder ihren letzten Lauf
/// nachzuvollziehen — ein pausierter/gestoppter Zustand ueberlebte ausserdem keinen
/// Prozessneustart, weil nirgends etwas persistiert wurde. Diese Basisklasse macht Pause/
/// Resume/manuelles Triggern und den zuletzt beobachteten Lauf fuer JEDEN abgeleiteten Worker
/// einheitlich verfuegbar, ohne dass der abgeleitete Worker mehr tun muss als
/// RunOnceAsync(CancellationToken) zu implementieren und eine kurze Ergebnis-Zusammenfassung
/// zurueckzugeben.
/// </summary>
public abstract class PeriodicWorkerBase(
    string workerName,
    TimeSpan interval,
    IWorkerControlRepository workerControlRepository,
    ILogger logger) : BackgroundService
{
    /// <summary>Eindeutiger, stabiler Name dieses Workers — Primaerschluessel in WorkerControlState und in der Worker-Detailansicht.</summary>
    public string WorkerName { get; } = workerName;

    /// <summary>Konfiguriertes Intervall — rein informativ fuer die Detailansicht, die Ausfuehrung nutzt denselben Wert im PeriodicTimer.</summary>
    public TimeSpan Interval { get; } = interval;

    /// <summary>
    /// Fuehrt genau einen Durchlauf aus und liefert eine kurze menschenlesbare Zusammenfassung
    /// zurueck (analog zu IJobHandler.HandleAsync) — wird in WorkerControlState.LastRunSummary
    /// gespeichert und in der Detailansicht angezeigt.
    /// </summary>
    protected abstract Task<string?> RunOnceAsync(CancellationToken ct);

    // Poll-Intervall fuer manuelle Trigger-Anfragen (POST /api/dev/workers/{name}/trigger,
    // B2B.Portal.Api) — API und Worker sind getrennte Prozesse ohne direkten In-Process-
    // Aufrufkanal, daher schreibt der Endpoint nur TriggerRequestedAt in WorkerControlState
    // (Cosmos) und dieser kurze Zusatz-Timer holt die Anfrage zeitnah ab, statt bis zum
    // naechsten reguleren 10-Minuten-Tick zu warten. 5s ist ein bewusster Kompromiss: kurz
    // genug, dass ein manueller Trigger "sofort genug" wirkt, lang genug, um keine spuerbare
    // Zusatzlast auf Cosmos zu erzeugen.
    private static readonly TimeSpan TriggerPollInterval = TimeSpan.FromSeconds(5);
    private DateTimeOffset? _lastSeenTriggerRequestedAt;

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TryRunOnceAsync(triggeredBy: "startup", stoppingToken);

        using var mainTimer = new PeriodicTimer(Interval);
        using var triggerPollTimer = new PeriodicTimer(TriggerPollInterval);

        var mainTick = mainTimer.WaitForNextTickAsync(stoppingToken).AsTask();
        var triggerTick = triggerPollTimer.WaitForNextTickAsync(stoppingToken).AsTask();

        while (!stoppingToken.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(mainTick, triggerTick);
            if (completed == mainTick)
            {
                if (!await mainTick) break;
                await TryRunOnceAsync(triggeredBy: "scheduler", stoppingToken);
                mainTick = mainTimer.WaitForNextTickAsync(stoppingToken).AsTask();
            }
            else
            {
                if (!await triggerTick) break;
                await CheckForManualTriggerAsync(stoppingToken);
                triggerTick = triggerPollTimer.WaitForNextTickAsync(stoppingToken).AsTask();
            }
        }
    }

    private async Task CheckForManualTriggerAsync(CancellationToken ct)
    {
        var state = await workerControlRepository.GetAsync(WorkerName, ct);
        if (state?.TriggerRequestedAt is null || state.TriggerRequestedAt == _lastSeenTriggerRequestedAt)
        {
            return;
        }

        _lastSeenTriggerRequestedAt = state.TriggerRequestedAt;
        await TryRunOnceAsync(triggeredBy: state.TriggerRequestedBy ?? "manual-trigger", ct);
    }

    /// <summary>
    /// Fuehrt einen Durchlauf aus, es sei denn der Worker ist pausiert — wird sowohl vom
    /// PeriodicTimer-Loop als auch vom manuellen Trigger-Endpoint (POST /api/dev/workers/
    /// {name}/trigger) aufgerufen, damit beide Wege identisch geloggt/nachverfolgt werden.
    /// </summary>
    public async Task<bool> TryRunOnceAsync(string triggeredBy, CancellationToken ct)
    {
        var state = await workerControlRepository.GetAsync(WorkerName, ct);
        if (state?.IsPaused == true)
        {
            logger.LogInformation(
                "Worker {WorkerName}: Lauf uebersprungen — pausiert von {PausedBy} seit {PausedAt}.",
                WorkerName, state.PausedBy, state.PausedAt);
            return false;
        }

        var startedAt = DateTimeOffset.UtcNow;
        logger.LogInformation("Worker {WorkerName} GESTARTET (TriggeredBy={TriggeredBy}).", WorkerName, triggeredBy);

        string? summary = null;
        bool succeeded;
        try
        {
            summary = await RunOnceAsync(ct);
            succeeded = true;
            logger.LogInformation(
                "Worker {WorkerName} ERFOLGREICH (TriggeredBy={TriggeredBy}): {Summary}",
                WorkerName, triggeredBy, summary ?? "(keine Detail-Message)");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            succeeded = false;
            summary = $"Fehler: {ex.Message}";
            logger.LogError(ex, "Worker {WorkerName} FEHLGESCHLAGEN (TriggeredBy={TriggeredBy}).", WorkerName, triggeredBy);
        }

        var completedAt = DateTimeOffset.UtcNow;
        await workerControlRepository.UpsertAsync(
            (state ?? new WorkerControlState(WorkerName, false, null, null, null, null, null, null, null)) with
            {
                LastRunStartedAt = startedAt,
                LastRunCompletedAt = completedAt,
                LastRunSucceeded = succeeded,
                LastRunSummary = summary,
                LastTriggeredBy = triggeredBy,
            },
            ct);

        return true;
    }
}
