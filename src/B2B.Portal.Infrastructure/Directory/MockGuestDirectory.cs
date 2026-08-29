using B2B.Portal.Application.Ports;

namespace B2B.Portal.Infrastructure.Directory;

/// <summary>
/// Deterministischer Mock für IGuestDirectory (MVP-Dokument Abschnitt "Infrastructure
/// Provider"). Liefert immer dieselben Gäste/Gruppen für einen gegebenen directoryTenantId,
/// damit Tests reproduzierbar bleiben. Führt NIEMALS echte Schreibzugriffe aus.
/// </summary>
public sealed class MockGuestDirectory : IGuestDirectory
{
    private static readonly DirectoryGuestSnapshot[] SeedGuests =
    [
        new("mock-obj-anna", "anna@contoso.example", "Anna Contoso", "true"),
        new("mock-obj-peter", "peter@fabrikam.example", "Peter Fabrikam", "true"),
    ];

    // guestId (EntraObjectId) -> hat der Mock "Live" noch relevanten Zugriff?
    // Default: nein — kann in Tests über SimulatedLiveAccess gezielt gesteuert werden.
    public HashSet<string> SimulatedLiveAccess { get; } = new();
    public HashSet<string> SimulateConnectorErrorFor { get; } = new();

    public Task<IReadOnlyList<DirectoryGuestSnapshot>> ListGuestsAsync(string directoryTenantId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DirectoryGuestSnapshot>>(SeedGuests);

    public Task<IReadOnlyList<DirectoryGroupMembership>> ListMembershipsAsync(
        string directoryTenantId, string entraObjectId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DirectoryGroupMembership>>(
        [
            new DirectoryGroupMembership("mock-grp-reader", "SG-DEMO-READER", entraObjectId),
        ]);

    public Task<string> InviteGuestAsync(
        string directoryTenantId, string mail, string displayName, CancellationToken ct)
        => Task.FromResult($"mock-obj-{Guid.NewGuid():N}"[..20]);

    public Task ResendInvitationAsync(string directoryTenantId, string entraObjectId, CancellationToken ct)
        => Task.CompletedTask;

    public Task<bool> HasRelevantAccessAsync(string directoryTenantId, string entraObjectId, CancellationToken ct)
    {
        if (SimulateConnectorErrorFor.Contains(entraObjectId))
        {
            throw new InvalidOperationException($"Mock Connector Error für {entraObjectId} simuliert.");
        }

        return Task.FromResult(SimulatedLiveAccess.Contains(entraObjectId));
    }
}

/// <summary>
/// Mock-Ressourcen-Connector: bestätigt Grant/Revoke lokal, ohne Graph anzusprechen.
/// </summary>
public sealed class MockResourceConnector(string resourceType) : IResourceConnector
{
    public string ResourceType { get; } = resourceType;

    public Task GrantAccessAsync(
        string directoryTenantId, string entraObjectId, string resourceExternalId, CancellationToken ct)
        => Task.CompletedTask;

    public Task RevokeAccessAsync(
        string directoryTenantId, string entraObjectId, string resourceExternalId, CancellationToken ct)
        => Task.CompletedTask;

    public Task<string> CreateResourceAsync(
        string directoryTenantId, string namePattern, IReadOnlyDictionary<string, string> metadata, CancellationToken ct)
        => Task.FromResult($"mock-res-{Guid.NewGuid():N}"[..24]);
}
