using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>
/// Cosmos-Implementierung von IAuditWriter, Container "audit" — fachlich unveränderliche
/// Nachweise, getrennt vom Domain-State (Container "domain") und nie automatisch ablaufend
/// (defaultTtl=-1, siehe infra/modules/cosmos-free-tier.bicep).
/// </summary>
public sealed class CosmosAuditWriter(CosmosClientFactory factory) : IAuditWriter
{
    private const string EntityType = nameof(AuditEvent);
    private Container Container => factory.GetContainer("audit");

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct) =>
        Container.CreateItemAsync(
            AuditEventDocument.FromEntity(auditEvent),
            new PartitionKey(auditEvent.PlatformTenantId),
            cancellationToken: ct);

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(TenantContext tenant, int take, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<AuditEventDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type " +
                "ORDER BY c.timestamp DESC OFFSET 0 LIMIT @take")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType)
                .WithParameter("@take", take),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<AuditEvent>();
        while (query.HasMoreResults && results.Count < take)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }
}

internal sealed class AuditEventDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = nameof(AuditEvent);
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("actor")] public required string Actor { get; init; }
    [JsonPropertyName("action")] public required string Action { get; init; }
    [JsonPropertyName("auditEntityType")] public required string AuditEntityType { get; init; }
    [JsonPropertyName("entityId")] public required string EntityId { get; init; }
    [JsonPropertyName("policyVersion")] public string? PolicyVersion { get; init; }
    [JsonPropertyName("result")] public required string Result { get; init; }
    [JsonPropertyName("correlationId")] public required Guid CorrelationId { get; init; }
    [JsonPropertyName("timestamp")] public required DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("details")] public string? Details { get; init; }

    public static AuditEventDocument FromEntity(AuditEvent e) => new()
    {
        Id = e.Id.ToString(),
        PlatformTenantId = e.PlatformTenantId,
        Actor = e.Actor,
        Action = e.Action,
        AuditEntityType = e.EntityType,
        EntityId = e.EntityId,
        PolicyVersion = e.PolicyVersion,
        Result = e.Result,
        CorrelationId = e.CorrelationId,
        Timestamp = e.Timestamp,
        Details = e.Details,
    };

    public AuditEvent ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        PlatformTenantId = PlatformTenantId,
        Actor = Actor,
        Action = Action,
        EntityType = AuditEntityType,
        EntityId = EntityId,
        PolicyVersion = PolicyVersion,
        Result = Result,
        CorrelationId = CorrelationId,
        Timestamp = Timestamp,
        Details = Details,
    };
}
