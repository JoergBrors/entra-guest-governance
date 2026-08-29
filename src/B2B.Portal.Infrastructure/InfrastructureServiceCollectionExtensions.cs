using B2B.Portal.Application.Ports;
using B2B.Portal.Infrastructure.Data;
using B2B.Portal.Infrastructure.Data.Cosmos;
using B2B.Portal.Infrastructure.Directory;
using B2B.Portal.Infrastructure.Email;
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
    public static IServiceCollection AddB2BInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration["B2B_MODE"] ?? "LOCAL_MOCK";
        var directoryProvider = configuration["DIRECTORY_PROVIDER"] ?? "mock";
        var emailProvider = configuration["EMAIL_PROVIDER"] ?? "mock";
        // LOCAL_MOCK laeuft standardmaessig bereits vollstaendig gegen den lokalen Cosmos DB
        // Emulator (nicht nur InMemory) — "mock" bezieht sich auf Directory/Email (keine
        // echten Graph-/Mail-Schreibzugriffe), nicht auf die Datenhaltung selbst. Ein
        // Emulator muss dafuer laufen (scripts/requirements.ps1 -InitCosmosEmulator).
        // DATA_PROVIDER=local kann weiterhin explizit gesetzt werden, um ohne Emulator zu
        // arbeiten (z. B. schnelle Unit-artige manuelle Tests ohne laufenden Emulator).
        var dataProviderDefault = mode.Equals("LOCAL_MOCK", StringComparison.OrdinalIgnoreCase)
            ? "cosmos" : "local";
        var dataProvider = configuration["DATA_PROVIDER"] ?? dataProviderDefault;
        var allowGraphWrites = bool.TryParse(configuration["ALLOW_GRAPH_WRITES"], out var g) && g;

        services.AddSingleton<IClock, SystemClock>();

        // "cosmos" (Default unter LOCAL_MOCK) nutzt den lokalen Cosmos DB Emulator (siehe
        // scripts/requirements.ps1 -InitCosmosEmulator) oder eine echte Cosmos-DB in
        // DEV_INTEGRATION/AZURE_DEV — Container-/Datenbanknamen siehe
        // infra/modules/cosmos-free-tier.bicep. "local" ist der explizite Opt-out auf
        // InMemory (kein Emulator noetig).
        if (dataProvider.Equals("cosmos", StringComparison.OrdinalIgnoreCase))
        {
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
        }
        else
        {
            services.AddSingleton<IJobQueue, LocalJobQueue>();
            services.AddSingleton<IGuestAccountRepository, InMemoryGuestAccountRepository>();
            services.AddSingleton<IWorkloadRepository, InMemoryWorkloadRepository>();
            services.AddSingleton<IAssignmentRepository, InMemoryAssignmentRepository>();
            services.AddSingleton<IReviewRepository, InMemoryReviewRepository>();
            services.AddSingleton<IJobRepository, InMemoryJobRepository>();
            services.AddSingleton<IResourceAccessRepository, InMemoryResourceAccessRepository>();
            services.AddSingleton<IAuditWriter, InMemoryAuditWriter>();
            services.AddSingleton<IWorkloadScenarioRepository, InMemoryWorkloadScenarioRepository>();
            services.AddSingleton<IExternalOrganizationRepository, InMemoryExternalOrganizationRepository>();
        }

        if (directoryProvider.Equals("graph", StringComparison.OrdinalIgnoreCase) && mode != "LOCAL_MOCK")
        {
            // Integration pending: siehe docs/architecture/mvp-test-report.md.
            // Es wird bewusst kein Graph-Adapter mit erfundenen IDs registriert.
            throw new NotSupportedException(
                "DIRECTORY_PROVIDER=graph ist noch nicht implementiert (Integration pending). " +
                "Nutze DIRECTORY_PROVIDER=mock oder ergänze den Graph-Adapter in DEV_INTEGRATION.");
        }

        services.AddSingleton<IGuestDirectory, MockGuestDirectory>();
        services.AddSingleton<IResourceConnector>(new MockResourceConnector("SecurityGroup"));

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
