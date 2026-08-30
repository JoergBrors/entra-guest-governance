using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>
/// Cosmos-Implementierung von IReminderPolicyRepository, Container "discovery" (geteilt mit
/// CosmosMockEntraUserRepository/CosmosResourceAccessRepository — disambiguiert per
/// entityType, dasselbe Muster wie CosmosJobRepository/CosmosJobQueue im Container "jobs").
/// Genau ein Dokument pro PlatformTenantId (feste Dokument-Id "reminder-policy-{tenantId}").
/// </summary>
public sealed class CosmosReminderPolicyRepository(CosmosClientFactory factory) : IReminderPolicyRepository
{
    private const string EntityType = "ReminderPolicy";
    private Container Container => factory.GetContainer("discovery");

    public async Task<ReminderPolicy?> GetAsync(TenantContext tenant, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<ReminderPolicyDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            var doc = page.FirstOrDefault();
            if (doc is not null)
            {
                return doc.ToEntity();
            }
        }
        return null;
    }

    public Task UpsertAsync(ReminderPolicy policy, CancellationToken ct) =>
        Container.UpsertItemAsync(
            ReminderPolicyDocument.FromEntity(policy),
            new PartitionKey(policy.PlatformTenantId),
            cancellationToken: ct);
}

internal sealed class ReminderStageDocument
{
    [JsonPropertyName("stageNumber")] public required int StageNumber { get; init; }
    [JsonPropertyName("daysAfterInvite")] public required int DaysAfterInvite { get; init; }
    [JsonPropertyName("templateId")] public required string TemplateId { get; init; }
    [JsonPropertyName("templateSubject")] public required string TemplateSubject { get; init; }
    [JsonPropertyName("templateBody")] public required string TemplateBody { get; init; }

    public static ReminderStageDocument FromEntity(ReminderStage s) => new()
    {
        StageNumber = s.StageNumber,
        DaysAfterInvite = s.DaysAfterInvite,
        TemplateId = s.TemplateId,
        TemplateSubject = s.TemplateSubject,
        TemplateBody = s.TemplateBody,
    };

    public ReminderStage ToEntity() => new()
    {
        StageNumber = StageNumber,
        DaysAfterInvite = DaysAfterInvite,
        TemplateId = TemplateId,
        TemplateSubject = TemplateSubject,
        TemplateBody = TemplateBody,
    };
}

internal sealed class ReminderPolicyDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = "ReminderPolicy";
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("stages")] public required List<ReminderStageDocument> Stages { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; init; }

    public static ReminderPolicyDocument FromEntity(ReminderPolicy p) => new()
    {
        Id = $"reminder-policy-{p.PlatformTenantId}",
        PlatformTenantId = p.PlatformTenantId,
        Stages = [.. p.Stages.Select(ReminderStageDocument.FromEntity)],
        UpdatedAt = p.UpdatedAt,
    };

    public ReminderPolicy ToEntity() => new()
    {
        PlatformTenantId = PlatformTenantId,
        Stages = [.. Stages.Select(s => s.ToEntity())],
        UpdatedAt = UpdatedAt,
    };
}
