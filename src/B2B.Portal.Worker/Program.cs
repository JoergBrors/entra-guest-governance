using B2B.Portal.Application.Services;
using B2B.Portal.Infrastructure;
using B2B.Portal.Worker;
using B2B.Portal.Worker.Handlers.Discovery;
using B2B.Portal.Worker.Handlers.Invitation;
using B2B.Portal.Worker.Handlers.Lifecycle;
using B2B.Portal.Worker.Handlers.Notifications;
using B2B.Portal.Worker.Handlers.Provisioning;
using B2B.Portal.Worker.Handlers.Reconciliation;
using B2B.Portal.Worker.Handlers.Reviews;
using B2B.Portal.Worker.Processing;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables();

var mode = builder.Configuration["B2B_MODE"] ?? "LOCAL_MOCK";
Console.WriteLine($"[B2B.Portal.Worker] Startmodus: {mode}");
if (mode == "LOCAL_MOCK")
{
    Console.WriteLine("[B2B.Portal.Worker] LOCAL_MOCK aktiv — keine externen Directory-/Mail-Schreibzugriffe.");
}

builder.Services.AddB2BInfrastructure(builder.Configuration);

// Composition Root: konkrete Application Services registrieren.
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<ProvisioningService>();
builder.Services.AddSingleton<LifecycleService>();

// Handlergruppen registrieren (MVP-Dokument Abschnitt 5).
builder.Services.AddSingleton<IJobHandler, InvitationHandler>();
builder.Services.AddSingleton<IJobHandler, ResendInvitationHandler>();
builder.Services.AddSingleton<IJobHandler, GrantWorkloadRoleHandler>();
builder.Services.AddSingleton<IJobHandler, RevokeWorkloadRoleHandler>();
builder.Services.AddSingleton<IJobHandler, DiscoveryHandler>();
builder.Services.AddSingleton<IJobHandler, ReconciliationHandler>();
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
    allowGuestDelete: bool.TryParse(builder.Configuration["ALLOW_GUEST_DELETE"], out var d) && d,
    sp.GetRequiredService<ILogger<DeleteGuestHandler>>()));

builder.Services.AddSingleton<JobDispatcher>();
builder.Services.AddHostedService<PollingWorker>();

var host = builder.Build();
host.Run();
