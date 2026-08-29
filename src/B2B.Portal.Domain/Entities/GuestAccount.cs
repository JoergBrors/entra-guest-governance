using B2B.Portal.Domain.Enums;

namespace B2B.Portal.Domain.Entities;

/// <summary>
/// Zentrale Gastidentität (Blueprint 5.1 "Guest Pool als fachliche Source of Truth").
/// Ein GuestAccount gehört fachlich keinem einzelnen Workload — Workload-Zuweisungen
/// referenzieren diese Entität. Nur das Governance-/Lifecycle-Modul darf den Lifecycle-
/// Status auf Disabled/Deleted setzen (Anhang A, Regel 3).
/// </summary>
public sealed class GuestAccount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlatformTenantId { get; init; }
    public required string DirectoryTenantId { get; init; }

    /// <summary>Entra Object ID — technischer Verweis, aber nie alleiniger Plattformschlüssel.</summary>
    public string? EntraObjectId { get; set; }

    public required string Mail { get; set; }
    public required string DisplayName { get; set; }
    public Guid? ExternalOrganizationId { get; set; }
    public string? Sponsor { get; set; }

    public GuestAccountState AccountState { get; private set; } = GuestAccountState.Discovered;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Zustandsübergang. Absichtlich restriktiv: Disabled/Deleted dürfen laut
    /// Sicherheitsinvariante nur vom LifecycleService (Governance Core) über das
    /// Deletion Gate gesetzt werden, niemals direkt von einem Workload/Connector.
    /// </summary>
    public void TransitionTo(GuestAccountState next, bool viaGovernanceCore = false)
    {
        if ((next is GuestAccountState.Disabled or GuestAccountState.Deleted) && !viaGovernanceCore)
        {
            throw new InvalidOperationException(
                "Nur der Governance Core (LifecycleService) darf einen Gast disablen/löschen. " +
                "Workloads und Connectoren dürfen ausschließlich eigene Assignments/Zugriffe entziehen.");
        }

        AccountState = next;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
