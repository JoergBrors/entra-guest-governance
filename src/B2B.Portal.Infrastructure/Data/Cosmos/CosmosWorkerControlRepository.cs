using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>
/// Cosmos-Implementierung von IWorkerControlRepository, Container "jobs" (geteilt mit
/// CosmosJobRepository/CosmosJobQueue — disambiguiert per entityType, dasselbe Muster). Worker-
/// Status ist global (nicht tenant-gebunden — ein periodischer BackgroundService laeuft je
/// Worker-Prozess unabhaengig von einem Platform-Tenant), daher fester Partitions-Platzhalter
/// "worker-control" statt einer echten platformTenantId (der Container ist ueberall im Projekt
/// mit Partition-Key-Pfad "/platformTenantId" angelegt, siehe infra/modules/
/// cosmos-free-tier.bicep — jedes Dokument braucht daher trotzdem dieses Feld).
/// </summary>
public sealed class CosmosWorkerControlRepository(CosmosClientFactory factory) : IWorkerControlRepository
{
    private const string EntityType = "WorkerControlState";
    private const string FixedPartition = "worker-control";
    private static readonly PartitionKey Partition = new(FixedPartition);
    private Container Container => factory.GetContainer("jobs");

    public async Task<IReadOnlyList<WorkerControlState>> ListAllAsync(CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<WorkerControlStateDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.entityType = @type")
                .WithParameter("@type", EntityType),
            requestOptions: new QueryRequestOptions { PartitionKey = Partition });

        var results = new List<WorkerControlState>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToRecord()));
        }
        return results;
    }

    public async Task<WorkerControlState?> GetAsync(string workerName, CancellationToken ct)
    {
        try
        {
            var response = await Container.ReadItemAsync<WorkerControlStateDocument>(
                WorkerControlStateDocument.BuildId(workerName), Partition, cancellationToken: ct);
            return response.Resource.ToRecord();
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task UpsertAsync(WorkerControlState state, CancellationToken ct) =>
        Container.UpsertItemAsync(WorkerControlStateDocument.FromRecord(state), Partition, cancellationToken: ct);
}

internal sealed class WorkerControlStateDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = "WorkerControlState";
    [JsonPropertyName("platformTenantId")] public string PlatformTenantId { get; init; } = "worker-control";
    [JsonPropertyName("workerName")] public required string WorkerName { get; init; }
    [JsonPropertyName("isPaused")] public bool IsPaused { get; init; }
    [JsonPropertyName("pausedBy")] public string? PausedBy { get; init; }
    [JsonPropertyName("pausedAt")] public DateTimeOffset? PausedAt { get; init; }
    [JsonPropertyName("lastRunStartedAt")] public DateTimeOffset? LastRunStartedAt { get; init; }
    [JsonPropertyName("lastRunCompletedAt")] public DateTimeOffset? LastRunCompletedAt { get; init; }
    [JsonPropertyName("lastRunSucceeded")] public bool? LastRunSucceeded { get; init; }
    [JsonPropertyName("lastRunSummary")] public string? LastRunSummary { get; init; }
    [JsonPropertyName("lastTriggeredBy")] public string? LastTriggeredBy { get; init; }
    [JsonPropertyName("triggerRequestedAt")] public DateTimeOffset? TriggerRequestedAt { get; init; }
    [JsonPropertyName("triggerRequestedBy")] public string? TriggerRequestedBy { get; init; }

    public static string BuildId(string workerName) => $"worker-control-{workerName}";

    public static WorkerControlStateDocument FromRecord(WorkerControlState s) => new()
    {
        Id = BuildId(s.WorkerName),
        WorkerName = s.WorkerName,
        IsPaused = s.IsPaused,
        PausedBy = s.PausedBy,
        PausedAt = s.PausedAt,
        LastRunStartedAt = s.LastRunStartedAt,
        LastRunCompletedAt = s.LastRunCompletedAt,
        LastRunSucceeded = s.LastRunSucceeded,
        LastRunSummary = s.LastRunSummary,
        LastTriggeredBy = s.LastTriggeredBy,
        TriggerRequestedAt = s.TriggerRequestedAt,
        TriggerRequestedBy = s.TriggerRequestedBy,
    };

    public WorkerControlState ToRecord() => new(
        WorkerName, IsPaused, PausedBy, PausedAt, LastRunStartedAt, LastRunCompletedAt,
        LastRunSucceeded, LastRunSummary, LastTriggeredBy, TriggerRequestedAt, TriggerRequestedBy);
}
