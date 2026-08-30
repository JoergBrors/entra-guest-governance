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

        try
        {
            logger.LogInformation(
                "Verarbeite Job {JobId} Type={JobType} Tenant={Tenant} CorrelationId={CorrelationId}",
                job.JobId, job.JobType, job.PlatformTenantId, job.CorrelationId);

            await UpdateOperationStatusAsync(job, JobStatus.Running, null, ct);
            await handler.HandleAsync(job, ct);
            if (!await IsOperationCancelledAsync(job, ct))
            {
                await UpdateOperationStatusAsync(job, JobStatus.Success, null, ct);
            }
            await jobQueue.CompleteAsync(job.JobId, ct);
        }
        catch (Exception ex)
        {
            // Der Attempt-Zaehler wird von der IJobQueue-Implementierung selbst gefuehrt
            // (dauerhaft bei CosmosJobQueue, in-memory bei LocalJobQueue) statt hier im
            // Dispatcher, damit er einen Worker-Neustart bzw. mehrere Worker-Instanzen
            // uebersteht (siehe IJobQueue.RetryAsync-Dokumentation).
            var attempt = await jobQueue.RetryAsync(job.JobId, ex.Message, ct);

            if (attempt >= MaxRetries)
            {
                logger.LogError(ex, "Job {JobId} nach {Attempts} Versuchen -> DeadLetter", job.JobId, attempt);
                await UpdateOperationStatusAsync(job, JobStatus.DeadLetter, ex.Message, ct, attempt);
                await jobQueue.DeadLetterAsync(job.JobId, ex.Message, ct);
            }
            else
            {
                logger.LogWarning(ex, "Job {JobId} fehlgeschlagen (Versuch {Attempt}) -> Retry", job.JobId, attempt);
                await UpdateOperationStatusAsync(job, JobStatus.Retry, ex.Message, ct, attempt);
            }
        }

        return true;
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
