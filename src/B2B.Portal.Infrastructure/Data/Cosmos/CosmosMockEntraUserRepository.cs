using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>
/// Cosmos-Implementierung von IMockEntraUserRepository, Container "discovery" (geteilt mit
/// CosmosResourceAccessRepository — disambiguiert per entityType, dasselbe Muster wie
/// CosmosJobRepository/CosmosJobQueue im Container "jobs"). Persistiert Mock-Entra-Benutzer
/// inkl. PortalRoles (Erweiterung 2026-08-30 (Teil 3): vorher lebten diese nur im
/// In-Memory-Singleton MockEntraDirectoryStore und gingen bei jedem API-Neustart verloren —
/// insbesondere die einzige Quelle fuer "wer ist GovernanceAdmin").
/// </summary>
public sealed class CosmosMockEntraUserRepository(CosmosClientFactory factory) : IMockEntraUserRepository
{
    private const string EntityType = "MockEntraUser";
    private Container Container => factory.GetContainer("discovery");

    public async Task<IReadOnlyList<MockEntraUserRecord>> ListAllAsync(CancellationToken ct)
    {
        // Cross-Partition-Query: beim kalten Start (frisch resetterte Cosmos-DB, API noch
        // nicht gestartet) ist noch kein Tenant-Kontext bekannt — anders als alle uebrigen
        // Repositories, die TenantContext als Pflichtparameter erzwingen, siehe CorePorts.cs.
        var query = Container.GetItemQueryIterator<MockEntraUserDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.entityType = @type")
                .WithParameter("@type", EntityType));

        var results = new List<MockEntraUserRecord>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToRecord()));
        }
        return results;
    }

    public async Task<IReadOnlyList<MockEntraUserRecord>> ListAsync(TenantContext tenant, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<MockEntraUserDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<MockEntraUserRecord>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToRecord()));
        }
        return results;
    }

    public Task UpsertAsync(MockEntraUserRecord user, CancellationToken ct) =>
        Container.UpsertItemAsync(
            MockEntraUserDocument.FromRecord(user),
            new PartitionKey(user.PlatformTenantId),
            cancellationToken: ct);
}

internal sealed class MockEntraUserDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = "MockEntraUser";
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("objectId")] public required string ObjectId { get; init; }
    [JsonPropertyName("userPrincipalName")] public required string UserPrincipalName { get; init; }
    [JsonPropertyName("mail")] public required string Mail { get; init; }
    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }
    [JsonPropertyName("givenName")] public required string GivenName { get; init; }
    [JsonPropertyName("surname")] public required string Surname { get; init; }
    [JsonPropertyName("companyName")] public required string CompanyName { get; init; }
    [JsonPropertyName("department")] public required string Department { get; init; }
    [JsonPropertyName("jobTitle")] public required string JobTitle { get; init; }
    [JsonPropertyName("sponsor")] public required string Sponsor { get; init; }
    [JsonPropertyName("accountEnabled")] public required string AccountEnabled { get; init; }
    [JsonPropertyName("userType")] public required string UserType { get; init; }
    [JsonPropertyName("portalRoles")] public required List<string> PortalRoles { get; init; }
    [JsonPropertyName("lastLoginAt")] public DateTimeOffset? LastLoginAt { get; init; }

    public static MockEntraUserDocument FromRecord(MockEntraUserRecord u) => new()
    {
        Id = $"mock-entra-user-{u.ObjectId}",
        PlatformTenantId = u.PlatformTenantId,
        ObjectId = u.ObjectId,
        UserPrincipalName = u.UserPrincipalName,
        Mail = u.Mail,
        DisplayName = u.DisplayName,
        GivenName = u.GivenName,
        Surname = u.Surname,
        CompanyName = u.CompanyName,
        Department = u.Department,
        JobTitle = u.JobTitle,
        Sponsor = u.Sponsor,
        AccountEnabled = u.AccountEnabled,
        UserType = u.UserType,
        PortalRoles = [.. u.PortalRoles],
        LastLoginAt = u.LastLoginAt,
    };

    public MockEntraUserRecord ToRecord() => new(
        ObjectId, UserPrincipalName, Mail, DisplayName, GivenName, Surname, CompanyName,
        Department, JobTitle, Sponsor, AccountEnabled, UserType, PortalRoles, PlatformTenantId, LastLoginAt);
}
