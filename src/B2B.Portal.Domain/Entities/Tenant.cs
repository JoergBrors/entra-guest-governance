using B2B.Portal.Domain.Enums;

namespace B2B.Portal.Domain.Entities;

/// <summary>
/// Plattformmandant / Verwaltungseinheit (Blueprint 7 "Datenmodell").
/// Ein Tenant kann einen oder mehrere directoryTenantId-Connectoren besitzen.
/// </summary>
public sealed class Tenant
{
    public required string PlatformTenantId { get; init; }
    public required string DisplayName { get; init; }
    public List<string> DirectoryTenantIds { get; init; } = new();
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Firma / Partner / Lieferant (Blueprint 5.2). Ermöglicht Auswertungen aus beiden
/// Blickrichtungen: "Wo hat Gast X Zugriff?" und "Welche Gäste von Firma Y haben Zugriff?".
/// </summary>
public sealed class ExternalOrganization
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlatformTenantId { get; init; }
    public required string Name { get; set; }
    public List<string> VerifiedDomains { get; set; } = new();
    public string? RiskClassification { get; set; }
    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
