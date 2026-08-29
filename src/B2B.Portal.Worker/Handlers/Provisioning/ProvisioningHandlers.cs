using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Worker.Processing;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Handlers.Provisioning;

/// <summary>
/// Grant/Revoke Workload Role (Blueprint 6, MVP-Dokument "Provisioning"-Handlergruppe).
/// Führt eine Idempotenzprüfung vor dem technischen Write durch: ist das Assignment
/// bereits Active, wird kein zweiter Grant an den Connector geschickt.
/// </summary>
public sealed class GrantWorkloadRoleHandler(
    IAssignmentRepository assignmentRepository,
    IResourceConnector connector,
    ILogger<GrantWorkloadRoleHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.GrantWorkloadRole;

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var assignmentId = Guid.Parse(job.EntityId);
        var guestId = job.Payload.GetProperty("GuestId").GetGuid();

        var assignments = await assignmentRepository.ListByGuestAsync(
            TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId), guestId, ct);
        var assignment = assignments.FirstOrDefault(a => a.Id == assignmentId);
        if (assignment is null)
        {
            logger.LogWarning("GrantWorkloadRole: Assignment {AssignmentId} nicht gefunden.", assignmentId);
            return;
        }

        if (assignment.Status == AssignmentStatus.Active)
        {
            logger.LogInformation(
                "GrantWorkloadRole: Assignment {AssignmentId} bereits aktiv — idempotent, kein Write.",
                assignmentId);
            return;
        }

        // directoryTenantId wird im MVP über den Guest ermittelt (Payload trägt hier nur GuestId).
        await connector.GrantAccessAsync(
            directoryTenantId: job.DirectoryTenantId ?? string.Empty,
            entraObjectId: guestId.ToString(),
            resourceExternalId: assignment.RoleId.ToString(),
            ct);

        assignment.Status = AssignmentStatus.Active;
        assignment.UpdatedAt = DateTimeOffset.UtcNow;
        await assignmentRepository.UpsertAsync(assignment, ct);

        logger.LogInformation("Assignment {AssignmentId} granted. CorrelationId={CorrelationId}",
            assignmentId, job.CorrelationId);
    }
}

public sealed class RevokeWorkloadRoleHandler(
    IResourceConnector connector,
    ILogger<RevokeWorkloadRoleHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.RevokeWorkloadRole;

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var assignmentId = Guid.Parse(job.EntityId);
        var guestId = job.Payload.GetProperty("GuestId").GetGuid();

        // Entfernt ausschließlich die Member-Referenz des Workload-Zugriffs — die
        // Gastidentität selbst wird hier nie berührt (Anhang A, Regel 3).
        await connector.RevokeAccessAsync(
            directoryTenantId: job.DirectoryTenantId ?? string.Empty,
            entraObjectId: guestId.ToString(),
            resourceExternalId: assignmentId.ToString(),
            ct);

        logger.LogInformation("Assignment {AssignmentId} revoked. CorrelationId={CorrelationId}",
            assignmentId, job.CorrelationId);
    }
}
