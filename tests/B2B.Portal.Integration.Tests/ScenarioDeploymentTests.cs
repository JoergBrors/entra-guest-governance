using System.Text.Json;
using B2B.Portal.Application.Commands;
using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Scenarios;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Data;
using B2B.Portal.Infrastructure.Data.Cosmos;
using B2B.Portal.Infrastructure.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// End-to-End-Test für den Szenario-Deploy-Fluss (Command → Job → Worker-Handler,
/// analog zum bestehenden GrantWorkloadRole-Fluss), gegen den echten lokalen Cosmos DB
/// Emulator (InMemory-Repositories entfernt) — deckt den kompletten Weg ab: Workload+
/// Ressourcen anlegen, WorkloadScenario mit ScenarioResourceRules (freie Fields +
/// optionale Bedingung pro Regel) anlegen, DeployScenarioCommandHandler aufrufen, den
/// resultierenden Job über JobDispatcher/DeployScenarioHandler verarbeiten, und
/// verifizieren dass der Connector nur die Ressourcen der Regeln mit erfüllter Bedingung
/// erhalten hat. Übersprungen (frühes return), wenn kein Emulator läuft (siehe
/// CosmosEmulatorAvailability) — dotnet test bleibt CI-sicher. Nutzt pro Testlauf
/// eindeutige Tenant-IDs (Guid-Suffix), damit parallele/wiederholte Testläufe sich nicht
/// gegenseitig über bereits vorhandene Cosmos-Dokumente stören.
/// </summary>
public class ScenarioDeploymentTests
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
        if (!EmulatorAvailable) { return; }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"scenario-e2e-tenant-{suffix}");
        var factory = BuildFactory();
        var workloadRepo = new CosmosWorkloadRepository(factory);
        var scenarioRepo = new CosmosWorkloadScenarioRepository(factory);
        var jobRepo = new CosmosJobRepository(factory);
        var queue = new CosmosJobQueue(factory);
        var clock = new SystemClock();
        var auditService = new AuditService(new CosmosAuditWriter(factory), clock);
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
        if (!EmulatorAvailable) { return; }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"scenario-e2e-tenant-unmet-{suffix}");
        var factory = BuildFactory();
        var workloadRepo = new CosmosWorkloadRepository(factory);
        var scenarioRepo = new CosmosWorkloadScenarioRepository(factory);
        var jobRepo = new CosmosJobRepository(factory);
        var queue = new CosmosJobQueue(factory);
        var clock = new SystemClock();
        var auditService = new AuditService(new CosmosAuditWriter(factory), clock);
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
    public async Task ImportTemplate_ThenDeploy_UsesWorkloadResourcesAndDeploysOnlyMatchingRule()
    {
        if (!EmulatorAvailable) { return; }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"scenario-e2e-tenant-import-{suffix}");
        var factory = BuildFactory();
        var workloadRepo = new CosmosWorkloadRepository(factory);
        var scenarioRepo = new CosmosWorkloadScenarioRepository(factory);
        var jobRepo = new CosmosJobRepository(factory);
        var queue = new CosmosJobQueue(factory);
        var clock = new SystemClock();
        var auditService = new AuditService(new CosmosAuditWriter(factory), clock);
        var provisioningService = new ProvisioningService(jobRepo, queue, clock);
        var importExportService = new ScenarioImportExportService(workloadRepo, scenarioRepo, auditService);

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "SAP-Rollout" };
        // ScenarioImportExportService.ImportAsync loest Template-Ressourcen ueber DisplayName
        // auf (Anzeigename), nicht ueber ExternalId (das ist die Entra-Object-ID) — siehe
        // WorkloadResource-Kommentar. ExternalId wird hier trotzdem mitgegeben, um zu
        // verifizieren, dass die Aufloesung tatsaechlich ueber DisplayName laeuft.
        workload.Resources.Add(new WorkloadResource
        {
            WorkloadId = workload.Id,
            ResourceType = "SecurityGroup",
            ExternalId = "mock-grp-fabrikam-disponent",
            DisplayName = "SG-FABRIKAM-DISPONENT",
            Managed = false,
        });
        workload.Resources.Add(new WorkloadResource
        {
            WorkloadId = workload.Id,
            ResourceType = "SecurityGroup",
            ExternalId = "mock-grp-fabrikam-reader",
            DisplayName = "SG-FABRIKAM-READER",
            Managed = false,
        });
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
        Assert.Empty(importResult.CreatedResourceNames);
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
        if (!EmulatorAvailable) { return; }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"scenario-e2e-tenant-delete-{suffix}");
        var factory = BuildFactory();
        var workloadRepo = new CosmosWorkloadRepository(factory);
        var scenarioRepo = new CosmosWorkloadScenarioRepository(factory);
        var clock = new SystemClock();
        var auditService = new AuditService(new CosmosAuditWriter(factory), clock);
        var importExportService = new ScenarioImportExportService(workloadRepo, scenarioRepo, auditService);

        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "Delete-Test-Workload" };
        // Ressource, die zusaetzlich von einer WorkloadRole referenziert wird -> darf beim
        // Szenario-Loeschen NICHT entfernt werden.
        var sharedResource = new WorkloadResource { WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "mock-grp-shared", DisplayName = "SG-SHARED" };
        var orphanResource = new WorkloadResource { WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "mock-grp-orphan-only", DisplayName = "SG-ORPHAN-ONLY" };
        var role = new WorkloadRole { WorkloadId = workload.Id, Name = "Reader" };
        role.ResourceMappings.Add(sharedResource.Id);
        workload.Resources.Add(sharedResource);
        workload.Resources.Add(orphanResource);
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
        Assert.Contains(reloadedWorkload!.Resources, r => r.DisplayName == "SG-SHARED");
        Assert.DoesNotContain(reloadedWorkload.Resources, r => r.DisplayName == "SG-ORPHAN-ONLY");
        Assert.Null(await scenarioRepo.GetAsync(tenant, importResult.ScenarioId!.Value, CancellationToken.None));
    }
}
