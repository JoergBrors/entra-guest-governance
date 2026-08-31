using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>
/// Cosmos-Implementierung von IMockEntraDirectoryRepository, dedizierter Container "entraid"
/// (Erweiterung 2026-08-31 "EntraId-Persistenz": eigener Container statt Unterbringung im
/// geteilten "discovery"-Container, geteilt nur noch mit CosmosMockEntraUserRepository —
/// disambiguiert per entityType, siehe dortiger Kommentar). Der Container ist wie alle
/// uebrigen im Projekt mit Partition-Key-PFAD "/platformTenantId" angelegt (siehe
/// scripts/requirements.ps1, scripts/reset-cosmos-dev-data.ps1, docker/cosmos-init.ps1,
/// infra/modules/cosmos-free-tier.bicep) — jedes Dokument braucht daher zwingend ein
/// "platformTenantId"-Feld, auch wenn Gruppen/Anwendungen im Mock-Entra-Stamm inhaltlich nicht
/// tenant-gebunden sind (anders als MockEntraUserDocument). Alle Dokumente dieser Klasse
/// tragen dafuer den festen Platzhalterwert "mock-entra" in diesem Feld und landen so in einer
/// gemeinsamen logischen Partition.
/// </summary>
public sealed class CosmosMockEntraDirectoryRepository(CosmosClientFactory factory) : IMockEntraDirectoryRepository
{
    private const string FixedPartition = "mock-entra";
    private static readonly PartitionKey Partition = new(FixedPartition);
    private Container Container => factory.GetContainer("entraid");

    public async Task<IReadOnlyList<MockEntraGroupRecord>> ListGroupsAsync(CancellationToken ct) =>
        await QueryAsync<MockEntraGroupDocument, MockEntraGroupRecord>("MockEntraGroup", d => d.ToRecord(), ct);

    public Task UpsertGroupAsync(MockEntraGroupRecord group, CancellationToken ct) =>
        Container.UpsertItemAsync(MockEntraGroupDocument.FromRecord(group), Partition, cancellationToken: ct);

    public Task DeleteGroupAsync(string objectId, CancellationToken ct) =>
        DeleteIfExistsAsync(MockEntraGroupDocument.BuildId(objectId), ct);

    public async Task<IReadOnlyList<MockEntraMembershipRecord>> ListMembershipsAsync(CancellationToken ct) =>
        await QueryAsync<MockEntraMembershipDocument, MockEntraMembershipRecord>("MockEntraMembership", d => d.ToRecord(), ct);

    public Task UpsertMembershipAsync(MockEntraMembershipRecord membership, CancellationToken ct) =>
        Container.UpsertItemAsync(MockEntraMembershipDocument.FromRecord(membership), Partition, cancellationToken: ct);

    public Task DeleteMembershipAsync(string groupId, string entraObjectId, CancellationToken ct) =>
        DeleteIfExistsAsync(MockEntraMembershipDocument.BuildId(groupId, entraObjectId), ct);

    public async Task DeleteMembershipsByGroupAsync(string groupId, CancellationToken ct)
    {
        var memberships = await ListMembershipsAsync(ct);
        foreach (var membership in memberships.Where(m => string.Equals(m.GroupId, groupId, StringComparison.OrdinalIgnoreCase)))
        {
            await DeleteMembershipAsync(membership.GroupId, membership.EntraObjectId, ct);
        }
    }

    public async Task<IReadOnlyList<MockEntraApplicationRecord>> ListApplicationsAsync(CancellationToken ct) =>
        await QueryAsync<MockEntraApplicationDocument, MockEntraApplicationRecord>("MockEntraApplication", d => d.ToRecord(), ct);

    public Task UpsertApplicationAsync(MockEntraApplicationRecord application, CancellationToken ct) =>
        Container.UpsertItemAsync(MockEntraApplicationDocument.FromRecord(application), Partition, cancellationToken: ct);

    public Task DeleteApplicationAsync(string objectId, CancellationToken ct) =>
        DeleteIfExistsAsync(MockEntraApplicationDocument.BuildId(objectId), ct);

    public async Task<IReadOnlyList<MockEntraApplicationSignInRecord>> ListApplicationSignInsAsync(CancellationToken ct) =>
        await QueryAsync<MockEntraApplicationSignInDocument, MockEntraApplicationSignInRecord>("MockEntraApplicationSignIn", d => d.ToRecord(), ct);

    public Task UpsertApplicationSignInAsync(MockEntraApplicationSignInRecord signIn, CancellationToken ct) =>
        Container.UpsertItemAsync(MockEntraApplicationSignInDocument.FromRecord(signIn), Partition, cancellationToken: ct);

    private async Task<IReadOnlyList<TRecord>> QueryAsync<TDocument, TRecord>(
        string entityType, Func<TDocument, TRecord> toRecord, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<TDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.entityType = @type")
                .WithParameter("@type", entityType),
            requestOptions: new QueryRequestOptions { PartitionKey = Partition });

        var results = new List<TRecord>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(toRecord));
        }
        return results;
    }

    private async Task DeleteIfExistsAsync(string id, CancellationToken ct)
    {
        try
        {
            await Container.DeleteItemAsync<object>(id, Partition, cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }
    }
}

internal sealed class MockEntraGroupDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = "MockEntraGroup";
    [JsonPropertyName("platformTenantId")] public string PlatformTenantId { get; init; } = "mock-entra";
    [JsonPropertyName("objectId")] public required string ObjectId { get; init; }
    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }
    [JsonPropertyName("mailNickname")] public required string MailNickname { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("groupTypes")] public required List<string> GroupTypes { get; init; }
    [JsonPropertyName("mailEnabled")] public bool MailEnabled { get; init; }
    [JsonPropertyName("securityEnabled")] public bool SecurityEnabled { get; init; }
    [JsonPropertyName("resourceProvisioningOptions")] public required List<string> ResourceProvisioningOptions { get; init; }

    public static string BuildId(string objectId) => $"mock-entra-group-{objectId}";

    public static MockEntraGroupDocument FromRecord(MockEntraGroupRecord g) => new()
    {
        Id = BuildId(g.ObjectId),
        ObjectId = g.ObjectId,
        DisplayName = g.DisplayName,
        MailNickname = g.MailNickname,
        Description = g.Description,
        GroupTypes = [.. g.GroupTypes],
        MailEnabled = g.MailEnabled,
        SecurityEnabled = g.SecurityEnabled,
        ResourceProvisioningOptions = [.. g.ResourceProvisioningOptions],
    };

    public MockEntraGroupRecord ToRecord() => new(
        ObjectId, DisplayName, MailNickname, Description, GroupTypes, MailEnabled, SecurityEnabled, ResourceProvisioningOptions);
}

internal sealed class MockEntraMembershipDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = "MockEntraMembership";
    [JsonPropertyName("platformTenantId")] public string PlatformTenantId { get; init; } = "mock-entra";
    [JsonPropertyName("groupId")] public required string GroupId { get; init; }
    [JsonPropertyName("entraObjectId")] public required string EntraObjectId { get; init; }

    public static string BuildId(string groupId, string entraObjectId) => $"mock-entra-membership-{groupId}-{entraObjectId}";

    public static MockEntraMembershipDocument FromRecord(MockEntraMembershipRecord m) => new()
    {
        Id = BuildId(m.GroupId, m.EntraObjectId),
        GroupId = m.GroupId,
        EntraObjectId = m.EntraObjectId,
    };

    public MockEntraMembershipRecord ToRecord() => new(GroupId, EntraObjectId);
}

internal sealed class MockEntraApplicationRoleDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("value")] public required string Value { get; init; }
    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }

    public static MockEntraApplicationRoleDocument FromRecord(MockEntraApplicationRoleRecord r) => new()
    {
        Id = r.Id,
        Value = r.Value,
        DisplayName = r.DisplayName,
        Description = r.Description,
    };

    public MockEntraApplicationRoleRecord ToRecord() => new(Id, Value, DisplayName, Description);
}

internal sealed class MockEntraApplicationDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = "MockEntraApplication";
    [JsonPropertyName("platformTenantId")] public string PlatformTenantId { get; init; } = "mock-entra";
    [JsonPropertyName("objectId")] public required string ObjectId { get; init; }
    [JsonPropertyName("appId")] public required string AppId { get; init; }
    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }
    [JsonPropertyName("appRoles")] public required List<MockEntraApplicationRoleDocument> AppRoles { get; init; }

    public static string BuildId(string objectId) => $"mock-entra-application-{objectId}";

    public static MockEntraApplicationDocument FromRecord(MockEntraApplicationRecord a) => new()
    {
        Id = BuildId(a.ObjectId),
        ObjectId = a.ObjectId,
        AppId = a.AppId,
        DisplayName = a.DisplayName,
        AppRoles = [.. a.AppRoles.Select(MockEntraApplicationRoleDocument.FromRecord)],
    };

    public MockEntraApplicationRecord ToRecord() => new(
        ObjectId, AppId, DisplayName, [.. AppRoles.Select(r => r.ToRecord())]);
}

internal sealed class MockEntraApplicationSignInDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = "MockEntraApplicationSignIn";
    [JsonPropertyName("platformTenantId")] public string PlatformTenantId { get; init; } = "mock-entra";
    [JsonPropertyName("appId")] public required string AppId { get; init; }
    [JsonPropertyName("entraObjectId")] public required string EntraObjectId { get; init; }
    [JsonPropertyName("lastLoginAt")] public DateTimeOffset LastLoginAt { get; init; }

    public static string BuildId(string appId, string entraObjectId) => $"mock-entra-appsignin-{appId}-{entraObjectId}";

    public static MockEntraApplicationSignInDocument FromRecord(MockEntraApplicationSignInRecord s) => new()
    {
        Id = BuildId(s.AppId, s.EntraObjectId),
        AppId = s.AppId,
        EntraObjectId = s.EntraObjectId,
        LastLoginAt = s.LastLoginAt,
    };

    public MockEntraApplicationSignInRecord ToRecord() => new(AppId, EntraObjectId, LastLoginAt);
}
