using System.Text.RegularExpressions;
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
        TenantContext tenant, string name, string? owner, string? templateId,
        bool isDefault, string? administrativeUnitExternalId, string? applicationExternalId,
        List<string> resourceNamePatterns,
        string actor, CancellationToken ct)
    {
        var workload = new Workload
        {
            PlatformTenantId = tenant.PlatformTenantId,
            Name = name,
            Owner = owner,
            TemplateId = templateId,
            IsDefault = isDefault,
            AdministrativeUnitExternalId = administrativeUnitExternalId,
            ApplicationExternalId = applicationExternalId,
        };
        workload.ResourceNamePatterns.AddRange(ValidateResourceNamePatterns(resourceNamePatterns));

        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "CreateWorkload", nameof(Workload),
            workload.Id.ToString(), "Accepted", Guid.NewGuid(), ct: ct);

        return workload;
    }

    public async Task<Workload> UpdateWorkloadAsync(
        TenantContext tenant, Guid workloadId, string name, string? owner,
        string? administrativeUnitExternalId, string? applicationExternalId,
        List<string> resourceNamePatterns,
        string actor, CancellationToken ct)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        workload.Name = name;
        workload.Owner = owner;
        workload.AdministrativeUnitExternalId = administrativeUnitExternalId;
        workload.ApplicationExternalId = applicationExternalId;
        workload.ResourceNamePatterns.Clear();
        workload.ResourceNamePatterns.AddRange(ValidateResourceNamePatterns(resourceNamePatterns));
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
        string? applicationId, string? applicationRoleId, List<Guid> resourceMappings,
        string actor, CancellationToken ct)
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

        var hasGroupMapping = workload.Resources
            .Where(r => resourceMappings.Contains(r.Id))
            .Any(r => IsGroupResourceType(r.ResourceType));
        if (!string.IsNullOrWhiteSpace(workload.ApplicationExternalId)
            && !hasGroupMapping
            && (string.IsNullOrWhiteSpace(applicationId) || string.IsNullOrWhiteSpace(applicationRoleId)))
        {
            throw new InvalidOperationException(
                "Dieser Workload ist einer Application zugeordnet. Eine Rolle braucht dann entweder eine Gruppen-Zuweisung oder eine Application mit App-Rolle.");
        }
        if (!string.IsNullOrWhiteSpace(workload.ApplicationExternalId)
            && !hasGroupMapping
            && !string.Equals(workload.ApplicationExternalId, applicationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Die Rolle muss die dem Workload zugeordnete Application '{workload.ApplicationExternalId}' verwenden.");
        }

        var role = roleId is null ? null : workload.Roles.FirstOrDefault(r => r.Id == roleId);
        if (role is null)
        {
            role = new WorkloadRole { WorkloadId = workload.Id, Name = name };
            role.ApplicationId = applicationId;
            role.ApplicationRoleId = applicationRoleId;
            role.ResourceMappings.AddRange(resourceMappings);
            workload.Roles.Add(role);
        }
        else
        {
            role.Name = name;
            role.ApplicationId = hasGroupMapping ? null : applicationId;
            role.ApplicationRoleId = hasGroupMapping ? null : applicationRoleId;
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
        string? externalId, string actor, CancellationToken ct, string? displayName = null)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        var resource = resourceId is null ? null : workload.Resources.FirstOrDefault(r => r.Id == resourceId);
        if (resource is null)
        {
            resource = new WorkloadResource
            {
                WorkloadId = workload.Id, ResourceType = resourceType, ExternalId = externalId,
                DisplayName = displayName, Managed = true,
            };
            workload.Resources.Add(resource);
        }
        else
        {
            resource.ResourceType = resourceType;
            resource.ExternalId = externalId;
            resource.DisplayName = displayName ?? resource.DisplayName;
        }

        workload.UpdatedAt = DateTimeOffset.UtcNow;
        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "UpsertWorkloadResource", nameof(WorkloadResource),
            resource.Id.ToString(), "Accepted", Guid.NewGuid(), ct: ct);

        return resource;
    }

    /// <summary>
    /// Haengt eine Ressource an einen Workload — externalId MUSS die stabile Entra-Object-ID
    /// sein (siehe WorkloadResource-Kommentar), displayName ist der rein informative,
    /// snapshot-artige Anzeigename zum Zeitpunkt des Attachments. Dedupliziert bewusst nach
    /// ResourceType+ExternalId (ObjectId), nicht nach DisplayName — eine im Verzeichnis
    /// umbenannte Gruppe mit gleichbleibender ObjectId erzeugt so weiterhin keinen doppelten
    /// Eintrag, nur der DisplayName-Snapshot wird beim erneuten Attach aufgefrischt.
    /// </summary>
    public async Task<WorkloadResource> AttachResourceAsync(
        TenantContext tenant, Guid workloadId, string resourceType, string externalId, string actor, CancellationToken ct,
        string? displayName = null)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId, ct)
            ?? throw new InvalidOperationException($"Workload {workloadId} nicht gefunden.");

        var existing = workload.Resources.FirstOrDefault(r =>
            string.Equals(r.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.ExternalId, externalId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (displayName is not null && !string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal))
            {
                existing.DisplayName = displayName;
                workload.UpdatedAt = DateTimeOffset.UtcNow;
                await workloadRepository.UpsertAsync(workload, ct);
            }
            return existing;
        }

        var resource = new WorkloadResource
        {
            WorkloadId = workload.Id,
            ResourceType = resourceType,
            ExternalId = externalId,
            DisplayName = displayName,
            Managed = false,
        };
        workload.Resources.Add(resource);
        workload.UpdatedAt = DateTimeOffset.UtcNow;
        await workloadRepository.UpsertAsync(workload, ct);

        await auditService.RecordAsync(
            tenant.PlatformTenantId, actor, "AttachWorkloadResource", nameof(WorkloadResource),
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

    private static List<string> ValidateResourceNamePatterns(List<string>? patterns)
    {
        var result = new List<string>();
        foreach (var pattern in patterns ?? [])
        {
            var value = pattern.Trim();
            if (value.Length == 0)
            {
                continue;
            }
            if (IsRegexPattern(value))
            {
                try
                {
                    _ = new Regex(ToRegexExpression(value), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException($"Regex-Pattern '{value}' ist ungültig: {ex.Message}");
                }
            }
            else if (value.Any(c => !(char.IsLetterOrDigit(c) || c is '*' or '?' or '-' or '_' or ' ')))
            {
                throw new InvalidOperationException(
                    $"Pattern '{value}' ist ungültig. Erlaubt sind Wildcards mit Buchstaben, Zahlen, Leerzeichen, '-', '_', '*' und '?' oder Regex als 'regex:<ausdruck>' bzw. '/<ausdruck>/'.");
            }
            result.Add(value);
        }
        return result;
    }

    private static bool IsRegexPattern(string value) =>
        value.StartsWith("regex:", StringComparison.OrdinalIgnoreCase)
        || (value.Length >= 2 && value.StartsWith('/') && value.EndsWith('/'));

    private static string ToRegexExpression(string value)
    {
        if (value.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            return value["regex:".Length..];
        }
        return value.Length >= 2 && value.StartsWith('/') && value.EndsWith('/')
            ? value[1..^1]
            : value;
    }

    private static bool IsGroupResourceType(string resourceType) =>
        resourceType.Equals("SecurityGroup", StringComparison.OrdinalIgnoreCase)
        || resourceType.Equals("M365Group", StringComparison.OrdinalIgnoreCase)
        || resourceType.Equals("Team", StringComparison.OrdinalIgnoreCase);
}
