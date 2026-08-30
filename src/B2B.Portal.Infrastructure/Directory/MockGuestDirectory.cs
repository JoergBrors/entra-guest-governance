using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;

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
    string UserType,
    IReadOnlyList<string> PortalRoles,
    DateTimeOffset? LastLoginAt = null,
    // Tenant-Claim-Bindung fuer den EntraIdMock-Login (Auth/MockJwtIssuer.cs): der Tenant
    // wird beim Login aus dem gewaehlten Mock-User abgeleitet, nicht separat gewaehlt.
    // Default "dev-tenant-a" haelt bestehende Seeds/Scripts konsistent (siehe
    // scripts/seed-dev-data.ps1, scripts/seed-large-workload.ps1).
    string PlatformTenantId = "dev-tenant-a");

public sealed record MockEntraGroup(
    string ObjectId,
    string DisplayName,
    string MailNickname,
    string Description,
    IReadOnlyList<string> GroupTypes,
    bool MailEnabled,
    bool SecurityEnabled,
    IReadOnlyList<string> ResourceProvisioningOptions);

public sealed record MockEntraApplicationRole(string Id, string Value, string DisplayName, string Description);

public sealed record MockEntraApplication(
    string ObjectId,
    string AppId,
    string DisplayName,
    IReadOnlyList<MockEntraApplicationRole> AppRoles);

public sealed record MockEntraApplicationSignIn(
    string Id,
    string AppId,
    string EntraObjectId,
    DateTimeOffset LastLoginAt);

public sealed class MockEntraDirectoryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MockEntraUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MockEntraGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MockEntraApplication> _applications = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MockEntraApplicationSignIn> _applicationSignIns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _membersByGroupId = new(StringComparer.OrdinalIgnoreCase);

    // Optional, damit bestehende Tests/Call-Sites ohne Cosmos weiterhin einen reinen
    // In-Memory-Store bekommen (siehe MockEntraDirectoryStoreTests). In der API ist dies
    // immer gesetzt (Erweiterung 2026-08-30 (Teil 3): Persistenz der PortalRoles, siehe
    // IMockEntraUserRepository) — UpsertUser schreibt dann fire-and-forget durch, und
    // HydrateFromRepositoryAsync laedt beim API-Start alle bekannten Benutzer nach, damit
    // POST /api/auth/mock/login sofort nach dotnet run funktioniert (vorher: leerer Store
    // nach jedem Prozess-Neustart, kein Login moeglich, siehe docs/development/local-mock.md).
    private readonly IMockEntraUserRepository? _repository;

    public MockEntraDirectoryStore() : this(null)
    {
    }

    public MockEntraDirectoryStore(IMockEntraUserRepository? repository)
    {
        _repository = repository;
        SeedUser(new("mock-obj-anna", "anna_contoso.example#EXT#@platform.example", "anna@contoso.example",
            "Anna Contoso", "Anna", "Contoso", "Contoso Consulting", "Logistics",
            "External Consultant", "sponsor.mueller@platform.example", "true", "Guest", ["User"]));
        SeedUser(new("mock-obj-peter", "peter_fabrikam.example#EXT#@platform.example", "peter@fabrikam.example",
            "Peter Fabrikam", "Peter", "Fabrikam", "Fabrikam Logistics", "Operations",
            "Supplier Manager", "sponsor.schmidt@platform.example", "true", "Guest", ["User"]));
        SeedUser(new("mock-obj-lea", "lea_northwind.example#EXT#@platform.example", "lea@northwind.example",
            "Lea Northwind", "Lea", "Northwind", "Northwind Partners", "Finance",
            "Project Auditor", "sponsor.becker@platform.example", "true", "Guest", ["User", "Reviewer"]));
        SeedUser(new("mock-member-admin", "admin@platform.example", "admin@platform.example",
            "Platform Admin", "Platform", "Admin", "Platform", "IT",
            "Governance Administrator", "configuration required", "true", "Member", ["GovernanceAdmin", "User", "Reviewer"]));
        SeedUser(new("mock-member-owner", "workload-owner@platform.example", "workload-owner@platform.example",
            "Workload Owner", "Workload", "Owner", "Platform", "Business",
            "Workload Owner", "configuration required", "true", "Member", ["WorkloadOwner", "User"]));

        SeedGroup(BuildGroup("mock-grp-reader", "SG-DEMO-READER", "SecurityGroup",
            "Mock security group for reader access."));
        SeedGroup(BuildGroup("mock-grp-contributor", "SG-DEMO-CONTRIBUTOR", "SecurityGroup",
            "Mock security group for contributor access."));
        SeedGroup(BuildGroup("mock-m365-collab", "M365-DEMO-COLLAB", "M365Group",
            "Mock Microsoft 365 collaboration group."));
        SeedApplication(new(
            "mock-app-meridian",
            "app-meridian-governance",
            "Meridian Governance App",
            [
                new("app-role-reader", "Reader", "Reader", "Read access in the Meridian app."),
                new("app-role-contributor", "Contributor", "Contributor", "Write access in the Meridian app."),
                new("app-role-admin", "ProjectAdmin", "Project Admin", "Administrative access in the Meridian app."),
            ]));

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

    public IReadOnlyList<MockEntraApplication> ListApplications()
    {
        lock (_gate) return [.. _applications.Values.OrderBy(a => a.DisplayName)];
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

    public MockEntraUser UpsertUser(MockEntraUser user)
    {
        lock (_gate)
        {
            var objectId = string.IsNullOrWhiteSpace(user.ObjectId)
                ? $"mock-obj-{Guid.NewGuid():N}"[..20]
                : user.ObjectId;
            var existingRoles = _users.TryGetValue(objectId, out var existing)
                ? existing.PortalRoles
                : Array.Empty<string>();
            var preserveExistingRoles = existingRoles.Count > 0
                && user.PortalRoles.Count == 1
                && user.PortalRoles.Contains("User", StringComparer.OrdinalIgnoreCase);
            var normalized = user with
            {
                ObjectId = objectId,
                UserPrincipalName = string.IsNullOrWhiteSpace(user.UserPrincipalName)
                    ? $"{user.Mail.Replace("@", "_", StringComparison.OrdinalIgnoreCase)}#EXT#@platform.example"
                    : user.UserPrincipalName,
                AccountEnabled = string.IsNullOrWhiteSpace(user.AccountEnabled) ? "true" : user.AccountEnabled,
            UserType = string.IsNullOrWhiteSpace(user.UserType) ? "Guest" : user.UserType,
            PortalRoles = preserveExistingRoles ? existingRoles : user.PortalRoles.Count == 0 ? ["User"] : user.PortalRoles,
            LastLoginAt = user.LastLoginAt ?? existing?.LastLoginAt,
            PlatformTenantId = string.IsNullOrWhiteSpace(user.PlatformTenantId)
                ? existing?.PlatformTenantId ?? "dev-tenant-a"
                : user.PlatformTenantId,
        };
            SeedUser(normalized);
            PersistUser(normalized);
            return normalized;
        }
    }

    /// <summary>
    /// Laedt beim API-Start alle in Cosmos bekannten Mock-Entra-Benutzer (inkl. persistierter
    /// PortalRoles) in den In-Memory-Store, damit POST /api/auth/mock/login sofort nach
    /// Prozessstart funktioniert — vorher war der Store bis zum ersten (Anonymous-)Aufruf von
    /// GET /api/dev/mock-entra/login-users leer, und DIESER Endpoint hydrierte nur Tenants,
    /// die im (leeren) Store bereits bekannt waren: ein Henne-Ei-Problem nach jedem Reset/
    /// Neustart (siehe docs/development/local-mock.md). Ergaenzt, ueberschreibt aber nicht die
    /// hart codierten Default-Demo-User aus dem Konstruktor, falls Cosmos (noch) leer ist.
    /// </summary>
    public async Task HydrateFromRepositoryAsync(CancellationToken ct)
    {
        if (_repository is null)
        {
            return;
        }

        var persisted = await _repository.ListAllAsync(ct);
        lock (_gate)
        {
            foreach (var user in persisted)
            {
                SeedUser(new MockEntraUser(
                    user.ObjectId, user.UserPrincipalName, user.Mail, user.DisplayName, user.GivenName,
                    user.Surname, user.CompanyName, user.Department, user.JobTitle, user.Sponsor,
                    user.AccountEnabled, user.UserType, user.PortalRoles, user.LastLoginAt, user.PlatformTenantId));
            }
        }
    }

    private void PersistUser(MockEntraUser user)
    {
        if (_repository is null)
        {
            return;
        }

        var record = new MockEntraUserRecord(
            user.ObjectId, user.UserPrincipalName, user.Mail, user.DisplayName, user.GivenName,
            user.Surname, user.CompanyName, user.Department, user.JobTitle, user.Sponsor,
            user.AccountEnabled, user.UserType, user.PortalRoles, user.PlatformTenantId, user.LastLoginAt);

        // Fire-and-forget: UpsertUser wird auch aus synchronen Minimal-API-Handlern
        // (POST/PUT /api/dev/mock-entra/users) und aus HydrateMockEntraFromRepositoriesAsync
        // (pro Gast in einer Schleife) aufgerufen — ein await hier wuerde UpsertUser async
        // machen und alle Call-Sites in Program.cs aendern. Verlorene Schreibversuche bei
        // einem Absturz zwischen Login und Persistierung sind fuer LOCAL_MOCK-Devdaten
        // akzeptabel; Fehler werden geloggt statt verschluckt.
        _ = _repository.UpsertAsync(record, CancellationToken.None)
            .ContinueWith(t =>
            {
                if (t.Exception is not null)
                {
                    Console.WriteLine(
                        $"[MockEntraDirectoryStore] WARNUNG: Persistieren von {user.Mail} nach Cosmos fehlgeschlagen: " +
                        $"{t.Exception.GetBaseException().Message}");
                }
            }, TaskScheduler.Default);
    }

    public bool DeleteUser(string objectId)
    {
        lock (_gate)
        {
            if (!_users.Remove(objectId))
            {
                return false;
            }
            foreach (var members in _membersByGroupId.Values)
            {
                members.Remove(objectId);
            }
            return true;
        }
    }

    public MockEntraGroup UpsertGroup(MockEntraGroup group)
    {
        lock (_gate)
        {
            var objectId = string.IsNullOrWhiteSpace(group.ObjectId)
                ? $"mock-grp-{Guid.NewGuid():N}"[..24]
                : group.ObjectId;
            var normalized = group with
            {
                ObjectId = objectId,
                MailNickname = string.IsNullOrWhiteSpace(group.MailNickname)
                    ? ToMailNickname(group.DisplayName)
                    : group.MailNickname,
                GroupTypes = group.GroupTypes ?? [],
                ResourceProvisioningOptions = group.ResourceProvisioningOptions ?? [],
            };
            SeedGroup(normalized);
            return normalized;
        }
    }

    public bool DeleteGroup(string objectId)
    {
        lock (_gate)
        {
            _membersByGroupId.Remove(objectId);
            return _groups.Remove(objectId);
        }
    }

    public MockEntraApplication UpsertApplication(MockEntraApplication application)
    {
        lock (_gate)
        {
            var objectId = string.IsNullOrWhiteSpace(application.ObjectId)
                ? $"mock-app-{Guid.NewGuid():N}"[..24]
                : application.ObjectId;
            var normalized = application with
            {
                ObjectId = objectId,
                AppId = string.IsNullOrWhiteSpace(application.AppId)
                    ? $"app-{Guid.NewGuid():N}"
                    : application.AppId,
                AppRoles = application.AppRoles ?? [],
            };
            SeedApplication(normalized);
            return normalized;
        }
    }

    public bool DeleteApplication(string objectId)
    {
        lock (_gate) return _applications.Remove(objectId);
    }

    public IReadOnlyList<MockEntraApplicationSignIn> ListApplicationSignIns(string? appId = null)
    {
        lock (_gate)
        {
            var query = _applicationSignIns.Values.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(appId))
            {
                query = query.Where(s => string.Equals(s.AppId, appId, StringComparison.OrdinalIgnoreCase));
            }
            return [.. query.OrderByDescending(s => s.LastLoginAt)];
        }
    }

    public MockEntraApplicationSignIn UpsertApplicationSignIn(string appId, string entraObjectId, DateTimeOffset lastLoginAt)
    {
        lock (_gate)
        {
            var id = $"{appId}:{entraObjectId}";
            var signIn = new MockEntraApplicationSignIn(id, appId, entraObjectId, lastLoginAt);
            _applicationSignIns[id] = signIn;
            return signIn;
        }
    }

    public void UpsertGuestAccount(GuestAccount guest)
    {
        if (string.IsNullOrWhiteSpace(guest.EntraObjectId))
        {
            return;
        }

        var parts = guest.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        UpsertUser(new(
            guest.EntraObjectId,
            $"{guest.Mail.Replace("@", "_", StringComparison.OrdinalIgnoreCase)}#EXT#@platform.example",
            guest.Mail,
            guest.DisplayName,
            parts.FirstOrDefault() ?? guest.DisplayName,
            parts.Skip(1).FirstOrDefault() ?? string.Empty,
            "configuration required",
            "configuration required",
            "Guest",
            guest.Sponsor ?? "configuration required",
            "true",
            guest.UserType,
            guest.UserType.Equals("Member", StringComparison.OrdinalIgnoreCase) ? ["User"] : ["User"],
            DateTimeOffset.UtcNow.AddDays(-Math.Abs(guest.Id.GetHashCode() % 45))));
    }

    public string UpsertInvitedGuest(string mail, string displayName)
    {
        lock (_gate)
        {
            var existing = _users.Values.FirstOrDefault(u => string.Equals(u.Mail, mail, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing.ObjectId;

            var objectId = $"mock-obj-{Guid.NewGuid():N}"[..20];
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var user = UpsertUser(new(
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
                "Guest",
                ["User"]));
            return user.ObjectId;
        }
    }

    public string EnsureGroup(string resourceType, string namePattern, IReadOnlyDictionary<string, string> metadata)
    {
        lock (_gate)
        {
            var existing = _groups.Values.FirstOrDefault(g => string.Equals(g.DisplayName, namePattern, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing.ObjectId;

            var objectId = $"mock-grp-{Guid.NewGuid():N}"[..24];
            SeedGroup(BuildGroup(
                objectId,
                namePattern,
                resourceType,
                metadata.TryGetValue("ScenarioId", out var scenarioId) ? $"Scenario {scenarioId}" : "Created by LOCAL_MOCK worker."));
            return objectId;
        }
    }

    public void AddMember(string groupIdOrDisplayName, string entraObjectId)
    {
        lock (_gate)
        {
            var group = ResolveGroup(groupIdOrDisplayName) ?? BuildGroup(
                groupIdOrDisplayName,
                groupIdOrDisplayName,
                "SecurityGroup",
                "Created by LOCAL_MOCK assignment.");
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

    public int RemoveAllMembers(string groupIdOrDisplayName)
    {
        lock (_gate)
        {
            var group = ResolveGroup(groupIdOrDisplayName);
            if (group is null || !_membersByGroupId.TryGetValue(group.ObjectId, out var members))
            {
                return 0;
            }
            var count = members.Count;
            members.Clear();
            return count;
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

    private void SeedApplication(MockEntraApplication application)
    {
        _applications[application.ObjectId] = application;
    }

    private MockEntraGroup? ResolveGroup(string groupIdOrDisplayName) =>
        _groups.TryGetValue(groupIdOrDisplayName, out var byId)
            ? byId
            : _groups.Values.FirstOrDefault(g => string.Equals(g.DisplayName, groupIdOrDisplayName, StringComparison.OrdinalIgnoreCase));

    private static MockEntraGroup BuildGroup(string objectId, string displayName, string resourceType, string description)
    {
        var isUnified = resourceType.Equals("M365Group", StringComparison.OrdinalIgnoreCase)
            || resourceType.Equals("Team", StringComparison.OrdinalIgnoreCase);
        return new(
            objectId,
            displayName,
            ToMailNickname(displayName),
            description,
            isUnified ? ["Unified"] : [],
            isUnified,
            !isUnified,
            resourceType.Equals("Team", StringComparison.OrdinalIgnoreCase) ? ["Team"] : []);
    }

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
            [.. store.ListUsers()
                // Discovery bildet den Guest Pool ab — nur externe B2B-Gaeste (UserType
                // "Guest"), keine internen Mitglieder/Admins (z.B. admin@platform.example,
                // workload-owner@platform.example), die im Mock-Stamm ebenfalls als
                // MockEntraUser existieren, aber niemals externe Gaeste sind.
                .Where(u => string.Equals(u.UserType, "Guest", StringComparison.OrdinalIgnoreCase))
                .Select(u => new DirectoryGuestSnapshot(u.ObjectId, u.Mail, u.DisplayName, u.AccountEnabled))]);

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
