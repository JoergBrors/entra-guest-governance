using System.Net;
using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>Cosmos-Implementierung von IExternalOrganizationRepository, Container "domain".</summary>
public sealed class CosmosExternalOrganizationRepository(CosmosClientFactory factory) : IExternalOrganizationRepository
{
    private const string EntityType = nameof(ExternalOrganization);
    private Container Container => factory.GetContainer("domain");

    public async Task<ExternalOrganization?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        try
        {
            var response = await Container.ReadItemAsync<ExternalOrganizationDocument>(
                id.ToString(), new PartitionKey(tenant.PlatformTenantId), cancellationToken: ct);
            return response.Resource.EntityType == EntityType ? response.Resource.ToEntity() : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ExternalOrganization?> GetByNameAsync(TenantContext tenant, string name, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<ExternalOrganizationDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type AND LOWER(c.name) = LOWER(@name)")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType)
                .WithParameter("@name", name),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        if (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            return page.FirstOrDefault()?.ToEntity();
        }
        return null;
    }

    public async Task<IReadOnlyList<ExternalOrganization>> ListAsync(TenantContext tenant, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<ExternalOrganizationDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<ExternalOrganization>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    public Task UpsertAsync(ExternalOrganization organization, CancellationToken ct) =>
        Container.UpsertItemAsync(
            ExternalOrganizationDocument.FromEntity(organization),
            new PartitionKey(organization.PlatformTenantId),
            cancellationToken: ct);
}

internal sealed class ExternalOrganizationDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = nameof(ExternalOrganization);
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("verifiedDomains")] public required List<string> VerifiedDomains { get; init; }
    [JsonPropertyName("riskClassification")] public string? RiskClassification { get; init; }
    [JsonPropertyName("status")] public required OrganizationStatus Status { get; init; }
    [JsonPropertyName("createdAt")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public required DateTimeOffset UpdatedAt { get; init; }

    public static ExternalOrganizationDocument FromEntity(ExternalOrganization o) => new()
    {
        Id = o.Id.ToString(),
        PlatformTenantId = o.PlatformTenantId,
        Name = o.Name,
        VerifiedDomains = [.. o.VerifiedDomains],
        RiskClassification = o.RiskClassification,
        Status = o.Status,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
    };

    public ExternalOrganization ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        PlatformTenantId = PlatformTenantId,
        Name = Name,
        VerifiedDomains = [.. VerifiedDomains],
        RiskClassification = RiskClassification,
        Status = Status,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };
}
