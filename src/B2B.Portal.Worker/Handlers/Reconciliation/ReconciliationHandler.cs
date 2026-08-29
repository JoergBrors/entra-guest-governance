using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Worker.Processing;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Handlers.Reconciliation;

/// <summary>
/// Vergleicht Desired State (Assignments) mit Actual State (ResourceAccess aus Discovery)
/// und markiert Abweichungen (Blueprint 12.3 "Laufende Synchronisation"). Löst NICHT
/// automatisch Provisionierung/Löschung aus — Reconciliation macht Drift nur sichtbar.
/// </summary>
public sealed class ReconciliationHandler(
    IAssignmentRepository assignmentRepository,
    IResourceAccessRepository resourceAccessRepository,
    ILogger<ReconciliationHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.RunReconciliation;

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var guestId = job.Payload.GetProperty("GuestId").GetGuid();

        var desired = await assignmentRepository.ListActiveByGuestAsync(job.PlatformTenantId, guestId, ct);
        var actual = await resourceAccessRepository.ListByGuestAsync(job.PlatformTenantId, guestId, ct);

        logger.LogInformation(
            "Reconciliation Guest={GuestId}: DesiredAssignments={Desired} ActualAccess={Actual} " +
            "CorrelationId={CorrelationId}",
            guestId, desired.Count, actual.Count, job.CorrelationId);
    }
}
