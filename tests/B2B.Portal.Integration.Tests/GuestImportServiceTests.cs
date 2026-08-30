using B2B.Portal.Application.Commands;
using B2B.Portal.Application.Import;
using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Data;
using B2B.Portal.Infrastructure.Data.Cosmos;
using B2B.Portal.Infrastructure.Import;
using B2B.Portal.Infrastructure.Queue;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// Testet GuestImportService gegen den echten lokalen Cosmos DB Emulator (InMemory-
/// Repositories entfernt): Regel-Matching über freie Fields, Preview-Schreibfreiheit,
/// Commit-Idempotenz, und den Fremd-Workload-Review-Hinweis bei geänderten Gast-Daten
/// (siehe Prompt "wennn der workload... bitte auch im review für den alten Workload...
/// sichtbar machen"). Übersprungen (frühes return), wenn kein Emulator läuft (siehe
/// CosmosEmulatorAvailability) — dotnet test bleibt CI-sicher. Nutzt pro Testlauf
/// eindeutige Tenant-IDs (Guid-Suffix), damit parallele/wiederholte Testläufe sich nicht
/// gegenseitig über bereits vorhandene Cosmos-Dokumente stören.
/// </summary>
public class GuestImportServiceTests
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

    private sealed record Fixture(
        GuestImportService Service, CosmosWorkloadRepository WorkloadRepo,
        CosmosWorkloadScenarioRepository ScenarioRepo, CosmosGuestAccountRepository GuestRepo,
        CosmosAssignmentRepository AssignmentRepo, CosmosReviewRepository ReviewRepo);

    private static Fixture Build()
    {
        var factory = BuildFactory();
        var workloadRepo = new CosmosWorkloadRepository(factory);
        var scenarioRepo = new CosmosWorkloadScenarioRepository(factory);
        var guestRepo = new CosmosGuestAccountRepository(factory);
        var assignmentRepo = new CosmosAssignmentRepository(factory);
        var reviewRepo = new CosmosReviewRepository(factory);
        var jobRepo = new CosmosJobRepository(factory);
        var queue = new CosmosJobQueue(factory);
        var clock = new SystemClock();
        var auditService = new AuditService(new CosmosAuditWriter(factory), clock);
        var provisioningService = new ProvisioningService(jobRepo, queue, clock);
        var grantHandler = new GrantWorkloadRoleCommandHandler(assignmentRepo, provisioningService, auditService);
        var reader = new ClosedXmlSpreadsheetReader();

        var service = new GuestImportService(
            reader, workloadRepo, scenarioRepo, guestRepo, assignmentRepo, reviewRepo, grantHandler, auditService);

        return new Fixture(service, workloadRepo, scenarioRepo, guestRepo, assignmentRepo, reviewRepo);
    }

    private static MemoryStream BuildWorkbook(string[] headers, params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Import");
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(r + 2, c + 1).Value = rows[r][c];
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static GuestImportColumnMapping DefaultMapping() => new(
        SheetName: "Import",
        HeaderRowIndex: 1,
        DataStartColumnIndex: 1,
        ColumnToField: new Dictionary<int, string>
        {
            [0] = GuestImportReservedFields.Mail,
            [1] = GuestImportReservedFields.DisplayName,
            [2] = GuestImportReservedFields.Workload,
            [3] = GuestImportReservedFields.Szenario,
            [4] = "Rolle",
        });

    private static async Task<(Workload Workload, WorkloadScenario Scenario, WorkloadRole DisponentRole)> SeedWorkloadAsync(
        Fixture fx, TenantContext tenant, string workloadName = "SAP-Rollout")
    {
        var workload = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = workloadName };
        var resource = new WorkloadResource { WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "SG-DISPONENT" };
        var role = new WorkloadRole { WorkloadId = workload.Id, Name = "Disponent-Rolle" };
        role.ResourceMappings.Add(resource.Id);
        workload.Resources.Add(resource);
        workload.Roles.Add(role);
        await fx.WorkloadRepo.UpsertAsync(workload, CancellationToken.None);

        var scenario = new WorkloadScenario { PlatformTenantId = tenant.PlatformTenantId, WorkloadId = workload.Id, Name = "Onboarding" };
        scenario.Rules.Add(new ScenarioResourceRule
        {
            WorkloadScenarioId = scenario.Id, ResourceId = resource.Id,
            Fields = new Dictionary<string, string> { ["Rolle"] = "Disponent" },
        });
        await fx.ScenarioRepo.UpsertAsync(scenario, CancellationToken.None);

        return (workload, scenario, role);
    }

    [Fact]
    public async Task Preview_MatchesRuleAndDoesNotWrite()
    {
        if (!EmulatorAvailable) { return; }

        var fx = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"guest-import-tenant-1-{suffix}");
        await SeedWorkloadAsync(fx, tenant);

        using var stream = BuildWorkbook(
            ["Mail", "Name", "Workload", "Szenario", "Rolle"],
            ["anna@example.com", "Anna Muster", "SAP-Rollout", "Onboarding", "Disponent"]);

        var result = await fx.Service.PreviewAsync(tenant, stream, DefaultMapping(), CancellationToken.None);

        Assert.Single(result.Rows);
        Assert.Equal(1, result.NewGuestCount);
        Assert.Contains("Disponent-Rolle", result.Rows[0].MatchedRoleNames);
        Assert.Empty(await fx.GuestRepo.ListAsync(tenant, CancellationToken.None));
    }

    [Fact]
    public async Task Commit_CreatesGuestAndAssignment()
    {
        if (!EmulatorAvailable) { return; }

        var fx = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"guest-import-tenant-2-{suffix}");
        var (workload, _, role) = await SeedWorkloadAsync(fx, tenant);

        using var stream = BuildWorkbook(
            ["Mail", "Name", "Workload", "Szenario", "Rolle"],
            [$"anna-{suffix}@example.com", "Anna Muster", "SAP-Rollout", "Onboarding", "Disponent"]);

        var result = await fx.Service.CommitAsync(tenant, stream, DefaultMapping(), "test", CancellationToken.None);

        Assert.Equal(1, result.NewGuestCount);
        var guest = await fx.GuestRepo.GetByMailAsync(tenant, $"anna-{suffix}@example.com", CancellationToken.None);
        Assert.NotNull(guest);
        var assignments = await fx.AssignmentRepo.ListActiveByGuestAsync(tenant, guest!.Id, CancellationToken.None);
        Assert.Single(assignments);
        Assert.Equal(role.Id, assignments[0].RoleId);
        Assert.Equal(workload.Id, assignments[0].WorkloadId);
    }

    [Fact]
    public async Task Commit_NoRuleMatch_StillCreatesGuestWithWarning()
    {
        if (!EmulatorAvailable) { return; }

        var fx = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"guest-import-tenant-3-{suffix}");
        await SeedWorkloadAsync(fx, tenant);

        using var stream = BuildWorkbook(
            ["Mail", "Name", "Workload", "Szenario", "Rolle"],
            [$"ben-{suffix}@example.com", "Ben Muster", "SAP-Rollout", "Onboarding", "Unbekannt"]);

        var result = await fx.Service.CommitAsync(tenant, stream, DefaultMapping(), "test", CancellationToken.None);

        Assert.Equal(1, result.NewGuestCount);
        Assert.NotEmpty(result.Rows[0].Warnings);
        Assert.Empty(result.Rows[0].MatchedRoleNames);
        var guest = await fx.GuestRepo.GetByMailAsync(tenant, $"ben-{suffix}@example.com", CancellationToken.None);
        Assert.NotNull(guest);
    }

    [Fact]
    public async Task Commit_Twice_IsIdempotent()
    {
        if (!EmulatorAvailable) { return; }

        var fx = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"guest-import-tenant-4-{suffix}");
        await SeedWorkloadAsync(fx, tenant);

        var mapping = DefaultMapping();
        var mail = $"anna-{suffix}@example.com";
        using (var stream1 = BuildWorkbook(
            ["Mail", "Name", "Workload", "Szenario", "Rolle"],
            [mail, "Anna Muster", "SAP-Rollout", "Onboarding", "Disponent"]))
        {
            await fx.Service.CommitAsync(tenant, stream1, mapping, "test", CancellationToken.None);
        }
        using (var stream2 = BuildWorkbook(
            ["Mail", "Name", "Workload", "Szenario", "Rolle"],
            [mail, "Anna Muster", "SAP-Rollout", "Onboarding", "Disponent"]))
        {
            await fx.Service.CommitAsync(tenant, stream2, mapping, "test", CancellationToken.None);
        }

        var guest = await fx.GuestRepo.GetByMailAsync(tenant, mail, CancellationToken.None);
        var assignments = await fx.AssignmentRepo.ListActiveByGuestAsync(tenant, guest!.Id, CancellationToken.None);
        Assert.Single(assignments);
    }

    [Fact]
    public async Task Commit_ChangedDataForExistingGuest_CreatesReviewItemForForeignWorkloadOnly()
    {
        if (!EmulatorAvailable) { return; }

        var fx = Build();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = TenantContext.Create($"guest-import-tenant-5-{suffix}");
        var (workloadA, _, roleA) = await SeedWorkloadAsync(fx, tenant);

        // Zweiter Workload mit einer BESTEHENDEN Zuweisung fuer denselben Gast - simuliert
        // eine Zuweisung, die NICHT Teil dieses Imports ist.
        var workloadB = new Workload { PlatformTenantId = tenant.PlatformTenantId, Name = "Other-Workload" };
        await fx.WorkloadRepo.UpsertAsync(workloadB, CancellationToken.None);

        var mail = $"anna-{suffix}@example.com";
        var guest = new GuestAccount
        {
            PlatformTenantId = tenant.PlatformTenantId, DirectoryTenantId = "dir-a",
            Mail = mail, DisplayName = "Anna Alt",
        };
        await fx.GuestRepo.UpsertAsync(guest, CancellationToken.None);

        var foreignAssignment = new GuestWorkloadAssignment
        {
            PlatformTenantId = tenant.PlatformTenantId, GuestId = guest.Id, WorkloadId = workloadB.Id,
            RoleId = Guid.NewGuid(), Status = AssignmentStatus.Active,
        };
        await fx.AssignmentRepo.UpsertAsync(foreignAssignment, CancellationToken.None);

        using var stream = BuildWorkbook(
            ["Mail", "Name", "Workload", "Szenario", "Rolle"],
            [mail, "Anna Neu", "SAP-Rollout", "Onboarding", "Disponent"]);

        await fx.Service.CommitAsync(tenant, stream, DefaultMapping(), "test", CancellationToken.None);

        var openReviews = await fx.ReviewRepo.ListOpenAsync(tenant, CancellationToken.None);
        var reviewItem = openReviews.SelectMany(r => r.Items).FirstOrDefault(i => i.AssignmentId == foreignAssignment.Id);
        Assert.NotNull(reviewItem);
        Assert.NotNull(reviewItem!.Reason);

        // Keine ReviewItems fuer Zuweisungen INNERHALB des gerade importierten Workload A.
        var assignmentsInA = await fx.AssignmentRepo.ListActiveByGuestAsync(tenant, guest.Id, CancellationToken.None);
        var assignmentInA = assignmentsInA.FirstOrDefault(a => a.WorkloadId == workloadA.Id);
        Assert.NotNull(assignmentInA);
        Assert.DoesNotContain(openReviews.SelectMany(r => r.Items), i => i.AssignmentId == assignmentInA!.Id);
        _ = roleA;
    }
}
