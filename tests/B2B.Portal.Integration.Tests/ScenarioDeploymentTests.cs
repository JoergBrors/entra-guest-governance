using System.Text.Json;
using B2B.Portal.Application.Commands;
using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Scenarios;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Data;
using B2B.Portal.Infrastructure.Queue;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// End-to-End-Test für den Szenario-Deploy-Fluss (Command → Job → Worker-Handler,
/// analog zum bestehenden GrantWorkloadRole-Fluss), gegen InMemory-Repositories — deckt
/// den kompletten Weg ab: Workload+Ressourcen anlegen, WorkloadScenario mit
/// ScenarioResourceRules (freie Fields + optionale Bedingung pro Regel) anlegen,
/// DeployScenarioCommandHandler aufrufen, den resultierenden Job über
/// JobDispatcher/DeployScenarioHandler verarbeiten, und verifizieren dass der Connector
/// nur die Ressourcen der Regeln mit erfüllter Bedingung erhalten hat.
/// </summary>
public class ScenarioDeploymentTests
{
    private sealed class RecordingResourceConnector : IResourceConnector
    {
        public string ResourceType => "SecurityGroup";
        public List<string> CreatedResourceNames { get; } = new();

        public Task GrantAccessAsync(string directoryTenantId, string entraObjectId, string resourceExternalId, CancellationToken ct)
            => Task.CompletedTask;

        public Task RevokeAccessAsync(string directoryTenantId, string entraObjectId, string resourceExternalId, CancellationToken ct)
            => Task.CompletedTask;

        public Task<string> CreateResourceAsync(
            string directoryTenantId, string namePattern, IReadOnlyDictionary<string, string> metadata, CancellationToken ct)
        {
            CreatedResourceNames.Add(namePattern);
            return Task.FromResult($"mock-resource-{Guid.NewGuid():N}");
        }
    }

    [Fact]
    public async Task DeployScenario_WithMetCondition_DeploysResourceViaConnector()
    {
        var tenant = TenantContext.Create("scenario-e2e-tenant");
        var workloadRepo = new InMemoryWorkloadRepository();
        var scenarioRepo = new InMemoryWorkloadScenarioRepository();
        var jobRepo = new InMemoryJobRepository();
        var queue = new LocalJobQueue();
        var clock = new SystemClock();
        var auditService = new AuditService(new InMemoryAuditWriter(), clock);
        var provisioningService = new ProvisioningService(jobRepo, queue, clock);

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "Test-Workload" };
        var resource = new WorkloadResource
        {
            WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "SG-TEST", Managed = true,
        };
        workload.Resources.Add(resource);
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        var scenario = new WorkloadScenario
        {
            PlatformTenantId = tenant.PlatformTenantId,
            WorkloadId = workload.Id,
            Name = "Fabrikam-Test",
        };
        scenario.Rules.Add(new ScenarioResourceRule
        {
            WorkloadScenarioId = scenario.Id,
            ResourceId = resource.Id,
            Fields = new Dictionary<string, string> { ["Firma"] = "Fabrikam", ["Environment"] = "Test" },
            Condition = JsonDocument.Parse("""{"==": [{"var": "Fields.Environment"}, "Test"]}""").RootElement,
        });
        await scenarioRepo.UpsertAsync(scenario, CancellationToken.None);

        var commandHandler = new DeployScenarioCommandHandler(scenarioRepo, provisioningService, auditService);
        var request = new DeployScenarioRequest(tenant.PlatformTenantId, scenario.Id, Actor: "test");
        await commandHandler.HandleAsync(request, CancellationToken.None);

        var connector = new RecordingResourceConnector();
        var handler = new B2B.Portal.Worker.Handlers.Provisioning.DeployScenarioHandler(
            scenarioRepo, workloadRepo, connector, NullLogger<B2B.Portal.Worker.Handlers.Provisioning.DeployScenarioHandler>.Instance);
        var dispatcher = new B2B.Portal.Worker.Processing.JobDispatcher(
            [handler], queue, NullLogger<B2B.Portal.Worker.Processing.JobDispatcher>.Instance);

        var processed = await dispatcher.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Single(connector.CreatedResourceNames);
        Assert.Contains("Test-Workload", connector.CreatedResourceNames[0]);
        Assert.Contains("Fabrikam-Test", connector.CreatedResourceNames[0]);
    }

    [Fact]
    public async Task DeployScenario_WithUnmetCondition_DoesNotDeployResource()
    {
        var tenant = TenantContext.Create("scenario-e2e-tenant-unmet");
        var workloadRepo = new InMemoryWorkloadRepository();
        var scenarioRepo = new InMemoryWorkloadScenarioRepository();
        var jobRepo = new InMemoryJobRepository();
        var queue = new LocalJobQueue();
        var clock = new SystemClock();
        var auditService = new AuditService(new InMemoryAuditWriter(), clock);
        var provisioningService = new ProvisioningService(jobRepo, queue, clock);

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "Prod-Only-Workload" };
        var resource = new WorkloadResource
        {
            WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "SG-PROD", Managed = true,
        };
        workload.Resources.Add(resource);
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        var scenario = new WorkloadScenario
        {
            PlatformTenantId = tenant.PlatformTenantId,
            WorkloadId = workload.Id,
            Name = "Fabrikam-Prod",
        };
        scenario.Rules.Add(new ScenarioResourceRule
        {
            WorkloadScenarioId = scenario.Id,
            ResourceId = resource.Id,
            Fields = new Dictionary<string, string> { ["Firma"] = "Fabrikam", ["Environment"] = "Test" },
            // Bedingung verlangt Prod, Field ist Test -> nicht erfuellt.
            Condition = JsonDocument.Parse("""{"==": [{"var": "Fields.Environment"}, "Prod"]}""").RootElement,
        });
        await scenarioRepo.UpsertAsync(scenario, CancellationToken.None);

        var commandHandler = new DeployScenarioCommandHandler(scenarioRepo, provisioningService, auditService);
        var request = new DeployScenarioRequest(tenant.PlatformTenantId, scenario.Id, Actor: "test");
        await commandHandler.HandleAsync(request, CancellationToken.None);

        var connector = new RecordingResourceConnector();
        var handler = new B2B.Portal.Worker.Handlers.Provisioning.DeployScenarioHandler(
            scenarioRepo, workloadRepo, connector, NullLogger<B2B.Portal.Worker.Handlers.Provisioning.DeployScenarioHandler>.Instance);
        var dispatcher = new B2B.Portal.Worker.Processing.JobDispatcher(
            [handler], queue, NullLogger<B2B.Portal.Worker.Processing.JobDispatcher>.Instance);

        var processed = await dispatcher.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Empty(connector.CreatedResourceNames);
    }

    [Fact]
    public async Task ImportTemplate_ThenDeploy_AutoCreatesResourceAndDeploysOnlyMatchingRule()
    {
        var tenant = TenantContext.Create("scenario-e2e-tenant-import");
        var workloadRepo = new InMemoryWorkloadRepository();
        var scenarioRepo = new InMemoryWorkloadScenarioRepository();
        var jobRepo = new InMemoryJobRepository();
        var queue = new LocalJobQueue();
        var clock = new SystemClock();
        var auditService = new AuditService(new InMemoryAuditWriter(), clock);
        var provisioningService = new ProvisioningService(jobRepo, queue, clock);
        var importExportService = new ScenarioImportExportService(workloadRepo, scenarioRepo, auditService);

        // Workload existiert bereits, aber ohne die im Template referenzierten Ressourcen.
        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "SAP-Rollout" };
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        var template = new ScenarioTemplateDto(
            WorkloadName: "SAP-Rollout",
            ScenarioName: "Fabrikam-Onboarding",
            Rules:
            [
                new ScenarioTemplateRuleDto(
                    ResourceName: "SG-FABRIKAM-DISPONENT",
                    ResourceType: "SecurityGroup",
                    Fields: new Dictionary<string, string> { ["Firma"] = "Fabrikam", ["Rolle"] = "Disponent" },
                    Condition: JsonDocument.Parse("""{"==": [{"var": "Fields.Rolle"}, "Disponent"]}""").RootElement),
                new ScenarioTemplateRuleDto(
                    ResourceName: "SG-FABRIKAM-READER",
                    ResourceType: "SecurityGroup",
                    Fields: new Dictionary<string, string> { ["Firma"] = "Fabrikam", ["Rolle"] = "Reader" },
                    Condition: JsonDocument.Parse("""{"==": [{"var": "Fields.Rolle"}, "Disponent"]}""").RootElement),
            ]);

        var importResult = await importExportService.ImportAsync(tenant, template, CancellationToken.None);

        Assert.Empty(importResult.Errors);
        Assert.Equal(2, importResult.CreatedResourceNames.Count);
        Assert.NotNull(importResult.ScenarioId);

        var commandHandler = new DeployScenarioCommandHandler(scenarioRepo, provisioningService, auditService);
        var request = new DeployScenarioRequest(tenant.PlatformTenantId, importResult.ScenarioId!.Value, Actor: "test");
        await commandHandler.HandleAsync(request, CancellationToken.None);

        var connector = new RecordingResourceConnector();
        var deployHandler = new B2B.Portal.Worker.Handlers.Provisioning.DeployScenarioHandler(
            scenarioRepo, workloadRepo, connector, NullLogger<B2B.Portal.Worker.Handlers.Provisioning.DeployScenarioHandler>.Instance);
        var dispatcher = new B2B.Portal.Worker.Processing.JobDispatcher(
            [deployHandler], queue, NullLogger<B2B.Portal.Worker.Processing.JobDispatcher>.Instance);

        var processed = await dispatcher.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        // Nur die Disponent-Regel erfuellt ihre eigene Bedingung -> nur eine Ressource deployt.
        Assert.Single(connector.CreatedResourceNames);
        Assert.Contains("SG-FABRIKAM-DISPONENT", connector.CreatedResourceNames[0]);
    }

    [Fact]
    public async Task DeleteScenario_RemovesOrphanedResources_ButKeepsResourcesStillReferenced()
    {
        var tenant = TenantContext.Create("scenario-e2e-tenant-delete");
        var workloadRepo = new InMemoryWorkloadRepository();
        var scenarioRepo = new InMemoryWorkloadScenarioRepository();
        var clock = new SystemClock();
        var auditService = new AuditService(new InMemoryAuditWriter(), clock);
        var importExportService = new ScenarioImportExportService(workloadRepo, scenarioRepo, auditService);

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "Delete-Test-Workload" };
        // Ressource, die zusaetzlich von einer WorkloadRole referenziert wird -> darf beim
        // Szenario-Loeschen NICHT entfernt werden.
        var sharedResource = new WorkloadResource { WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "SG-SHARED" };
        var role = new WorkloadRole { WorkloadId = workload.Id, Name = "Reader" };
        role.ResourceMappings.Add(sharedResource.Id);
        workload.Resources.Add(sharedResource);
        workload.Roles.Add(role);
        await workloadRepo.UpsertAsync(workload, CancellationToken.None);

        var template = new ScenarioTemplateDto(
            WorkloadName: "Delete-Test-Workload",
            ScenarioName: "ToDelete",
            Rules:
            [
                new ScenarioTemplateRuleDto(
                    ResourceName: "SG-SHARED", ResourceType: "SecurityGroup",
                    Fields: new Dictionary<string, string>(), Condition: null),
                new ScenarioTemplateRuleDto(
                    ResourceName: "SG-ORPHAN-ONLY", ResourceType: "SecurityGroup",
                    Fields: new Dictionary<string, string>(), Condition: null),
            ]);

        var importResult = await importExportService.ImportAsync(tenant, template, CancellationToken.None);
        Assert.NotNull(importResult.ScenarioId);

        await importExportService.DeleteAsync(tenant, importResult.ScenarioId!.Value, "test", CancellationToken.None);

        var reloadedWorkload = await workloadRepo.GetAsync(tenant, workload.Id, CancellationToken.None);
        Assert.Contains(reloadedWorkload!.Resources, r => r.ExternalId == "SG-SHARED");
        Assert.DoesNotContain(reloadedWorkload.Resources, r => r.ExternalId == "SG-ORPHAN-ONLY");
        Assert.Null(await scenarioRepo.GetAsync(tenant, importResult.ScenarioId!.Value, CancellationToken.None));
    }
}
