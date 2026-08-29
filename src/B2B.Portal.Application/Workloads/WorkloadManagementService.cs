using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;

namespace B2B.Portal.Application.Workloads;

public sealed record WorkloadAssignmentCounts(int Active, int Inactive);

/// <summary>
/// Bündelt alle schreibenden Operationen auf Workload/WorkloadRole/WorkloadResource
/// (Anlegen, Bearbeiten, Deaktivieren/Entfernen), inkl. der Konsistenzprüfungen, die eine
/// reine Repository-Schicht nicht kennen kann: eine Rolle/Ressource darf nicht entfernt
/// werden, solange noch etwas darauf zeigt (aktive GuestWorkloadAssignments auf eine Rolle,
/// WorkloadRole.ResourceMappings oder ScenarioResourceRule.ResourceId auf eine Ressource) —
/// sonst entstehen tote Referenzen. Lebt in Application (nicht Domain), weil die Prüfungen
/// mehrere Repositories/Ports brauchen.
/// </summary>
public sealed class WorkloadManagementService(
    IWorkloadRepository workloadRepository,
    IWorkloadScenarioRepository scenarioRepository,
    IAssignmentRepository assignmentRepository,
    AuditService auditService)
{
    public async Task<Workload> CreateWorkloadAsync(
        TenantContext tenant, string name, string? owner, string? templateId, string actor, CancellationToken ct)
    {
        var workload = new Workload
        {
            PlatformTenantId = tenant.PlatformTenantId,
            Name = name,
            Owner = owner,
            TemplateId = templateId,
        };

        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "CreateWorkload", nameof(Workload),
            workload.Id.ToString(), "Accepted", Guid.NewGuid(), ct: ct);

        return workload;
    }

    public async Task<Workload> UpdateWorkloadAsync(
        TenantContext tenant, Guid workloadId, string name, string? owner, string actor, CancellationToken ct)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        workload.Name = name;
        workload.Owner = owner;
        workload.UpdatedAt = DateTimeOffset.UtcNow;
        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "UpdateWorkload", nameof(Workload),
            workload.Id.ToString(), "Accepted", Guid.NewGuid(), ct: ct);

        return workload;
    }

    /// <summary>Soft-Delete (Active=false) — bewahrt Historie/Referenzen (Assignments,
    /// Szenarien), verschwindet aber aus aktiven Listen. Für einen Workload mit noch
    /// laufender Historie die richtige Wahl; siehe DeleteWorkloadAsync für endgültiges
    /// Löschen, sobald keine aktiven Nutzer mehr vorhanden sind.</summary>
    public async Task DeactivateWorkloadAsync(TenantContext tenant, Guid workloadId, string actor, CancellationToken ct)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        workload.Active = false;
        workload.UpdatedAt = DateTimeOffset.UtcNow;
        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "DeactivateWorkload", nameof(Workload),
            workload.Id.ToString(), "Accepted", Guid.NewGuid(), ct: ct);
    }

    /// <summary>Macht DeactivateWorkloadAsync rückgängig (Active=true) — derselbe Workload-
    /// Datensatz samt Historie war die ganze Zeit über erhalten, es wird nur wieder
    /// sichtbar/aktiv geschaltet.</summary>
    public async Task ReactivateWorkloadAsync(TenantContext tenant, Guid workloadId, string actor, CancellationToken ct)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        workload.Active = true;
        workload.UpdatedAt = DateTimeOffset.UtcNow;
        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "ReactivateWorkload", nameof(Workload),
            workload.Id.ToString(), "Accepted", Guid.NewGuid(), ct: ct);
    }

    /// <summary>Aktive vs. inaktive/beendete Zuweisungen eines Workload — Grundlage für die
    /// "Wie viele Nutzer hat dieser Workload noch?"-Anzeige und die Hart-Löschen-Sperre.</summary>
    public async Task<WorkloadAssignmentCounts> GetAssignmentCountsAsync(
        TenantContext tenant, Guid workloadId, CancellationToken ct)
    {
        var assignments = await assignmentRepository.ListByWorkloadAsync(tenant, workloadId, ct);
        var active = assignments.Count(a => a.Status is AssignmentStatus.Active or AssignmentStatus.Approved or AssignmentStatus.Requested);
        return new WorkloadAssignmentCounts(active, assignments.Count - active);
    }

    /// <summary>Hartes Löschen — nur erlaubt, wenn keine aktiven Zuweisungen mehr existieren
    /// (sonst würden Gäste unbemerkt Zugriff auf einen nicht mehr existierenden Workload
    /// behalten). Entfernt zusätzlich alle Szenarien des Workload (Szenarien haben keine
    /// Fremdreferenzen von außen, siehe IWorkloadScenarioRepository) und die historischen
    /// (nicht mehr aktiven) Assignments — ein gelöschter Workload soll keine Datenleichen
    /// hinterlassen.</summary>
    public async Task DeleteWorkloadAsync(TenantContext tenant, Guid workloadId, string actor, CancellationToken ct)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        var counts = await GetAssignmentCountsAsync(tenant, workloadId, ct);
        if (counts.Active > 0)
        {
            throw new InvalidOperationException(
                $"Workload '{workload.Name}' hat noch {counts.Active} aktive Zuweisung(en) und kann nicht endgültig gelöscht werden.");
        }

        var scenarios = await scenarioRepository.ListByWorkloadAsync(tenant, workloadId, ct);
        foreach (var scenario in scenarios)
        {
            await scenarioRepository.DeleteAsync(tenant, scenario.Id, ct);
        }

        var assignments = await assignmentRepository.ListByWorkloadAsync(tenant, workloadId, ct);
        foreach (var assignment in assignments)
        {
            await assignmentRepository.DeleteAsync(tenant, assignment.Id, ct);
        }

        await workloadRepository.DeleteAsync(tenant, workloadId, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "DeleteWorkload", nameof(Workload),
            workloadId.ToString(), "Accepted", Guid.NewGuid(),
            details: $"{scenarios.Count} Szenario(en), {assignments.Count} historische Zuweisung(en) mitgelöscht.", ct: ct);
    }

    public async Task<WorkloadRole> UpsertRoleAsync(
        TenantContext tenant, Guid workloadId, Guid? roleId, string name,
        List<Guid> resourceMappings, string actor, CancellationToken ct)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        var knownResourceIds = workload.Resources.Select(r => r.Id).ToHashSet();
        var unknownMappings = resourceMappings.Where(id => !knownResourceIds.Contains(id)).ToList();
        if (unknownMappings.Count > 0)
        {
            throw new InvalidOperationException(
                $"ResourceMappings verweisen auf unbekannte Ressourcen: {string.Join(", ", unknownMappings)}.");
        }

        var role = roleId is null ? null : workload.Roles.FirstOrDefault(r => r.Id == roleId);
        if (role is null)
        {
            role = new WorkloadRole { WorkloadId = workload.Id, Name = name };
            role.ResourceMappings.AddRange(resourceMappings);
            workload.Roles.Add(role);
        }
        else
        {
            role.Name = name;
            role.ResourceMappings.Clear();
            role.ResourceMappings.AddRange(resourceMappings);
        }

        workload.UpdatedAt = DateTimeOffset.UtcNow;
        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "UpsertWorkloadRole", nameof(WorkloadRole),
            role.Id.ToString(), "Accepted", Guid.NewGuid(), ct: ct);

        return role;
    }

    /// <summary>Blockiert, wenn noch aktive Assignments auf die Rolle zeigen — sonst
    /// hinge die Zuweisung an einer nicht mehr existierenden Rolle (Datenkonsistenz).</summary>
    public async Task DeleteRoleAsync(TenantContext tenant, Guid workloadId, Guid roleId, string actor, CancellationToken ct)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        var role = workload.Roles.FirstOrDefault(r => r.Id == roleId)
            ?? throw new InvalidOperationException($"WorkloadRole {roleId} nicht gefunden.");

        var assignments = await assignmentRepository.ListByWorkloadAsync(tenant, workloadId, ct);
        var activeCount = assignments.Count(a => a.RoleId == roleId
            && a.Status is AssignmentStatus.Active or AssignmentStatus.Approved or AssignmentStatus.Requested);
        if (activeCount > 0)
        {
            throw new InvalidOperationException(
                $"Rolle '{role.Name}' hat noch {activeCount} aktive Zuweisung(en) und kann nicht gelöscht werden.");
        }

        workload.Roles.Remove(role);
        workload.UpdatedAt = DateTimeOffset.UtcNow;
        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "DeleteWorkloadRole", nameof(WorkloadRole),
            roleId.ToString(), "Accepted", Guid.NewGuid(), ct: ct);
    }

    public async Task<WorkloadResource> UpsertResourceAsync(
        TenantContext tenant, Guid workloadId, Guid? resourceId, string resourceType,
        string? externalId, string actor, CancellationToken ct)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        var resource = resourceId is null ? null : workload.Resources.FirstOrDefault(r => r.Id == resourceId);
        if (resource is null)
        {
            resource = new WorkloadResource { WorkloadId = workload.Id, ResourceType = resourceType, ExternalId = externalId, Managed = true };
            workload.Resources.Add(resource);
        }
        else
        {
            resource.ResourceType = resourceType;
            resource.ExternalId = externalId;
        }

        workload.UpdatedAt = DateTimeOffset.UtcNow;
        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "UpsertWorkloadResource", nameof(WorkloadResource),
            resource.Id.ToString(), "Accepted", Guid.NewGuid(), ct: ct);

        return resource;
    }

    /// <summary>Blockiert, wenn eine WorkloadRole.ResourceMappings oder ein
    /// ScenarioResourceRule.ResourceId noch auf die Ressource zeigt (Datenkonsistenz) —
    /// beide müssten sonst zuerst entkoppelt/gelöscht werden.</summary>
    public async Task DeleteResourceAsync(TenantContext tenant, Guid workloadId, Guid resourceId, string actor, CancellationToken ct)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        var resource = workload.Resources.FirstOrDefault(r => r.Id == resourceId)
            ?? throw new InvalidOperationException($"WorkloadResource {resourceId} nicht gefunden.");

        var blockingRoles = workload.Roles.Where(r => r.ResourceMappings.Contains(resourceId)).Select(r => r.Name).ToList();
        if (blockingRoles.Count > 0)
        {
            throw new InvalidOperationException(
                $"Ressource wird noch von Rolle(n) referenziert: {string.Join(", ", blockingRoles)}.");
        }

        var scenarios = await scenarioRepository.ListByWorkloadAsync(tenant, workloadId, ct);
        var blockingScenarios = scenarios
            .Where(s => s.Rules.Any(r => r.ResourceId == resourceId))
            .Select(s => s.Name)
            .ToList();
        if (blockingScenarios.Count > 0)
        {
            throw new InvalidOperationException(
                $"Ressource wird noch von Szenario(en) referenziert: {string.Join(", ", blockingScenarios)}.");
        }

        workload.Resources.Remove(resource);
        workload.UpdatedAt = DateTimeOffset.UtcNow;
        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "DeleteWorkloadResource", nameof(WorkloadResource),
            resourceId.ToString(), "Accepted", Guid.NewGuid(), ct: ct);
    }
}
