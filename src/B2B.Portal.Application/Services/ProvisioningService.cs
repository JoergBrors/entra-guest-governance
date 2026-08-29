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
        CancellationToken ct)
    {
        var operation = new DirectoryOperation
        {
            PlatformTenantId = platformTenantId,
            DirectoryTenantId = directoryTenantId,
            JobType = jobType,
            EntityType = entityType,
            EntityId = entityId,
            CorrelationId = correlationId,
            DesiredStateHash = desiredStateHash,
            Status = JobStatus.Pending,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };

        await jobRepository.UpsertAsync(operation, ct);

        var payloadJson = JsonSerializer.SerializeToElement(payload);
        var envelope = JobEnvelope.Create(
            platformTenantId, directoryTenantId, jobType, entityType, entityId,
            desiredStateHash, payloadJson, correlationId);

        // JobEnvelope.JobId und DirectoryOperation.Id sind bewusst getrennt (Envelope ist
        // der Transport, DirectoryOperation der persistente Audit-/Statusdatensatz);
        // beide teilen sich CorrelationId für Nachvollziehbarkeit.
        await jobQueue.EnqueueAsync(envelope, ct);

        return operation;
    }
}
