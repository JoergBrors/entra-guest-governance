using B2B.Portal.Domain.Enums;

namespace B2B.Portal.Domain.Entities;

/// <summary>
/// Fachlicher Zugriffskontext (Blueprint 6.1). Bündelt technische Ressourcen unter
/// einem nachvollziehbaren fachlichen Zweck, z. B. "SAP S/4 Projekt".
/// </summary>
public sealed class Workload
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlatformTenantId { get; init; }
    public required string Name { get; set; }
    public string? Owner { get; set; }
    public string? TemplateId { get; set; }
    public bool Active { get; set; } = true;
    public bool IsDefault { get; set; }
    public string? AdministrativeUnitExternalId { get; set; }
    public string? ApplicationExternalId { get; set; }
    public List<string> ResourceNamePatterns { get; init; } = new();

    public List<WorkloadRole> Roles { get; init; } = new();
    public List<WorkloadResource> Resources { get; init; } = new();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Fachliche Rolle innerhalb eines Workload (Blueprint 6.2). Eine Rolle kann mehrere
/// technische Ressourcen (Gruppen, App-Rollen, Teams) bündeln.
/// </summary>
public sealed class WorkloadRole
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid WorkloadId { get; init; }
    public required string Name { get; set; }
    public string? ApplicationId { get; set; }
    public string? ApplicationRoleId { get; set; }
    public List<Guid> ResourceMappings { get; init; } = new();
}

/// <summary>
/// Technische Ressource, die einer Workload-Rolle zugeordnet werden kann.
///
/// ExternalId ist IMMER die stabile Entra-Object-ID der referenzierten Ressource (z.B. einer
/// Mock-Entra-Gruppe), NIE ihr Anzeigename (Erweiterung 2026-08-31 "EntraId-Persistenz +
/// Object-ID-Referenzierung"; vorher schrieb SyncWorkloadPatternResourcesHandler hier
/// group.DisplayName hinein, was bei einer Gruppen-Umbenennung im Verzeichnis zu einer toten
/// Referenz gefuehrt haette, da DisplayNames im Gegensatz zur ObjectId nicht stabil sind).
/// DisplayName ist ein rein informativer Snapshot fuer die Admin-UI (WorkloadsAdminPage,
/// ScenariosPage) und fuer den ResourceNamePatterns-Abgleich (SyncWorkloadPatternResourcesHandler)
/// — er kann veralten, wenn die Ressource im Verzeichnis umbenannt wird, ohne dass ein erneuter
/// Sync gelaufen ist, ist aber fuer die Zugriffssteuerung selbst nie massgeblich.
/// </summary>
public sealed class WorkloadResource
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid WorkloadId { get; init; }
    public required string ResourceType { get; set; } // z.B. SecurityGroup, M365Group, Team, AppRole
    public string? ExternalId { get; set; }
    public string? DisplayName { get; set; }
    public bool Managed { get; set; } = true; // true = vom Portal verwaltet, false = discovered
}

/// <summary>
/// Referenz zwischen Gast, Workload und fachlicher Rolle (Blueprint 7). Aus dieser
/// Zuweisung entsteht ein idempotenter Provisioning Job.
/// </summary>
public sealed class GuestWorkloadAssignment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlatformTenantId { get; init; }
    public required Guid GuestId { get; init; }
    public required Guid WorkloadId { get; init; }
    public required Guid RoleId { get; init; }
    public DateTimeOffset ValidFrom { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ValidUntil { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Requested;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Tatsächlich entdeckter technischer Zugriff (Actual State, Blueprint 12.2).
/// Unclassified Access blockiert eine Gastlöschung (Sicherheitsinvariante 4).
/// </summary>
public sealed class ResourceAccess
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlatformTenantId { get; init; }
    public required Guid GuestId { get; init; }
    public required string ResourceType { get; set; }
    public required string ExternalResourceId { get; set; }
    public AccessClassification Classification { get; set; } = AccessClassification.Unclassified;
    public DateTimeOffset DiscoveredAt { get; init; } = DateTimeOffset.UtcNow;
}
