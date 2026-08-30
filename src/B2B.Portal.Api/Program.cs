using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using B2B.Portal.Api.Auth;
using B2B.Portal.Api.Tenancy;
using B2B.Portal.Application.Commands;
using B2B.Portal.Application.Import;
using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Scenarios;
using B2B.Portal.Application.Services;
using B2B.Portal.Application.Workloads;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.Services;
using B2B.Portal.Infrastructure;
using B2B.Portal.Infrastructure.Auth;
using B2B.Portal.Infrastructure.Directory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddDotEnvLocal();
builder.Configuration.AddEnvironmentVariables();

var mode = builder.Configuration["B2B_MODE"] ?? "LOCAL_MOCK";
var identityProviderConfig = IdentityProviderConfig.FromConfiguration(builder.Configuration, mode);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITenantContextAccessor, ClaimsTenantContextAccessor>();
builder.Services.AddSingleton<IPortalUserContextAccessor, ClaimsPortalUserContextAccessor>();
builder.Services.AddB2BInfrastructure(builder.Configuration, identityProviderConfig);

// JWT-Bearer-Validierung ersetzt die freien X-Portal-*-Header (Erweiterung 2026-08-30:
// Identity Provider + JWT). Derselbe Signing-Key wie bei der Ausstellung (MockJwtIssuer) —
// beide lesen ihn ueber IdentityProviderConfig aus derselben Konfigurationsquelle.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = IdentityProviderConfig.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = IdentityProviderConfig.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(identityProviderConfig.JwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
// FallbackPolicy statt einzelner [Authorize]-Attribute: erzwingt Auth fuer JEDEN Endpoint,
// der nicht explizit .AllowAnonymous() traegt (Program.cs nutzt durchgehend Minimal APIs ohne
// Controller-Attribute). Die in-handler Rollenpruefungen (IsGovernanceAdmin etc.) bleiben
// unveraendert bestehen — dies ist nur die Authentifizierungs-Schicht davor.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<ProvisioningService>();
builder.Services.AddSingleton<LifecycleService>();
builder.Services.AddSingleton<InviteGuestCommandHandler>();
builder.Services.AddSingleton<GrantWorkloadRoleCommandHandler>();
builder.Services.AddSingleton<RevokeWorkloadRoleCommandHandler>();
builder.Services.AddSingleton<DeployScenarioCommandHandler>();
builder.Services.AddSingleton<ScenarioImportExportService>();
builder.Services.AddSingleton<WorkloadManagementService>();
builder.Services.AddSingleton<GuestImportService>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration["WEB_BASE_URL"] ?? "http://localhost:5301")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

Console.WriteLine($"[B2B.Portal.Api] Startmodus: {mode}, IdentityProvider: {identityProviderConfig.Kind}");

// ---- Startup-Hydration Mock-Entra-Store (nur LOCAL_MOCK) -------------------
// Erweiterung 2026-08-30 (Teil 3): loest das Henne-Ei-Problem nach einem Cosmos-Reset/
// API-Neustart — vorher war MockEntraDirectoryStore ein reiner In-Memory-Singleton, der bei
// jedem Prozessstart leer war, und POST /api/auth/mock/login konnte dadurch niemanden
// finden, solange nicht zuvor GET /api/dev/mock-entra/login-users aufgerufen wurde (das
// wiederum nur bereits bekannte Tenants re-hydriert hat — bei leerem Store also gar keine).
// Laedt jetzt beim Start direkt aus Cosmos (IMockEntraUserRepository, der "Source of Truth"
// fuer PortalRoles seit CosmosMockEntraUserRepository), sodass der Login direkt nach
// `dotnet run` funktioniert, ohne vorherigen Warm-up-Request.
if (mode == "LOCAL_MOCK")
{
    using var startupScope = app.Services.CreateScope();
    var mockEntraStore = startupScope.ServiceProvider.GetRequiredService<MockEntraDirectoryStore>();
    try
    {
        await mockEntraStore.HydrateFromRepositoryAsync(CancellationToken.None);
        Console.WriteLine(
            $"[B2B.Portal.Api] Mock-Entra-Store beim Start hydriert: {mockEntraStore.ListUsers().Count} Benutzer bekannt.");
    }
    catch (Exception ex)
    {
        // Cosmos-Emulator evtl. noch nicht bereit / nicht erreichbar — die hart codierten
        // Default-Demo-User aus dem MockEntraDirectoryStore-Konstruktor bleiben trotzdem
        // nutzbar, daher hier nur warnen statt den Start abzubrechen.
        Console.WriteLine(
            $"[B2B.Portal.Api] WARNUNG: Startup-Hydration des Mock-Entra-Store fehlgeschlagen " +
            $"(Cosmos evtl. nicht erreichbar): {ex.Message}");
    }
}

// ---- Health (kein Auth) ----------------------------------------------------
app.MapGet("/health", () => Results.Ok(new { status = "healthy", mode })).AllowAnonymous();

// ---- Auth (Erweiterung 2026-08-30: Identity Provider + JWT) ---------------
// Nur registriert, wenn LOCAL_MOCK aktiv UND der konfigurierte Identity Provider
// EntraIdMock ist — analog zum bestehenden Muster fuer /api/dev/mock-entra/* weiter unten.
if (mode == "LOCAL_MOCK" && identityProviderConfig.Kind == IdentityProviderKind.EntraIdMock)
{
    app.MapPost("/api/auth/mock/login", async (
        MockLoginRequest body, MockEntraDirectoryStore store, MockJwtIssuer issuer,
        IWorkloadRepository workloadRepo, IWorkloadScenarioRepository scenarioRepo,
        CancellationToken ct) =>
    {
        var user = store.ListUsers()
            .FirstOrDefault(u => string.Equals(u.Mail, body.Mail, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            return Results.NotFound(new { error = $"Kein Mock-Entra-Benutzer mit Mail {body.Mail} gefunden." });
        }

        // X-Scenario-Manager-Workload-Ids war zuvor ein freier Header (nie tatsaechlich vom
        // Client gesetzt). Ersatz: workloadIds serverseitig aus WorkloadScenario.ScenarioManagers
        // ableiten (einzige bestehende Quelle fuer "welcher ScenarioManager gehoert zu welchem
        // Workload", siehe Domain/Entities/WorkloadScenario.cs) und als Claim in den Token packen.
        var tenant = B2B.Portal.Domain.ValueObjects.TenantContext.Create(user.PlatformTenantId);
        var workloads = await workloadRepo.ListAsync(tenant, ct);
        var scenarioManagerWorkloadIds = new List<Guid>();
        foreach (var workload in workloads)
        {
            var scenarios = await scenarioRepo.ListByWorkloadAsync(tenant, workload.Id, ct);
            if (scenarios.Any(s => s.ScenarioManagers.Any(m => string.Equals(m, user.Mail, StringComparison.OrdinalIgnoreCase))))
            {
                scenarioManagerWorkloadIds.Add(workload.Id);
            }
        }

        var token = issuer.IssueToken(
            user.ObjectId, user.Mail, user.PortalRoles, user.PlatformTenantId, scenarioManagerWorkloadIds);

        return Results.Ok(new MockLoginResponse(token, user.Mail, user.PortalRoles, user.PlatformTenantId));
    }).AllowAnonymous();

    // JWT ist zustandslos — es gibt serverseitig nichts zu invalidieren (kein Token-Store,
    // keine Revocation-Liste im MVP). Sign-out ist daher rein clientseitig (sessionStorage
    // leeren, siehe AppLayout.tsx) und dieser Endpoint ist ein reiner No-op, der nur existiert
    // damit der Client einen symmetrischen /login-/logout-Aufruf hat, falls spaeter serverseitige
    // Token-Revocation noetig wird (z.B. bei echtem EntraId-Provider mit Refresh-Tokens).
    app.MapPost("/api/auth/mock/logout", () => Results.Ok()).AllowAnonymous();
}

app.MapGet("/api/jobs/{id:guid}", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IJobRepository jobRepository, IWorkloadRepository workloadRepository, CancellationToken ct) =>
{
    var job = await jobRepository.GetAsync(tenantCtx.Current, id, ct);
    if (job is null)
    {
        return Results.NotFound(new { error = $"Job {id} nicht gefunden." });
    }

    if (!await CanAccessJobAsync(userCtx.Current, tenantCtx.Current, job, workloadRepository, ct))
    {
        return Results.StatusCode(403);
    }

    return Results.Ok(await ToJobStatusResponseAsync(tenantCtx.Current, job, workloadRepository, ct));
});

app.MapGet("/api/jobs", async (
    ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IJobRepository jobRepository, IWorkloadRepository workloadRepository, CancellationToken ct) =>
{
    var jobs = await jobRepository.ListAsync(tenantCtx.Current, ct);
    var visible = new List<JobStatusResponse>();
    foreach (var job in jobs)
    {
        if (await CanAccessJobAsync(userCtx.Current, tenantCtx.Current, job, workloadRepository, ct))
        {
            visible.Add(await ToJobStatusResponseAsync(tenantCtx.Current, job, workloadRepository, ct));
        }
    }

    return Results.Ok(visible);
});

app.MapPost("/api/jobs/{id:guid}/stop", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IJobRepository jobRepository, IWorkloadRepository workloadRepository, IJobQueue jobQueue,
    CancellationToken ct) =>
{
    var job = await jobRepository.GetAsync(tenantCtx.Current, id, ct);
    if (job is null)
    {
        return Results.NotFound(new { error = $"Job {id} nicht gefunden." });
    }

    if (!await CanAccessJobAsync(userCtx.Current, tenantCtx.Current, job, workloadRepository, ct))
    {
        return Results.StatusCode(403);
    }

    if (job.Status is JobStatus.Success or JobStatus.DeadLetter or JobStatus.Failed or JobStatus.Cancelled)
    {
        return Results.BadRequest(new { error = $"Job {id} ist bereits im Endstatus {job.Status}." });
    }

    job.Status = JobStatus.Cancelled;
    job.LastError = $"Gestoppt durch {userCtx.Current.Mail}.";
    job.UpdatedAt = DateTimeOffset.UtcNow;
    job.Log.Add(new JobLogEntry(job.UpdatedAt, JobStatus.Cancelled, job.LastError));
    await jobRepository.UpsertAsync(job, ct);
    await jobQueue.CancelAsync(id, ct);

    return Results.Ok(await ToJobStatusResponseAsync(tenantCtx.Current, job, workloadRepository, ct));
});

// Erweiterung 2026-08-30 (Worker/Trigger-Uebersicht): Restart legt bewusst einen NEUEN Job
// mit denselben Parametern an statt den bestehenden DirectoryOperation-Datensatz erneut zu
// versuchen — der fehlgeschlagene Datensatz bleibt unveraendert als Historie/Audit-Spur
// erhalten, der neue Job startet mit RetryCount 0 und eigener CorrelationId. Zugriff: dieselbe
// Sichtbarkeitsregel wie beim Betrachten des Jobs (CanAccessJobAsync) reicht aus — wer den
// fehlgeschlagenen Job sehen darf (Governance Admin oder Workload Owner des betroffenen
// Workload), darf ihn auch neu anstossen; kein zusaetzliches Admin-Gate noetig.
app.MapPost("/api/jobs/{id:guid}/restart", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IJobRepository jobRepository, IWorkloadRepository workloadRepository,
    ProvisioningService provisioningService, CancellationToken ct) =>
{
    var job = await jobRepository.GetAsync(tenantCtx.Current, id, ct);
    if (job is null)
    {
        return Results.NotFound(new { error = $"Job {id} nicht gefunden." });
    }

    if (!await CanAccessJobAsync(userCtx.Current, tenantCtx.Current, job, workloadRepository, ct))
    {
        return Results.StatusCode(403);
    }

    if (job.Status is not (JobStatus.Failed or JobStatus.DeadLetter))
    {
        return Results.BadRequest(new { error = $"Job {id} ist im Status {job.Status} und kann nicht neu gestartet werden (nur Failed/DeadLetter)." });
    }

    if (job.PayloadJson is null)
    {
        return Results.BadRequest(new { error = $"Job {id} hat keinen gespeicherten Payload (vermutlich vor Einfuehrung von Restart erzeugt) und kann nicht neu gestartet werden." });
    }

    object payload = JsonSerializer.Deserialize<JsonElement>(job.PayloadJson);
    var newJob = await provisioningService.EnqueueJobAsync(
        job.PlatformTenantId,
        job.DirectoryTenantId,
        job.JobType,
        job.EntityType,
        job.EntityId,
        job.DesiredStateHash,
        payload,
        correlationId: Guid.NewGuid(),
        ct,
        triggeredBy: $"Restart von {userCtx.Current.Mail} (Original: {job.Id})",
        workloadId: job.WorkloadId);

    return Results.Ok(await ToJobStatusResponseAsync(tenantCtx.Current, newJob, workloadRepository, ct));
});

// Erweiterung 2026-08-30: generische "Jetzt ausfuehren"-Trigger fuer Job-Typen ohne fachlichen
// Kontext-Parameter (kein Guest/Workload/Role aus einem bestehenden Flow). RunDiscovery und
// RunReconciliation hatten bisher UEBERHAUPT keinen Enqueue-Aufrufer im Code. Bewusst unter
// /api/jobs/* (Erweiterung des bestehenden Jobs-Endpunkt-Blocks) statt /api/dev/* — Discovery/
// Reconciliation sind echte Governance-Operationen (auch wenn aktuell nur Mock-Handler
// existieren), kein LOCAL_MOCK-only Test-Tooling wie die /api/dev/seed/*-Endpunkte.
app.MapPost("/api/jobs/trigger/discovery", async (
    ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepository, ProvisioningService provisioningService, CancellationToken ct) =>
{
    if (!userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var tenant = tenantCtx.Current;
    var directoryTenantId = tenant.DirectoryTenantId ?? string.Empty;
    var correlationId = Guid.NewGuid();
    var hash = DesiredStateHasher.Hash("RunDiscovery", tenant.PlatformTenantId, directoryTenantId, correlationId.ToString());

    // Discovery liest ueber IGuestDirectory tenant-weit alle Gaeste/Memberships (siehe
    // DiscoveryHandler) — es gibt keine einzelne Zielentitaet, daher EntityType "Tenant" mit
    // dem DirectoryTenantId als EntityId.
    var job = await provisioningService.EnqueueJobAsync(
        tenant.PlatformTenantId, tenant.DirectoryTenantId, JobTypes.RunDiscovery,
        "Tenant", directoryTenantId, hash, new { }, correlationId, ct,
        triggeredBy: userCtx.Current.Mail);

    return Results.Ok(await ToJobStatusResponseAsync(tenant, job, workloadRepository, ct));
});

app.MapPost("/api/jobs/trigger/reconciliation", async (
    ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IGuestAccountRepository guestRepository, IAssignmentRepository assignmentRepository,
    IWorkloadRepository workloadRepository, ProvisioningService provisioningService, CancellationToken ct) =>
{
    if (!userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var tenant = tenantCtx.Current;
    // ReconciliationHandler vergleicht Desired-/Actual-State PRO GAST (job.Payload.GuestId) —
    // anders als Discovery gibt es keinen tenant-weiten Reconciliation-Job. "Jetzt ausfuehren"
    // bedeutet hier daher: fuer jeden Gast mit mindestens einer aktiven Zuweisung einen
    // Reconciliation-Job einreihen (ein Sweep ueber den ganzen Tenant).
    var guests = await guestRepository.ListAsync(tenant, ct);
    var createdJobIds = new List<Guid>();
    foreach (var guest in guests)
    {
        var activeAssignments = await assignmentRepository.ListActiveByGuestAsync(tenant, guest.Id, ct);
        if (activeAssignments.Count == 0)
        {
            continue;
        }

        var correlationId = Guid.NewGuid();
        var hash = DesiredStateHasher.Hash("RunReconciliation", guest.Id.ToString(), correlationId.ToString());
        var job = await provisioningService.EnqueueJobAsync(
            tenant.PlatformTenantId, tenant.DirectoryTenantId, JobTypes.RunReconciliation,
            nameof(GuestAccount), guest.Id.ToString(), hash, new { GuestId = guest.Id }, correlationId, ct,
            triggeredBy: userCtx.Current.Mail);
        createdJobIds.Add(job.Id);
    }

    return Results.Ok(new { queuedJobCount = createdJobIds.Count, jobIds = createdJobIds });
});

// Bewusst ohne [Authorize]/AllowAnonymous-Zwang durch userCtx: die Login-Seite braucht
// Theme/Branding, bevor ein Token existiert. user/platformTenantId sind nur gefuellt, wenn
// ein gueltiges Bearer-Token vorliegt (kein Fallback auf einen Default-User mehr — genau das
// war der urspruengliche Bug: stiller Re-Login nach Sign-out).
app.MapGet("/api/ui/configuration", (HttpContext ctx, IPortalUserContextAccessor userCtx, IConfiguration configuration) =>
{
    var themeId = configuration["DEFAULT_PORTAL_THEME_ID"] ?? "corporate-vibrant";
    if (mode == "LOCAL_MOCK")
    {
        // X-Portal-Theme-Id bleibt bewusst ein freier Header — reine UI-Praeferenz, kein
        // Auth-/Identitaetsbezug (siehe docs/development/local-mock.md).
        var headerThemeId = ctx.Request.Headers["X-Portal-Theme-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerThemeId))
        {
            themeId = headerThemeId;
        }
    }

    var allowedThemeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "corporate-vibrant",
        "functional-minimal",
    };

    if (!allowedThemeIds.Contains(themeId))
    {
        themeId = "corporate-vibrant";
    }

    string? platformTenantId = null;
    object? user = null;
    if (ctx.User.Identity is { IsAuthenticated: true })
    {
        var current = userCtx.Current;
        platformTenantId = app.Services.GetRequiredService<ITenantContextAccessor>().Current.PlatformTenantId;
        user = new { current.Mail, roles = current.Roles };
    }

    return Results.Ok(new
    {
        platformTenantId,
        themeId,
        branding = new { productName = "B2B Guest Governance Portal" },
        user,
    });
}).AllowAnonymous();

// ---- Queries (Blueprint 16.1) --------------------------------------------
app.MapGet("/api/guest-accounts", async (
    ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx, IGuestAccountRepository repo, CancellationToken ct) =>
{
    if (!userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var guests = await repo.ListAsync(tenantCtx.Current, ct);
    return Results.Ok(guests);
});

app.MapGet("/api/guest-accounts/{id:guid}", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx, IGuestAccountRepository repo, CancellationToken ct) =>
{
    var guest = await repo.GetAsync(tenantCtx.Current, id, ct);
    if (guest is null)
    {
        return Results.NotFound();
    }

    if (!userCtx.Current.IsGovernanceAdmin && !string.Equals(userCtx.Current.Mail, guest.Mail, StringComparison.OrdinalIgnoreCase))
    {
        return Results.StatusCode(403);
    }

    return Results.Ok(guest);
});

app.MapGet("/api/me/workloads", async (
    ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IGuestAccountRepository guestRepo, IAssignmentRepository assignmentRepo, IWorkloadRepository workloadRepo,
    CancellationToken ct) =>
{
    var guest = await guestRepo.GetByMailAsync(tenantCtx.Current, userCtx.Current.Mail, ct);
    if (guest is null)
    {
        return Results.Ok(Array.Empty<Workload>());
    }

    var assignments = await assignmentRepo.ListActiveByGuestAsync(tenantCtx.Current, guest.Id, ct);
    var result = new List<Workload>();
    foreach (var assignment in assignments)
    {
        var workload = await workloadRepo.GetAsync(tenantCtx.Current, assignment.WorkloadId, ct);
        if (workload is not null)
        {
            result.Add(ProjectWorkloadForUser(workload, assignment.RoleId));
        }
    }

    return Results.Ok(result);
});

app.MapGet("/api/me/navigation", (IPortalUserContextAccessor userCtx) =>
{
    var user = userCtx.Current;
    var items = new List<string> { "Start", "Meine Workloads", "Meine Zugriffe", "Anträge", "Profil" };
    if (user.CanReview)
    {
        items.Insert(4, "Meine Reviews");
    }
    if (user.IsGovernanceAdmin)
    {
        items.AddRange(["Übersicht", "Guest Pool", "Workloads", "Einladungen", "Reviews", "Zugriffsanträge", "Compliance", "Audit", "Ressourcen / Discovery", "Jobs", "Templates", "Konfiguration"]);
    }
    else if (user.HasRole(PortalRoles.WorkloadOwner) || user.HasRole(PortalRoles.ScenarioManager))
    {
        items.Add("Workloads");
    }

    return Results.Ok(new { items });
});

app.MapGet("/api/workloads/{id:guid}", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, IAssignmentRepository assignmentRepo, IGuestAccountRepository guestRepo,
    CancellationToken ct) =>
{
    var workload = await workloadRepo.GetAsync(tenantCtx.Current, id, ct);
    if (workload is null)
    {
        return Results.NotFound();
    }

    if (userCtx.Current.IsGovernanceAdmin || userCtx.Current.CanManageWorkload(workload.Owner)
        || userCtx.Current.ScenarioManagerWorkloadIds.Contains(workload.Id))
    {
        return Results.Ok(workload);
    }

    var guest = await guestRepo.GetByMailAsync(tenantCtx.Current, userCtx.Current.Mail, ct);
    if (guest is null)
    {
        return Results.StatusCode(403);
    }

    var assignments = await assignmentRepo.ListActiveByGuestAsync(tenantCtx.Current, guest.Id, ct);
    return assignments.Any(a => a.WorkloadId == workload.Id)
        ? Results.Ok(ProjectWorkloadForUser(workload, assignments.First(a => a.WorkloadId == workload.Id).RoleId))
        : Results.StatusCode(403);
});

app.MapGet("/api/workloads", async (
    ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx, IWorkloadRepository repo, CancellationToken ct) =>
{
    var workloads = await repo.ListAsync(tenantCtx.Current, ct);
    if (userCtx.Current.IsGovernanceAdmin)
    {
        return Results.Ok(workloads);
    }

    var scoped = workloads
        .Where(w => userCtx.Current.CanManageWorkload(w.Owner) || userCtx.Current.ScenarioManagerWorkloadIds.Contains(w.Id))
        .ToList();
    return Results.Ok(scoped);
});

app.MapPost("/api/workloads", async (
    CreateWorkloadBody body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    WorkloadManagementService service, ProvisioningService provisioningService, CancellationToken ct) =>
{
    if (!userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var workload = await service.CreateWorkloadAsync(
        tenantCtx.Current, body.Name, body.Owner, body.TemplateId,
        body.IsDefault, body.AdministrativeUnitExternalId, body.ApplicationExternalId,
        body.ResourceNamePatterns ?? [],
        userCtx.Current.Mail, ct);
    var syncJob = await EnqueuePatternSyncJobAsync(tenantCtx.Current, workload, provisioningService, userCtx.Current.Mail, ct);
    return Results.Created($"/api/workloads/{workload.Id}", new WorkloadMutationResponse(workload, syncJob?.Id));
});

app.MapPut("/api/workloads/{id:guid}", async (
    Guid id, UpdateWorkloadBody body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, ProvisioningService provisioningService, CancellationToken ct) =>
{
    try
    {
        var existing = await workloadRepo.GetAsync(tenantCtx.Current, id, ct);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Workload {id} nicht gefunden." });
        }
        if (!userCtx.Current.CanManageWorkload(existing.Owner))
        {
            return Results.StatusCode(403);
        }

        var workload = await service.UpdateWorkloadAsync(
            tenantCtx.Current, id, body.Name, body.Owner,
            body.AdministrativeUnitExternalId, body.ApplicationExternalId,
            body.ResourceNamePatterns ?? [],
            actor: userCtx.Current.Mail, ct);
        var syncJob = await EnqueuePatternSyncJobAsync(tenantCtx.Current, workload, provisioningService, userCtx.Current.Mail, ct);
        return Results.Ok(new WorkloadMutationResponse(workload, syncJob?.Id));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapDelete("/api/workloads/{id:guid}", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, CancellationToken ct) =>
{
    try
    {
        var existing = await workloadRepo.GetAsync(tenantCtx.Current, id, ct);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Workload {id} nicht gefunden." });
        }
        if (!userCtx.Current.CanManageWorkload(existing.Owner))
        {
            return Results.StatusCode(403);
        }

        await service.DeactivateWorkloadAsync(tenantCtx.Current, id, actor: userCtx.Current.Mail, ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapPost("/api/workloads/{id:guid}/reactivate", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, CancellationToken ct) =>
{
    try
    {
        var existing = await workloadRepo.GetAsync(tenantCtx.Current, id, ct);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Workload {id} nicht gefunden." });
        }
        if (!userCtx.Current.CanManageWorkload(existing.Owner))
        {
            return Results.StatusCode(403);
        }

        await service.ReactivateWorkloadAsync(tenantCtx.Current, id, actor: userCtx.Current.Mail, ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapDelete("/api/workloads/{id:guid}/permanent", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, CancellationToken ct) =>
{
    try
    {
        var existing = await workloadRepo.GetAsync(tenantCtx.Current, id, ct);
        if (existing is null)
        {
            return Results.NotFound(new { error = $"Workload {id} nicht gefunden." });
        }
        if (!userCtx.Current.CanManageWorkload(existing.Owner))
        {
            return Results.StatusCode(403);
        }

        await service.DeleteWorkloadAsync(tenantCtx.Current, id, actor: userCtx.Current.Mail, ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapGet("/api/workloads/{id:guid}/assignment-counts", async (
    Guid id, ITenantContextAccessor tenantCtx, WorkloadManagementService service, CancellationToken ct) =>
{
    var counts = await service.GetAssignmentCountsAsync(tenantCtx.Current, id, ct);
    return Results.Ok(counts);
});

app.MapPost("/api/workloads/{workloadId:guid}/roles", async (
    Guid workloadId, UpsertWorkloadRoleBody body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, CancellationToken ct) =>
{
    try
    {
        var workload = await workloadRepo.GetAsync(tenantCtx.Current, workloadId, ct);
        if (workload is null) return Results.NotFound(new { error = $"Workload {workloadId} nicht gefunden." });
        if (!userCtx.Current.CanManageWorkload(workload.Owner)) return Results.StatusCode(403);

        var role = await service.UpsertRoleAsync(
            tenantCtx.Current, workloadId, roleId: null, body.Name,
            body.ApplicationId, body.ApplicationRoleId, body.ResourceMappings,
            actor: userCtx.Current.Mail, ct);
        return Results.Ok(role);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPut("/api/workloads/{workloadId:guid}/roles/{roleId:guid}", async (
    Guid workloadId, Guid roleId, UpsertWorkloadRoleBody body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, CancellationToken ct) =>
{
    try
    {
        var workload = await workloadRepo.GetAsync(tenantCtx.Current, workloadId, ct);
        if (workload is null) return Results.NotFound(new { error = $"Workload {workloadId} nicht gefunden." });
        if (!userCtx.Current.CanManageWorkload(workload.Owner)) return Results.StatusCode(403);

        var role = await service.UpsertRoleAsync(
            tenantCtx.Current, workloadId, roleId, body.Name,
            body.ApplicationId, body.ApplicationRoleId, body.ResourceMappings,
            actor: userCtx.Current.Mail, ct);
        return Results.Ok(role);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/workloads/{workloadId:guid}/roles/{roleId:guid}", async (
    Guid workloadId, Guid roleId, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, CancellationToken ct) =>
{
    try
    {
        var workload = await workloadRepo.GetAsync(tenantCtx.Current, workloadId, ct);
        if (workload is null) return Results.NotFound(new { error = $"Workload {workloadId} nicht gefunden." });
        if (!userCtx.Current.CanManageWorkload(workload.Owner)) return Results.StatusCode(403);

        await service.DeleteRoleAsync(tenantCtx.Current, workloadId, roleId, actor: userCtx.Current.Mail, ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapPost("/api/workloads/{workloadId:guid}/resources", async (
    Guid workloadId, UpsertWorkloadResourceBody body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, CancellationToken ct) =>
{
    try
    {
        var workload = await workloadRepo.GetAsync(tenantCtx.Current, workloadId, ct);
        if (workload is null) return Results.NotFound(new { error = $"Workload {workloadId} nicht gefunden." });
        if (!userCtx.Current.CanManageWorkload(workload.Owner)) return Results.StatusCode(403);

        var resource = await service.UpsertResourceAsync(
            tenantCtx.Current, workloadId, resourceId: null, body.ResourceType, body.ExternalId, actor: userCtx.Current.Mail, ct);
        return Results.Ok(resource);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPut("/api/workloads/{workloadId:guid}/resources/{resourceId:guid}", async (
    Guid workloadId, Guid resourceId, UpsertWorkloadResourceBody body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, CancellationToken ct) =>
{
    try
    {
        var workload = await workloadRepo.GetAsync(tenantCtx.Current, workloadId, ct);
        if (workload is null) return Results.NotFound(new { error = $"Workload {workloadId} nicht gefunden." });
        if (!userCtx.Current.CanManageWorkload(workload.Owner)) return Results.StatusCode(403);

        var resource = await service.UpsertResourceAsync(
            tenantCtx.Current, workloadId, resourceId, body.ResourceType, body.ExternalId, actor: userCtx.Current.Mail, ct);
        return Results.Ok(resource);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/workloads/{workloadId:guid}/resources/{resourceId:guid}", async (
    Guid workloadId, Guid resourceId, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, CancellationToken ct) =>
{
    try
    {
        var workload = await workloadRepo.GetAsync(tenantCtx.Current, workloadId, ct);
        if (workload is null) return Results.NotFound(new { error = $"Workload {workloadId} nicht gefunden." });
        if (!userCtx.Current.CanManageWorkload(workload.Owner)) return Results.StatusCode(403);

        await service.DeleteResourceAsync(tenantCtx.Current, workloadId, resourceId, actor: userCtx.Current.Mail, ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapGet("/api/reviews", async (
    ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx, IReviewRepository repo, CancellationToken ct) =>
{
    if (!userCtx.Current.CanReview)
    {
        return Results.StatusCode(403);
    }

    var reviews = await repo.ListOpenAsync(tenantCtx.Current, ct);
    return Results.Ok(reviews);
});

app.MapPost("/api/workloads/{workloadId:guid}/resources/attach", async (
    Guid workloadId, AttachWorkloadResourceBody body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, WorkloadManagementService service, CancellationToken ct) =>
{
    try
    {
        var workload = await workloadRepo.GetAsync(tenantCtx.Current, workloadId, ct);
        if (workload is null) return Results.NotFound(new { error = $"Workload {workloadId} nicht gefunden." });
        if (!userCtx.Current.CanManageWorkload(workload.Owner)) return Results.StatusCode(403);

        var resource = await service.AttachResourceAsync(
            tenantCtx.Current, workloadId, body.ResourceType, body.ExternalId, actor: userCtx.Current.Mail, ct);
        return Results.Ok(resource);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/reviews/{reviewInstanceId:guid}/items/{reviewItemId:guid}/decision", async (
    Guid reviewInstanceId, Guid reviewItemId, ReviewDecisionBody body,
    ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IReviewRepository reviewRepo, ProvisioningService provisioningService, CancellationToken ct) =>
{
    if (!userCtx.Current.CanReview)
    {
        return Results.StatusCode(403);
    }

    var review = await reviewRepo.GetAsync(tenantCtx.Current, reviewInstanceId, ct);
    if (review is null || review.Items.All(i => i.Id != reviewItemId))
    {
        return Results.NotFound();
    }

    var decision = Enum.Parse<ReviewDecision>(body.Decision, ignoreCase: true);
    var hash = DesiredStateHasher.Hash("ApplyReviewDecision", reviewInstanceId.ToString(), reviewItemId.ToString(), decision.ToString());
    await provisioningService.EnqueueJobAsync(
        tenantCtx.Current.PlatformTenantId,
        tenantCtx.Current.DirectoryTenantId,
        JobTypes.ApplyReviewDecision,
        nameof(ReviewInstance),
        reviewInstanceId.ToString(),
        hash,
        new { ReviewItemId = reviewItemId, Decision = decision.ToString(), Actor = userCtx.Current.Mail },
        Guid.NewGuid(),
        ct,
        triggeredBy: userCtx.Current.Mail);

    return Results.Accepted();
});

app.MapGet("/api/audit-events", async (
    ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx, IAuditWriter auditWriter, CancellationToken ct) =>
{
    if (!userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var events = await auditWriter.QueryAsync(tenantCtx.Current, take: 100, ct);
    return Results.Ok(events);
});

// ---- Commands (Blueprint 16.1) -------------------------------------------
app.MapPost("/api/guests/invite", async (
    InviteGuestBody body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx, InviteGuestCommandHandler handler,
    CancellationToken ct) =>
{
    if (!userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var request = new InviteGuestRequest(
        tenantCtx.Current.PlatformTenantId,
        tenantCtx.Current.DirectoryTenantId ?? body.DirectoryTenantId ?? string.Empty,
        body.Mail, body.DisplayName, Actor: userCtx.Current.Mail);
    var guest = await handler.HandleAsync(request, ct);
    return Results.Ok(guest);
});

app.MapPost("/api/workloads/{workloadId:guid}/assignments", async (
    Guid workloadId, AssignmentBody body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, GrantWorkloadRoleCommandHandler handler, CancellationToken ct) =>
{
    var workload = await workloadRepo.GetAsync(tenantCtx.Current, workloadId, ct);
    if (workload is null)
    {
        return Results.NotFound(new { error = $"Workload {workloadId} nicht gefunden." });
    }
    if (!userCtx.Current.CanManageWorkload(workload.Owner))
    {
        return Results.StatusCode(403);
    }

    var request = new GrantWorkloadRoleRequest(
        tenantCtx.Current.PlatformTenantId, body.GuestId, workloadId, body.RoleId, Actor: userCtx.Current.Mail);
    var assignment = await handler.HandleAsync(request, ct);
    return Results.Ok(assignment);
});

app.MapPost("/api/assignments/{id:guid}/revoke", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IAssignmentRepository assignmentRepo, IWorkloadRepository workloadRepo,
    RevokeWorkloadRoleCommandHandler handler, CancellationToken ct) =>
{
    var assignment = await assignmentRepo.GetAsync(tenantCtx.Current, id, ct);
    if (assignment is null)
    {
        return Results.NotFound();
    }
    var workload = await workloadRepo.GetAsync(tenantCtx.Current, assignment.WorkloadId, ct);
    if (workload is null)
    {
        return Results.NotFound();
    }
    if (!userCtx.Current.CanManageWorkload(workload.Owner) && !userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var request = new RevokeWorkloadRoleRequest(tenantCtx.Current.PlatformTenantId, id, Actor: userCtx.Current.Mail);
    await handler.HandleAsync(request, assignment, ct);
    return Results.Accepted();
});

app.MapGet("/api/guest-accounts/{id:guid}/assignments", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IGuestAccountRepository guestRepo, IAssignmentRepository assignmentRepo, CancellationToken ct) =>
{
    var guest = await guestRepo.GetAsync(tenantCtx.Current, id, ct);
    if (guest is null)
    {
        return Results.NotFound();
    }
    if (!userCtx.Current.IsGovernanceAdmin && !string.Equals(userCtx.Current.Mail, guest.Mail, StringComparison.OrdinalIgnoreCase))
    {
        return Results.StatusCode(403);
    }

    var assignments = await assignmentRepo.ListByGuestAsync(tenantCtx.Current, id, ct);
    return Results.Ok(assignments);
});

app.MapPost("/api/deletion-candidates/{guestId:guid}/validate", async (
    Guid guestId, DeletionValidationBody? body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    LifecycleService lifecycleService, CancellationToken ct) =>
{
    if (!userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var gracePeriodReached = body?.GracePeriodReached ?? false;
    var evaluation = await lifecycleService.EvaluateDeletionAsync(
        tenantCtx.Current.PlatformTenantId, guestId, gracePeriodReached, Guid.NewGuid(), ct);
    return Results.Ok(evaluation);
});

// ---- Workload-Szenarien -----------------------------------------------------
app.MapGet("/api/workloads/{workloadId:guid}/scenarios", async (
    Guid workloadId, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, IWorkloadScenarioRepository repo, CancellationToken ct) =>
{
    var workload = await workloadRepo.GetAsync(tenantCtx.Current, workloadId, ct);
    if (workload is null)
    {
        return Results.NotFound();
    }
    if (!userCtx.Current.CanManageWorkload(workload.Owner) &&
        !userCtx.Current.ScenarioManagerWorkloadIds.Contains(workloadId) &&
        !userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var scenarios = await repo.ListByWorkloadAsync(tenantCtx.Current, workloadId, ct);
    return Results.Ok(scenarios);
});

app.MapGet("/api/workloads/{workloadId:guid}/scenarios/{scenarioId:guid}/users", async (
    Guid workloadId, Guid scenarioId, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo, IWorkloadScenarioRepository scenarioRepo, IGuestAccountRepository guestRepo,
    IAssignmentRepository assignmentRepo, MockEntraDirectoryStore mockEntraStore, CancellationToken ct) =>
{
    var workload = await workloadRepo.GetAsync(tenantCtx.Current, workloadId, ct);
    if (workload is null)
    {
        return Results.NotFound(new { error = $"Workload {workloadId} nicht gefunden." });
    }
    if (!userCtx.Current.CanManageWorkload(workload.Owner) &&
        !userCtx.Current.ScenarioManagerWorkloadIds.Contains(workloadId) &&
        !userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var scenario = await scenarioRepo.GetAsync(tenantCtx.Current, scenarioId, ct);
    if (scenario is null || scenario.WorkloadId != workloadId)
    {
        return Results.NotFound(new { error = $"Szenario {scenarioId} nicht gefunden." });
    }

    await HydrateMockEntraFromRepositoriesAsync(tenantCtx.Current, mockEntraStore, guestRepo, workloadRepo, assignmentRepo, ct);
    var scenarioResourceIds = scenario.Rules.Select(r => r.ResourceId).ToHashSet();
    var roleIds = workload.Roles
        .Where(role => role.ResourceMappings.Any(scenarioResourceIds.Contains))
        .Select(role => role.Id)
        .ToHashSet();
    var guests = (await guestRepo.ListAsync(tenantCtx.Current, ct)).ToDictionary(g => g.Id);
    var usersByObjectId = mockEntraStore.ListUsers().ToDictionary(u => u.ObjectId, StringComparer.OrdinalIgnoreCase);
    var appSignIns = string.IsNullOrWhiteSpace(workload.ApplicationExternalId)
        ? []
        : mockEntraStore.ListApplicationSignIns(workload.ApplicationExternalId)
            .ToDictionary(s => s.EntraObjectId, StringComparer.OrdinalIgnoreCase);
    var assignments = await assignmentRepo.ListByWorkloadAsync(tenantCtx.Current, workloadId, ct);
    var rows = assignments
        .Where(a => roleIds.Contains(a.RoleId))
        .Select(a =>
        {
            guests.TryGetValue(a.GuestId, out var guest);
            var entraObjectId = guest?.EntraObjectId ?? string.Empty;
            usersByObjectId.TryGetValue(entraObjectId, out var mockUser);
            appSignIns.TryGetValue(entraObjectId, out var appSignIn);
            var role = workload.Roles.FirstOrDefault(r => r.Id == a.RoleId);
            return new ScenarioUserDto(
                a.GuestId,
                entraObjectId,
                guest?.Mail ?? string.Empty,
                guest?.DisplayName ?? entraObjectId,
                guest?.UserType ?? mockUser?.UserType ?? "Guest",
                role?.Name ?? a.RoleId.ToString(),
                a.Status.ToString(),
                a.Status is AssignmentStatus.Active or AssignmentStatus.Approved or AssignmentStatus.Requested,
                mockUser?.LastLoginAt,
                appSignIn?.LastLoginAt);
        })
        .OrderByDescending(row => row.Active)
        .ThenBy(row => row.DisplayName)
        .ToList();
    return Results.Ok(rows);
});

app.MapPost("/api/scenarios/import", async (
    ScenarioTemplateDto body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadRepository workloadRepo,
    ScenarioImportExportService importExportService, CancellationToken ct) =>
{
    var workloads = await workloadRepo.ListAsync(tenantCtx.Current, ct);
    var targetWorkload = workloads.FirstOrDefault(w => string.Equals(w.Name, body.WorkloadName, StringComparison.OrdinalIgnoreCase));
    if (targetWorkload is not null &&
        !userCtx.Current.CanManageWorkload(targetWorkload.Owner) &&
        !userCtx.Current.ScenarioManagerWorkloadIds.Contains(targetWorkload.Id))
    {
        return Results.StatusCode(403);
    }
    if (targetWorkload is null && !userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    foreach (var rule in body.Rules)
    {
        if (rule.Condition is System.Text.Json.JsonElement condition)
        {
            try
            {
                JsonLogicEvaluator.Validate(condition);
            }
            catch (NotSupportedException ex)
            {
                return Results.BadRequest(new { error = $"Regel für Ressource '{rule.ResourceName}': {ex.Message}" });
            }
        }
    }

    var result = await importExportService.ImportAsync(tenantCtx.Current, body, ct);
    return Results.Ok(result);
});

app.MapGet("/api/scenarios/{id:guid}/export", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadScenarioRepository scenarioRepo, IWorkloadRepository workloadRepo,
    ScenarioImportExportService importExportService, CancellationToken ct) =>
{
    var scenario = await scenarioRepo.GetAsync(tenantCtx.Current, id, ct);
    if (scenario is null)
    {
        return Results.NotFound();
    }
    var workload = await workloadRepo.GetAsync(tenantCtx.Current, scenario.WorkloadId, ct);
    if (workload is null)
    {
        return Results.NotFound();
    }
    if (!userCtx.Current.CanManageScenario(workload.Id, workload.Owner, scenario.ScenarioManagers))
    {
        return Results.StatusCode(403);
    }

    var template = await importExportService.ExportAsync(tenantCtx.Current, id, ct);
    return Results.Ok(template);
});

app.MapPost("/api/scenarios/{id:guid}/deploy", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadScenarioRepository scenarioRepo, IWorkloadRepository workloadRepo,
    DeployScenarioCommandHandler handler, CancellationToken ct) =>
{
    var scenario = await scenarioRepo.GetAsync(tenantCtx.Current, id, ct);
    if (scenario is null)
    {
        return Results.NotFound();
    }
    var workload = await workloadRepo.GetAsync(tenantCtx.Current, scenario.WorkloadId, ct);
    if (workload is null)
    {
        return Results.NotFound();
    }
    if (!userCtx.Current.CanManageScenario(workload.Id, workload.Owner, scenario.ScenarioManagers))
    {
        return Results.StatusCode(403);
    }

    var request = new DeployScenarioRequest(tenantCtx.Current.PlatformTenantId, id, Actor: userCtx.Current.Mail);
    var deployedScenario = await handler.HandleAsync(request, ct);
    return Results.Accepted(value: deployedScenario);
});

app.MapDelete("/api/scenarios/{id:guid}", async (
    Guid id, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    IWorkloadScenarioRepository scenarioRepo, IWorkloadRepository workloadRepo,
    ScenarioImportExportService importExportService, CancellationToken ct) =>
{
    try
    {
        var scenario = await scenarioRepo.GetAsync(tenantCtx.Current, id, ct);
        if (scenario is null)
        {
            return Results.NotFound(new { error = $"WorkloadScenario {id} nicht gefunden." });
        }
        var workload = await workloadRepo.GetAsync(tenantCtx.Current, scenario.WorkloadId, ct);
        if (workload is null)
        {
            return Results.NotFound(new { error = $"Workload {scenario.WorkloadId} nicht gefunden." });
        }
        if (!userCtx.Current.CanManageScenario(workload.Id, workload.Owner, scenario.ScenarioManagers))
        {
            return Results.StatusCode(403);
        }

        await importExportService.DeleteAsync(tenantCtx.Current, id, actor: userCtx.Current.Mail, ct);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

// ---- Excel-Gäste-Import -----------------------------------------------------
// Erster echter Datei-Upload-Endpoint im Projekt (multipart/form-data, IFormFile) — die
// bestehenden Import-Endpoints (Szenario-Template) nehmen reines JSON. Ablauf: inspect
// (Sheet-/Spaltennamen ermitteln) -> preview (Dry-Run, keine Schreibzugriffe) -> commit
// (schreibt Gäste/Zuweisungen, siehe GuestImportService). Mapping wird als JSON-String im
// Formularfeld "mapping" mitgeschickt (kein natives multipart-JSON-Binding in Minimal APIs).
app.MapPost("/api/guest-import/inspect", async (
    HttpRequest request, GuestImportService importService, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Keine Datei im Formularfeld 'file' gefunden." });
    }

    var sheetName = form["sheetName"].FirstOrDefault();
    var headerRowIndex = int.TryParse(form["headerRowIndex"].FirstOrDefault(), out var h) ? h : 1;
    var dataStartColumnIndex = int.TryParse(form["dataStartColumnIndex"].FirstOrDefault(), out var c) ? c : 1;

    await using var stream = file.OpenReadStream();
    var result = importService.Inspect(stream, sheetName, headerRowIndex, dataStartColumnIndex);
    return Results.Ok(result);
});

app.MapPost("/api/guest-import/preview", async (
    HttpRequest request, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    GuestImportService importService, CancellationToken ct) =>
{
    if (!userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var (stream, mapping, error) = await ReadGuestImportForm(request, ct);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    var result = await importService.PreviewAsync(tenantCtx.Current, stream!, mapping!, ct);
    return Results.Ok(result);
});

app.MapPost("/api/guest-import/commit", async (
    HttpRequest request, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
    GuestImportService importService, CancellationToken ct) =>
{
    if (!userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var (stream, mapping, error) = await ReadGuestImportForm(request, ct);
    if (error is not null)
    {
        return Results.BadRequest(new { error });
    }

    var result = await importService.CommitAsync(tenantCtx.Current, stream!, mapping!, actor: userCtx.Current.Mail, ct);
    return Results.Ok(result);
});

// ---- Dev-Only Seed (nur LOCAL_MOCK) ---------------------------------------
// Erzeugt aussagekräftige Demo-/Mockdaten: einen Workload mit mehreren Rollen und eine
// konfigurierbare Anzahl Gäste inkl. Assignments, direkt über die vorhandenen Repositories
// und den ProvisioningService (also über denselben Pfad wie die echten Commands, nur ohne
// 500 einzelne HTTP-Requests). Bewusst nur unter LOCAL_MOCK aktiv — kein Produktionscode,
// keine Graph-Schreibzugriffe (siehe README "Drei Development-Modi").
if (mode == "LOCAL_MOCK")
{
    app.MapGet("/api/dev/mock-entra/users", async (
        ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store,
        IGuestAccountRepository guestRepo, IWorkloadRepository workloadRepo, IAssignmentRepository assignmentRepo,
        CancellationToken ct) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        await HydrateMockEntraFromRepositoriesAsync(tenantCtx.Current, store, guestRepo, workloadRepo, assignmentRepo, ct);
        return Results.Ok(store.ListUsers());
    });

    app.MapGet("/api/dev/mock-entra/login-users", async (
        MockEntraDirectoryStore store,
        IGuestAccountRepository guestRepo, IWorkloadRepository workloadRepo, IAssignmentRepository assignmentRepo,
        CancellationToken ct) =>
    {
        // Muss vor dem Login erreichbar sein (Login-Screen listet hier die waehlbaren
        // Mock-User auf) — daher AllowAnonymous und ohne ITenantContextAccessor (der ein
        // gueltiges Token voraussetzt). Hydration laeuft je Tenant aus dem Mock-Stamm selbst,
        // nicht mehr aus dem (vor Login nicht vorhandenen) Tenant-Kontext einer Request.
        foreach (var tenantId in store.ListUsers().Select(u => u.PlatformTenantId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await HydrateMockEntraFromRepositoriesAsync(
                B2B.Portal.Domain.ValueObjects.TenantContext.Create(tenantId), store, guestRepo, workloadRepo, assignmentRepo, ct);
        }
        return Results.Ok(store.ListUsers());
    }).AllowAnonymous();

    app.MapPost("/api/dev/mock-entra/users", (
        UpsertMockEntraUserBody body, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        var user = store.UpsertUser(ToMockEntraUser(body));
        return Results.Ok(user);
    });

    app.MapPut("/api/dev/mock-entra/users/{objectId}", (
        string objectId, UpsertMockEntraUserBody body, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        var user = store.UpsertUser(ToMockEntraUser(body with { ObjectId = objectId }));
        return Results.Ok(user);
    });

    app.MapDelete("/api/dev/mock-entra/users/{objectId}", (
        string objectId, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        return store.DeleteUser(objectId) ? Results.NoContent() : Results.NotFound();
    });

    app.MapGet("/api/dev/mock-entra/groups", async (
        ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store,
        IGuestAccountRepository guestRepo, IWorkloadRepository workloadRepo, IAssignmentRepository assignmentRepo,
        CancellationToken ct) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        await HydrateMockEntraFromRepositoriesAsync(tenantCtx.Current, store, guestRepo, workloadRepo, assignmentRepo, ct);
        return Results.Ok(store.ListGroups());
    });

    app.MapPost("/api/dev/mock-entra/groups", (
        UpsertMockEntraGroupBody body, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        var group = store.UpsertGroup(ToMockEntraGroup(body));
        return Results.Ok(group);
    });

    app.MapPut("/api/dev/mock-entra/groups/{objectId}", (
        string objectId, UpsertMockEntraGroupBody body, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        var group = store.UpsertGroup(ToMockEntraGroup(body with { ObjectId = objectId }));
        return Results.Ok(group);
    });

    app.MapDelete("/api/dev/mock-entra/groups/{objectId}", (
        string objectId, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        return store.DeleteGroup(objectId) ? Results.NoContent() : Results.NotFound();
    });

    app.MapGet("/api/dev/mock-entra/applications", (
        IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        return Results.Ok(store.ListApplications());
    });

    app.MapPost("/api/dev/mock-entra/applications", (
        UpsertMockEntraApplicationBody body, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        var application = store.UpsertApplication(ToMockEntraApplication(body));
        return Results.Ok(application);
    });

    app.MapPut("/api/dev/mock-entra/applications/{objectId}", (
        string objectId, UpsertMockEntraApplicationBody body, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        var application = store.UpsertApplication(ToMockEntraApplication(body with { ObjectId = objectId }));
        return Results.Ok(application);
    });

    app.MapDelete("/api/dev/mock-entra/applications/{objectId}", (
        string objectId, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        return store.DeleteApplication(objectId) ? Results.NoContent() : Results.NotFound();
    });

    app.MapGet("/api/dev/mock-entra/memberships", async (
        ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store,
        IGuestAccountRepository guestRepo, IWorkloadRepository workloadRepo, IAssignmentRepository assignmentRepo,
        CancellationToken ct) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        await HydrateMockEntraFromRepositoriesAsync(tenantCtx.Current, store, guestRepo, workloadRepo, assignmentRepo, ct);
        return Results.Ok(store.ListAllMemberships());
    });

    app.MapPost("/api/dev/mock-entra/memberships", (
        UpsertMockEntraMembershipBody body, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        store.AddMember(body.GroupId, body.EntraObjectId);
        return Results.NoContent();
    });

    app.MapDelete("/api/dev/mock-entra/groups/{groupId}/members", (
        string groupId, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        var removed = store.RemoveAllMembers(groupId);
        return Results.Ok(new { removed });
    });

    app.MapDelete("/api/dev/mock-entra/memberships", (
        [FromBody] UpsertMockEntraMembershipBody body, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        store.RemoveMember(body.GroupId, body.EntraObjectId);
        return Results.NoContent();
    });

    app.MapGet("/api/dev/mock-entra/application-signins", (
        string? appId, IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        return Results.Ok(store.ListApplicationSignIns(appId));
    });

    app.MapPost("/api/dev/seed/large-workload", async (
        SeedLargeWorkloadBody? body, ITenantContextAccessor tenantCtx,
        IWorkloadRepository workloadRepo, IGuestAccountRepository guestRepo,
        IAssignmentRepository assignmentRepo, ProvisioningService provisioningService,
        MockEntraDirectoryStore mockEntraStore,
        AuditService auditService, CancellationToken ct) =>
    {
        var guestCount = Math.Clamp(body?.GuestCount ?? 500, 1, 5000);
        var tenantId = tenantCtx.Current.PlatformTenantId;
        var directoryTenantId = tenantCtx.Current.DirectoryTenantId ?? "dev-directory-a";
        var correlationId = Guid.NewGuid();

        var workload = DevSeedData.BuildWorkload(tenantId, body?.WorkloadName);
        await workloadRepo.UpsertAsync(workload, ct);
        var defaultWorkload = DevSeedData.BuildDefaultWorkload(tenantId);
        await workloadRepo.UpsertAsync(defaultWorkload, ct);
        foreach (var resource in workload.Resources.Where(r => IsMockEntraGroupResource(r) && r.ExternalId is not null))
        {
            mockEntraStore.EnsureGroup(
                resource.ResourceType,
                resource.ExternalId!,
                new Dictionary<string, string> { ["ResourceType"] = resource.ResourceType });
        }
        foreach (var resource in defaultWorkload.Resources.Where(r => IsMockEntraGroupResource(r) && r.ExternalId is not null))
        {
            mockEntraStore.EnsureGroup(
                resource.ResourceType,
                resource.ExternalId!,
                new Dictionary<string, string> { ["ResourceType"] = resource.ResourceType });
        }
        foreach (var group in mockEntraStore.ListGroups())
        {
            if (DevSeedData.MatchesAnyPattern(group.DisplayName, defaultWorkload.ResourceNamePatterns))
            {
                defaultWorkload.Resources.Add(new WorkloadResource
                {
                    WorkloadId = defaultWorkload.Id,
                    ResourceType = group.ResourceProvisioningOptions.Contains("Team") ? "Team" : group.GroupTypes.Contains("Unified") ? "M365Group" : "SecurityGroup",
                    ExternalId = group.DisplayName,
                    Managed = false,
                });
            }
        }
        defaultWorkload.UpdatedAt = DateTimeOffset.UtcNow;
        await workloadRepo.UpsertAsync(defaultWorkload, ct);

        var createdGuests = new List<GuestAccount>(guestCount);
        foreach (var member in DevSeedData.BuildPlatformMembers(tenantId, directoryTenantId))
        {
            await guestRepo.UpsertAsync(member, ct);
            mockEntraStore.UpsertGuestAccount(member);
            if (!string.IsNullOrWhiteSpace(workload.ApplicationExternalId) && !string.IsNullOrWhiteSpace(member.EntraObjectId))
            {
                mockEntraStore.UpsertApplicationSignIn(workload.ApplicationExternalId, member.EntraObjectId, DateTimeOffset.UtcNow.AddDays(-2));
            }
        }
        for (var i = 0; i < guestCount; i++)
        {
            var guest = DevSeedData.BuildGuest(tenantId, directoryTenantId, i);
            await guestRepo.UpsertAsync(guest, ct);
            mockEntraStore.UpsertGuestAccount(guest);
            if (!string.IsNullOrWhiteSpace(workload.ApplicationExternalId) && !string.IsNullOrWhiteSpace(guest.EntraObjectId))
            {
                mockEntraStore.UpsertApplicationSignIn(workload.ApplicationExternalId, guest.EntraObjectId, DateTimeOffset.UtcNow.AddDays(-(i % 90)));
            }
            createdGuests.Add(guest);

            var role = DevSeedData.PickRole(workload, i);
            var assignment = new GuestWorkloadAssignment
            {
                PlatformTenantId = tenantId,
                GuestId = guest.Id,
                WorkloadId = workload.Id,
                RoleId = role.Id,
                Status = DevSeedData.PickAssignmentStatus(i),
            };
            await assignmentRepo.UpsertAsync(assignment, ct);
            if (assignment.Status == AssignmentStatus.Active && guest.EntraObjectId is not null)
            {
                foreach (var resourceId in role.ResourceMappings)
                {
                    var resource = workload.Resources.FirstOrDefault(r => r.Id == resourceId);
                    if (resource?.ExternalId is not null)
                    {
                        mockEntraStore.AddMember(resource.ExternalId, guest.EntraObjectId);
                    }
                }
            }

            // Realistische Job-Historie: pro Gast ein GrantWorkloadRole-Job, damit
            // Worker-Dashboards/Job-Listen (falls spaeter ergaenzt) nicht leer sind.
            var hash = DesiredStateHasher.Hash(
                "GrantWorkloadRole", guest.Id.ToString(), workload.Id.ToString(), role.Id.ToString());
            await provisioningService.EnqueueJobAsync(
                tenantId, directoryTenantId, JobTypes.GrantWorkloadRole,
                nameof(GuestWorkloadAssignment), assignment.Id.ToString(), hash,
                new { GuestId = guest.Id, WorkloadId = workload.Id, RoleId = role.Id },
                correlationId, ct, triggeredBy: "dev-seed", workloadId: workload.Id);
        }

        await auditService.RecordAsync(
            tenantId, actor: "dev-seed", action: "SeedLargeWorkload", entityType: nameof(Workload),
            entityId: workload.Id.ToString(), result: "Accepted", correlationId: correlationId,
            details: $"{guestCount} guests seeded", ct: ct);

        return Results.Ok(new
        {
            workloadId = workload.Id,
            workloadName = workload.Name,
            roles = workload.Roles.Select(r => new { r.Id, r.Name }),
            guestCount = createdGuests.Count,
            defaultWorkloadId = defaultWorkload.Id,
        });
    });
}

app.Run();

// ---- Helfer für den Excel-Gäste-Import --------------------------------------
// Liest Datei + Mapping-JSON aus einem multipart/form-data-Request. Gibt den Datei-Stream
// (Aufrufer ist für das Schließen verantwortlich — MemoryStream wird hier bewusst
// zwischengepuffert, da ISpreadsheetReader den Stream mehrfach von Position 0 liest) sowie
// das geparste Mapping zurück, oder eine Fehlermeldung statt beidem.
static async Task<(Stream? Stream, GuestImportColumnMapping? Mapping, string? Error)> ReadGuestImportForm(
    HttpRequest request, CancellationToken ct)
{
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return (null, null, "Keine Datei im Formularfeld 'file' gefunden.");
    }

    var mappingJson = form["mapping"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(mappingJson))
    {
        return (null, null, "Kein Mapping im Formularfeld 'mapping' gefunden.");
    }

    GuestImportMappingBody? mappingBody;
    try
    {
        mappingBody = System.Text.Json.JsonSerializer.Deserialize<GuestImportMappingBody>(
            mappingJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (System.Text.Json.JsonException ex)
    {
        return (null, null, $"Mapping konnte nicht gelesen werden: {ex.Message}");
    }
    if (mappingBody is null)
    {
        return (null, null, "Mapping konnte nicht gelesen werden.");
    }

    var buffer = new MemoryStream();
    await using (var uploadStream = file.OpenReadStream())
    {
        await uploadStream.CopyToAsync(buffer, ct);
    }
    buffer.Position = 0;

    var mapping = new GuestImportColumnMapping(
        mappingBody.SheetName, mappingBody.HeaderRowIndex, mappingBody.DataStartColumnIndex,
        mappingBody.ColumnToField.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value));

    return (buffer, mapping, null);
}

static Workload ProjectWorkloadForUser(Workload workload, Guid assignedRoleId)
{
    var assignedRole = workload.Roles.FirstOrDefault(r => r.Id == assignedRoleId);
    var resourceIds = assignedRole?.ResourceMappings.ToHashSet() ?? [];

    var projected = new Workload
    {
        Id = workload.Id,
        PlatformTenantId = workload.PlatformTenantId,
        Name = workload.Name,
        Owner = workload.Owner,
        TemplateId = workload.TemplateId,
        Active = workload.Active,
        IsDefault = workload.IsDefault,
        AdministrativeUnitExternalId = workload.AdministrativeUnitExternalId,
        ApplicationExternalId = workload.ApplicationExternalId,
        CreatedAt = workload.CreatedAt,
        UpdatedAt = workload.UpdatedAt,
        Roles = assignedRole is null ? [] : [assignedRole],
        Resources = [.. workload.Resources.Where(r => resourceIds.Contains(r.Id))],
    };
    projected.ResourceNamePatterns.AddRange(workload.ResourceNamePatterns);
    return projected;
}

static async Task<DirectoryOperation?> EnqueuePatternSyncJobAsync(
    B2B.Portal.Domain.ValueObjects.TenantContext tenant,
    Workload workload,
    ProvisioningService provisioningService,
    string actor,
    CancellationToken ct)
{
    if (workload.ResourceNamePatterns.Count == 0)
    {
        return null;
    }

    return await provisioningService.EnqueueJobAsync(
        tenant.PlatformTenantId,
        tenant.DirectoryTenantId,
        JobTypes.SyncWorkloadPatternResources,
        nameof(Workload),
        workload.Id.ToString(),
        $"{workload.Id}:{string.Join('|', workload.ResourceNamePatterns)}",
        new
        {
            WorkloadId = workload.Id,
            ResourceNamePatterns = workload.ResourceNamePatterns.ToArray(),
            Actor = actor,
        },
        Guid.NewGuid(),
        ct,
        triggeredBy: actor,
        workloadId: workload.Id);
}

static async Task<bool> CanAccessJobAsync(
    PortalUserContext user,
    B2B.Portal.Domain.ValueObjects.TenantContext tenant,
    DirectoryOperation job,
    IWorkloadRepository workloadRepository,
    CancellationToken ct)
{
    if (user.IsGovernanceAdmin)
    {
        return true;
    }

    var workloadId = ResolveJobWorkloadId(job);
    if (workloadId is null)
    {
        return false;
    }

    var workload = await workloadRepository.GetAsync(tenant, workloadId.Value, ct);
    return workload is not null && user.CanManageWorkload(workload.Owner);
}

static Guid? ResolveJobWorkloadId(DirectoryOperation job)
{
    if (job.WorkloadId is not null)
    {
        return job.WorkloadId;
    }

    return job.EntityType == nameof(Workload) && Guid.TryParse(job.EntityId, out var workloadId)
        ? workloadId
        : null;
}

static async Task<JobStatusResponse> ToJobStatusResponseAsync(
    B2B.Portal.Domain.ValueObjects.TenantContext tenant,
    DirectoryOperation job,
    IWorkloadRepository workloadRepository,
    CancellationToken ct)
{
    var workloadId = ResolveJobWorkloadId(job);
    string? workloadName = null;
    if (workloadId is not null)
    {
        var workload = await workloadRepository.GetAsync(tenant, workloadId.Value, ct);
        workloadName = workload?.Name;
    }

    return new JobStatusResponse(
        job.Id, job.JobType, job.EntityType, job.EntityId,
        job.TriggeredBy, workloadId, workloadName,
        job.Status.ToString(), job.RetryCount, job.LastError, job.CreatedAt, job.UpdatedAt,
        [.. job.Log.Select(l => new JobLogEntryResponse(l.Timestamp, l.Status.ToString(), l.Message))]);
}

static async Task HydrateMockEntraFromRepositoriesAsync(
    B2B.Portal.Domain.ValueObjects.TenantContext tenant,
    MockEntraDirectoryStore mockEntraStore,
    IGuestAccountRepository guestRepo,
    IWorkloadRepository workloadRepo,
    IAssignmentRepository assignmentRepo,
    CancellationToken ct)
{
    var guests = await guestRepo.ListAsync(tenant, ct);
    foreach (var guest in guests)
    {
        mockEntraStore.UpsertGuestAccount(guest);
    }

    var guestById = guests
        .Where(g => !string.IsNullOrWhiteSpace(g.EntraObjectId))
        .ToDictionary(g => g.Id, g => g.EntraObjectId!, EqualityComparer<Guid>.Default);
    var workloads = await workloadRepo.ListAsync(tenant, ct);
    foreach (var workload in workloads)
    {
        foreach (var resource in workload.Resources.Where(r => IsMockEntraGroupResource(r) && !string.IsNullOrWhiteSpace(r.ExternalId)))
        {
            mockEntraStore.EnsureGroup(
                resource.ResourceType,
                resource.ExternalId!,
                new Dictionary<string, string> { ["ResourceType"] = resource.ResourceType, ["WorkloadId"] = workload.Id.ToString() });
        }

        var assignments = await assignmentRepo.ListByWorkloadAsync(tenant, workload.Id, ct);
        foreach (var assignment in assignments.Where(a => a.Status is AssignmentStatus.Active or AssignmentStatus.Approved or AssignmentStatus.Requested))
        {
            if (!guestById.TryGetValue(assignment.GuestId, out var entraObjectId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(workload.ApplicationExternalId)
                && mockEntraStore.ListApplicationSignIns(workload.ApplicationExternalId)
                    .All(s => !string.Equals(s.EntraObjectId, entraObjectId, StringComparison.OrdinalIgnoreCase)))
            {
                mockEntraStore.UpsertApplicationSignIn(
                    workload.ApplicationExternalId,
                    entraObjectId,
                    DateTimeOffset.UtcNow.AddDays(-Math.Abs(assignment.Id.GetHashCode() % 90)));
            }

            var role = workload.Roles.FirstOrDefault(r => r.Id == assignment.RoleId);
            if (role is null)
            {
                continue;
            }

            var groupResources = workload.Resources
                .Where(r => role.ResourceMappings.Contains(r.Id))
                .Where(IsMockEntraGroupResource)
                .Where(r => !string.IsNullOrWhiteSpace(r.ExternalId));
            foreach (var resource in groupResources)
            {
                mockEntraStore.AddMember(resource.ExternalId!, entraObjectId);
            }
        }
    }
}

static bool IsMockEntraGroupResource(WorkloadResource resource) =>
    resource.ResourceType.Equals("SecurityGroup", StringComparison.OrdinalIgnoreCase)
    || resource.ResourceType.Equals("M365Group", StringComparison.OrdinalIgnoreCase)
    || resource.ResourceType.Equals("Team", StringComparison.OrdinalIgnoreCase);

static MockEntraUser ToMockEntraUser(UpsertMockEntraUserBody body)
{
    var parts = body.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return new(
        body.ObjectId ?? string.Empty,
        body.UserPrincipalName ?? string.Empty,
        body.Mail,
        body.DisplayName,
        body.GivenName ?? parts.FirstOrDefault() ?? body.DisplayName,
        body.Surname ?? parts.Skip(1).FirstOrDefault() ?? string.Empty,
        body.CompanyName ?? string.Empty,
        body.Department ?? string.Empty,
        body.JobTitle ?? string.Empty,
        body.Sponsor ?? string.Empty,
        body.AccountEnabled ?? "true",
        body.UserType ?? "Guest",
        body.PortalRoles ?? ["User"],
        body.LastLoginAt,
        body.PlatformTenantId ?? "dev-tenant-a");
}

static MockEntraGroup ToMockEntraGroup(UpsertMockEntraGroupBody body) => new(
    body.ObjectId ?? string.Empty,
    body.DisplayName,
    body.MailNickname ?? string.Empty,
    body.Description ?? string.Empty,
    body.GroupTypes ?? [],
    body.MailEnabled,
    body.SecurityEnabled,
    body.ResourceProvisioningOptions ?? []);

static MockEntraApplication ToMockEntraApplication(UpsertMockEntraApplicationBody body) => new(
    body.ObjectId ?? string.Empty,
    body.AppId ?? string.Empty,
    body.DisplayName,
    body.AppRoles ?? []);

// ---- Request-DTOs ----------------------------------------------------------
public sealed record InviteGuestBody(string Mail, string DisplayName, string? DirectoryTenantId = null);
public sealed record AssignmentBody(Guid GuestId, Guid RoleId);
public sealed record ScenarioUserDto(
    Guid GuestId,
    string EntraObjectId,
    string Mail,
    string DisplayName,
    string UserType,
    string RoleName,
    string AssignmentStatus,
    bool Active,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? ApplicationLastLoginAt);
public sealed record DeletionValidationBody(bool GracePeriodReached);
public sealed record ReviewDecisionBody(string Decision);
public sealed record SeedLargeWorkloadBody(int? GuestCount, string? WorkloadName);
public sealed record JobStatusResponse(
    Guid Id,
    string JobType,
    string EntityType,
    string EntityId,
    string? TriggeredBy,
    Guid? WorkloadId,
    string? WorkloadName,
    string Status,
    int RetryCount,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<JobLogEntryResponse> Log);
public sealed record JobLogEntryResponse(DateTimeOffset Timestamp, string Status, string? Message);
public sealed record WorkloadMutationResponse(Workload Workload, Guid? PatternSyncJobId);
public sealed record UpsertMockEntraUserBody(
    string? ObjectId,
    string? UserPrincipalName,
    string Mail,
    string DisplayName,
    string? GivenName,
    string? Surname,
    string? CompanyName,
    string? Department,
    string? JobTitle,
    string? Sponsor,
    string? AccountEnabled,
    string? UserType,
    List<string>? PortalRoles,
    DateTimeOffset? LastLoginAt,
    string? PlatformTenantId);

public sealed record MockLoginRequest(string Mail);

public sealed record MockLoginResponse(string Token, string Mail, IReadOnlyList<string> Roles, string PlatformTenantId);
public sealed record UpsertMockEntraGroupBody(
    string? ObjectId,
    string DisplayName,
    string? MailNickname,
    string? Description,
    List<string>? GroupTypes,
    bool MailEnabled,
    bool SecurityEnabled,
    List<string>? ResourceProvisioningOptions);
public sealed record UpsertMockEntraApplicationBody(
    string? ObjectId,
    string? AppId,
    string DisplayName,
    List<MockEntraApplicationRole>? AppRoles);
public sealed record UpsertMockEntraMembershipBody(string GroupId, string EntraObjectId);
public sealed record CreateWorkloadBody(
    string Name,
    string? Owner,
    string? TemplateId = null,
    bool IsDefault = false,
    string? AdministrativeUnitExternalId = null,
    string? ApplicationExternalId = null,
    List<string>? ResourceNamePatterns = null);
public sealed record UpdateWorkloadBody(
    string Name,
    string? Owner,
    string? AdministrativeUnitExternalId = null,
    string? ApplicationExternalId = null,
    List<string>? ResourceNamePatterns = null);
public sealed record UpsertWorkloadRoleBody(
    string Name,
    string? ApplicationId,
    string? ApplicationRoleId,
    List<Guid> ResourceMappings);
public sealed record UpsertWorkloadResourceBody(string ResourceType, string? ExternalId);
public sealed record AttachWorkloadResourceBody(string ResourceType, string ExternalId);

/// <summary>JSON-Form des GuestImportColumnMapping für den multipart-Formularfeld
/// "mapping" — ColumnToField kommt als Dictionary&lt;string,string&gt; über JSON (Spalten-
/// Offset als String-Schlüssel, da JSON keine numerischen Objekt-Schlüssel kennt) und wird
/// in ReadGuestImportForm zu Dictionary&lt;int,string&gt; konvertiert.</summary>
public sealed record GuestImportMappingBody(
    string SheetName, int HeaderRowIndex, int DataStartColumnIndex, Dictionary<string, string> ColumnToField);

/// <summary>
/// Deterministische, aussagekräftige Demo-Daten für den Dev-Seed-Endpoint
/// (POST /api/dev/seed/large-workload). Keine echten Personen/Firmen — Namen/Firmen sind
/// offensichtlich fiktiv (Beispiel-Domains nach RFC 2606, z.B. .example).
/// </summary>
public static class DevSeedData
{
    private static readonly string[] FirstNames =
    [
        "Anna", "Ben", "Clara", "David", "Elena", "Felix", "Greta", "Hannah", "Ivo", "Julia",
        "Karl", "Lena", "Marco", "Nina", "Oskar", "Petra", "Quentin", "Rosa", "Stefan", "Tina",
        "Uwe", "Vera", "Wolf", "Xenia", "Yannick", "Zoe",
    ];

    private static readonly string[] LastNames =
    [
        "Bergmann", "Cortez", "Diallo", "Eriksson", "Fischer", "Gomez", "Hoffmann", "Ivanov",
        "Jansen", "Klein", "Lindqvist", "Meyer", "Novak", "Ortiz", "Petrov", "Quiroga",
        "Richter", "Santos", "Tanaka", "Ulrich", "Vogel", "Weber", "Yilmaz", "Zimmermann",
    ];

    private static readonly (string Company, string Domain)[] Organizations =
    [
        ("Contoso Consulting", "contoso.example"),
        ("Fabrikam Logistics", "fabrikam.example"),
        ("Northwind Partners", "northwind.example"),
        ("Adventure Works", "adventure-works.example"),
        ("Tailspin Toys", "tailspintoys.example"),
        ("Wingtip Solutions", "wingtiptoys.example"),
        ("Litware Systems", "litware.example"),
        ("Proseware Digital", "proseware.example"),
    ];

    private static readonly string[] Sponsors =
    [
        "sponsor.mueller@platform.example", "sponsor.schmidt@platform.example",
        "sponsor.becker@platform.example", "sponsor.wagner@platform.example",
    ];

    public static Workload BuildWorkload(string platformTenantId, string? name)
    {
        var workload = new Workload
        {
            PlatformTenantId = platformTenantId,
            Name = string.IsNullOrWhiteSpace(name) ? "SAP S/4 Rollout — Projekt Meridian" : name,
            Owner = "workload-owner@platform.example",
            TemplateId = "external-project-standard",
            Active = true,
            AdministrativeUnitExternalId = "AU-MERIDIAN",
            ApplicationExternalId = "app-meridian-governance",
        };
        workload.ResourceNamePatterns.AddRange(["SG-MERIDIAN-*", "GRP-MERIDIAN-*", "TEAM-MERIDIAN-*"]);

        var resources = new[]
        {
            new WorkloadResource { WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "SG-MERIDIAN-READ", Managed = true },
            new WorkloadResource { WorkloadId = workload.Id, ResourceType = "SecurityGroup", ExternalId = "SG-MERIDIAN-CONTRIBUTE", Managed = true },
            new WorkloadResource { WorkloadId = workload.Id, ResourceType = "M365Group", ExternalId = "GRP-MERIDIAN-COLLAB", Managed = true },
            new WorkloadResource { WorkloadId = workload.Id, ResourceType = "Team", ExternalId = "TEAM-MERIDIAN-CORE", Managed = true },
            new WorkloadResource { WorkloadId = workload.Id, ResourceType = "AppRole", ExternalId = "APP-MERIDIAN-ADMIN", Managed = false },
        };
        workload.Resources.AddRange(resources);

        var reader = new WorkloadRole { WorkloadId = workload.Id, Name = "Reader" };
        reader.ResourceMappings.Add(resources[0].Id);

        var contributor = new WorkloadRole { WorkloadId = workload.Id, Name = "Contributor" };
        contributor.ResourceMappings.AddRange([resources[0].Id, resources[1].Id, resources[2].Id]);

        var coreTeam = new WorkloadRole { WorkloadId = workload.Id, Name = "Core Team" };
        coreTeam.ResourceMappings.AddRange([resources[0].Id, resources[1].Id, resources[2].Id, resources[3].Id]);

        var admin = new WorkloadRole
        {
            WorkloadId = workload.Id,
            Name = "Project Admin",
            ApplicationId = "app-meridian-governance",
            ApplicationRoleId = "app-role-admin",
        };
        admin.ResourceMappings.Add(resources[4].Id);

        workload.Roles.AddRange([reader, contributor, coreTeam, admin]);
        return workload;
    }

    public static Workload BuildDefaultWorkload(string platformTenantId)
    {
        var workload = new Workload
        {
            PlatformTenantId = platformTenantId,
            Name = "Default Workload - All Groups",
            Owner = "admin@platform.example",
            TemplateId = "default-all-groups-anchor",
            Active = true,
            IsDefault = true,
            AdministrativeUnitExternalId = "AU-DEFAULT-ALL",
        };
        workload.ResourceNamePatterns.Add("*");
        workload.Resources.Add(new WorkloadResource
        {
            WorkloadId = workload.Id,
            ResourceType = "AdministrativeUnit",
            ExternalId = "AU-DEFAULT-ALL",
            Managed = true,
        });
        return workload;
    }

    public static IReadOnlyList<GuestAccount> BuildPlatformMembers(string platformTenantId, string directoryTenantId) =>
    [
        new()
        {
            PlatformTenantId = platformTenantId,
            DirectoryTenantId = directoryTenantId,
            Mail = "admin@platform.example",
            DisplayName = "Platform Admin",
            Sponsor = "configuration required",
            EntraObjectId = "mock-member-admin",
            UserType = "Member",
        },
        new()
        {
            PlatformTenantId = platformTenantId,
            DirectoryTenantId = directoryTenantId,
            Mail = "workload-owner@platform.example",
            DisplayName = "Workload Owner",
            Sponsor = "admin@platform.example",
            EntraObjectId = "mock-member-owner",
            UserType = "Member",
        },
    ];

    public static bool MatchesAnyPattern(string value, IEnumerable<string> patterns) =>
        patterns.Any(pattern => MatchesPattern(value, pattern));

    private static bool MatchesPattern(string value, string pattern)
    {
        if (pattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            return Regex.IsMatch(value, pattern["regex:".Length..], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (pattern.Length >= 2 && pattern.StartsWith('/') && pattern.EndsWith('/'))
        {
            return Regex.IsMatch(value, pattern[1..^1], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (pattern == "*")
        {
            return true;
        }

        var expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Rollenverteilung, die ein realistisches Projekt widerspiegelt: die meisten Gäste
    /// sind Reader, wenige Contributor/Core Team, eine Handvoll Admins.
    /// </summary>
    public static WorkloadRole PickRole(Workload workload, int index)
    {
        var bucket = index % 20;
        return bucket switch
        {
            < 13 => workload.Roles[0], // 65% Reader
            < 17 => workload.Roles[1], // 20% Contributor
            < 19 => workload.Roles[2], // 10% Core Team
            _ => workload.Roles[3],    // 5% Project Admin
        };
    }

    /// <summary>
    /// Statusverteilung für Assignments, damit Filter/Badges in der UI unterschiedliche
    /// Zustände zeigen statt einer eintönigen Liste.
    /// </summary>
    public static AssignmentStatus PickAssignmentStatus(int index) => (index % 25) switch
    {
        0 => AssignmentStatus.PendingReview,
        1 => AssignmentStatus.Requested,
        2 => AssignmentStatus.Expired,
        _ => AssignmentStatus.Active,
    };

    public static GuestAccount BuildGuest(string platformTenantId, string directoryTenantId, int index)
    {
        var first = FirstNames[index % FirstNames.Length];
        var last = LastNames[(index / FirstNames.Length) % LastNames.Length];
        var org = Organizations[index % Organizations.Length];
        // Eindeutige Mailadresse auch bei Namenswiederholungen durch fortlaufenden Index.
        var mail = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}{index}@{org.Domain}";

        var guest = new GuestAccount
        {
            PlatformTenantId = platformTenantId,
            DirectoryTenantId = directoryTenantId,
            Mail = mail,
            DisplayName = $"{first} {last} ({org.Company})",
            Sponsor = Sponsors[index % Sponsors.Length],
            EntraObjectId = $"seed-obj-{index:D5}",
            UserType = "Guest",
        };

        // Realistischer Lifecycle-Mix statt aller Gäste im selben Zustand.
        var stateBucket = index % 50;
        var targetState = stateBucket switch
        {
            0 => GuestAccountState.Discovered,
            < 4 => GuestAccountState.Invited,
            < 6 => GuestAccountState.OrphanCandidate,
            _ => GuestAccountState.Active,
        };

        if (targetState != GuestAccountState.Discovered)
        {
            guest.TransitionTo(targetState);
        }

        return guest;
    }
}

/// <summary>Partial-Klasse, damit WebApplicationFactory&lt;Program&gt; in Integrationstests funktioniert.</summary>
public partial class Program;

