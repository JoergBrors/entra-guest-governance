using System.Text.Json;
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
using B2B.Portal.Infrastructure.Directory;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddDotEnvLocal();
builder.Configuration.AddEnvironmentVariables();

var mode = builder.Configuration["B2B_MODE"] ?? "LOCAL_MOCK";

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITenantContextAccessor, HeaderTenantContextAccessor>();
builder.Services.AddSingleton<IPortalUserContextAccessor, HeaderPortalUserContextAccessor>();
builder.Services.AddB2BInfrastructure(builder.Configuration);

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

Console.WriteLine($"[B2B.Portal.Api] Startmodus: {mode}");

// ---- Health --------------------------------------------------------------
app.MapGet("/health", () => Results.Ok(new { status = "healthy", mode }));

app.MapGet("/api/ui/configuration", (IPortalUserContextAccessor userCtx, IConfiguration configuration) =>
{
    var themeId = configuration["DEFAULT_PORTAL_THEME_ID"] ?? "corporate-vibrant";
    if (mode == "LOCAL_MOCK")
    {
        var headerThemeId = app.Services.GetRequiredService<IHttpContextAccessor>()
            .HttpContext?.Request.Headers["X-Portal-Theme-Id"].FirstOrDefault();
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

    var user = userCtx.Current;
    return Results.Ok(new
    {
        platformTenantId = app.Services.GetRequiredService<IHttpContextAccessor>()
            .HttpContext?.Request.Headers["X-Platform-Tenant-Id"].FirstOrDefault(),
        themeId,
        branding = new { productName = "B2B Guest Governance Portal" },
        user = new { user.Mail, roles = user.Roles },
    });
});

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
    WorkloadManagementService service, CancellationToken ct) =>
{
    if (!userCtx.Current.IsGovernanceAdmin)
    {
        return Results.StatusCode(403);
    }

    var workload = await service.CreateWorkloadAsync(
        tenantCtx.Current, body.Name, body.Owner, body.TemplateId, userCtx.Current.Mail, ct);
    return Results.Created($"/api/workloads/{workload.Id}", workload);
});

app.MapPut("/api/workloads/{id:guid}", async (
    Guid id, UpdateWorkloadBody body, ITenantContextAccessor tenantCtx, IPortalUserContextAccessor userCtx,
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

        var workload = await service.UpdateWorkloadAsync(
            tenantCtx.Current, id, body.Name, body.Owner, actor: userCtx.Current.Mail, ct);
        return Results.Ok(workload);
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
            tenantCtx.Current, workloadId, roleId: null, body.Name, body.ResourceMappings, actor: userCtx.Current.Mail, ct);
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
            tenantCtx.Current, workloadId, roleId, body.Name, body.ResourceMappings, actor: userCtx.Current.Mail, ct);
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
        ct);

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
    app.MapGet("/api/dev/mock-entra/users", (
        IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        return Results.Ok(store.ListUsers());
    });

    app.MapGet("/api/dev/mock-entra/groups", (
        IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        return Results.Ok(store.ListGroups());
    });

    app.MapGet("/api/dev/mock-entra/memberships", (
        IPortalUserContextAccessor userCtx, MockEntraDirectoryStore store) =>
    {
        if (!userCtx.Current.IsGovernanceAdmin)
        {
            return Results.StatusCode(403);
        }

        return Results.Ok(store.ListAllMemberships());
    });

    app.MapPost("/api/dev/seed/large-workload", async (
        SeedLargeWorkloadBody? body, ITenantContextAccessor tenantCtx,
        IWorkloadRepository workloadRepo, IGuestAccountRepository guestRepo,
        IAssignmentRepository assignmentRepo, ProvisioningService provisioningService,
        AuditService auditService, CancellationToken ct) =>
    {
        var guestCount = Math.Clamp(body?.GuestCount ?? 500, 1, 5000);
        var tenantId = tenantCtx.Current.PlatformTenantId;
        var directoryTenantId = tenantCtx.Current.DirectoryTenantId ?? "dev-directory-a";
        var correlationId = Guid.NewGuid();

        var workload = DevSeedData.BuildWorkload(tenantId, body?.WorkloadName);
        await workloadRepo.UpsertAsync(workload, ct);

        var createdGuests = new List<GuestAccount>(guestCount);
        for (var i = 0; i < guestCount; i++)
        {
            var guest = DevSeedData.BuildGuest(tenantId, directoryTenantId, i);
            await guestRepo.UpsertAsync(guest, ct);
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

            // Realistische Job-Historie: pro Gast ein GrantWorkloadRole-Job, damit
            // Worker-Dashboards/Job-Listen (falls spaeter ergaenzt) nicht leer sind.
            var hash = DesiredStateHasher.Hash(
                "GrantWorkloadRole", guest.Id.ToString(), workload.Id.ToString(), role.Id.ToString());
            await provisioningService.EnqueueJobAsync(
                tenantId, directoryTenantId, JobTypes.GrantWorkloadRole,
                nameof(GuestWorkloadAssignment), assignment.Id.ToString(), hash,
                new { GuestId = guest.Id, WorkloadId = workload.Id, RoleId = role.Id }, correlationId, ct);
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

    return new Workload
    {
        Id = workload.Id,
        PlatformTenantId = workload.PlatformTenantId,
        Name = workload.Name,
        Owner = workload.Owner,
        TemplateId = workload.TemplateId,
        Active = workload.Active,
        CreatedAt = workload.CreatedAt,
        UpdatedAt = workload.UpdatedAt,
        Roles = assignedRole is null ? [] : [assignedRole],
        Resources = [.. workload.Resources.Where(r => resourceIds.Contains(r.Id))],
    };
}

// ---- Request-DTOs ----------------------------------------------------------
public sealed record InviteGuestBody(string Mail, string DisplayName, string? DirectoryTenantId = null);
public sealed record AssignmentBody(Guid GuestId, Guid RoleId);
public sealed record DeletionValidationBody(bool GracePeriodReached);
public sealed record ReviewDecisionBody(string Decision);
public sealed record SeedLargeWorkloadBody(int? GuestCount, string? WorkloadName);
public sealed record CreateWorkloadBody(string Name, string? Owner, string? TemplateId = null);
public sealed record UpdateWorkloadBody(string Name, string? Owner);
public sealed record UpsertWorkloadRoleBody(string Name, List<Guid> ResourceMappings);
public sealed record UpsertWorkloadResourceBody(string ResourceType, string? ExternalId);

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
        };

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

        var admin = new WorkloadRole { WorkloadId = workload.Id, Name = "Project Admin" };
        admin.ResourceMappings.AddRange(resources.Select(r => r.Id));

        workload.Roles.AddRange([reader, contributor, coreTeam, admin]);
        return workload;
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

