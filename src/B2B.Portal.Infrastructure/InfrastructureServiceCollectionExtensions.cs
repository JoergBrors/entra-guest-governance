using B2B.Portal.Application.Ports;
using B2B.Portal.Infrastructure.Auth;
using B2B.Portal.Infrastructure.Data;
using B2B.Portal.Infrastructure.Data.Cosmos;
using B2B.Portal.Infrastructure.Directory;
using B2B.Portal.Infrastructure.Email;
using B2B.Portal.Infrastructure.Import;
using B2B.Portal.Infrastructure.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Portal.Infrastructure;

/// <summary>
/// Composition-Root-Helfer für API und Worker (Blueprint: "API und Worker bilden
/// Composition Roots und registrieren konkrete Adapter via Dependency Injection").
/// LOCAL_MOCK ist der Default — nur eine explizite Konfiguration schaltet reale
/// Adapter frei (Sicherheitsregel für Development, MVP-Dokument Abschnitt 1).
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <param name="identityProviderConfig">
    /// WICHTIG: wird vom Aufrufer (Program.cs) exakt einmal per FromConfiguration(...)
    /// erzeugt und hier NICHT erneut berechnet — sonst wuerden bei fehlendem
    /// JWT_SIGNING_KEY zwei unterschiedliche Dev-Ephemeral-Keys entstehen (einer fuer die
    /// JwtBearer-Validierung in Program.cs, einer fuer MockJwtIssuer hier), und jedes
    /// ausgestellte Token wuerde als ungueltig zurueckgewiesen.
    /// </param>
    public static IServiceCollection AddB2BInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IdentityProviderConfig identityProviderConfig)
    {
        var mode = configuration["B2B_MODE"] ?? "LOCAL_MOCK";
        var directoryProvider = configuration["DIRECTORY_PROVIDER"] ?? "mock";
        var emailProvider = configuration["EMAIL_PROVIDER"] ?? "mock";
        var allowGraphWrites = bool.TryParse(configuration["ALLOW_GRAPH_WRITES"], out var g) && g;

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISpreadsheetReader, ClosedXmlSpreadsheetReader>();
        services.AddSingleton<MockEntraDirectoryStore>();

        // Identity Provider (Erweiterung 2026-08-30: Ablösung der freien X-Portal-*-Header
        // durch JWT). EntraIdMock ist unter LOCAL_MOCK der Default, EntraId bleibt ein reiner
        // Konfigurations-Platzhalter (integration pending, siehe IdentityProviderConfig.cs).
        if (identityProviderConfig.Kind == IdentityProviderKind.EntraIdMock
            && identityProviderConfig.JwtSigningKey.StartsWith("dev-ephemeral-", StringComparison.Ordinal))
        {
            Console.WriteLine(
                "[B2B.Portal.Infrastructure] WARNUNG: JWT_SIGNING_KEY nicht gesetzt — " +
                "verwende einen zufaelligen, nur fuer diesen Prozess gueltigen Dev-Key. " +
                "NIEMALS als echtes Secret verwenden, siehe .env.example.");
        }
        services.AddSingleton(identityProviderConfig);
        services.AddSingleton<MockJwtIssuer>();

        // Cosmos DB ist der einzige Datenprovider (Erweiterung 2026-08-30 (Teil 2): InMemory-
        // Repositories entfernt). Nutzt den lokalen Cosmos DB Emulator (siehe
        // scripts/requirements.ps1 -InitCosmosEmulator) oder eine echte Cosmos-DB in
        // DEV_INTEGRATION/AZURE_DEV — Container-/Datenbanknamen siehe
        // infra/modules/cosmos-free-tier.bicep. "mock" (DIRECTORY_PROVIDER/EMAIL_PROVIDER)
        // bezieht sich weiterhin nur auf Directory/Email, nicht auf die Datenhaltung.
        services.AddSingleton<CosmosClientFactory>();
        services.AddSingleton<IJobQueue, CosmosJobQueue>();
        services.AddSingleton<IGuestAccountRepository, CosmosGuestAccountRepository>();
        services.AddSingleton<IWorkloadRepository, CosmosWorkloadRepository>();
        services.AddSingleton<IAssignmentRepository, CosmosAssignmentRepository>();
        services.AddSingleton<IReviewRepository, CosmosReviewRepository>();
        services.AddSingleton<IJobRepository, CosmosJobRepository>();
        services.AddSingleton<IResourceAccessRepository, CosmosResourceAccessRepository>();
        services.AddSingleton<IAuditWriter, CosmosAuditWriter>();
        services.AddSingleton<IWorkloadScenarioRepository, CosmosWorkloadScenarioRepository>();
        services.AddSingleton<IExternalOrganizationRepository, CosmosExternalOrganizationRepository>();

        if (directoryProvider.Equals("graph", StringComparison.OrdinalIgnoreCase) && mode != "LOCAL_MOCK")
        {
            // Integration pending: siehe docs/architecture/mvp-test-report.md.
            // Es wird bewusst kein Graph-Adapter mit erfundenen IDs registriert.
            throw new NotSupportedException(
                "DIRECTORY_PROVIDER=graph ist noch nicht implementiert (Integration pending). " +
                "Nutze DIRECTORY_PROVIDER=mock oder ergänze den Graph-Adapter in DEV_INTEGRATION.");
        }

        services.AddSingleton<IGuestDirectory, MockGuestDirectory>();
        services.AddSingleton<IResourceConnector>(sp => new MockResourceConnector(
            "SecurityGroup", sp.GetRequiredService<MockEntraDirectoryStore>()));

        if (emailProvider.Equals("graph", StringComparison.OrdinalIgnoreCase))
        {
            var sharedMailbox = configuration["NOTIFICATIONS_SHARED_MAILBOX"] ?? string.Empty;
            services.AddSingleton<IEmailProvider>(
                new GraphSharedMailboxEmailProvider(sharedMailbox, allowGraphWrites));
        }
        else
        {
            services.AddSingleton<MockEmailProvider>();
            services.AddSingleton<IEmailProvider>(sp => sp.GetRequiredService<MockEmailProvider>());
        }

        return services;
    }
}
