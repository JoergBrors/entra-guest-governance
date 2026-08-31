using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

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

    // Ergaenzung 2026-08-31: analog zu _repository, aber fuer Gruppen/Mitgliedschaften/
    // Anwendungen/App-Sign-ins — vorher lebten diese ausschliesslich im In-Memory-Singleton
    // und gingen bei jedem Prozessneustart (API wie Worker) verloren, was z.B. dazu fuehrte,
    // dass eine manuell angelegte oder ueber einen Workload provisionierte Mock-Gruppe nach
    // einem Neustart spurlos verschwand, obwohl ein Workload sie weiterhin referenzierte.
    private readonly IMockEntraDirectoryRepository? _directoryRepository;

    public MockEntraDirectoryStore() : this(null, null)
    {
    }

    public MockEntraDirectoryStore(IMockEntraUserRepository? repository)
        : this(repository, null)
    {
    }

    public MockEntraDirectoryStore(IMockEntraUserRepository? repository, IMockEntraDirectoryRepository? directoryRepository)
    {
        _repository = repository;
        _directoryRepository = directoryRepository;
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

    /// <summary>
    /// Alle PlatformTenantIds, die im Mock-Stamm bekannt sind (Erweiterung 2026-08-30 (Teil 3)
    /// "Multi-Tenant-Scanner"). Grundlage fuer periodische BackgroundServices
    /// (InvitationReminderWorker, ApplicationSignInSyncWorker), die vorher nur einen einzigen,
    /// aus der Frontend-Env-Variable VITE_DEV_PLATFORM_TENANT_ID gelesenen Tenant scannten —
    /// bei mehreren Platform-Tenants blieben alle ausser dem ersten unbeachtet. Der Mock-Stamm
    /// wird beim API-Start bereits vollstaendig aus Cosmos hydriert (siehe
    /// HydrateFromRepositoryAsync), ist also eine verlaessliche Quelle "welche Tenants
    /// existieren ueberhaupt", ohne dass die Repository-Ports selbst eine
    /// Tenant-uebergreifende Query anbieten muessten (die sind bewusst alle TenantContext-
    /// pflichtig, siehe CorePorts.cs — Tenant-Isolation by Design).
    /// </summary>
    public IReadOnlyList<string> ListKnownPlatformTenantIds()
    {
        lock (_gate)
            return [.. _users.Values.Select(u => u.PlatformTenantId).Distinct(StringComparer.OrdinalIgnoreCase)];
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
            // Bug (gefunden live: Owner-Dropdown in WorkloadsAdminPage blieb leer, weil
            // admin@platform.example/workload-owner@platform.example als UserType "Guest"
            // statt "Member" im Store landeten): HydrateMockEntraFromRepositoriesAsync ruft
            // bei praktisch jedem Request UpsertGuestAccount(guest) fuer ALLE GuestAccounts
            // auf, mit GuestAccount.UserType als Default "Guest" (siehe GuestAccount.cs). Ein
            // bereits im Store als "Member" bekannter User (aus BuildPlatformMembers oder der
            // reset-cosmos-dev-data.ps1-Bootstrap-Zeile) wurde dadurch bei der naechsten
            // Hydration stumpf auf "Guest" zurueckgesetzt — analog zur bereits bestehenden
            // PortalRoles-Preserve-Logik oben wird "Member" daher nicht mehr von "Guest"
            // ueberschrieben, wohl aber umgekehrt (ein echter Rollenwechsel Member->Guest ueber
            // die Mock-Entra-Admin-UI bleibt moeglich, da dort ein expliziter UserType kommt,
            // der hier nicht "Guest" waere, wenn er es nicht sein soll).
            var preserveExistingUserType = existing is not null
                && existing.UserType.Equals("Member", StringComparison.OrdinalIgnoreCase)
                && user.UserType.Equals("Guest", StringComparison.OrdinalIgnoreCase);
            var normalized = user with
            {
                ObjectId = objectId,
                UserPrincipalName = string.IsNullOrWhiteSpace(user.UserPrincipalName)
                    ? $"{user.Mail.Replace("@", "_", StringComparison.OrdinalIgnoreCase)}#EXT#@platform.example"
                    : user.UserPrincipalName,
                AccountEnabled = string.IsNullOrWhiteSpace(user.AccountEnabled) ? "true" : user.AccountEnabled,
            UserType = preserveExistingUserType
                ? existing!.UserType
                : string.IsNullOrWhiteSpace(user.UserType) ? "Guest" : user.UserType,
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
        if (_repository is not null)
        {
            var persistedUsers = await _repository.ListAllAsync(ct);
            lock (_gate)
            {
                foreach (var user in persistedUsers)
                {
                    SeedUser(new MockEntraUser(
                        user.ObjectId, user.UserPrincipalName, user.Mail, user.DisplayName, user.GivenName,
                        user.Surname, user.CompanyName, user.Department, user.JobTitle, user.Sponsor,
                        user.AccountEnabled, user.UserType, user.PortalRoles, user.LastLoginAt, user.PlatformTenantId));
                }
            }
        }

        if (_directoryRepository is null)
        {
            return;
        }

        var persistedGroups = await _directoryRepository.ListGroupsAsync(ct);
        var persistedMemberships = await _directoryRepository.ListMembershipsAsync(ct);
        var persistedApplications = await _directoryRepository.ListApplicationsAsync(ct);
        var persistedSignIns = await _directoryRepository.ListApplicationSignInsAsync(ct);
        lock (_gate)
        {
            foreach (var group in persistedGroups)
            {
                SeedGroup(new MockEntraGroup(
                    group.ObjectId, group.DisplayName, group.MailNickname, group.Description,
                    group.GroupTypes, group.MailEnabled, group.SecurityEnabled, group.ResourceProvisioningOptions));
            }

            foreach (var membership in persistedMemberships)
            {
                if (!_membersByGroupId.TryGetValue(membership.GroupId, out var members))
                {
                    members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _membersByGroupId[membership.GroupId] = members;
                }
                members.Add(membership.EntraObjectId);
            }

            foreach (var application in persistedApplications)
            {
                SeedApplication(new MockEntraApplication(
                    application.ObjectId, application.AppId, application.DisplayName,
                    [.. application.AppRoles.Select(r => new MockEntraApplicationRole(r.Id, r.Value, r.DisplayName, r.Description))]));
            }

            foreach (var signIn in persistedSignIns)
            {
                _applicationSignIns[$"{signIn.AppId}:{signIn.EntraObjectId}"] = new MockEntraApplicationSignIn(
                    $"{signIn.AppId}:{signIn.EntraObjectId}", signIn.AppId, signIn.EntraObjectId, signIn.LastLoginAt);
            }
        }
    }

    /// <summary>
    /// Gleicht den Ist-Zustand des Mock-Entra-Verzeichnisses (dieser Store, hydriert aus dem
    /// dedizierten Cosmos-Container "entraid", siehe HydrateFromRepositoryAsync) gegen den
    /// Soll-Zustand der Workloads eines Tenants ab (Desired State, Container "domain") und
    /// meldet Abweichungen nur — schreibt sie NICHT automatisch zurueck.
    ///
    /// Vor Erweiterung 2026-08-31 ("EntraId-Persistenz + Discovery-Reconciliation") war diese
    /// Methode ein Reparatur-Mechanismus: sie legte fehlende Gruppen/Mitgliedschaften aus
    /// WorkloadResource-Eintraegen im In-Memory-Store neu an (EnsureGroupByObjectId/AddMember),
    /// weil Gruppen damals nur fire-and-forget nach Cosmos geschrieben wurden und bei einem
    /// Prozessabsturz zwischen In-Memory-Update und Persistierung verloren gehen konnten. Mit
    /// dem eigenen "entraid"-Container und HydrateFromRepositoryAsync als vollstaendiger,
    /// garantierter Quelle beim Prozessstart ist dieser Reparaturbedarf strukturell nicht mehr
    /// gegeben — eine WorkloadResource, die auf eine im Verzeichnis fehlende ObjectId zeigt,
    /// ist jetzt ein echtes Datenproblem (z.B. die Gruppe wurde im Mock-Entra-Stamm geloescht,
    /// waehrend der Workload sie noch referenziert), kein Timing-Artefakt — daher nur Logging,
    /// analog zu einem klassischen Discovery/Reconciliation-Lauf (Ist vs. Soll, keine
    /// automatische Selbstheilung).
    /// </summary>
    public async Task<int> ReconcileWorkloadResourcesAsync(
        TenantContext tenant,
        IWorkloadRepository workloadRepo,
        ILogger? logger,
        CancellationToken ct)
    {
        var workloads = await workloadRepo.ListAsync(tenant, ct);
        var missingCount = 0;
        foreach (var workload in workloads)
        {
            foreach (var resource in workload.Resources.Where(r => IsMockEntraGroupResource(r) && !string.IsNullOrWhiteSpace(r.ExternalId)))
            {
                bool known;
                lock (_gate)
                {
                    known = _groups.ContainsKey(resource.ExternalId!);
                }
                if (!known)
                {
                    missingCount++;
                    logger?.LogWarning(
                        "Discovery-Reconciliation: Workload {WorkloadId} ({WorkloadName}) referenziert Ressource " +
                        "{ResourceType}:{DisplayName} (ObjectId {ExternalId}), die im Mock-Entra-Verzeichnis " +
                        "(Container 'entraid') nicht mehr existiert.",
                        workload.Id, workload.Name, resource.ResourceType, resource.DisplayName, resource.ExternalId);
                }
            }
        }
        return missingCount;
    }

    public static bool IsMockEntraGroupResource(WorkloadResource resource) =>
        resource.ResourceType.Equals("SecurityGroup", StringComparison.OrdinalIgnoreCase)
        || resource.ResourceType.Equals("M365Group", StringComparison.OrdinalIgnoreCase)
        || resource.ResourceType.Equals("Team", StringComparison.OrdinalIgnoreCase);

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
        RunFireAndForget(
            () => _repository.UpsertAsync(record, CancellationToken.None),
            $"Persistieren von {user.Mail}");
    }

    private void PersistGroup(MockEntraGroup? group)
    {
        if (_directoryRepository is null || group is null)
        {
            return;
        }

        var record = new MockEntraGroupRecord(
            group.ObjectId, group.DisplayName, group.MailNickname, group.Description,
            group.GroupTypes, group.MailEnabled, group.SecurityEnabled, group.ResourceProvisioningOptions);
        RunFireAndForget(
            () => _directoryRepository.UpsertGroupAsync(record, CancellationToken.None),
            $"Persistieren von Gruppe {group.DisplayName}");
    }

    private void PersistApplication(MockEntraApplication application)
    {
        if (_directoryRepository is null)
        {
            return;
        }

        var record = new MockEntraApplicationRecord(
            application.ObjectId, application.AppId, application.DisplayName,
            [.. application.AppRoles.Select(r => new MockEntraApplicationRoleRecord(r.Id, r.Value, r.DisplayName, r.Description))]);
        RunFireAndForget(
            () => _directoryRepository.UpsertApplicationAsync(record, CancellationToken.None),
            $"Persistieren von Anwendung {application.DisplayName}");
    }

    // Fire-and-forget-Wrapper fuer alle Cosmos-Schreibzugriffe dieser Klasse: Upsert*/Add*/
    // Remove*-Methoden sind bewusst synchron (werden auch aus synchronen Minimal-API-Handlern
    // und aus Schleifen in HydrateFromWorkloadsAndGuestsAsync aufgerufen, siehe PersistUser-
    // Kommentar) — ein await hier wuerde alle Call-Sites async machen. Verlorene
    // Schreibversuche bei einem Absturz zwischen In-Memory-Update und Persistierung sind fuer
    // LOCAL_MOCK-Devdaten akzeptabel; Fehler werden geloggt statt verschluckt.
    private static void RunFireAndForget(Func<Task> action, string description)
    {
        _ = action()
            .ContinueWith(t =>
            {
                if (t.Exception is not null)
                {
                    Console.WriteLine(
                        $"[MockEntraDirectoryStore] WARNUNG: {description} nach Cosmos fehlgeschlagen: " +
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
        MockEntraGroup normalized;
        lock (_gate)
        {
            var objectId = string.IsNullOrWhiteSpace(group.ObjectId)
                ? $"mock-grp-{Guid.NewGuid():N}"[..24]
                : group.ObjectId;
            normalized = group with
            {
                ObjectId = objectId,
                MailNickname = string.IsNullOrWhiteSpace(group.MailNickname)
                    ? ToMailNickname(group.DisplayName)
                    : group.MailNickname,
                GroupTypes = group.GroupTypes ?? [],
                ResourceProvisioningOptions = group.ResourceProvisioningOptions ?? [],
            };
            SeedGroup(normalized);
        }
        PersistGroup(normalized);
        return normalized;
    }

    public bool DeleteGroup(string objectId)
    {
        bool removed;
        lock (_gate)
        {
            _membersByGroupId.Remove(objectId);
            removed = _groups.Remove(objectId);
        }
        if (removed)
        {
            RunFireAndForget(
                async () =>
                {
                    if (_directoryRepository is null) return;
                    await _directoryRepository.DeleteGroupAsync(objectId, CancellationToken.None);
                    await _directoryRepository.DeleteMembershipsByGroupAsync(objectId, CancellationToken.None);
                },
                $"Loeschen von Gruppe {objectId}");
        }
        return removed;
    }

    public MockEntraApplication UpsertApplication(MockEntraApplication application)
    {
        MockEntraApplication normalized;
        lock (_gate)
        {
            var objectId = string.IsNullOrWhiteSpace(application.ObjectId)
                ? $"mock-app-{Guid.NewGuid():N}"[..24]
                : application.ObjectId;
            normalized = application with
            {
                ObjectId = objectId,
                AppId = string.IsNullOrWhiteSpace(application.AppId)
                    ? $"app-{Guid.NewGuid():N}"
                    : application.AppId,
                AppRoles = application.AppRoles ?? [],
            };
            SeedApplication(normalized);
        }
        PersistApplication(normalized);
        return normalized;
    }

    public bool DeleteApplication(string objectId)
    {
        bool removed;
        lock (_gate) removed = _applications.Remove(objectId);
        if (removed)
        {
            RunFireAndForget(
                () => _directoryRepository?.DeleteApplicationAsync(objectId, CancellationToken.None) ?? Task.CompletedTask,
                $"Loeschen von Anwendung {objectId}");
        }
        return removed;
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
        MockEntraApplicationSignIn signIn;
        lock (_gate)
        {
            var id = $"{appId}:{entraObjectId}";
            signIn = new MockEntraApplicationSignIn(id, appId, entraObjectId, lastLoginAt);
            _applicationSignIns[id] = signIn;
        }
        RunFireAndForget(
            () => _directoryRepository?.UpsertApplicationSignInAsync(
                new MockEntraApplicationSignInRecord(signIn.AppId, signIn.EntraObjectId, signIn.LastLoginAt),
                CancellationToken.None) ?? Task.CompletedTask,
            $"Persistieren von App-Sign-in {signIn.Id}");
        return signIn;
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
        MockEntraGroup? created = null;
        string objectId;
        lock (_gate)
        {
            var existing = _groups.Values.FirstOrDefault(g => string.Equals(g.DisplayName, namePattern, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing.ObjectId;
            }

            objectId = $"mock-grp-{Guid.NewGuid():N}"[..24];
            created = BuildGroup(
                objectId,
                namePattern,
                resourceType,
                metadata.TryGetValue("ScenarioId", out var scenarioId) ? $"Scenario {scenarioId}" : "Created by LOCAL_MOCK worker.");
            SeedGroup(created);
        }
        PersistGroup(created);
        return objectId;
    }

    /// <summary>
    /// Stellt sicher, dass eine Gruppe mit der gegebenen Entra-Object-ID existiert — anders
    /// als EnsureGroup (das per DisplayName sucht/erstellt und daher nur fuer
    /// namensbasierte Aufrufer wie MockResourceConnector.CreateResourceAsync passt) wird hier
    /// GEZIELT nach der ObjectId gesucht (Erweiterung 2026-08-31 "Object-ID-Referenzierung"):
    /// WorkloadResource.ExternalId ist immer eine ObjectId, daher wuerde EnsureGroup bei einem
    /// Cache-Miss faelschlich eine zusaetzliche Gruppe mit DisplayName = ObjectId anlegen,
    /// statt die eigentlich schon existierende Gruppe wiederzufinden. Nur falls wirklich keine
    /// Gruppe mit dieser ObjectId bekannt ist (z.B. externes Cosmos-Backup unvollstaendig),
    /// wird sie mit der gegebenen ObjectId neu angelegt, mit displayNameFallback als Anzeigename.
    /// </summary>
    public void EnsureGroupByObjectId(string objectId, string resourceType, string? displayNameFallback)
    {
        MockEntraGroup? created = null;
        lock (_gate)
        {
            if (_groups.ContainsKey(objectId))
            {
                return;
            }

            created = BuildGroup(objectId, displayNameFallback ?? objectId, resourceType, "Reconstructed from WorkloadResource.");
            SeedGroup(created);
        }
        PersistGroup(created);
    }

    public void AddMember(string groupIdOrDisplayName, string entraObjectId)
    {
        string groupId;
        bool added;
        MockEntraGroup? createdGroup = null;
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
                createdGroup = group;
            }
            groupId = group.ObjectId;
            if (!_users.ContainsKey(entraObjectId))
            {
                return;
            }
            if (!_membersByGroupId.TryGetValue(group.ObjectId, out var members))
            {
                members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _membersByGroupId[group.ObjectId] = members;
            }
            added = members.Add(entraObjectId);
        }
        if (createdGroup is not null)
        {
            PersistGroup(createdGroup);
        }
        if (added)
        {
            RunFireAndForget(
                () => _directoryRepository?.UpsertMembershipAsync(
                    new MockEntraMembershipRecord(groupId, entraObjectId), CancellationToken.None) ?? Task.CompletedTask,
                $"Persistieren von Mitgliedschaft {groupId}/{entraObjectId}");
        }
    }

    public void RemoveMember(string groupIdOrDisplayName, string entraObjectId)
    {
        string? groupId = null;
        bool removed = false;
        lock (_gate)
        {
            var group = ResolveGroup(groupIdOrDisplayName);
            if (group is not null && _membersByGroupId.TryGetValue(group.ObjectId, out var members))
            {
                groupId = group.ObjectId;
                removed = members.Remove(entraObjectId);
            }
        }
        if (removed && groupId is not null)
        {
            RunFireAndForget(
                () => _directoryRepository?.DeleteMembershipAsync(groupId, entraObjectId, CancellationToken.None) ?? Task.CompletedTask,
                $"Loeschen von Mitgliedschaft {groupId}/{entraObjectId}");
        }
    }

    public int RemoveAllMembers(string groupIdOrDisplayName)
    {
        string? groupId = null;
        int count;
        lock (_gate)
        {
            var group = ResolveGroup(groupIdOrDisplayName);
            if (group is null || !_membersByGroupId.TryGetValue(group.ObjectId, out var members))
            {
                return 0;
            }
            groupId = group.ObjectId;
            count = members.Count;
            members.Clear();
        }
        if (count > 0 && groupId is not null)
        {
            RunFireAndForget(
                () => _directoryRepository?.DeleteMembershipsByGroupAsync(groupId, CancellationToken.None) ?? Task.CompletedTask,
                $"Loeschen aller Mitgliedschaften von Gruppe {groupId}");
        }
        return count;
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
