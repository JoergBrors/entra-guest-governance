using System.Net;
using System.Text.Json.Serialization;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Azure.Cosmos;

namespace B2B.Portal.Infrastructure.Data.Cosmos;

/// <summary>
/// Cosmos-Implementierung von IGuestAccountRepository, Container "domain" (siehe
/// infra/modules/cosmos-free-tier.bicep). Dokumentkonvention: id = Guid.ToString(),
/// entityType-Discriminator, platformTenantId als Partition Key. Alle anderen
/// Cosmos-Repositories in diesem Ordner folgen demselben Muster.
/// </summary>
public sealed class CosmosGuestAccountRepository(CosmosClientFactory factory) : IGuestAccountRepository
{
    private const string EntityType = nameof(GuestAccount);
    private Container Container => factory.GetContainer("domain");

    public async Task<GuestAccount?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        try
        {
            var response = await Container.ReadItemAsync<GuestAccountDocument>(
                id.ToString(), new PartitionKey(tenant.PlatformTenantId), cancellationToken: ct);
            return response.Resource.EntityType == EntityType ? response.Resource.ToEntity() : null;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<GuestAccount?> GetByMailAsync(TenantContext tenant, string mail, CancellationToken ct)
    {
        // Case-insensitiver Vergleich per UPPER() beidseitig, da Cosmos SQL keine
        // eingebaute Ordinal-Ignore-Case-Funktion kennt — dieselbe Konvention wie
        // CosmosExternalOrganizationRepository.GetByNameAsync (dort clientseitig gefiltert;
        // hier serverseitig, da GuestAccounts deutlich zahlreicher sein können).
        var query = Container.GetItemQueryIterator<GuestAccountDocument>(
            new QueryDefinition(
                "SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type " +
                "AND UPPER(c.mail) = UPPER(@mail)")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType)
                .WithParameter("@mail", mail),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            var match = page.FirstOrDefault();
            if (match is not null)
            {
                return match.ToEntity();
            }
        }
        return null;
    }

    public async Task<IReadOnlyList<GuestAccount>> ListAsync(TenantContext tenant, CancellationToken ct)
    {
        var query = Container.GetItemQueryIterator<GuestAccountDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.platformTenantId = @tenant AND c.entityType = @type")
                .WithParameter("@tenant", tenant.PlatformTenantId)
                .WithParameter("@type", EntityType),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(tenant.PlatformTenantId) });

        var results = new List<GuestAccount>();
        while (query.HasMoreResults)
        {
            var page = await query.ReadNextAsync(ct);
            results.AddRange(page.Select(d => d.ToEntity()));
        }
        return results;
    }

    public Task UpsertAsync(GuestAccount guest, CancellationToken ct) =>
        Container.UpsertItemAsync(
            GuestAccountDocument.FromEntity(guest),
            new PartitionKey(guest.PlatformTenantId),
            cancellationToken: ct);
}

internal sealed class GuestAccountDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("entityType")]
    public string EntityType { get; init; } = nameof(GuestAccount);

    [JsonPropertyName("platformTenantId")]
    public required string PlatformTenantId { get; init; }

    [JsonPropertyName("directoryTenantId")]
    public required string DirectoryTenantId { get; init; }

    [JsonPropertyName("entraObjectId")]
    public string? EntraObjectId { get; init; }

    [JsonPropertyName("mail")]
    public required string Mail { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("externalOrganizationId")]
    public Guid? ExternalOrganizationId { get; init; }

    [JsonPropertyName("sponsor")]
    public string? Sponsor { get; init; }

    [JsonPropertyName("userType")]
    public string? UserType { get; init; }

    [JsonPropertyName("accountState")]
    public required GuestAccountState AccountState { get; init; }

    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public required DateTimeOffset UpdatedAt { get; init; }

    // Erweiterung 2026-08-30 "Invitation Reminder Worker" — optional/nullable, damit
    // bestehende Cosmos-Dokumente ohne diese Felder weiterhin fehlerfrei deserialisieren.
    [JsonPropertyName("invitationRedemptionLink")]
    public string? InvitationRedemptionLink { get; init; }

    [JsonPropertyName("lastReminderStageSent")]
    public int? LastReminderStageSent { get; init; }

    [JsonPropertyName("lastReminderSentAt")]
    public DateTimeOffset? LastReminderSentAt { get; init; }

    public static GuestAccountDocument FromEntity(GuestAccount g) => new()
    {
        Id = g.Id.ToString(),
        PlatformTenantId = g.PlatformTenantId,
        DirectoryTenantId = g.DirectoryTenantId,
        EntraObjectId = g.EntraObjectId,
        Mail = g.Mail,
        DisplayName = g.DisplayName,
        ExternalOrganizationId = g.ExternalOrganizationId,
        Sponsor = g.Sponsor,
        UserType = g.UserType,
        AccountState = g.AccountState,
        CreatedAt = g.CreatedAt,
        UpdatedAt = g.UpdatedAt,
        InvitationRedemptionLink = g.InvitationRedemptionLink,
        LastReminderStageSent = g.LastReminderStageSent,
        LastReminderSentAt = g.LastReminderSentAt,
    };

    public GuestAccount ToEntity()
    {
        var guest = new GuestAccount
        {
            Id = Guid.Parse(Id),
            PlatformTenantId = PlatformTenantId,
            DirectoryTenantId = DirectoryTenantId,
            EntraObjectId = EntraObjectId,
            Mail = Mail,
            DisplayName = DisplayName,
            ExternalOrganizationId = ExternalOrganizationId,
            Sponsor = Sponsor,
            UserType = string.IsNullOrWhiteSpace(UserType) ? "Guest" : UserType,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            InvitationRedemptionLink = InvitationRedemptionLink,
            LastReminderStageSent = LastReminderStageSent,
            LastReminderSentAt = LastReminderSentAt,
        };

        // AccountState hat einen private setter (GuestAccount.TransitionTo erzwingt die
        // Governance-Core-Invariante) — beim Rehydrieren aus Cosmos wird der Zustand daher
        // ueber denselben Weg wie in der Domain gesetzt, mit viaGovernanceCore:true, da hier
        // kein fachlicher Übergang stattfindet, sondern nur ein Laden des bereits
        // gespeicherten Zustands aus der Persistenz.
        guest.TransitionTo(AccountState, viaGovernanceCore: true);
        return guest;
    }
}
