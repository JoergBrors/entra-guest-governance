namespace B2B.Portal.Application.Ports;

/// <summary>Ergebnis einer Discovery-Abfrage für einen Directory Tenant.</summary>
public sealed record DirectoryGuestSnapshot(
    string EntraObjectId,
    string Mail,
    string DisplayName,
    string AccountEnabled);

public sealed record DirectoryGroupMembership(string GroupId, string GroupName, string EntraObjectId);

/// <summary>
/// Port für den Zugriff auf das externe Verzeichnis (Microsoft Entra via Graph).
/// Domain/Application kennen nur dieses Interface — keine direkten Graph-Aufrufe
/// (Blueprint 15 "Microsoft Graph Integration").
/// </summary>
public interface IGuestDirectory
{
    Task<IReadOnlyList<DirectoryGuestSnapshot>> ListGuestsAsync(string directoryTenantId, CancellationToken ct);

    Task<IReadOnlyList<DirectoryGroupMembership>> ListMembershipsAsync(
        string directoryTenantId, string entraObjectId, CancellationToken ct);

    /// <summary>
    /// Alle Entra-Object-IDs, die tatsaechlich Mitglied der gegebenen Gruppe/Ressource sind
    /// (Kehrseite von ListMembershipsAsync, das pro Gast abfragt) — Erweiterung 2026-08-31
    /// "Ist-Mitgliederzahl je Workload-Ressource": Grundlage fuer die Anzeige "N Mitglieder im
    /// Verzeichnis" neben den formalen GuestWorkloadAssignments, damit eine Diskrepanz wie
    /// "Gruppe hat 3 tatsaechliche Mitglieder, aber 0 Assignments" sichtbar wird, ohne
    /// automatisch etwas an Assignments zu aendern (siehe WorkloadManagementService.
    /// GetAssignmentCountsAsync-Kommentar und die Discovery-Review-Erweiterung).
    /// </summary>
    Task<IReadOnlyList<string>> ListGroupMemberObjectIdsAsync(
        string directoryTenantId, string groupExternalId, CancellationToken ct);

    Task<string> InviteGuestAsync(
        string directoryTenantId, string mail, string displayName, CancellationToken ct);

    Task ResendInvitationAsync(string directoryTenantId, string entraObjectId, CancellationToken ct);

    /// <summary>
    /// Live-Check unmittelbar vor Disable/Delete (Blueprint 14.4). Wirft, wenn der
    /// Connector nicht zuverlässig antworten kann — der Aufrufer MUSS das konservativ
    /// als Blocker behandeln, niemals als "kein Zugriff".
    /// </summary>
    Task<bool> HasRelevantAccessAsync(string directoryTenantId, string entraObjectId, CancellationToken ct);
}

/// <summary>
/// Port für Ressourcen-Connectoren (Groups, Enterprise Apps, Teams). Erweiterbar um
/// weitere Zielsysteme (Blueprint 4.1 "Connector Layer").
/// </summary>
public interface IResourceConnector
{
    string ResourceType { get; }

    Task GrantAccessAsync(
        string directoryTenantId, string entraObjectId, string resourceExternalId, CancellationToken ct);

    /// <summary>
    /// Entfernt ausschließlich die Member-Referenz, niemals das Benutzerobjekt
    /// (Blueprint 15.2 "Safety beim Entfernen von Gruppenmitgliedschaften").
    /// </summary>
    Task RevokeAccessAsync(
        string directoryTenantId, string entraObjectId, string resourceExternalId, CancellationToken ct);

    Task<string> CreateResourceAsync(
        string directoryTenantId, string namePattern, IReadOnlyDictionary<string, string> metadata, CancellationToken ct);
}
