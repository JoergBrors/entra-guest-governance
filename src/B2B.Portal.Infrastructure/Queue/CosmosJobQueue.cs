using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Infrastructure.Data.Cosmos;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Queue;

/// <summary>
/// Cosmos-Implementierung von IJobQueue, Container "jobs" (dort liegen ausserdem die
/// DirectoryOperation-Dokumente von CosmosJobRepository — disambiguiert per entityType).
/// Cosmos kennt kein natives FIFO-Dequeue-mit-Lock, daher: Status-Feld
/// (Pending -&gt; Leased -&gt; Done/DeadLetter) plus optimistische Nebenläufigkeitskontrolle
/// über ETag-conditional Replace, mit LeaseExpiresAt und Reclaim abgelaufener Leases beim
/// Dequeue. Kein Change Feed — würde eine dauerhaft laufende Listener-Infrastruktur
/// brauchen, unverhältnismäßig für den Single-Worker-Prozess (ADR-0001).
/// </summary>
public sealed class CosmosJobQueue(CosmosClientFactory factory) : IJobQueue
{
    private const string EntityType = "JobEnvelope";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private const int MaxDequeueCandidates = 5;

    private Container Container => factory.GetContainer("jobs");

    public Task EnqueueAsync(JobEnvelope job, CancellationToken ct) =>
        // UpsertItemAsync statt CreateItemAsync: ProvisioningService.EnqueueJobAsync legt
        // vorab per IJobRepository.UpsertAsync bereits ein DirectoryOperation-Dokument mit
        // derselben Id im selben "jobs"-Container an (siehe Klassenkommentar) — CreateItemAsync
        // wuerde dort mit 409 Conflict scheitern.
        Container.UpsertItemAsync(
            JobEnvelopeDocument.FromEnvelope(job, status: "Pending", leaseExpiresAt: null, attemptCount: 0),
            new PartitionKey(job.PlatformTenantId),
            cancellationToken: ct);

    public async Task<JobEnvelope?> DequeueAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Cross-Partition-Query: der Worker verarbeitet Jobs aller Tenants (eine
        // gemeinsame Queue). Kandidaten: entweder Pending, oder
        // Leased mit abgelaufener Lease (Reclaim eines verwaisten Claims).
        var query = Container.GetItemQueryIterator<JobEnvelopeDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.entityType = @type AND (c.status = @pending " +
                "OR (c.status = @leased AND c.leaseExpiresAt < @now)) " +
                "ORDER BY c.createdAt ASC OFFSET 0 LIMIT @limit")
                .WithParameter("@type", EntityType)
                .WithParameter("@pending", "Pending")
                .WithParameter("@leased", "Leased")
                .WithParameter("@now", now)
                .WithParameter("@limit", MaxDequeueCandidates));

        var page = await query.ReadNextAsync(ct);

        foreach (var candidate in page)
        {
            var claimed = await TryClaimAsync(candidate, now, ct);
            if (claimed is not null)
            {
                return claimed;
            }
        }

        return null;
    }

    private async Task<JobEnvelope?> TryClaimAsync(JobEnvelopeDocument candidate, DateTimeOffset now, CancellationToken ct)
    {
        var updated = candidate with
        {
            Status = "Leased",
            LeaseExpiresAt = now + LeaseDuration,
        };

        try
        {
            await Container.ReplaceItemAsync(
                updated, candidate.Id, new PartitionKey(candidate.PlatformTenantId),
                new ItemRequestOptions { IfMatchEtag = candidate.ETag }, cancellationToken: ct);
            return updated.ToEnvelope();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // Ein anderer Worker/Dispatcher-Durchlauf hat den Job zwischenzeitlich
            // geclaimt — auf den naechsten Kandidaten ausweichen statt zu scheitern.
            return null;
        }
    }

    public async Task CompleteAsync(Guid jobId, CancellationToken ct)
    {
        var doc = await FindByJobIdAsync(jobId, ct);
        if (doc is null) return;

        var updated = doc with { Status = "Done", LeaseExpiresAt = null };
        await Container.ReplaceItemAsync(
            updated, doc.Id, new PartitionKey(doc.PlatformTenantId),
            new ItemRequestOptions { IfMatchEtag = doc.ETag }, cancellationToken: ct);
    }

    public async Task CancelAsync(Guid jobId, CancellationToken ct)
    {
        var doc = await FindByJobIdAsync(jobId, ct);
        if (doc is null) return;

        var updated = doc with { Status = "Cancelled", LeaseExpiresAt = null };
        await Container.ReplaceItemAsync(
            updated, doc.Id, new PartitionKey(doc.PlatformTenantId),
            new ItemRequestOptions { IfMatchEtag = doc.ETag }, cancellationToken: ct);
    }

    public async Task<int> RetryAsync(Guid jobId, string error, CancellationToken ct)
    {
        var doc = await FindByJobIdAsync(jobId, ct);
        if (doc is null) return 0;

        var newAttemptCount = doc.AttemptCount + 1;
        var updated = doc with { Status = "Pending", LeaseExpiresAt = null, AttemptCount = newAttemptCount };
        await Container.ReplaceItemAsync(
            updated, doc.Id, new PartitionKey(doc.PlatformTenantId),
            new ItemRequestOptions { IfMatchEtag = doc.ETag }, cancellationToken: ct);

        return newAttemptCount;
    }

    public async Task DeadLetterAsync(Guid jobId, string error, CancellationToken ct)
    {
        var doc = await FindByJobIdAsync(jobId, ct);
        if (doc is null) return;

        var updated = doc with { Status = "DeadLetter", LeaseExpiresAt = null };
        await Container.ReplaceItemAsync(
            updated, doc.Id, new PartitionKey(doc.PlatformTenantId),
            new ItemRequestOptions { IfMatchEtag = doc.ETag }, cancellationToken: ct);
    }

    private async Task<JobEnvelopeDocument?> FindByJobIdAsync(Guid jobId, CancellationToken ct)
    {
        // JobId ist die Cosmos-Dokument-Id (siehe FromEnvelope) - Cross-Partition-Point-Read
        // ueber eine ID-Query, da die Partition (platformTenantId) hier nicht bekannt ist.
        var query = Container.GetItemQueryIterator<JobEnvelopeDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.id = @id AND c.entityType = @type")
                .WithParameter("@id", jobId.ToString())
                .WithParameter("@type", EntityType));

        var page = await query.ReadNextAsync(ct);
        return page.FirstOrDefault();
    }
}

internal sealed record JobEnvelopeDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = "JobEnvelope";
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("directoryTenantId")] public string? DirectoryTenantId { get; init; }
    [JsonPropertyName("jobType")] public required string JobType { get; init; }
    [JsonPropertyName("entityTypeName")] public required string EntityTypeName { get; init; }
    [JsonPropertyName("targetEntityId")] public required string TargetEntityId { get; init; }
    [JsonPropertyName("correlationId")] public required Guid CorrelationId { get; init; }
    [JsonPropertyName("desiredStateHash")] public required string DesiredStateHash { get; init; }
    [JsonPropertyName("createdAt")] public required DateTimeOffset CreatedAt { get; init; }
    // Als Raw-JSON-String statt JsonElement gespeichert: JsonElement ist eng an den
    // System.Text.Json-Reader gebunden und wird bei der Cosmos-SDK-eigenen (De-)Serialisierung
    // nicht zuverlaessig rehydriert (beobachtet: GetProperty() auf einem aus Cosmos gelesenen
    // JobEnvelope wirft "Operation is not valid due to the current state of the object").
    [JsonPropertyName("payload")] public required string PayloadJson { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("leaseExpiresAt")] public DateTimeOffset? LeaseExpiresAt { get; init; }
    [JsonPropertyName("attemptCount")] public required int AttemptCount { get; init; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }

    public static JobEnvelopeDocument FromEnvelope(
        JobEnvelope job, string status, DateTimeOffset? leaseExpiresAt, int attemptCount) => new()
    {
        Id = job.JobId.ToString(),
        PlatformTenantId = job.PlatformTenantId,
        DirectoryTenantId = job.DirectoryTenantId,
        JobType = job.JobType,
        EntityTypeName = job.EntityType,
        TargetEntityId = job.EntityId,
        CorrelationId = job.CorrelationId,
        DesiredStateHash = job.DesiredStateHash,
        CreatedAt = job.CreatedAt,
        PayloadJson = job.Payload.GetRawText(),
        Status = status,
        LeaseExpiresAt = leaseExpiresAt,
        AttemptCount = attemptCount,
    };

    public JobEnvelope ToEnvelope() => new(
        Guid.Parse(Id), PlatformTenantId, DirectoryTenantId, JobType, EntityTypeName,
        TargetEntityId, CorrelationId, DesiredStateHash, CreatedAt,
        JsonDocument.Parse(PayloadJson).RootElement);
}
