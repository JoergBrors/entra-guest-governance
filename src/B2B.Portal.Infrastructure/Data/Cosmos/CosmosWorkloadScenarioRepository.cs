using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>Cosmos-Implementierung von IWorkloadScenarioRepository, Container "domain".</summary>
public sealed class CosmosWorkloadScenarioRepository(CosmosClientFactory factory) : IWorkloadScenarioRepository
{
    private const string EntityType = nameof(WorkloadScenario);
    private Container Container => factory.GetContainer("domain");

    public async Task<WorkloadScenario?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        try
        {
            var response = await Container.ReadItemAsync<WorkloadScenarioDocument>(
                id.ToString(), new PartitionKey(tenant.PlatformTenantId), cancellationToken: ct);
            return response.Resource.EntityType == EntityType ? response.Resource.ToEntity() : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<WorkloadScenario>> ListByWorkloadAsync(
        TenantContext tenant, Guid workloadId, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<WorkloadScenarioDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type AND c.workloadId = @workloadId")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType)
                .WithParameter("@workloadId", workloadId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<WorkloadScenario>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    public Task UpsertAsync(WorkloadScenario scenario, CancellationToken ct) =>
        Container.UpsertItemAsync(
            WorkloadScenarioDocument.FromEntity(scenario),
            new PartitionKey(scenario.PlatformTenantId),
            cancellationToken: ct);
}

internal sealed class ScenarioResourceRuleDocument
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("workloadScenarioId")] public required Guid WorkloadScenarioId { get; init; }
    [JsonPropertyName("resourceId")] public required Guid ResourceId { get; init; }

    // Als Raw-JSON-String gespeichert — dieselbe Begründung wie bei ConditionJson/
    // CosmosJobQueue.PayloadJson: der CosmosClient ist global auf CamelCase-Property-
    // Naming konfiguriert (siehe CosmosClientFactory), was NICHT nur C#-Properties,
    // sondern auch Dictionary<string,string>-Schlüssel verändert ("Rolle" -> "rolle").
    // Fields sind aber frei definierte fachliche Schlüssel (spaeter 1:1 gegen
    // Excel-Spaltennamen gematcht) — deren Original-Casing muss erhalten bleiben, ein
    // natives Dictionary<string,string>-Property würde das stillschweigend zerstören.
    [JsonPropertyName("fieldsJson")] public required string FieldsJson { get; init; }

    [JsonPropertyName("conditionJson")] public string? ConditionJson { get; init; }

    public static ScenarioResourceRuleDocument FromEntity(ScenarioResourceRule r) => new()
    {
        Id = r.Id,
        WorkloadScenarioId = r.WorkloadScenarioId,
        ResourceId = r.ResourceId,
        FieldsJson = JsonSerializer.Serialize(r.Fields),
        ConditionJson = r.Condition?.GetRawText(),
    };

    public ScenarioResourceRule ToEntity() => new()
    {
        Id = Id,
        WorkloadScenarioId = WorkloadScenarioId,
        ResourceId = ResourceId,
        Fields = string.IsNullOrEmpty(FieldsJson)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(FieldsJson) ?? new(),
        Condition = ConditionJson is null ? null : JsonDocument.Parse(ConditionJson).RootElement,
    };
}

internal sealed class WorkloadScenarioDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = nameof(WorkloadScenario);
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("workloadId")] public required Guid WorkloadId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("rules")] public required List<ScenarioResourceRuleDocument> Rules { get; init; }
    [JsonPropertyName("active")] public required bool Active { get; init; }
    [JsonPropertyName("createdAt")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public required DateTimeOffset UpdatedAt { get; init; }

    public static WorkloadScenarioDocument FromEntity(WorkloadScenario s) => new()
    {
        Id = s.Id.ToString(),
        PlatformTenantId = s.PlatformTenantId,
        WorkloadId = s.WorkloadId,
        Name = s.Name,
        Rules = [.. s.Rules.Select(ScenarioResourceRuleDocument.FromEntity)],
        Active = s.Active,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt,
    };

    public WorkloadScenario ToEntity()
    {
        var scenario = new WorkloadScenario
        {
            Id = Guid.Parse(Id),
            PlatformTenantId = PlatformTenantId,
            WorkloadId = WorkloadId,
            Name = Name,
            Active = Active,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
        scenario.Rules.AddRange(Rules.Select(r => r.ToEntity()));
        return scenario;
    }
}
