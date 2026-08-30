using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>
/// Cosmos-Implementierung von IMailSinkRepository, Container "discovery" (geteilt mit
/// CosmosMockEntraUserRepository/CosmosReminderPolicyRepository/CosmosResourceAccessRepository
/// — disambiguiert per entityType, dasselbe Muster wie CosmosJobRepository/CosmosJobQueue im
/// Container "jobs"). Ein Dokument pro versendeter Mail, damit der Mail Monitor unabhaengig
/// davon funktioniert, ob API oder Worker die Mail tatsaechlich versendet haben.
/// </summary>
public sealed class CosmosMailSinkRepository(CosmosClientFactory factory) : IMailSinkRepository
{
    private const string EntityType = "MailSinkEntry";
    private Container Container => factory.GetContainer("discovery");

    public Task AppendAsync(TenantContext tenant, EmailMessage message, DateTimeOffset sentAt, CancellationToken ct) =>
        Container.UpsertItemAsync(
            MailSinkDocument.FromMessage(tenant.PlatformTenantId, message, sentAt),
            new PartitionKey(tenant.PlatformTenantId),
            cancellationToken: ct);

    public async Task<IReadOnlyList<(EmailMessage Message, DateTimeOffset SentAt)>> ListAsync(
        TenantContext tenant, int take, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<MailSinkDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type " +
                "ORDER BY c.sentAt DESC OFFSET 0 LIMIT @take")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType)
                .WithParameter("@take", take),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<(EmailMessage, DateTimeOffset)>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => (d.ToMessage(), d.SentAt)));
        }
        return results;
    }
}

internal sealed class MailSinkDocument
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("entityType")] public string EntityType { get; init; } = "MailSinkEntry";
    [JsonPropertyName("platformTenantId")] public required string PlatformTenantId { get; init; }
    [JsonPropertyName("senderMailbox")] public required string SenderMailbox { get; init; }
    [JsonPropertyName("recipientMail")] public required string RecipientMail { get; init; }
    [JsonPropertyName("templateId")] public required string TemplateId { get; init; }
    [JsonPropertyName("templateData")] public required Dictionary<string, string> TemplateData { get; init; }
    [JsonPropertyName("correlationId")] public required Guid CorrelationId { get; init; }
    [JsonPropertyName("workloadContext")] public string? WorkloadContext { get; init; }
    [JsonPropertyName("sentAt")] public required DateTimeOffset SentAt { get; init; }

    public static MailSinkDocument FromMessage(string platformTenantId, EmailMessage m, DateTimeOffset sentAt) => new()
    {
        Id = $"mail-{Guid.NewGuid()}",
        PlatformTenantId = platformTenantId,
        SenderMailbox = m.SenderMailbox,
        RecipientMail = m.RecipientMail,
        TemplateId = m.TemplateId,
        TemplateData = new Dictionary<string, string>(m.TemplateData),
        CorrelationId = m.CorrelationId,
        WorkloadContext = m.WorkloadContext,
        SentAt = sentAt,
    };

    public EmailMessage ToMessage() => new(
        SenderMailbox, RecipientMail, TemplateId, TemplateData, CorrelationId, WorkloadContext, PlatformTenantId);
}
