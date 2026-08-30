using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>
/// Cosmos-Implementierung von IJobRepository, Container "jobs" (dort liegen ausserdem die
/// JobEnvelope-Transportdokumente von CosmosJobQueue — disambiguiert per entityType UND per
/// Cosmos-Dokument-Id: JobEnvelopeDocument nutzt seit dem Ueberschreib-Bug (siehe dortiger
/// Kommentar) das Praefix "envelope-{jobId}" statt derselben Id wie DirectoryOperation).
/// </summary>
public sealed class CosmosJobRepository(CosmosClientFactory factory) : IJobRepository
{
    private const string EntityType = nameof(DirectoryOperation);
    private Container Container => factory.GetContainer("jobs");

    private static readonly HashSet<string> SecurityRelevantJobTypes =
    [
        JobTypes.GrantWorkloadRole, JobTypes.RevokeWorkloadRole, JobTypes.DisableGuest,
        JobTypes.DeleteGuest, JobTypes.ValidateDeletion,
    ];

    public async Task<DirectoryOperation?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        // Kein ReadItemAsync per Point-Read: JobEnvelope (CosmosJobQueue) und
        // DirectoryOperation teilen dieselbe Id im selben Container (siehe Klassenkommentar).
        // Ein Point-Read kennt kein zusaetzliches Filterpraedikat und deserialisiert die
        // Rohantwort direkt in DirectoryOperationDocument, BEVOR der EntityType geprueft
        // werden kann — trifft er zuerst auf das JobEnvelope-Dokument (z.B. status "Leased",
        // kein gueltiger JobStatus-Enum-Wert), wirft der System.Text.Json-EnumConverter statt
        // eines sauberen Null-Ergebnisses. Daher per Query mit entityType-Filter lesen, wie
        // die uebrigen Methoden dieser Klasse es bereits tun.
        var query = Container.GetItemQueryIterator<DirectoryOperationDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.id = @id AND c.entityType = @type")
                .WithParameter("@id", id.ToString())
                .WithParameter("@type", EntityType),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var page = await query.ReadNextAsync(ct);
        return page.FirstOrDefault()?.ToEntity();
    }

    public async Task<IReadOnlyList<DirectoryOperation>> ListOpenSecurityRelevantAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<DirectoryOperationDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type " +
                "AND c.entityId = @guestId AND c.status IN (@pending, @running, @retry)")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType)
                .WithParameter("@guestId", guestId.ToString())
                .WithParameter("@pending", nameof(JobStatus.Pending))
                .WithParameter("@running", nameof(JobStatus.Running))
                .WithParameter("@retry", nameof(JobStatus.Retry)),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<DirectoryOperation>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity())
                .Where(op => SecurityRelevantJobTypes.Contains(op.JobType)));
        }
        return results;
    }

    public async Task<IReadOnlyList<DirectoryOperation>> ListAsync(TenantContext tenant, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<DirectoryOperationDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type ORDER BY c.createdAt DESC")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<DirectoryOperation>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    public Task UpsertAsync(DirectoryOperation job, CancellationToken ct) =>
        Container.UpsertItemAsync(
            DirectoryOperationDocument.FromEntity(job),
            new PartitionKey(job.PlatformTenantId),
            cancellationToken: ct);
}

internal sealed class DirectoryOperationDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = nameof(DirectoryOperation);
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("directoryTenantId")] public string? DirectoryTenantId { get; init; }
    [JsonPropertyName("jobType")] public required string JobType { get; init; }
    [JsonPropertyName("entityTypeName")] public required string EntityTypeName { get; init; }
    [JsonPropertyName("entityId")] public required string EntityId { get; init; }
    [JsonPropertyName("triggeredBy")] public string? TriggeredBy { get; init; }
    [JsonPropertyName("workloadId")] public Guid? WorkloadId { get; init; }
    [JsonPropertyName("correlationId")] public required Guid CorrelationId { get; init; }
    [JsonPropertyName("desiredStateHash")] public required string DesiredStateHash { get; init; }
    [JsonPropertyName("status")] public required JobStatus Status { get; init; }
    [JsonPropertyName("retryCount")] public required int RetryCount { get; init; }
    [JsonPropertyName("lastError")] public string? LastError { get; init; }
    [JsonPropertyName("createdAt")] public required DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public required DateTimeOffset UpdatedAt { get; init; }
    [JsonPropertyName("log")] public List<JobLogEntryDocument> Log { get; init; } = [];

    public static DirectoryOperationDocument FromEntity(DirectoryOperation op) => new()
    {
        Id = op.Id.ToString(),
        PlatformTenantId = op.PlatformTenantId,
        DirectoryTenantId = op.DirectoryTenantId,
        JobType = op.JobType,
        EntityTypeName = op.EntityType,
        EntityId = op.EntityId,
        TriggeredBy = op.TriggeredBy,
        WorkloadId = op.WorkloadId,
        CorrelationId = op.CorrelationId,
        DesiredStateHash = op.DesiredStateHash,
        Status = op.Status,
        RetryCount = op.RetryCount,
        LastError = op.LastError,
        CreatedAt = op.CreatedAt,
        UpdatedAt = op.UpdatedAt,
        Log = [.. op.Log.Select(l => new JobLogEntryDocument(l.Timestamp, l.Status, l.Message))],
    };

    public DirectoryOperation ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        PlatformTenantId = PlatformTenantId,
        DirectoryTenantId = DirectoryTenantId,
        JobType = JobType,
        EntityType = EntityTypeName,
        EntityId = EntityId,
        TriggeredBy = TriggeredBy,
        WorkloadId = WorkloadId,
        CorrelationId = CorrelationId,
        DesiredStateHash = DesiredStateHash,
        Status = Status,
        RetryCount = RetryCount,
        LastError = LastError,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        Log = [.. Log.Select(l => new JobLogEntry(l.Timestamp, l.Status, l.Message))],
    };
}

internal sealed record JobLogEntryDocument(
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("status")] JobStatus Status,
    [property: JsonPropertyName("message")] string? Message);
