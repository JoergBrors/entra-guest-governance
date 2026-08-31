namespace B2B.Portal.Domain.Entities;

/// <summary>
/// Eine Stufe einer Erinnerungs-Policy fuer offene Einladungen (Erweiterung 2026-08-30
/// "Invitation Reminder Worker"). Template-Felder liegen bewusst direkt auf der Stufe statt
/// in einer separaten Template-Entitaet — die einfachste Ausbaustufe, die "voll
/// admin-konfigurierbar" erfuellt, ohne eine eigene Template-Verwaltung zu erfinden.
/// </summary>
public sealed class ReminderStage
{
    /// <summary>1-basierte Reihenfolge/Stufennummer — sowohl Sortierschluessel als auch der
    /// Wert, der in GuestAccount.LastReminderStageSent landet.</summary>
    public required int StageNumber { get; set; }

    /// <summary>Schwelle in Tagen seit Einladung (GuestAccount.CreatedAt), ab der diese Stufe
    /// faellig wird.</summary>
    public required int DaysAfterInvite { get; set; }

    public required string TemplateId { get; set; }
    public required string TemplateSubject { get; set; }
    public required string TemplateBody { get; set; }
}

/// <summary>
/// Genau eine aktive, geordnete Stufenliste pro PlatformTenantId (kein Mehrfach-Policy-Modell
/// noetig, siehe Aufgabenstellung). Persistiert ueber ICosmos-Repository im "discovery"-
/// Container, analog zu MockEntraUser (siehe CosmosReminderPolicyRepository).
/// </summary>
public sealed class ReminderPolicy
{
    public required string PlatformTenantId { get; init; }
    public List<ReminderStage> Stages { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
