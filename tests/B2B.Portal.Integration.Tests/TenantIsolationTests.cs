using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Data;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// Tenant-Isolation-Test mit mindestens zwei Platform-Tenants (Blueprint 22
/// "MVP-Abnahmekriterien" + MVP-Verification-Prompt Punkt 7). Ein Tenant darf keine
/// Daten eines anderen Tenant-/Scope-Kontexts lesen.
/// </summary>
public class TenantIsolationTests
{
    [Fact]
    public async Task GuestAccountRepository_TenantB_CannotReadTenantA_Guest()
    {
        var repo = new InMemoryGuestAccountRepository();

        var guestOfTenantA = new GuestAccount
        {
            PlatformTenantId = "tenant-a",
            DirectoryTenantId = "dir-a",
            Mail = "guest@tenant-a.example",
            DisplayName = "Guest A",
        };
        await repo.UpsertAsync(guestOfTenantA, CancellationToken.None);

        var readAsTenantB = await repo.GetAsync(TenantContext.Create("tenant-b"), guestOfTenantA.Id, CancellationToken.None);

        Assert.Null(readAsTenantB);
    }

    [Fact]
    public async Task GuestAccountRepository_ListAsync_OnlyReturnsOwnTenantData()
    {
        var repo = new InMemoryGuestAccountRepository();

        await repo.UpsertAsync(new GuestAccount
        {
            PlatformTenantId = "tenant-a", DirectoryTenantId = "dir-a",
            Mail = "a@tenant-a.example", DisplayName = "A",
        }, CancellationToken.None);

        await repo.UpsertAsync(new GuestAccount
        {
            PlatformTenantId = "tenant-b", DirectoryTenantId = "dir-b",
            Mail = "b@tenant-b.example", DisplayName = "B",
        }, CancellationToken.None);

        var tenantAGuests = await repo.ListAsync(TenantContext.Create("tenant-a"), CancellationToken.None);
        var tenantBGuests = await repo.ListAsync(TenantContext.Create("tenant-b"), CancellationToken.None);

        Assert.Single(tenantAGuests);
        Assert.Single(tenantBGuests);
        Assert.All(tenantAGuests, g => Assert.Equal("tenant-a", g.PlatformTenantId));
        Assert.All(tenantBGuests, g => Assert.Equal("tenant-b", g.PlatformTenantId));
    }

    [Fact]
    public async Task AuditWriter_QueryAsync_IsolatesEventsByTenant()
    {
        var writer = new InMemoryAuditWriter();

        await writer.WriteAsync(new AuditEvent
        {
            PlatformTenantId = "tenant-a", Actor = "x", Action = "Test",
            EntityType = "Guest", EntityId = "1", Result = "Success",
        }, CancellationToken.None);

        await writer.WriteAsync(new AuditEvent
        {
            PlatformTenantId = "tenant-b", Actor = "x", Action = "Test",
            EntityType = "Guest", EntityId = "2", Result = "Success",
        }, CancellationToken.None);

        var eventsForA = await writer.QueryAsync(TenantContext.Create("tenant-a"), 100, CancellationToken.None);

        Assert.Single(eventsForA);
        Assert.Equal("tenant-a", eventsForA[0].PlatformTenantId);
    }
}
