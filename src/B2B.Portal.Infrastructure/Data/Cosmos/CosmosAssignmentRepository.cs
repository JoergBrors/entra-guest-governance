using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>Cosmos-Implementierung von IAssignmentRepository, Container "domain".</summary>
public sealed class CosmosAssignmentRepository(CosmosClientFactory factory) : IAssignmentRepository
{
    private const string EntityType = nameof(GuestWorkloadAssignment);
    private Container Container => factory.GetContainer("domain");

    public async Task<IReadOnlyList<GuestWorkloadAssignment>> ListByGuestAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<AssignmentDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type AND c.guestId = @guestId")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType)
                .WithParameter("@guestId", guestId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<GuestWorkloadAssignment>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    public async Task<IReadOnlyList<GuestWorkloadAssignment>> ListActiveByGuestAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<AssignmentDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type " +
                "AND c.guestId = @guestId AND c.status IN (@active, @approved, @requested)")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType)
                .WithParameter("@guestId", guestId)
                .WithParameter("@active", nameof(AssignmentStatus.Active))
                .WithParameter("@approved", nameof(AssignmentStatus.Approved))
                .WithParameter("@requested", nameof(AssignmentStatus.Requested)),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<GuestWorkloadAssignment>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    public Task UpsertAsync(GuestWorkloadAssignment assignment, CancellationToken ct) =>
        Container.UpsertItemAsync(
            AssignmentDocument.FromEntity(assignment),
            new PartitionKey(assignment.PlatformTenantId),
            cancellationToken: ct);
}

internal sealed class AssignmentDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = nameof(GuestWorkloadAssignment);
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("guestId")] public required Guid GuestId { get; init; }
    [JsonPropertyName("workloadId")] public required Guid WorkloadId { get; init; }
    [JsonPropertyName("roleId")] public required Guid RoleId { get; init; }
    [JsonPropertyName("validFrom")] public required DateTimeOffset ValidFrom { get; init; }
    [JsonPropertyName("validUntil")] public DateTimeOffset? ValidUntil { get; init; }
    [JsonPropertyName("status")] public required AssignmentStatus Status { get; init; }
    [JsonPropertyName("updatedAt")] public required DateTimeOffset UpdatedAt { get; init; }

    public static AssignmentDocument FromEntity(GuestWorkloadAssignment a) => new()
    {
        Id = a.Id.ToString(),
        PlatformTenantId = a.PlatformTenantId,
        GuestId = a.GuestId,
        WorkloadId = a.WorkloadId,
        RoleId = a.RoleId,
        ValidFrom = a.ValidFrom,
        ValidUntil = a.ValidUntil,
        Status = a.Status,
        UpdatedAt = a.UpdatedAt,
    };

    public GuestWorkloadAssignment ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        PlatformTenantId = PlatformTenantId,
        GuestId = GuestId,
        WorkloadId = WorkloadId,
        RoleId = RoleId,
        ValidFrom = ValidFrom,
        ValidUntil = ValidUntil,
        Status = Status,
        UpdatedAt = UpdatedAt,
    };
}
