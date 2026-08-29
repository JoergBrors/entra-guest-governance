using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// API-Smoke-Tests (MVP-Dokument, TESTS / QUALITY GATES + MVP-Verification-Prompt Punkt 5).
/// Prüft Health-Endpoint sowie dass Query-Endpoints ohne Tenant-Kontext abgelehnt werden
/// (Tenant-Leak-Schutz, Blueprint 8/16.1).
/// </summary>
public class ApiSmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
    }

    [Fact]
    public async Task GuestAccounts_WithoutTenantHeader_ReturnsServerError_NotData()
    {
        // Kein X-Platform-Tenant-Id-Header gesetzt -> HeaderTenantContextAccessor wirft
        // UnauthorizedAccessException. Im MVP wird das als 500 sichtbar; für Produktion
        // ist eine dedizierte Exception-Middleware mit 401/403-Mapping vorgesehen
        // (siehe docs/architecture/mvp-test-report.md, offene Punkte).
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/guest-accounts");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GuestAccounts_WithTenantHeader_ReturnsOk()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Platform-Tenant-Id", "tenant-smoke-test");

        var response = await client.GetAsync("/api/guest-accounts");

        response.EnsureSuccessStatusCode();
    }
}
