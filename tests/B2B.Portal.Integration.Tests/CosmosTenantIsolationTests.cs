using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Data.Cosmos;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// Cosmos-Variante von TenantIsolationTests — dieselben Garantien, aber gegen den echten
/// lokalen Cosmos DB Emulator statt InMemory-Repositories. Übersprungen (frühes return),
/// wenn kein Emulator läuft (siehe CosmosEmulatorAvailability) — dotnet test bleibt CI-sicher.
/// Nutzt pro Testlauf eindeutige Tenant-IDs (Guid-Suffix), damit parallele/wiederholte
/// Testläufe sich nicht gegenseitig über bereits vorhandene Cosmos-Dokumente stören.
/// </summary>
public class CosmosTenantIsolationTests
{
    private static readonly bool EmulatorAvailable = CosmosEmulatorAvailability.IsRunning();

    private static CosmosClientFactory BuildFactory()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["COSMOS_EMULATOR_ENDPOINT"] = "https://localhost:8081",
                ["COSMOS_EMULATOR_KEY"] =
                    "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
                ["COSMOS_DATABASE_ID"] = "b2b-governance-dev",
            })
            .Build();
        return new CosmosClientFactory(config);
    }

    [Fact]
    public async Task GuestAccountRepository_TenantB_CannotReadTenantA_Guest()
    {
        if (!EmulatorAvailable) { return; }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var repo = new CosmosGuestAccountRepository(BuildFactory());

        var guestOfTenantA = new GuestAccount
        {
            PlatformTenantId = $"cosmos-tenant-a-{suffix}",
            DirectoryTenantId = "dir-a",
            Mail = "guest@tenant-a.example",
            DisplayName = "Guest A",
        };
        await repo.UpsertAsync(guestOfTenantA, CancellationToken.None);

        var readAsTenantB = await repo.GetAsync(
            TenantContext.Create($"cosmos-tenant-b-{suffix}"), guestOfTenantA.Id, CancellationToken.None);

        Assert.Null(readAsTenantB);
    }

    [Fact]
    public async Task GuestAccountRepository_ListAsync_OnlyReturnsOwnTenantData()
    {
        if (!EmulatorAvailable) { return; }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantA = $"cosmos-tenant-a-{suffix}";
        var tenantB = $"cosmos-tenant-b-{suffix}";
        var repo = new CosmosGuestAccountRepository(BuildFactory());

        await repo.UpsertAsync(new GuestAccount
        {
            PlatformTenantId = tenantA, DirectoryTenantId = "dir-a",
            Mail = "a@tenant-a.example", DisplayName = "A",
        }, CancellationToken.None);

        await repo.UpsertAsync(new GuestAccount
        {
            PlatformTenantId = tenantB, DirectoryTenantId = "dir-b",
            Mail = "b@tenant-b.example", DisplayName = "B",
        }, CancellationToken.None);

        var tenantAGuests = await repo.ListAsync(TenantContext.Create(tenantA), CancellationToken.None);
        var tenantBGuests = await repo.ListAsync(TenantContext.Create(tenantB), CancellationToken.None);

        Assert.Single(tenantAGuests);
        Assert.Single(tenantBGuests);
        Assert.All(tenantAGuests, g => Assert.Equal(tenantA, g.PlatformTenantId));
        Assert.All(tenantBGuests, g => Assert.Equal(tenantB, g.PlatformTenantId));
    }

    [Fact]
    public async Task AuditWriter_QueryAsync_IsolatesEventsByTenant()
    {
        if (!EmulatorAvailable) { return; }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantA = $"cosmos-tenant-a-{suffix}";
        var tenantB = $"cosmos-tenant-b-{suffix}";
        var writer = new CosmosAuditWriter(BuildFactory());

        await writer.WriteAsync(new AuditEvent
        {
            PlatformTenantId = tenantA, Actor = "x", Action = "Test",
            EntityType = "Guest", EntityId = "1", Result = "Success",
        }, CancellationToken.None);

        await writer.WriteAsync(new AuditEvent
        {
            PlatformTenantId = tenantB, Actor = "x", Action = "Test",
            EntityType = "Guest", EntityId = "2", Result = "Success",
        }, CancellationToken.None);

        var eventsForA = await writer.QueryAsync(TenantContext.Create(tenantA), 100, CancellationToken.None);

        Assert.Single(eventsForA);
        Assert.Equal(tenantA, eventsForA[0].PlatformTenantId);
    }
}
