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
    IGuestAccountRepository guestRepository,
    IWorkloadRepository workloadRepository,
    IResourceConnector connector,
    ILogger<GrantWorkloadRoleHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.GrantWorkloadRole;

    public async Task<string?> HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var assignmentId = Guid.Parse(job.EntityId);
        var guestId = job.Payload.GetProperty("GuestId").GetGuid();

        var assignments = await assignmentRepository.ListByGuestAsync(
            TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId), guestId, ct);
        var assignment = assignments.FirstOrDefault(a => a.Id == assignmentId);
        if (assignment is null)
        {
            logger.LogWarning("GrantWorkloadRole: Assignment {AssignmentId} nicht gefunden.", assignmentId);
            return $"Assignment {assignmentId} nicht gefunden.";
        }

        if (assignment.Status == AssignmentStatus.Active)
        {
            logger.LogInformation(
                "GrantWorkloadRole: Assignment {AssignmentId} bereits aktiv — idempotent, kein Write.",
                assignmentId);
            return $"Assignment {assignmentId} bereits aktiv — kein Write (idempotent).";
        }

        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);
        var guest = await guestRepository.GetAsync(tenant, assignment.GuestId, ct);
        var workload = await workloadRepository.GetAsync(tenant, assignment.WorkloadId, ct);
        var role = workload?.Roles.FirstOrDefault(r => r.Id == assignment.RoleId);
        if (guest?.EntraObjectId is null || workload is null || role is null)
        {
            logger.LogWarning(
                "GrantWorkloadRole: Fakten fehlen für Assignment {AssignmentId} (Guest/EntraObjectId/Workload/Role).",
                assignmentId);
            return $"Assignment {assignmentId}: fehlende Fakten (Guest/EntraObjectId={guest?.EntraObjectId ?? "null"}/" +
                $"Workload={workload?.Id.ToString() ?? "null"}/Role={role?.Id.ToString() ?? "null"}) — kein Grant ausgefuehrt.";
        }

        var grantedResources = new List<string>();
        var skippedResources = new List<string>();
        foreach (var resourceId in role.ResourceMappings)
        {
            var resource = workload.Resources.FirstOrDefault(r => r.Id == resourceId);
            if (resource?.ExternalId is null)
            {
                logger.LogWarning(
                    "GrantWorkloadRole: Assignment {AssignmentId} — Ressource {ResourceId} in Rolle {RoleName} " +
                    "hat keine ExternalId, wird uebersprungen.",
                    assignmentId, resourceId, role.Name);
                skippedResources.Add(resourceId.ToString());
                continue;
            }

            await connector.GrantAccessAsync(
                directoryTenantId: guest.DirectoryTenantId,
                entraObjectId: guest.EntraObjectId,
                resourceExternalId: resource.ExternalId,
                ct);

            logger.LogInformation(
                "GrantWorkloadRole: Zugriff gewaehrt — Guest={GuestId} ({EntraObjectId}) auf " +
                "{ResourceType}:{DisplayName} (ObjectId {ExternalId}) via Rolle {RoleName}. AssignmentId={AssignmentId}",
                guest.Id, guest.EntraObjectId, resource.ResourceType, resource.DisplayName ?? resource.ExternalId,
                resource.ExternalId, role.Name, assignmentId);
            grantedResources.Add($"{resource.ResourceType}:{resource.DisplayName ?? resource.ExternalId}");
        }

        assignment.Status = AssignmentStatus.Active;
        assignment.UpdatedAt = DateTimeOffset.UtcNow;
        await assignmentRepository.UpsertAsync(assignment, ct);

        logger.LogInformation(
            "GrantWorkloadRole ABGESCHLOSSEN: Assignment={AssignmentId} Guest={GuestId} Workload={WorkloadId} " +
            "({WorkloadName}) Rolle={RoleName} Ressourcen=[{Resources}] CorrelationId={CorrelationId}",
            assignmentId, guest.Id, workload.Id, workload.Name, role.Name,
            string.Join(", ", grantedResources), job.CorrelationId);

        return $"Guest {guest.DisplayName} ({guest.EntraObjectId}) erhielt Rolle '{role.Name}' auf Workload " +
            $"'{workload.Name}': {grantedResources.Count} Ressource(n) gewaehrt [{string.Join(", ", grantedResources)}]" +
            (skippedResources.Count > 0 ? $", {skippedResources.Count} ohne ExternalId uebersprungen." : ".");
    }
}

public sealed class RevokeWorkloadRoleHandler(
    IAssignmentRepository assignmentRepository,
    IGuestAccountRepository guestRepository,
    IWorkloadRepository workloadRepository,
    IResourceConnector connector,
    ILogger<RevokeWorkloadRoleHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.RevokeWorkloadRole;

    public async Task<string?> HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var assignmentId = Guid.Parse(job.EntityId);
        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);
        var assignment = await assignmentRepository.GetAsync(tenant, assignmentId, ct);
        var guestId = job.Payload.TryGetProperty("GuestId", out var guestIdValue)
            ? guestIdValue.GetGuid()
            : assignment?.GuestId ?? Guid.Empty;
        if (guestId == Guid.Empty)
        {
            logger.LogWarning("RevokeWorkloadRole: GuestId fehlt für {AssignmentId}.", assignmentId);
            return $"Assignment {assignmentId}: GuestId fehlt — kein Revoke ausgefuehrt.";
        }
        var guest = await guestRepository.GetAsync(tenant, guestId, ct);
        if (assignment is null || guest?.EntraObjectId is null)
        {
            logger.LogWarning("RevokeWorkloadRole: Assignment oder Guest fehlt für {AssignmentId}.", assignmentId);
            return $"Assignment {assignmentId}: Assignment oder Guest/EntraObjectId fehlt — kein Revoke ausgefuehrt.";
        }

        var workload = await workloadRepository.GetAsync(tenant, assignment.WorkloadId, ct);
        var role = workload?.Roles.FirstOrDefault(r => r.Id == assignment.RoleId);
        if (workload is null || role is null)
        {
            logger.LogWarning("RevokeWorkloadRole: Workload oder Rolle fehlt für {AssignmentId}.", assignmentId);
            return $"Assignment {assignmentId}: Workload oder Rolle fehlt — kein Revoke ausgefuehrt.";
        }

        // Entfernt ausschließlich die Member-Referenz des Workload-Zugriffs — die
        // Gastidentität selbst wird hier nie berührt (Anhang A, Regel 3).
        var revokedResources = new List<string>();
        var skippedResources = new List<string>();
        foreach (var resourceId in role.ResourceMappings)
        {
            var resource = workload.Resources.FirstOrDefault(r => r.Id == resourceId);
            if (resource?.ExternalId is null)
            {
                logger.LogWarning(
                    "RevokeWorkloadRole: Assignment {AssignmentId} — Ressource {ResourceId} in Rolle {RoleName} " +
                    "hat keine ExternalId, wird uebersprungen.",
                    assignmentId, resourceId, role.Name);
                skippedResources.Add(resourceId.ToString());
                continue;
            }

            await connector.RevokeAccessAsync(
                directoryTenantId: guest.DirectoryTenantId,
                entraObjectId: guest.EntraObjectId,
                resourceExternalId: resource.ExternalId,
                ct);

            logger.LogInformation(
                "RevokeWorkloadRole: Zugriff entzogen — Guest={GuestId} ({EntraObjectId}) auf " +
                "{ResourceType}:{DisplayName} (ObjectId {ExternalId}) via Rolle {RoleName}. AssignmentId={AssignmentId}",
                guest.Id, guest.EntraObjectId, resource.ResourceType, resource.DisplayName ?? resource.ExternalId,
                resource.ExternalId, role.Name, assignmentId);
            revokedResources.Add($"{resource.ResourceType}:{resource.DisplayName ?? resource.ExternalId}");
        }

        logger.LogInformation(
            "RevokeWorkloadRole ABGESCHLOSSEN: Assignment={AssignmentId} Guest={GuestId} Workload={WorkloadId} " +
            "({WorkloadName}) Rolle={RoleName} Ressourcen=[{Resources}] CorrelationId={CorrelationId}",
            assignmentId, guest.Id, workload.Id, workload.Name, role.Name,
            string.Join(", ", revokedResources), job.CorrelationId);

        return $"Guest {guest.DisplayName} ({guest.EntraObjectId}) verlor Rolle '{role.Name}' auf Workload " +
            $"'{workload.Name}': {revokedResources.Count} Ressource(n) entzogen [{string.Join(", ", revokedResources)}]" +
            (skippedResources.Count > 0 ? $", {skippedResources.Count} ohne ExternalId uebersprungen." : ".");
    }
}
