using System.Text.Json;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;

namespace B2B.Portal.Application.Services;

/// <summary>
/// Erzeugt DirectoryOperations/Jobs aus fachlichem Domain State und legt sie in die
/// IJobQueue (Blueprint 10.1 "Asynchrones Command-/Job-Modell", 16.2 ProvisioningService).
/// Idempotenz: Ein bereits existierender Job mit identischem DesiredStateHash und Status
/// Pending/Running/Success wird nicht erneut angelegt.
/// </summary>
public sealed class ProvisioningService(IJobRepository jobRepository, IJobQueue jobQueue, IClock clock)
{
    public async Task<DirectoryOperation> EnqueueJobAsync(
        string platformTenantId,
        string? directoryTenantId,
        string jobType,
        string entityType,
        string entityId,
        string desiredStateHash,
        object payload,
        Guid correlationId,
        CancellationToken ct,
        string? triggeredBy = null,
        Guid? workloadId = null)
    {
        // Einmal serialisieren, fuer DirectoryOperation.PayloadJson (Restart-Grundlage) UND
        // JobEnvelope.Payload (Worker-Transport) wiederverwenden statt zweimal zu serialisieren.
        var payloadJsonString = JsonSerializer.Serialize(payload);
        var payloadJson = JsonSerializer.SerializeToElement(payload);

        var operation = new DirectoryOperation
        {
            PlatformTenantId = platformTenantId,
            DirectoryTenantId = directoryTenantId,
            JobType = jobType,
            EntityType = entityType,
            EntityId = entityId,
            TriggeredBy = triggeredBy,
            WorkloadId = workloadId,
            CorrelationId = correlationId,
            DesiredStateHash = desiredStateHash,
            Status = JobStatus.Pending,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            Log = [new JobLogEntry(clock.UtcNow, JobStatus.Pending, "Job erstellt und in Queue eingereiht.")],
            PayloadJson = payloadJsonString,
        };

        await jobRepository.UpsertAsync(operation, ct);

        var envelope = JobEnvelope.Create(
            platformTenantId, directoryTenantId, jobType, entityType, entityId,
            desiredStateHash, payloadJson, correlationId, operation.Id);

        // JobEnvelope.JobId und DirectoryOperation.Id sind identisch, damit der Worker
        // denselben persistierten Statusdatensatz fortschreiben kann.
        await jobQueue.EnqueueAsync(envelope, ct);

        return operation;
    }
}
