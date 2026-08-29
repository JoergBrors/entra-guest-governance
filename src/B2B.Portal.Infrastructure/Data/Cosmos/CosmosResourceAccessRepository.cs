using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>
/// Cosmos-Implementierung von IResourceAccessRepository, Container "discovery" (Actual
/// State, getrennt vom Desired State im Container "domain").
/// </summary>
public sealed class CosmosResourceAccessRepository(CosmosClientFactory factory) : IResourceAccessRepository
{
    private const string EntityType = nameof(ResourceAccess);
    private Container Container => factory.GetContainer("discovery");

    public async Task<IReadOnlyList<ResourceAccess>> ListByGuestAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<ResourceAccessDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type AND c.guestId = @guestId")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType)
                .WithParameter("@guestId", guestId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<ResourceAccess>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    public Task UpsertAsync(ResourceAccess access, CancellationToken ct) =>
        Container.UpsertItemAsync(
            ResourceAccessDocument.FromEntity(access),
            new PartitionKey(access.PlatformTenantId),
            cancellationToken: ct);
}

internal sealed class ResourceAccessDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = nameof(ResourceAccess);
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("guestId")] public required Guid GuestId { get; init; }
    [JsonPropertyName("resourceType")] public required string ResourceType { get; init; }
    [JsonPropertyName("externalResourceId")] public required string ExternalResourceId { get; init; }
    [JsonPropertyName("classification")] public required AccessClassification Classification { get; init; }
    [JsonPropertyName("discoveredAt")] public required DateTimeOffset DiscoveredAt { get; init; }

    public static ResourceAccessDocument FromEntity(ResourceAccess a) => new()
    {
        Id = a.Id.ToString(),
        PlatformTenantId = a.PlatformTenantId,
        GuestId = a.GuestId,
        ResourceType = a.ResourceType,
        ExternalResourceId = a.ExternalResourceId,
        Classification = a.Classification,
        DiscoveredAt = a.DiscoveredAt,
    };

    public ResourceAccess ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        PlatformTenantId = PlatformTenantId,
        GuestId = GuestId,
        ResourceType = ResourceType,
        ExternalResourceId = ExternalResourceId,
        Classification = Classification,
        DiscoveredAt = DiscoveredAt,
    };
}
