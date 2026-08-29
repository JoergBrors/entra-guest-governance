using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Processing;

/// <summary>
/// Dispatcher, der JobEnvelopes aus der IJobQueue an registrierte IJobHandler verteilt
/// (MVP-Dokument Abschnitt 5). Validiert den Tenant-Kontext vor jeder Ausführung
/// (Blueprint 8, "Worker"-Zeile) und implementiert das Retry-/DeadLetter-Grundmodell
/// (Blueprint 10.3): Pending -&gt; Running -&gt; Success | Retry -&gt; Running | Failed/DeadLetter.
/// </summary>
public sealed class JobDispatcher(
    IEnumerable<IJobHandler> handlers, IJobQueue jobQueue, ILogger<JobDispatcher> logger)
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
            await jobQueue.DeadLetterAsync(job.JobId, $"No handler for {job.JobType}", ct);
            return true;
        }

        try
        {
            logger.LogInformation(
                "Verarbeite Job {JobId} Type={JobType} Tenant={Tenant} CorrelationId={CorrelationId}",
                job.JobId, job.JobType, job.PlatformTenantId, job.CorrelationId);

            await handler.HandleAsync(job, ct);
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
                await jobQueue.DeadLetterAsync(job.JobId, ex.Message, ct);
            }
            else
            {
                logger.LogWarning(ex, "Job {JobId} fehlgeschlagen (Versuch {Attempt}) -> Retry", job.JobId, attempt);
            }
        }

        return true;
    }
}
