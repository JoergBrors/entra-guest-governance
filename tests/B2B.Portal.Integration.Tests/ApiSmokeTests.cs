using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// API-Smoke-Tests (MVP-Dokument, TESTS / QUALITY GATES + MVP-Verification-Prompt Punkt 5).
/// Prüft Health-Endpoint sowie dass Query-Endpoints ohne Tenant-Kontext abgelehnt werden
/// (Tenant-Leak-Schutz, Blueprint 8/16.1). Repository-Ports sind jetzt ausschliesslich
/// Cosmos-backed (InMemory entfernt) — daher wird die Konfiguration hier explizit auf die
/// wohlbekannten lokalen Cosmos-Emulator-Werte gesetzt (unabhaengig davon, ob .env.local im
/// aktuellen Prozess vorhanden ist) und jeder Fact, der tatsaechlich ein Repository
/// anspricht, uebersprungen (frühes return), wenn kein Emulator läuft (siehe
/// CosmosEmulatorAvailability) — dotnet test bleibt damit CI-sicher.
/// </summary>
public class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly bool EmulatorAvailable = CosmosEmulatorAvailability.IsRunning();

    private readonly WebApplicationFactory<Program> _factory;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["COSMOS_EMULATOR_ENDPOINT"] = "https://localhost:8081",
                    ["COSMOS_EMULATOR_KEY"] =
                        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
                    ["COSMOS_DATABASE_ID"] = "b2b-governance-dev",
                });
            });
        });
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        // /health beruehrt keine Repository-Ports -> laeuft auch ohne Emulator.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
    }

    [Fact]
    public async Task GuestAccounts_WithoutTenantHeader_ReturnsServerError_NotData()
    {
        if (!EmulatorAvailable) { return; }

        // Kein X-Platform-Tenant-Id-Header gesetzt -> HeaderTenantContextAccessor wirft
        // UnauthorizedAccessException. Im MVP wird das als 500 sichtbar; für Produktion
        // ist eine dedizierte Exception-Middleware mit 401/403-Mapping vorgesehen
        // (siehe docs/architecture/mvp-test-report.md, offene Punkte).
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/guest-accounts");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GuestAccounts_WithTenantHeader_ReturnsOk()
    {
        if (!EmulatorAvailable) { return; }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Platform-Tenant-Id", "tenant-smoke-test");
        client.DefaultRequestHeaders.Add("X-Portal-User-Mail", "admin@platform.example");
        client.DefaultRequestHeaders.Add("X-Portal-Roles", "GovernanceAdmin");

        var response = await client.GetAsync("/api/guest-accounts");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GuestAccounts_NormalUser_ReturnsForbidden()
    {
        // Autorisierungspruefung greift vor jedem Repository-Zugriff -> kein Emulator noetig.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Platform-Tenant-Id", "tenant-smoke-test");
        client.DefaultRequestHeaders.Add("X-Portal-User-Mail", "guest@tenant.example");
        client.DefaultRequestHeaders.Add("X-Portal-Roles", "User");

        var response = await client.GetAsync("/api/guest-accounts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UiConfiguration_UnknownTheme_FallsBackToDefault()
    {
        // /api/ui/configuration beruehrt keine Repository-Ports -> laeuft auch ohne Emulator.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Platform-Tenant-Id", "tenant-smoke-test");
        client.DefaultRequestHeaders.Add("X-Portal-User-Mail", "admin@platform.example");
        client.DefaultRequestHeaders.Add("X-Portal-Roles", "GovernanceAdmin");
        client.DefaultRequestHeaders.Add("X-Portal-Theme-Id", "unknown-theme");

        var response = await client.GetAsync("/api/ui/configuration");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("corporate-vibrant", body);
    }

    [Fact]
    public async Task Workloads_AdminCanCreateWorkload()
    {
        if (!EmulatorAvailable) { return; }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Platform-Tenant-Id", "tenant-create-workload-test");
        client.DefaultRequestHeaders.Add("X-Portal-User-Mail", "admin@platform.example");
        client.DefaultRequestHeaders.Add("X-Portal-Roles", "GovernanceAdmin");

        var response = await client.PostAsJsonAsync("/api/workloads", new
        {
            name = "Admin Created Workload",
            owner = "owner@platform.example",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Admin Created Workload", body);
    }

    [Fact]
    public async Task MockEntra_AdminCanReadUsers()
    {
        // MockEntraDirectoryStore ist ein reiner In-Memory-Singleton (kein Repository-Port,
        // siehe Klassenkommentar oben) -> laeuft auch ohne Emulator.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Platform-Tenant-Id", "tenant-mock-entra-test");
        client.DefaultRequestHeaders.Add("X-Portal-User-Mail", "admin@platform.example");
        client.DefaultRequestHeaders.Add("X-Portal-Roles", "GovernanceAdmin");

        var response = await client.GetAsync("/api/dev/mock-entra/users");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("anna@contoso.example", body);
    }

    [Fact]
    public async Task SeedLargeWorkload_PopulatesMockEntra()
    {
        if (!EmulatorAvailable) { return; } // Seed-Endpoint schreibt ueber IWorkloadRepository (Cosmos).

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Platform-Tenant-Id", "tenant-seed-mock-entra-test");
        client.DefaultRequestHeaders.Add("X-Portal-User-Mail", "admin@platform.example");
        client.DefaultRequestHeaders.Add("X-Portal-Roles", "GovernanceAdmin");

        var seedResponse = await client.PostAsJsonAsync("/api/dev/seed/large-workload", new
        {
            guestCount = 10,
            workloadName = "Seed Mock Entra Test",
        });
        seedResponse.EnsureSuccessStatusCode();

        var users = await client.GetStringAsync("/api/dev/mock-entra/users");
        var groups = await client.GetStringAsync("/api/dev/mock-entra/groups");
        var memberships = await client.GetStringAsync("/api/dev/mock-entra/memberships");

        Assert.Contains("seed-obj-00009", users);
        Assert.Contains("SG-MERIDIAN-READ", groups);
        Assert.Contains("groupTypes", groups);
        Assert.DoesNotContain("workloadName", groups);
        Assert.Contains("seed-obj-00003", memberships);
    }
}
