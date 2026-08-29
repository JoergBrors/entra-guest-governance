using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.Services;
using B2B.Portal.Worker.Processing;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Handlers.Lifecycle;

/// <summary>
/// Führt das Deletion Gate im Dry-Run aus (Blueprint 14.2). Setzt NIEMALS selbst den
/// Gast-Status auf Disabled/Deleted — das bleibt DisableGuestHandler/DeleteGuestHandler
/// vorbehalten, die ihrerseits nur nach einer "Ready"-Evaluation aufgerufen werden dürfen.
/// </summary>
public sealed class ValidateDeletionHandler(
    LifecycleService lifecycleService, IJobRepository jobRepository, ILogger<ValidateDeletionHandler> logger)
    : IJobHandler
{
    public string JobType => JobTypes.ValidateDeletion;

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var guestId = Guid.Parse(job.EntityId);
        var gracePeriodReached = job.Payload.TryGetProperty("GracePeriodReached", out var g) && g.GetBoolean();

        var evaluation = await lifecycleService.EvaluateDeletionAsync(
            job.PlatformTenantId, guestId, gracePeriodReached, job.CorrelationId, ct);

        logger.LogInformation(
            "Deletion Gate für Guest {GuestId}: {Result} Blockers=[{Blockers}] CorrelationId={CorrelationId}",
            guestId, evaluation.Result, string.Join(',', evaluation.Blockers), job.CorrelationId);
    }
}

/// <summary>
/// Setzt einen Gast auf Disabled. Darf laut GuestAccount.TransitionTo nur mit
/// viaGovernanceCore=true aufgerufen werden — dieser Handler IST der Governance Core
/// für diesen Zweck und ruft entsprechend explizit auf.
/// </summary>
public sealed class DisableGuestHandler(IGuestAccountRepository guestRepository, ILogger<DisableGuestHandler> logger)
    : IJobHandler
{
    public string JobType => JobTypes.DisableGuest;

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var guest = await guestRepository.GetAsync(job.PlatformTenantId, Guid.Parse(job.EntityId), ct);
        if (guest is null)
        {
            logger.LogWarning("DisableGuest: Guest {EntityId} nicht gefunden.", job.EntityId);
            return;
        }

        guest.TransitionTo(GuestAccountState.Disabled, viaGovernanceCore: true);
        await guestRepository.UpsertAsync(guest, ct);

        logger.LogInformation("Guest {GuestId} disabled. CorrelationId={CorrelationId}", guest.Id, job.CorrelationId);
    }
}

/// <summary>
/// Löscht einen Gast technisch — nur nach Grace Period und ausschließlich, wenn zuvor
/// ValidateDeletion "Ready" ergeben hat. In LOCAL_MOCK wird niemals ein echter Graph-Delete
/// ausgeführt (siehe MVP-Verification-Prompt Punkt 9).
/// </summary>
public sealed class DeleteGuestHandler(
    IGuestAccountRepository guestRepository, bool allowGuestDelete, ILogger<DeleteGuestHandler> logger)
    : IJobHandler
{
    public string JobType => JobTypes.DeleteGuest;

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        if (!allowGuestDelete)
        {
            logger.LogWarning(
                "DeleteGuest blockiert: ALLOW_GUEST_DELETE=false (LOCAL_MOCK Default). " +
                "Job {JobId} wird nicht ausgeführt.", job.JobId);
            return;
        }

        var guest = await guestRepository.GetAsync(job.PlatformTenantId, Guid.Parse(job.EntityId), ct);
        if (guest is null)
        {
            return;
        }

        guest.TransitionTo(GuestAccountState.Deleted, viaGovernanceCore: true);
        await guestRepository.UpsertAsync(guest, ct);

        logger.LogInformation("Guest {GuestId} deleted. CorrelationId={CorrelationId}", guest.Id, job.CorrelationId);
    }
}
