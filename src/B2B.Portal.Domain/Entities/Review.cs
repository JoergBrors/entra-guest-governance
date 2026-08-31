using B2B.Portal.Domain.Enums;

namespace B2B.Portal.Domain.Entities;

/// <summary>
/// Review-Regel (Blueprint 13). Provider=Auto lässt den Capability Resolver entscheiden,
/// ob der interne oder ein nativer Entra-Provider verwendet wird.
/// </summary>
public sealed class ReviewDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlatformTenantId { get; init; }
    public required string Scope { get; set; } // z.B. WorkloadId oder "guest-account"
    public GovernanceProvider Provider { get; set; } = GovernanceProvider.Auto;
    public string? Reviewer { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// Laufende Review-Instanz mit Snapshot der zu prüfenden Assignments (Blueprint 13.2).
/// Ein laufender ReviewInstance wechselt seinen Provider nicht (Anhang A, Regel 11).
/// </summary>
public sealed class ReviewInstance
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlatformTenantId { get; init; }
    public required Guid ReviewDefinitionId { get; init; }
    public required GovernanceProvider Provider { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<ReviewItem> Items { get; init; } = new();

    public bool IsOpen => CompletedAt is null;
}

/// <summary>
/// Einzelne Prüf-/Entscheidungseinheit innerhalb einer ReviewInstance. Deckt zwei Faelle ab
/// (Erweiterung 2026-08-31 "Discovery-Sichtbarkeit ueber Review"):
/// 1. Assignment-Review (AssignmentId gesetzt, ResourceAccessId null): klassischer Fall,
///    prueft eine bestehende GuestWorkloadAssignment (Desired State).
/// 2. Discovery-Review (ResourceAccessId gesetzt, AssignmentId null): ein per Discovery
///    entdeckter, noch Unclassified ResourceAccess (Actual State) OHNE zugehoerige formale
///    Zuweisung — macht Blueprint-12-Drift ("Nutzer ist tatsaechlich Mitglied einer Gruppe,
///    aber es existiert keine GuestWorkloadAssignment dafuer") ueber denselben Keep/Remove-
///    Mechanismus sichtbar/entscheidbar, OHNE automatisch eine Assignment zu erzeugen (siehe
///    ApplyReviewDecisionHandler: bei einem Discovery-Item aendert Keep/Remove NUR die
///    Classification des ResourceAccess, niemals eine Assignment oder den Directory-Zugriff
///    selbst — das bleibt einer bewussten "Gast zuweisen"-Aktion vorbehalten, Anhang A Regel 4:
///    Desired State und Actual State sind getrennt).
/// Genau eine der beiden IDs ist gesetzt — kein Enum/Discriminator noetig, da beide Felder
/// optional und exklusiv sind.
/// </summary>
public sealed class ReviewItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ReviewInstanceId { get; init; }
    public Guid? AssignmentId { get; init; }
    public Guid? ResourceAccessId { get; init; }
    public ReviewDecision Decision { get; set; } = ReviewDecision.Pending;
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Freier Erklärungstext, warum dieses Item zur Prüfung markiert wurde — z.B.
    /// vom Excel-Gäste-Import gesetzt, wenn sich Gast-Daten geändert haben und eine
    /// bestehende Zuweisung in einem ANDEREN Workload dadurch fachlich fragwürdig werden
    /// könnte (siehe GuestImportService), oder automatisch von StartReviewHandler fuer ein
    /// Discovery-Item gesetzt (z.B. "Mitglied von SecurityGroup:TEST, keine Workload-
    /// Zuweisung gefunden"). Optional, da regulär entstandene ReviewItems (z.B.
    /// turnusmäßige Access Reviews) keinen Grund brauchen.</summary>
    public string? Reason { get; set; }
}
