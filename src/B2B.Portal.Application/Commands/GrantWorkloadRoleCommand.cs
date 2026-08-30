using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;

namespace B2B.Portal.Application.Commands;

public sealed record GrantWorkloadRoleRequest(
    string PlatformTenantId, Guid GuestId, Guid WorkloadId, Guid RoleId, string Actor);

/// <summary>
/// Ordnet einen Gast einer Workload-Rolle zu (Blueprint 9.1 "Minimaler Gast-Workflow").
/// Idempotenz: existiert bereits ein aktives Assignment mit demselben Desired-State-Hash,
/// wird kein zweiter GrantWorkloadRole-Job angelegt (MVP-Dokument, "Idempotenztest für
/// GrantWorkloadRole").
/// </summary>
public sealed class GrantWorkloadRoleCommandHandler(
    IAssignmentRepository assignmentRepository,
    ProvisioningService provisioningService,
    AuditService auditService)
{
    public async Task<GuestWorkloadAssignment> HandleAsync(GrantWorkloadRoleRequest request, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        var hash = DesiredStateHasher.Hash(
            "GrantWorkloadRole", request.GuestId.ToString(), request.WorkloadId.ToString(), request.RoleId.ToString());

        var tenant = TenantContext.Create(request.PlatformTenantId);
        var existing = await assignmentRepository.ListActiveByGuestAsync(tenant, request.GuestId, ct);
        var already = existing.FirstOrDefault(a => a.WorkloadId == request.WorkloadId && a.RoleId == request.RoleId);

        if (already is not null)
        {
            await auditService.RecordAsync(
                request.PlatformTenantId, request.Actor, "GrantWorkloadRole", nameof(GuestWorkloadAssignment),
                already.Id.ToString(), "NoOp-AlreadyActive", correlationId, ct: ct);
            return already;
        }

        var assignment = new GuestWorkloadAssignment
        {
            PlatformTenantId = request.PlatformTenantId,
            GuestId = request.GuestId,
            WorkloadId = request.WorkloadId,
            RoleId = request.RoleId,
            Status = AssignmentStatus.Requested,
        };

        await assignmentRepository.UpsertAsync(assignment, ct);

        await provisioningService.EnqueueJobAsync(
            request.PlatformTenantId, directoryTenantId: null, JobTypes.GrantWorkloadRole,
            nameof(GuestWorkloadAssignment), assignment.Id.ToString(), hash,
            new { request.GuestId, request.WorkloadId, request.RoleId },
            correlationId, ct, triggeredBy: request.Actor, workloadId: request.WorkloadId);

        await auditService.RecordAsync(
            request.PlatformTenantId, request.Actor, "GrantWorkloadRole", nameof(GuestWorkloadAssignment),
            assignment.Id.ToString(), "Accepted", correlationId, ct: ct);

        return assignment;
    }
}

public sealed record RevokeWorkloadRoleRequest(string PlatformTenantId, Guid AssignmentId, string Actor);

/// <summary>
/// Entzieht ein Assignment. Entfernt AUSSCHLIESSLICH den Workload-Zugriff — die
/// Gastidentität selbst bleibt unangetastet (Anhang A, Regel 3).
/// </summary>
public sealed class RevokeWorkloadRoleCommandHandler(
    IAssignmentRepository assignmentRepository,
    ProvisioningService provisioningService,
    AuditService auditService)
{
    public async Task HandleAsync(RevokeWorkloadRoleRequest request, GuestWorkloadAssignment assignment, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        assignment.Status = AssignmentStatus.Revoked;
        await assignmentRepository.UpsertAsync(assignment, ct);

        var hash = DesiredStateHasher.Hash("RevokeWorkloadRole", assignment.Id.ToString());
        await provisioningService.EnqueueJobAsync(
            request.PlatformTenantId, directoryTenantId: null, JobTypes.RevokeWorkloadRole,
            nameof(GuestWorkloadAssignment), assignment.Id.ToString(), hash,
            new { assignment.GuestId, assignment.WorkloadId, assignment.RoleId },
            correlationId, ct, triggeredBy: request.Actor, workloadId: assignment.WorkloadId);

        await auditService.RecordAsync(
            request.PlatformTenantId, request.Actor, "RevokeWorkloadRole", nameof(GuestWorkloadAssignment),
            assignment.Id.ToString(), "Accepted", correlationId, ct: ct);
    }
}
