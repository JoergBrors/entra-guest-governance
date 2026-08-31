using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Processing;

/// <summary>
/// Dispatcher, der JobEnvelopes aus der IJobQueue an registrierte IJobHandler verteilt
/// (MVP-Dokument Abschnitt 5). Validiert den Tenant-Kontext vor jeder Ausführung
/// (Blueprint 8, "Worker"-Zeile) und implementiert das Retry-/DeadLetter-Grundmodell
/// (Blueprint 10.3): Pending -&gt; Running -&gt; Success | Retry -&gt; Running | Failed/DeadLetter.
/// </summary>
public sealed class JobDispatcher(
    IEnumerable<IJobHandler> handlers, IJobQueue jobQueue, ILogger<JobDispatcher> logger, IJobRepository? jobRepository = null)
{
    private const int MaxRetries = 3;
    private readonly Dictionary<string, IJobHandler> _handlersByType =
        handlers.ToDictionary(h => h.JobType, StringComparer.Ordinal);

    /// <summary>Verarbeitet genau einen anstehenden Job, falls vorhanden. Gibt true zurück, wenn ein Job verarbeitet wurde.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var job = await jobQueue.DequeueAsync(ct);
        if (job is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(job.PlatformTenantId))
        {
            logger.LogError("Job {JobId} ohne PlatformTenantId — abgelehnt (Tenant-Isolation).", job.JobId);
            await jobQueue.DeadLetterAsync(job.JobId, "Missing PlatformTenantId", ct);
            return true;
        }

        if (!_handlersByType.TryGetValue(job.JobType, out var handler))
        {
            logger.LogError("Kein Handler für JobType {JobType} registriert.", job.JobType);
            await UpdateOperationStatusAsync(job, JobStatus.DeadLetter, $"No handler for {job.JobType}", ct);
            await jobQueue.DeadLetterAsync(job.JobId, $"No handler for {job.JobType}", ct);
            return true;
        }

        if (await IsOperationCancelledAsync(job, ct))
        {
            logger.LogInformation("Job {JobId} wurde vor der Ausführung gestoppt.", job.JobId);
            await jobQueue.CancelAsync(job.JobId, ct);
            return true;
        }

        var triggeredBy = await GetTriggeredByAsync(job, ct);
        var handlerName = handler.GetType().Name;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Ausfuehrlich VOR der Ausfuehrung geloggt (Erweiterung 2026-08-31 "Job/Worker-
            // Audit": vorher fehlten Handler-Name, TriggeredBy und Payload komplett — bei
            // einem haengenden/fehlerhaften Job liess sich aus den Logs allein nicht
            // rekonstruieren, WER den Job warum ausgeloest hat und mit welchen Parametern).
            logger.LogInformation(
                "Job GESTARTET: {JobId} Type={JobType} Handler={Handler} Tenant={Tenant} " +
                "EntityType={EntityType} EntityId={EntityId} TriggeredBy={TriggeredBy} " +
                "CorrelationId={CorrelationId} Payload={Payload}",
                job.JobId, job.JobType, handlerName, job.PlatformTenantId,
                job.EntityType, job.EntityId, triggeredBy ?? "(unbekannt)",
                job.CorrelationId, job.Payload.GetRawText());

            await UpdateOperationStatusAsync(job, JobStatus.Running, null, ct);
            await handler.HandleAsync(job, ct);
            if (!await IsOperationCancelledAsync(job, ct))
            {
                await UpdateOperationStatusAsync(job, JobStatus.Success, null, ct);
            }
            await jobQueue.CompleteAsync(job.JobId, ct);

            stopwatch.Stop();
            logger.LogInformation(
                "Job ERFOLGREICH: {JobId} Type={JobType} Handler={Handler} Tenant={Tenant} " +
                "Dauer={ElapsedMs}ms CorrelationId={CorrelationId}",
                job.JobId, job.JobType, handlerName, job.PlatformTenantId,
                stopwatch.ElapsedMilliseconds, job.CorrelationId);
        }
        catch (Exception ex)
        {
            // Der Attempt-Zaehler wird von der IJobQueue-Implementierung selbst gefuehrt
            // (dauerhaft bei CosmosJobQueue) statt hier im Dispatcher, damit er einen
            // Worker-Neustart bzw. mehrere Worker-Instanzen uebersteht (siehe
            // IJobQueue.RetryAsync-Dokumentation).
            var attempt = await jobQueue.RetryAsync(job.JobId, ex.Message, ct);
            stopwatch.Stop();

            if (attempt >= MaxRetries)
            {
                logger.LogError(
                    ex, "Job DEADLETTER: {JobId} Type={JobType} Handler={Handler} Tenant={Tenant} " +
                    "nach {Attempts} Versuchen, Dauer letzter Versuch={ElapsedMs}ms CorrelationId={CorrelationId}",
                    job.JobId, job.JobType, handlerName, job.PlatformTenantId, attempt, stopwatch.ElapsedMilliseconds, job.CorrelationId);
                await UpdateOperationStatusAsync(job, JobStatus.DeadLetter, ex.Message, ct, attempt);
                await jobQueue.DeadLetterAsync(job.JobId, ex.Message, ct);
            }
            else
            {
                logger.LogWarning(
                    ex, "Job FEHLGESCHLAGEN (Retry folgt): {JobId} Type={JobType} Handler={Handler} Tenant={Tenant} " +
                    "Versuch={Attempt} Dauer={ElapsedMs}ms CorrelationId={CorrelationId}",
                    job.JobId, job.JobType, handlerName, job.PlatformTenantId, attempt, stopwatch.ElapsedMilliseconds, job.CorrelationId);
                await UpdateOperationStatusAsync(job, JobStatus.Retry, ex.Message, ct, attempt);
            }
        }

        return true;
    }

    /// <summary>
    /// Liest TriggeredBy aus der persistierten DirectoryOperation (siehe ProvisioningService.
    /// EnqueueJobAsync, das TriggeredBy beim Anlegen setzt) — nicht Teil von JobEnvelope
    /// selbst, da JobEnvelope der schlanke Transport-Typ ist, den auch In-Memory/Test-Queues
    /// ohne Job-Repository verwenden koennen (siehe jobRepository als optionaler Parameter).
    /// null, wenn kein Repository verdrahtet ist oder kein Log-Eintrag existiert.
    /// </summary>
    private async Task<string?> GetTriggeredByAsync(JobEnvelope job, CancellationToken ct)
    {
        if (jobRepository is null)
        {
            return null;
        }

        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);
        var operation = await jobRepository.GetAsync(tenant, job.JobId, ct);
        return operation?.TriggeredBy;
    }

    private async Task UpdateOperationStatusAsync(
        JobEnvelope job,
        JobStatus status,
        string? error,
        CancellationToken ct,
        int? retryCount = null)
    {
        if (jobRepository is null)
        {
            return;
        }

        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);
        var operation = await jobRepository.GetAsync(tenant, job.JobId, ct);
        if (operation is null)
        {
            return;
        }

        operation.Status = status;
        operation.LastError = error;
        operation.UpdatedAt = DateTimeOffset.UtcNow;
        if (retryCount is not null)
        {
            operation.RetryCount = retryCount.Value;
        }
        operation.Log.Add(new JobLogEntry(operation.UpdatedAt, status, error));

        await jobRepository.UpsertAsync(operation, ct);
    }

    private async Task<bool> IsOperationCancelledAsync(JobEnvelope job, CancellationToken ct)
    {
        if (jobRepository is null)
        {
            return false;
        }

        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);
        var operation = await jobRepository.GetAsync(tenant, job.JobId, ct);
        return operation?.Status == JobStatus.Cancelled;
    }
}
