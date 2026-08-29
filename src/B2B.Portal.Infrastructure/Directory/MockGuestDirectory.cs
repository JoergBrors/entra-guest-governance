using B2B.Portal.Application.Ports;

namespace B2B.Portal.Infrastructure.Directory;

public sealed record MockEntraUser(
    string ObjectId,
    string UserPrincipalName,
    string Mail,
    string DisplayName,
    string GivenName,
    string Surname,
    string CompanyName,
    string Department,
    string JobTitle,
    string Sponsor,
    string AccountEnabled,
    string UserType);

public sealed record MockEntraGroup(
    string ObjectId,
    string DisplayName,
    string MailNickname,
    string Description,
    string GroupType,
    bool SecurityEnabled,
    string? WorkloadName);

public sealed class MockEntraDirectoryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MockEntraUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MockEntraGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _membersByGroupId = new(StringComparer.OrdinalIgnoreCase);

    public MockEntraDirectoryStore()
    {
        SeedUser(new("mock-obj-anna", "anna_contoso.example#EXT#@platform.example", "anna@contoso.example",
            "Anna Contoso", "Anna", "Contoso", "Contoso Consulting", "Logistics",
            "External Consultant", "sponsor.mueller@platform.example", "true", "Guest"));
        SeedUser(new("mock-obj-peter", "peter_fabrikam.example#EXT#@platform.example", "peter@fabrikam.example",
            "Peter Fabrikam", "Peter", "Fabrikam", "Fabrikam Logistics", "Operations",
            "Supplier Manager", "sponsor.schmidt@platform.example", "true", "Guest"));
        SeedUser(new("mock-obj-lea", "lea_northwind.example#EXT#@platform.example", "lea@northwind.example",
            "Lea Northwind", "Lea", "Northwind", "Northwind Partners", "Finance",
            "Project Auditor", "sponsor.becker@platform.example", "true", "Guest"));

        SeedGroup(new("mock-grp-reader", "SG-DEMO-READER", "sg-demo-reader",
            "Mock security group for reader access.", "SecurityGroup", true, "Demo Workload"));
        SeedGroup(new("mock-grp-contributor", "SG-DEMO-CONTRIBUTOR", "sg-demo-contributor",
            "Mock security group for contributor access.", "SecurityGroup", true, "Demo Workload"));
        SeedGroup(new("mock-m365-collab", "M365-DEMO-COLLAB", "m365-demo-collab",
            "Mock Microsoft 365 collaboration group.", "M365Group", false, "Demo Workload"));

        AddMember("mock-grp-reader", "mock-obj-anna");
        AddMember("mock-grp-reader", "mock-obj-peter");
        AddMember("mock-grp-contributor", "mock-obj-peter");
        AddMember("mock-m365-collab", "mock-obj-lea");
    }

    public IReadOnlyList<MockEntraUser> ListUsers()
    {
        lock (_gate) return [.. _users.Values.OrderBy(u => u.Mail)];
    }

    public IReadOnlyList<MockEntraGroup> ListGroups()
    {
        lock (_gate) return [.. _groups.Values.OrderBy(g => g.DisplayName)];
    }

    public IReadOnlyList<DirectoryGroupMembership> ListAllMemberships()
    {
        lock (_gate)
        {
            return [.. _membersByGroupId
                .SelectMany(kv => kv.Value.Select(memberId =>
                    new DirectoryGroupMembership(kv.Key, _groups[kv.Key].DisplayName, memberId)))
                .OrderBy(m => m.GroupName)
                .ThenBy(m => m.EntraObjectId)];
        }
    }

    public IReadOnlyList<DirectoryGroupMembership> ListMemberships(string entraObjectId)
    {
        lock (_gate)
        {
            return [.. _membersByGroupId
                .Where(kv => kv.Value.Contains(entraObjectId))
                .Select(kv => new DirectoryGroupMembership(kv.Key, _groups[kv.Key].DisplayName, entraObjectId))];
        }
    }

    public string UpsertInvitedGuest(string mail, string displayName)
    {
        lock (_gate)
        {
            var existing = _users.Values.FirstOrDefault(u => string.Equals(u.Mail, mail, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing.ObjectId;

            var objectId = $"mock-obj-{Guid.NewGuid():N}"[..20];
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            SeedUser(new(
                objectId,
                $"{mail.Replace("@", "_", StringComparison.OrdinalIgnoreCase)}#EXT#@platform.example",
                mail,
                displayName,
                parts.FirstOrDefault() ?? displayName,
                parts.Skip(1).FirstOrDefault() ?? string.Empty,
                "configuration required",
                "configuration required",
                "Guest",
                "configuration required",
                "true",
                "Guest"));
            return objectId;
        }
    }

    public string EnsureGroup(string resourceType, string namePattern, IReadOnlyDictionary<string, string> metadata)
    {
        lock (_gate)
        {
            var existing = _groups.Values.FirstOrDefault(g => string.Equals(g.DisplayName, namePattern, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing.ObjectId;

            var objectId = $"mock-grp-{Guid.NewGuid():N}"[..24];
            SeedGroup(new(
                objectId,
                namePattern,
                ToMailNickname(namePattern),
                metadata.TryGetValue("ScenarioId", out var scenarioId) ? $"Scenario {scenarioId}" : "Created by LOCAL_MOCK worker.",
                resourceType,
                resourceType.Equals("SecurityGroup", StringComparison.OrdinalIgnoreCase),
                metadata.TryGetValue("WorkloadName", out var workloadName) ? workloadName : null));
            return objectId;
        }
    }

    public void AddMember(string groupIdOrDisplayName, string entraObjectId)
    {
        lock (_gate)
        {
            var group = ResolveGroup(groupIdOrDisplayName) ?? new MockEntraGroup(
                groupIdOrDisplayName,
                groupIdOrDisplayName,
                ToMailNickname(groupIdOrDisplayName),
                "Created by LOCAL_MOCK assignment.",
                "SecurityGroup",
                true,
                null);
            if (!_groups.ContainsKey(group.ObjectId))
            {
                SeedGroup(group);
            }
            if (!_users.ContainsKey(entraObjectId)) return;
            if (!_membersByGroupId.TryGetValue(group.ObjectId, out var members))
            {
                members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _membersByGroupId[group.ObjectId] = members;
            }
            members.Add(entraObjectId);
        }
    }

    public void RemoveMember(string groupIdOrDisplayName, string entraObjectId)
    {
        lock (_gate)
        {
            var group = ResolveGroup(groupIdOrDisplayName);
            if (group is not null && _membersByGroupId.TryGetValue(group.ObjectId, out var members))
            {
                members.Remove(entraObjectId);
            }
        }
    }

    public bool HasMembership(string entraObjectId) =>
        ListMemberships(entraObjectId).Count > 0;

    private void SeedUser(MockEntraUser user) => _users[user.ObjectId] = user;

    private void SeedGroup(MockEntraGroup group)
    {
        _groups[group.ObjectId] = group;
        _membersByGroupId.TryAdd(group.ObjectId, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private MockEntraGroup? ResolveGroup(string groupIdOrDisplayName) =>
        _groups.TryGetValue(groupIdOrDisplayName, out var byId)
            ? byId
            : _groups.Values.FirstOrDefault(g => string.Equals(g.DisplayName, groupIdOrDisplayName, StringComparison.OrdinalIgnoreCase));

    private static string ToMailNickname(string value) =>
        new(value.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
}

/// <summary>
/// Deterministischer Mock für IGuestDirectory (MVP-Dokument Abschnitt "Infrastructure
/// Provider"). Liefert immer dieselben Gäste/Gruppen für einen gegebenen directoryTenantId,
/// damit Tests reproduzierbar bleiben. Führt NIEMALS echte Schreibzugriffe aus.
/// </summary>
public sealed class MockGuestDirectory(MockEntraDirectoryStore store) : IGuestDirectory
{
    // guestId (EntraObjectId) -> hat der Mock "Live" noch relevanten Zugriff?
    // Default: nein — kann in Tests über SimulatedLiveAccess gezielt gesteuert werden.
    public HashSet<string> SimulatedLiveAccess { get; } = new();
    public HashSet<string> SimulateConnectorErrorFor { get; } = new();

    public Task<IReadOnlyList<DirectoryGuestSnapshot>> ListGuestsAsync(string directoryTenantId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DirectoryGuestSnapshot>>(
            [.. store.ListUsers().Select(u => new DirectoryGuestSnapshot(u.ObjectId, u.Mail, u.DisplayName, u.AccountEnabled))]);

    public Task<IReadOnlyList<DirectoryGroupMembership>> ListMembershipsAsync(
        string directoryTenantId, string entraObjectId, CancellationToken ct)
        => Task.FromResult(store.ListMemberships(entraObjectId));

    public Task<string> InviteGuestAsync(
        string directoryTenantId, string mail, string displayName, CancellationToken ct)
        => Task.FromResult(store.UpsertInvitedGuest(mail, displayName));

    public Task ResendInvitationAsync(string directoryTenantId, string entraObjectId, CancellationToken ct)
        => Task.CompletedTask;

    public Task<bool> HasRelevantAccessAsync(string directoryTenantId, string entraObjectId, CancellationToken ct)
    {
        if (SimulateConnectorErrorFor.Contains(entraObjectId))
        {
            throw new InvalidOperationException($"Mock Connector Error für {entraObjectId} simuliert.");
        }

        return Task.FromResult(SimulatedLiveAccess.Contains(entraObjectId) || store.HasMembership(entraObjectId));
    }
}

/// <summary>
/// Mock-Ressourcen-Connector: bestätigt Grant/Revoke lokal, ohne Graph anzusprechen.
/// </summary>
public sealed class MockResourceConnector(string resourceType, MockEntraDirectoryStore store) : IResourceConnector
{
    public string ResourceType { get; } = resourceType;

    public Task GrantAccessAsync(
        string directoryTenantId, string entraObjectId, string resourceExternalId, CancellationToken ct)
    {
        store.AddMember(resourceExternalId, entraObjectId);
        return Task.CompletedTask;
    }

    public Task RevokeAccessAsync(
        string directoryTenantId, string entraObjectId, string resourceExternalId, CancellationToken ct)
    {
        store.RemoveMember(resourceExternalId, entraObjectId);
        return Task.CompletedTask;
    }

    public Task<string> CreateResourceAsync(
        string directoryTenantId, string namePattern, IReadOnlyDictionary<string, string> metadata, CancellationToken ct)
        => Task.FromResult(store.EnsureGroup(ResourceType, namePattern, metadata));
}
