using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// API-Smoke-Tests (MVP-Dokument, TESTS / QUALITY GATES + MVP-Verification-Prompt Punkt 5).
/// Prüft Health-Endpoint sowie dass Query-Endpoints ohne gültiges Bearer-Token abgelehnt
/// werden (Tenant-/Identity-Leak-Schutz, Blueprint 8/16.1). Repository-Ports sind
/// ausschliesslich Cosmos-backed (InMemory entfernt) — daher wird die Konfiguration hier
/// explizit auf die wohlbekannten lokalen Cosmos-Emulator-Werte gesetzt (unabhaengig davon,
/// ob .env.local im aktuellen Prozess vorhanden ist) und jeder Fact, der tatsaechlich ein
/// Repository anspricht, uebersprungen (frühes return), wenn kein Emulator läuft (siehe
/// CosmosEmulatorAvailability) — dotnet test bleibt damit CI-sicher.
///
/// Erweiterung 2026-08-30: Die frueheren freien X-Portal-*-Header sind durch JWT ersetzt
/// (EntraIdMock-Identity-Provider). Tests loggen sich ueber POST /api/auth/mock/login mit
/// bekannten Mock-Entra-Mails ein und haengen das zurueckgegebene Token als Bearer-Header an,
/// statt Header direkt zu setzen. Custom-Tenants werden ueber
/// POST /api/dev/mock-entra/users (als admin@platform.example, GovernanceAdmin) angelegt,
/// weil der Tenant seit der Umstellung aus dem gewaehlten Mock-User abgeleitet wird, nicht
/// mehr aus einem freien Header.
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

    private async Task<string> LoginAsync(HttpClient client, string mail)
    {
        var response = await client.PostAsJsonAsync("/api/auth/mock/login", new { mail });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MockLoginResponseDto>();
        return body!.Token;
    }

    private static void UseToken(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    /// <summary>Legt einen Mock-Entra-Benutzer mit gewuenschtem Tenant/Rollen an (ueber den
    /// bereits authentifizierten admin@platform.example-Client) und loggt sich anschliessend
    /// als dieser Benutzer in einem frischen Client ein.</summary>
    private async Task<HttpClient> CreateLoggedInClientAsync(string mail, string platformTenantId, params string[] roles)
    {
        var adminClient = _factory.CreateClient();
        UseToken(adminClient, await LoginAsync(adminClient, "admin@platform.example"));

        var upsertResponse = await adminClient.PostAsJsonAsync("/api/dev/mock-entra/users", new
        {
            mail,
            displayName = mail,
            portalRoles = roles.Length == 0 ? new[] { "User" } : roles,
            platformTenantId,
        });
        upsertResponse.EnsureSuccessStatusCode();

        var client = _factory.CreateClient();
        UseToken(client, await LoginAsync(client, mail));
        return client;
    }

    private sealed record MockLoginResponseDto(string Token, string Mail, List<string> Roles, string PlatformTenantId);

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
    public async Task GuestAccounts_WithoutToken_ReturnsUnauthorized()
    {
        // Kein Bearer-Token -> FallbackPolicy (RequireAuthenticatedUser) greift vor jedem
        // Handler-Code, JwtBearer-Middleware antwortet mit 401.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/guest-accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GuestAccounts_WithValidToken_ReturnsOk()
    {
        if (!EmulatorAvailable) { return; }

        var client = await CreateLoggedInClientAsync(
            "governance-admin-smoke-test@platform.example", "tenant-smoke-test", "GovernanceAdmin");

        var response = await client.GetAsync("/api/guest-accounts");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GuestAccounts_NormalUser_ReturnsForbidden()
    {
        // Autorisierungspruefung greift vor jedem Repository-Zugriff -> kein Emulator noetig.
        // anna@contoso.example ist im Mock-Stamm seeded (Rolle "User", Tenant dev-tenant-a).
        var client = _factory.CreateClient();
        UseToken(client, await LoginAsync(client, "anna@contoso.example"));

        var response = await client.GetAsync("/api/guest-accounts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MockLogin_UnknownMail_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/mock/login", new { mail = "unknown@nowhere.example" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UiConfiguration_UnknownTheme_FallsBackToDefault()
    {
        // /api/ui/configuration beruehrt keine Repository-Ports -> laeuft auch ohne Emulator.
        // Bewusst ohne Login getestet — die Route muss vor dem Login erreichbar sein
        // (Login-Screen-Bootstrap), das Theme-Fallback funktioniert unabhaengig vom Auth-Status.
        var client = _factory.CreateClient();
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

        var client = await CreateLoggedInClientAsync(
            "workload-create-admin-smoke-test@platform.example", "tenant-create-workload-test", "GovernanceAdmin");

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
        UseToken(client, await LoginAsync(client, "admin@platform.example"));

        var response = await client.GetAsync("/api/dev/mock-entra/users");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("anna@contoso.example", body);
    }

    [Fact]
    public async Task SeedLargeWorkload_PopulatesMockEntra()
    {
        if (!EmulatorAvailable) { return; } // Seed-Endpoint schreibt ueber IWorkloadRepository (Cosmos).

        var client = await CreateLoggedInClientAsync(
            "seed-mock-entra-admin-smoke-test@platform.example", "tenant-seed-mock-entra-test", "GovernanceAdmin");

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
