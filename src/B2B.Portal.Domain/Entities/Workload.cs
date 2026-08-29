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
    public List<Guid> ResourceMappings { get; init; } = new();
}

/// <summary>Technische Ressource, die einer Workload-Rolle zugeordnet werden kann.</summary>
public sealed class WorkloadResource
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid WorkloadId { get; init; }
    public required string ResourceType { get; set; } // z.B. SecurityGroup, M365Group, Team, AppRole
    public string? ExternalId { get; set; }
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
