using B2B.Portal.Application.Commands;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Data;
using B2B.Portal.Infrastructure.Data.Cosmos;
using B2B.Portal.Infrastructure.Queue;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace B2B.Portal.Application.Tests;

/// <summary>
/// Idempotenztest für GrantWorkloadRole (MVP-Dokument, Abschnitt "TESTS / QUALITY GATES"),
/// gegen den echten lokalen Cosmos DB Emulator (InMemory-Repositories entfernt). Derselbe
/// Grant darf keinen doppelten technischen Zustand erzeugen: ein zweiter Aufruf mit
/// identischem Gast/Workload/Rolle gibt das bestehende aktive Assignment zurück und legt
/// keinen zweiten Job an. Übersprungen (frühes return), wenn kein Emulator läuft (siehe
/// CosmosEmulatorAvailability) — dotnet test bleibt CI-sicher. Nutzt pro Testlauf eine
/// eindeutige Tenant-ID (Guid-Suffix), damit parallele/wiederholte Testläufe sich nicht
/// gegenseitig über bereits vorhandene Cosmos-Dokumente stören.
/// </summary>
public class GrantWorkloadRoleIdempotencyTests
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

    private static GrantWorkloadRoleCommandHandler BuildHandler(
        CosmosAssignmentRepository assignmentRepo, CosmosJobQueue queue, CosmosClientFactory factory)
    {
        var jobRepo = new CosmosJobRepository(factory);
        var clock = new SystemClock();
        var provisioning = new ProvisioningService(jobRepo, queue, clock);
        var auditService = new AuditService(new CosmosAuditWriter(factory), clock);
        return new GrantWorkloadRoleCommandHandler(assignmentRepo, provisioning, auditService);
    }

    [Fact]
    public async Task HandleAsync_CalledTwice_DoesNotCreateSecondActiveAssignment()
    {
        if (!EmulatorAvailable) { return; }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = $"tenant-a-{suffix}";
        var factory = BuildFactory();
        var assignmentRepo = new CosmosAssignmentRepository(factory);
        var queue = new CosmosJobQueue(factory);
        var handler = BuildHandler(assignmentRepo, queue, factory);

        var request = new GrantWorkloadRoleRequest(
            tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "tester");

        var first = await handler.HandleAsync(request, CancellationToken.None);

        // Simuliere, dass der Worker das erste Assignment bereits aktiv gesetzt hat.
        first.Status = AssignmentStatus.Active;
        await assignmentRepo.UpsertAsync(first, CancellationToken.None);

        var second = await handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);

        var allForGuest = await assignmentRepo.ListByGuestAsync(
            TenantContext.Create(tenantId), request.GuestId, CancellationToken.None);
        Assert.Single(allForGuest);
    }
}
