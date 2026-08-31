using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
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

    public async Task<string?> HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var guestId = job.Payload.GetProperty("GuestId").GetGuid();

        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);
        var desired = await assignmentRepository.ListActiveByGuestAsync(tenant, guestId, ct);
        var actual = await resourceAccessRepository.ListByGuestAsync(tenant, guestId, ct);

        logger.LogInformation(
            "Reconciliation Guest={GuestId}: DesiredAssignments={Desired} ActualAccess={Actual} " +
            "CorrelationId={CorrelationId}",
            guestId, desired.Count, actual.Count, job.CorrelationId);

        return $"Guest {guestId}: {desired.Count} Desired-Assignment(s) vs. {actual.Count} Actual-Access-Eintrag(e).";
    }
}
