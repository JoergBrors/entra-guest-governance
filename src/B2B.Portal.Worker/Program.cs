using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Application.Workloads;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure;
using B2B.Portal.Infrastructure.Auth;
using B2B.Portal.Infrastructure.Directory;
using B2B.Portal.Worker;
using B2B.Portal.Worker.Handlers.Discovery;
using B2B.Portal.Worker.Handlers.Invitation;
using B2B.Portal.Worker.Handlers.Lifecycle;
using B2B.Portal.Worker.Handlers.Notifications;
using B2B.Portal.Worker.Handlers.Provisioning;
using B2B.Portal.Worker.Handlers.Reconciliation;
using B2B.Portal.Worker.Handlers.Reviews;
using B2B.Portal.Worker.Handlers.Workloads;
using B2B.Portal.Worker.Processing;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddDotEnvLocal();
builder.Configuration.AddEnvironmentVariables();

var mode = builder.Configuration["B2B_MODE"] ?? "LOCAL_MOCK";
Console.WriteLine($"[B2B.Portal.Worker] Startmodus: {mode}");
if (mode == "LOCAL_MOCK")
{
    Console.WriteLine("[B2B.Portal.Worker] LOCAL_MOCK aktiv — keine externen Directory-/Mail-Schreibzugriffe.");
}

// Der Worker stellt/validiert selbst keine JWTs (kein HTTP-Endpoint), registriert
// IdentityProviderConfig/MockJwtIssuer aber trotzdem mit, weil AddB2BInfrastructure sie
// braucht — hier ist die Doppel-Erzeugung des Dev-Ephemeral-Keys unschaedlich, da der Worker
// im selben Prozess keine Tokens ausstellt oder prueft.
builder.Services.AddB2BInfrastructure(
    builder.Configuration, IdentityProviderConfig.FromConfiguration(builder.Configuration, mode));

// Composition Root: konkrete Application Services registrieren.
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<ProvisioningService>();
builder.Services.AddSingleton<LifecycleService>();
builder.Services.AddSingleton<WorkloadManagementService>();

// Handlergruppen registrieren (MVP-Dokument Abschnitt 5).
builder.Services.AddSingleton<IJobHandler, InvitationHandler>();
builder.Services.AddSingleton<IJobHandler, ResendInvitationHandler>();
builder.Services.AddSingleton<IJobHandler>(sp => new InvitationReminderHandler(
    sp.GetRequiredService<B2B.Portal.Application.Ports.IEmailProvider>(),
    sp.GetRequiredService<B2B.Portal.Application.Ports.IGuestAccountRepository>(),
    senderMailboxConfig: builder.Configuration["NOTIFICATIONS_SHARED_MAILBOX"] ?? "b2b-notifications@local.mock",
    sp.GetRequiredService<ILogger<InvitationReminderHandler>>()));
builder.Services.AddSingleton<IJobHandler, GrantWorkloadRoleHandler>();
builder.Services.AddSingleton<IJobHandler, RevokeWorkloadRoleHandler>();
builder.Services.AddSingleton<IJobHandler, DeployScenarioHandler>();
builder.Services.AddSingleton<IJobHandler, DiscoveryHandler>();
builder.Services.AddSingleton<IJobHandler, ReconciliationHandler>();
builder.Services.AddSingleton<IJobHandler, SyncWorkloadPatternResourcesHandler>();
builder.Services.AddSingleton<IJobHandler, StartReviewHandler>();
builder.Services.AddSingleton<IJobHandler, ApplyReviewDecisionHandler>();
builder.Services.AddSingleton<IJobHandler>(sp => new NotificationHandler(
    sp.GetRequiredService<B2B.Portal.Application.Ports.IEmailProvider>(),
    senderMailboxConfig: builder.Configuration["NOTIFICATIONS_SHARED_MAILBOX"] ?? "b2b-notifications@local.mock",
    sp.GetRequiredService<ILogger<NotificationHandler>>()));
builder.Services.AddSingleton<IJobHandler, ValidateDeletionHandler>();
builder.Services.AddSingleton<IJobHandler, DisableGuestHandler>();
builder.Services.AddSingleton<IJobHandler>(sp => new DeleteGuestHandler(
    sp.GetRequiredService<B2B.Portal.Application.Ports.IGuestAccountRepository>(),
    sp.GetRequiredService<B2B.Portal.Application.Ports.IAssignmentRepository>(),
    sp.GetRequiredService<LifecycleService>(),
    allowGuestDelete: bool.TryParse(builder.Configuration["ALLOW_GUEST_DELETE"], out var d) && d,
    sp.GetRequiredService<ILogger<DeleteGuestHandler>>()));

builder.Services.AddSingleton<JobDispatcher>();
builder.Services.AddHostedService<PollingWorker>();
if (mode == "LOCAL_MOCK")
{
    builder.Services.AddHostedService<ApplicationSignInSyncWorker>();
    builder.Services.AddHostedService<InvitationReminderWorker>();
    builder.Services.AddHostedService<WorkloadPatternSyncWorker>();
    builder.Services.AddHostedService<DiscoveryReconciliationWorker>();
}

var host = builder.Build();

// Der Worker hat sein eigenes MockEntraDirectoryStore-Singleton (separater Prozess von
// B2B.Portal.Api) und hydrierte es bisher nie — weder Users noch Gruppen. Ohne dies fand
// z.B. SyncWorkloadPatternResourcesHandler nach einem Worker-Neustart eine bereits am
// Workload haengende Gruppe (WorkloadResource.ExternalId) im eigenen MockEntraDirectoryStore
// nicht wieder. HydrateFromRepositoryAsync laedt jetzt den vollstaendigen Mock-Entra-Bestand
// (Users/Groups/Memberships/Applications/AppSignIns) aus dem dedizierten Cosmos-Container
// "entraid" — anschliessend prueft ReconcileWorkloadResourcesAsync je bekanntem Tenant nur
// noch (Discovery-Style: Ist aus "entraid" vs. Soll aus "domain"), ob alle von Workloads
// referenzierten Ressourcen im Verzeichnis noch existieren, OHNE fehlende Gruppen mehr
// selbst anzulegen (siehe dortiger Kommentar). Analog zum Startup-Hydration-Block in
// B2B.Portal.Api/Program.cs.
if (mode == "LOCAL_MOCK")
{
    using var startupScope = host.Services.CreateScope();
    var mockEntraStore = startupScope.ServiceProvider.GetRequiredService<MockEntraDirectoryStore>();
    try
    {
        await mockEntraStore.HydrateFromRepositoryAsync(CancellationToken.None);

        var workloadRepo = startupScope.ServiceProvider.GetRequiredService<IWorkloadRepository>();
        var startupLogger = startupScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var missingCount = 0;
        foreach (var tenantId in mockEntraStore.ListKnownPlatformTenantIds())
        {
            missingCount += await mockEntraStore.ReconcileWorkloadResourcesAsync(
                TenantContext.Create(tenantId), workloadRepo, startupLogger, CancellationToken.None);
        }

        Console.WriteLine(
            $"[B2B.Portal.Worker] Mock-Entra-Store beim Start hydriert: {mockEntraStore.ListUsers().Count} Benutzer, " +
            $"{mockEntraStore.ListGroups().Count} Gruppen bekannt" +
            (missingCount > 0 ? $" ({missingCount} Workload-Ressource(n) ohne bekannte Verzeichnis-Gruppe, siehe Warnungen oben)." : "."));
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"[B2B.Portal.Worker] WARNUNG: Startup-Hydration des Mock-Entra-Store fehlgeschlagen " +
            $"(Cosmos evtl. nicht erreichbar): {ex.Message}");
    }
}

host.Run();
