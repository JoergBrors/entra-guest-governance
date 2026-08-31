using System.Net;
using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>Cosmos-Implementierung von IWorkloadRepository, Container "domain".</summary>
public sealed class CosmosWorkloadRepository(CosmosClientFactory factory) : IWorkloadRepository
{
    private const string EntityType = nameof(Workload);
    private Container Container => factory.GetContainer("domain");

    public async Task<Workload?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        try
        {
            var response = await Container.ReadItemAsync<WorkloadDocument>(
                id.ToString(), new PartitionKey(tenant.PlatformTenantId), cancellationToken: ct);
            return response.Resource.EntityType == EntityType ? response.Resource.ToEntity() : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<Workload>> ListAsync(TenantContext tenant, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<WorkloadDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<Workload>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    public Task UpsertAsync(Workload workload, CancellationToken ct) =>
        Container.UpsertItemAsync(
            WorkloadDocument.FromEntity(workload),
            new PartitionKey(workload.PlatformTenantId),
            cancellationToken: ct);

    public async Task DeleteAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        try
        {
            await Container.DeleteItemAsync<WorkloadDocument>(
                id.ToString(), new PartitionKey(tenant.PlatformTenantId), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Bereits gelöscht/nie existent — idempotent, kein Fehler.
        }
    }
}

internal sealed class WorkloadRoleDocument
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("workloadId")] public required Guid WorkloadId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("applicationId")] public string? ApplicationId { get; init; }
    [JsonPropertyName("applicationRoleId")] public string? ApplicationRoleId { get; init; }
    [JsonPropertyName("resourceMappings")] public required List<Guid> ResourceMappings { get; init; }

    public static WorkloadRoleDocument FromEntity(WorkloadRole r) => new()
    {
        Id = r.Id, WorkloadId = r.WorkloadId, Name = r.Name,
        ApplicationId = r.ApplicationId,
        ApplicationRoleId = r.ApplicationRoleId,
        ResourceMappings = r.ResourceMappings,
    };

    public WorkloadRole ToEntity() => new()
    {
        Id = Id, WorkloadId = WorkloadId, Name = Name,
        ApplicationId = ApplicationId,
        ApplicationRoleId = ApplicationRoleId,
        ResourceMappings = [.. ResourceMappings],
    };
}

internal sealed class WorkloadResourceDocument
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("workloadId")] public required Guid WorkloadId { get; init; }
    [JsonPropertyName("resourceType")] public required string ResourceType { get; init; }
    [JsonPropertyName("externalId")] public string? ExternalId { get; init; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
    [JsonPropertyName("managed")] public required bool Managed { get; init; }

    public static WorkloadResourceDocument FromEntity(WorkloadResource r) => new()
    {
        Id = r.Id, WorkloadId = r.WorkloadId, ResourceType = r.ResourceType, ExternalId = r.ExternalId,
        DisplayName = r.DisplayName, Managed = r.Managed,
    };

    public WorkloadResource ToEntity() => new()
    {
        Id = Id, WorkloadId = WorkloadId, ResourceType = ResourceType, ExternalId = ExternalId,
        DisplayName = DisplayName, Managed = Managed,
    };
}

internal sealed class WorkloadDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("entityType")]
    public string EntityType { get; init; } = nameof(Workload);

    [JsonPropertyName("platformTenantId")]
    public required string PlatformTenantId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("templateId")]
    public string? TemplateId { get; init; }

    [JsonPropertyName("active")]
    public required bool Active { get; init; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    [JsonPropertyName("administrativeUnitExternalId")]
    public string? AdministrativeUnitExternalId { get; init; }

    [JsonPropertyName("applicationExternalId")]
    public string? ApplicationExternalId { get; init; }

    [JsonPropertyName("resourceNamePatterns")]
    public List<string>? ResourceNamePatterns { get; init; }

    [JsonPropertyName("roles")]
    public required List<WorkloadRoleDocument> Roles { get; init; }

    [JsonPropertyName("resources")]
    public required List<WorkloadResourceDocument> Resources { get; init; }

    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public required DateTimeOffset UpdatedAt { get; init; }

    public static WorkloadDocument FromEntity(Workload w) => new()
    {
        Id = w.Id.ToString(),
        PlatformTenantId = w.PlatformTenantId,
        Name = w.Name,
        Owner = w.Owner,
        TemplateId = w.TemplateId,
        Active = w.Active,
        IsDefault = w.IsDefault,
        AdministrativeUnitExternalId = w.AdministrativeUnitExternalId,
        ApplicationExternalId = w.ApplicationExternalId,
        ResourceNamePatterns = w.ResourceNamePatterns,
        Roles = [.. w.Roles.Select(WorkloadRoleDocument.FromEntity)],
        Resources = [.. w.Resources.Select(WorkloadResourceDocument.FromEntity)],
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt,
    };

    public Workload ToEntity()
    {
        var workload = new Workload
        {
            Id = Guid.Parse(Id),
            PlatformTenantId = PlatformTenantId,
            Name = Name,
            Owner = Owner,
            TemplateId = TemplateId,
            Active = Active,
            IsDefault = IsDefault,
            AdministrativeUnitExternalId = AdministrativeUnitExternalId,
            ApplicationExternalId = ApplicationExternalId,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
        workload.ResourceNamePatterns.AddRange(ResourceNamePatterns ?? []);
        workload.Roles.AddRange(Roles.Select(r => r.ToEntity()));
        workload.Resources.AddRange(Resources.Select(r => r.ToEntity()));
        return workload;
    }
}
