using System.Net;
using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>Cosmos-Implementierung von IReviewRepository, Container "domain".</summary>
public sealed class CosmosReviewRepository(CosmosClientFactory factory) : IReviewRepository
{
    private const string EntityType = nameof(ReviewInstance);
    private Container Container => factory.GetContainer("domain");

    public async Task<ReviewInstance?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        try
        {
            var response = await Container.ReadItemAsync<ReviewInstanceDocument>(
                id.ToString(), new PartitionKey(tenant.PlatformTenantId), cancellationToken: ct);
            return response.Resource.EntityType == EntityType ? response.Resource.ToEntity() : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ReviewInstance>> ListOpenAsync(TenantContext tenant, CancellationToken ct)
    {
        // IsOpen ist auf der Domain-Entity berechnet (CompletedAt == null) - hier direkt
        // als Query-Praedikat gespiegelt, um serverseitig zu filtern statt clientseitig
        // nachzufiltern.
        var query = Container.GetItemQueryIterator<ReviewInstanceDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type " +
                "AND (NOT IS_DEFINED(c.completedAt) OR IS_NULL(c.completedAt))")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<ReviewInstance>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    public Task UpsertAsync(ReviewInstance instance, CancellationToken ct) =>
        Container.UpsertItemAsync(
            ReviewInstanceDocument.FromEntity(instance),
            new PartitionKey(instance.PlatformTenantId),
            cancellationToken: ct);
}

internal sealed class ReviewItemDocument
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("reviewInstanceId")] public required Guid ReviewInstanceId { get; init; }
    [JsonPropertyName("assignmentId")] public required Guid AssignmentId { get; init; }
    [JsonPropertyName("decision")] public required ReviewDecision Decision { get; init; }
    [JsonPropertyName("decidedBy")] public string? DecidedBy { get; init; }
    [JsonPropertyName("decidedAt")] public DateTimeOffset? DecidedAt { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }

    public static ReviewItemDocument FromEntity(ReviewItem i) => new()
    {
        Id = i.Id, ReviewInstanceId = i.ReviewInstanceId, AssignmentId = i.AssignmentId,
        Decision = i.Decision, DecidedBy = i.DecidedBy, DecidedAt = i.DecidedAt, Reason = i.Reason,
    };

    public ReviewItem ToEntity() => new()
    {
        Id = Id, ReviewInstanceId = ReviewInstanceId, AssignmentId = AssignmentId,
        Decision = Decision, DecidedBy = DecidedBy, DecidedAt = DecidedAt, Reason = Reason,
    };
}

internal sealed class ReviewInstanceDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = nameof(ReviewInstance);
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("reviewDefinitionId")] public required Guid ReviewDefinitionId { get; init; }
    [JsonPropertyName("provider")] public required GovernanceProvider Provider { get; init; }
    [JsonPropertyName("startedAt")] public required DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("completedAt")] public DateTimeOffset? CompletedAt { get; init; }
    [JsonPropertyName("items")] public required List<ReviewItemDocument> Items { get; init; }

    public static ReviewInstanceDocument FromEntity(ReviewInstance r) => new()
    {
        Id = r.Id.ToString(),
        PlatformTenantId = r.PlatformTenantId,
        ReviewDefinitionId = r.ReviewDefinitionId,
        Provider = r.Provider,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        Items = [.. r.Items.Select(ReviewItemDocument.FromEntity)],
    };

    public ReviewInstance ToEntity()
    {
        var instance = new ReviewInstance
        {
            Id = Guid.Parse(Id),
            PlatformTenantId = PlatformTenantId,
            ReviewDefinitionId = ReviewDefinitionId,
            Provider = Provider,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
        };
        instance.Items.AddRange(Items.Select(i => i.ToEntity()));
        return instance;
    }
}
