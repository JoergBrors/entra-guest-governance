using B2B.Portal.Application.Services;
using B2B.Portal.Application.Workloads;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Data;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// Testet die Konsistenzprüfungen von WorkloadManagementService gegen InMemory-Repositories:
/// eine WorkloadRole mit aktiven Assignments darf nicht gelöscht werden, eine
/// WorkloadResource, die noch von einer Rolle oder einem Szenario referenziert wird, darf
/// nicht gelöscht werden. Ohne solche Referenzen funktioniert Löschen normal.
/// </summary>
public class WorkloadManagementServiceTests
{
    private static (WorkloadManagementService Service, InMemoryWorkloadRepository WorkloadRepo,
        InMemoryAssignmentRepository AssignmentRepo, InMemoryWorkloadScenarioRepository ScenarioRepo) Build()
    {
        var workloadRepo = new InMemoryWorkloadRepository();
        var scenarioRepo = new InMemoryWorkloadScenarioRepository();
        var assignmentRepo = new InMemoryAssignmentRepository();
        var auditService = new AuditService(new InMemoryAuditWriter(), new SystemClock());
        var service = new WorkloadManagementService(workloadRepo, scenarioRepo, assignmentRepo, auditService);
        return (service, workloadRepo, assignmentRepo, scenarioRepo);
    }

    [Fact]
    public async Task DeleteRole_WithActiveAssignment_Throws()
    {
        var (service, workloadRepo, assignmentRepo, _) = Build();
        var tenant = TenantContext.Create("workload-mgmt-tenant-1");

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
        var (service, workloadRepo, _, _) = Build();
        var tenant = TenantContext.Create("workload-mgmt-tenant-2");

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
        var (service, workloadRepo, _, _) = Build();
        var tenant = TenantContext.Create("workload-mgmt-tenant-3");

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
        var (service, workloadRepo, _, scenarioRepo) = Build();
        var tenant = TenantContext.Create("workload-mgmt-tenant-4");

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
        var (service, workloadRepo, _, _) = Build();
        var tenant = TenantContext.Create("workload-mgmt-tenant-5");

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
        var (service, workloadRepo, _, _) = Build();
        var tenant = TenantContext.Create("workload-mgmt-tenant-6");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "W6" };
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        await service.DeactivateWorkloadAsync(tenant, workload.Id, "test", CancellationToken.None);

        var reloaded = await workloadRepo.GetAsync(tenant, workload.Id, CancellationToken.None);
        Assert.False(reloaded!.Active);
    }

    [Fact]
    public async Task ReactivateWorkload_SetsActiveTrue()
    {
        var (service, workloadRepo, _, _) = Build();
        var tenant = TenantContext.Create("workload-mgmt-tenant-reactivate");

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "WR1", Active = false };
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        await service.ReactivateWorkloadAsync(tenant, workload.Id, "test", CancellationToken.None);

        var reloaded = await workloadRepo.GetAsync(tenant, workload.Id, CancellationToken.None);
        Assert.True(reloaded!.Active);
    }

    [Fact]
    public async Task DeleteWorkload_WithActiveAssignment_Throws()
    {
        var (service, workloadRepo, assignmentRepo, _) = Build();
        var tenant = TenantContext.Create("workload-mgmt-tenant-7");

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
        var (service, workloadRepo, assignmentRepo, scenarioRepo) = Build();
        var tenant = TenantContext.Create("workload-mgmt-tenant-8");

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
        var (service, workloadRepo, assignmentRepo, _) = Build();
        var tenant = TenantContext.Create("workload-mgmt-tenant-9");

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
