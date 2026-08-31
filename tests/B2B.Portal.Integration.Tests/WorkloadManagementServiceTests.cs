using B2B.Portal.Application.Services;
using B2B.Portal.Application.Workloads;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Data;
using B2B.Portal.Infrastructure.Data.Cosmos;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// Testet die Konsistenzprüfungen von WorkloadManagementService gegen den echten lokalen
/// Cosmos DB Emulator (InMemory-Repositories entfernt): eine WorkloadRole mit aktiven
/// Assignments darf nicht gelöscht werden, eine WorkloadResource, die noch von einer Rolle
/// oder einem Szenario referenziert wird, darf nicht gelöscht werden. Ohne solche
/// Referenzen funktioniert Löschen normal. Übersprungen (frühes return), wenn kein
/// Emulator läuft (siehe CosmosEmulatorAvailability) — dotnet test bleibt CI-sicher. Nutzt
/// pro Testlauf eindeutige Tenant-IDs (Guid-Suffix), damit parallele/wiederholte
/// Testläufe sich nicht gegenseitig über bereits vorhandene Cosmos-Dokumente stören.
/// </summary>
public class WorkloadManagementServiceTests
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

    private static (WorkloadManagementService Service, CosmosWorkloadRepository WorkloadRepo,
        CosmosAssignmentRepository AssignmentRepo, CosmosWorkloadScenarioRepository ScenarioRepo) Build()
    {
        var factory = BuildFactory();
        var workloadRepo = new CosmosWorkloadRepository(factory);
        var scenarioRepo = new CosmosWorkloadScenarioRepository(factory);
        var assignmentRepo = new CosmosAssignmentRepository(factory);
        var auditService = new AuditService(new CosmosAuditWriter(factory), new SystemClock());
        var service = new WorkloadManagementService(workloadRepo, scenarioRepo, assignmentRepo, auditService);
        return (service, workloadRepo, assignmentRepo, scenarioRepo);
    }

    [Fact]
    public async Task DeleteRole_WithActiveAssignment_Throws()
    {
        if (!EmulatorAvailable) { return; }

        var (service, workloadRepo, assignmentRepo, _) = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"workload-mgmt-tenant-1-{suffix}");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "W1" };
        var role = new WorkloadRole { WorkloadId = workload.Id, Name = "Reader" };
        workload.Roles.Add(role);
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        var assignment = new GuestWorkloadAssignment
        {
            PlatformTenantId = tenant.PlatformTenantId,
            GuestId = Guid.NewGuid(),
            WorkloadId = workload.Id,
            RoleId = role.Id,
            Status = AssignmentStatus.Active,
        };
        await assignmentRepo.UpsertAsync(assignment, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteRoleAsync(tenant, workload.Id, role.Id, "test", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRole_WithoutAssignments_Succeeds()
    {
        if (!EmulatorAvailable) { return; }

        var (service, workloadRepo, _, _) = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"workload-mgmt-tenant-2-{suffix}");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "W2" };
        var role = new WorkloadRole { WorkloadId = workload.Id, Name = "Reader" };
        workload.Roles.Add(role);
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        await service.DeleteRoleAsync(tenant, workload.Id, role.Id, "test", CancellationToken.None);

        var reloaded = await workloadRepo.GetAsync(tenant, workload.Id, CancellationToken.None);
        Assert.Empty(reloaded!.Roles);
    }

    [Fact]
    public async Task DeleteResource_ReferencedByRole_Throws()
    {
        if (!EmulatorAvailable) { return; }

        var (service, workloadRepo, _, _) = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"workload-mgmt-tenant-3-{suffix}");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "W3" };
        var resource = new WorkloadResource { WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "SG-1" };
        var role = new WorkloadRole { WorkloadId = workload.Id, Name = "Reader" };
        role.ResourceMappings.Add(resource.Id);
        workload.Resources.Add(resource);
        workload.Roles.Add(role);
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteResourceAsync(tenant, workload.Id, resource.Id, "test", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteResource_ReferencedByScenarioRule_Throws()
    {
        if (!EmulatorAvailable) { return; }

        var (service, workloadRepo, _, scenarioRepo) = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"workload-mgmt-tenant-4-{suffix}");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "W4" };
        var resource = new WorkloadResource { WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "SG-2" };
        workload.Resources.Add(resource);
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        var scenario = new WorkloadScenario { PlatformTenantId = tenant.PlatformTenantId, WorkloadId = workload.Id, Name = "S1" };
        scenario.Rules.Add(new ScenarioResourceRule
        {
            WorkloadScenarioId = scenario.Id, ResourceId = resource.Id, Fields = new(),
        });
        await scenarioRepo.UpsertAsync(scenario, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteResourceAsync(tenant, workload.Id, resource.Id, "test", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteResource_Unreferenced_Succeeds()
    {
        if (!EmulatorAvailable) { return; }

        var (service, workloadRepo, _, _) = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"workload-mgmt-tenant-5-{suffix}");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "W5" };
        var resource = new WorkloadResource { WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "SG-3" };
        workload.Resources.Add(resource);
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        await service.DeleteResourceAsync(tenant, workload.Id, resource.Id, "test", CancellationToken.None);

        var reloaded = await workloadRepo.GetAsync(tenant, workload.Id, CancellationToken.None);
        Assert.Empty(reloaded!.Resources);
    }

    [Fact]
    public async Task DeactivateWorkload_SetsActiveFalse()
    {
        if (!EmulatorAvailable) { return; }

        var (service, workloadRepo, _, _) = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"workload-mgmt-tenant-6-{suffix}");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "W6" };
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        await service.DeactivateWorkloadAsync(tenant, workload.Id, "test", CancellationToken.None);

        var reloaded = await workloadRepo.GetAsync(tenant, workload.Id, CancellationToken.None);
        Assert.False(reloaded!.Active);
    }

    [Fact]
    public async Task ReactivateWorkload_SetsActiveTrue()
    {
        if (!EmulatorAvailable) { return; }

        var (service, workloadRepo, _, _) = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"workload-mgmt-tenant-reactivate-{suffix}");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "WR1", Active = false };
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        await service.ReactivateWorkloadAsync(tenant, workload.Id, "test", CancellationToken.None);

        var reloaded = await workloadRepo.GetAsync(tenant, workload.Id, CancellationToken.None);
        Assert.True(reloaded!.Active);
    }

    [Fact]
    public async Task DeleteWorkload_WithActiveAssignment_Throws()
    {
        if (!EmulatorAvailable) { return; }

        var (service, workloadRepo, assignmentRepo, _) = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"workload-mgmt-tenant-7-{suffix}");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "W7" };
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        var assignment = new GuestWorkloadAssignment
        {
            PlatformTenantId = tenant.PlatformTenantId,
            GuestId = Guid.NewGuid(),
            WorkloadId = workload.Id,
            RoleId = Guid.NewGuid(),
            Status = AssignmentStatus.Active,
        };
        await assignmentRepo.UpsertAsync(assignment, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DeleteWorkloadAsync(tenant, workload.Id, "test", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteWorkload_NoActiveAssignments_RemovesWorkloadScenariosAndHistoricalAssignments()
    {
        if (!EmulatorAvailable) { return; }

        var (service, workloadRepo, assignmentRepo, scenarioRepo) = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"workload-mgmt-tenant-8-{suffix}");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "W8" };
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        var scenario = new WorkloadScenario { PlatformTenantId = tenant.PlatformTenantId, WorkloadId = workload.Id, Name = "S1" };
        await scenarioRepo.UpsertAsync(scenario, CancellationToken.None);

        var revokedAssignment = new GuestWorkloadAssignment
        {
            PlatformTenantId = tenant.PlatformTenantId,
            GuestId = Guid.NewGuid(),
            WorkloadId = workload.Id,
            RoleId = Guid.NewGuid(),
            Status = AssignmentStatus.Revoked,
        };
        await assignmentRepo.UpsertAsync(revokedAssignment, CancellationToken.None);

        await service.DeleteWorkloadAsync(tenant, workload.Id, "test", CancellationToken.None);

        Assert.Null(await workloadRepo.GetAsync(tenant, workload.Id, CancellationToken.None));
        Assert.Null(await scenarioRepo.GetAsync(tenant, scenario.Id, CancellationToken.None));
        Assert.Null(await assignmentRepo.GetAsync(tenant, revokedAssignment.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetAssignmentCounts_SeparatesActiveFromInactive()
    {
        if (!EmulatorAvailable) { return; }

        var (service, workloadRepo, assignmentRepo, _) = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"workload-mgmt-tenant-9-{suffix}");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "W9" };
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        await assignmentRepo.UpsertAsync(new GuestWorkloadAssignment
        {
            PlatformTenantId = tenant.PlatformTenantId, GuestId = Guid.NewGuid(), WorkloadId = workload.Id,
            RoleId = Guid.NewGuid(), Status = AssignmentStatus.Active,
        }, CancellationToken.None);
        await assignmentRepo.UpsertAsync(new GuestWorkloadAssignment
        {
            PlatformTenantId = tenant.PlatformTenantId, GuestId = Guid.NewGuid(), WorkloadId = workload.Id,
            RoleId = Guid.NewGuid(), Status = AssignmentStatus.Revoked,
        }, CancellationToken.None);
        await assignmentRepo.UpsertAsync(new GuestWorkloadAssignment
        {
            PlatformTenantId = tenant.PlatformTenantId, GuestId = Guid.NewGuid(), WorkloadId = workload.Id,
            RoleId = Guid.NewGuid(), Status = AssignmentStatus.Expired,
        }, CancellationToken.None);

        var counts = await service.GetAssignmentCountsAsync(tenant, workload.Id, CancellationToken.None);

        Assert.Equal(1, counts.Active);
        Assert.Equal(2, counts.Inactive);
    }
}
