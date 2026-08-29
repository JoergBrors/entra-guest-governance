using B2B.Portal.Api.Tenancy;
using B2B.Portal.Application.Commands;
using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

var mode = builder.Configuration["B2B_MODE"] ?? "LOCAL_MOCK";

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITenantContextAccessor, HeaderTenantContextAccessor>();
builder.Services.AddB2BInfrastructure(builder.Configuration);

builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<ProvisioningService>();
builder.Services.AddSingleton<LifecycleService>();
builder.Services.AddSingleton<InviteGuestCommandHandler>();
builder.Services.AddSingleton<GrantWorkloadRoleCommandHandler>();
builder.Services.AddSingleton<RevokeWorkloadRoleCommandHandler>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration["WEB_BASE_URL"] ?? "http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

Console.WriteLine($"[B2B.Portal.Api] Startmodus: {mode}");

// ---- Health --------------------------------------------------------------
app.MapGet("/health", () => Results.Ok(new { status = "healthy", mode }));

// ---- Queries (Blueprint 16.1) --------------------------------------------
app.MapGet("/api/guest-accounts", async (
    ITenantContextAccessor tenantCtx, IGuestAccountRepository repo, CancellationToken ct) =>
{
    var guests = await repo.ListAsync(tenantCtx.Current.PlatformTenantId, ct);
    return Results.Ok(guests);
});

app.MapGet("/api/guest-accounts/{id:guid}", async (
    Guid id, ITenantContextAccessor tenantCtx, IGuestAccountRepository repo, CancellationToken ct) =>
{
    var guest = await repo.GetAsync(tenantCtx.Current.PlatformTenantId, id, ct);
    return guest is null ? Results.NotFound() : Results.Ok(guest);
});

app.MapGet("/api/workloads", async (
    ITenantContextAccessor tenantCtx, IWorkloadRepository repo, CancellationToken ct) =>
{
    var workloads = await repo.ListAsync(tenantCtx.Current.PlatformTenantId, ct);
    return Results.Ok(workloads);
});

app.MapGet("/api/reviews", async (
    ITenantContextAccessor tenantCtx, IReviewRepository repo, CancellationToken ct) =>
{
    var reviews = await repo.ListOpenAsync(tenantCtx.Current.PlatformTenantId, ct);
    return Results.Ok(reviews);
});

app.MapGet("/api/audit-events", async (
    ITenantContextAccessor tenantCtx, IAuditWriter auditWriter, CancellationToken ct) =>
{
    var events = await auditWriter.QueryAsync(tenantCtx.Current.PlatformTenantId, take: 100, ct);
    return Results.Ok(events);
});

// ---- Commands (Blueprint 16.1) -------------------------------------------
app.MapPost("/api/guests/invite", async (
    InviteGuestBody body, ITenantContextAccessor tenantCtx, InviteGuestCommandHandler handler,
    CancellationToken ct) =>
{
    var request = new InviteGuestRequest(
        tenantCtx.Current.PlatformTenantId,
        tenantCtx.Current.DirectoryTenantId ?? body.DirectoryTenantId ?? string.Empty,
        body.Mail, body.DisplayName, Actor: "api-user");
    var guest = await handler.HandleAsync(request, ct);
    return Results.Ok(guest);
});

app.MapPost("/api/workloads/{workloadId:guid}/assignments", async (
    Guid workloadId, AssignmentBody body, ITenantContextAccessor tenantCtx,
    GrantWorkloadRoleCommandHandler handler, CancellationToken ct) =>
{
    var request = new GrantWorkloadRoleRequest(
        tenantCtx.Current.PlatformTenantId, body.GuestId, workloadId, body.RoleId, Actor: "api-user");
    var assignment = await handler.HandleAsync(request, ct);
    return Results.Ok(assignment);
});

app.MapPost("/api/assignments/{id:guid}/revoke", async (
    Guid id, ITenantContextAccessor tenantCtx, IAssignmentRepository assignmentRepo,
    RevokeWorkloadRoleCommandHandler handler, CancellationToken ct) =>
{
    var assignment = (await assignmentRepo.ListByGuestAsync(tenantCtx.Current.PlatformTenantId, id, ct))
        .FirstOrDefault();
    if (assignment is null)
    {
        return Results.NotFound();
    }

    var request = new RevokeWorkloadRoleRequest(tenantCtx.Current.PlatformTenantId, id, Actor: "api-user");
    await handler.HandleAsync(request, assignment, ct);
    return Results.Accepted();
});

app.MapPost("/api/deletion-candidates/{guestId:guid}/validate", async (
    Guid guestId, DeletionValidationBody? body, ITenantContextAccessor tenantCtx,
    LifecycleService lifecycleService, CancellationToken ct) =>
{
    var gracePeriodReached = body?.GracePeriodReached ?? false;
    var evaluation = await lifecycleService.EvaluateDeletionAsync(
        tenantCtx.Current.PlatformTenantId, guestId, gracePeriodReached, Guid.NewGuid(), ct);
    return Results.Ok(evaluation);
});

app.Run();

// ---- Request-DTOs ----------------------------------------------------------
public sealed record InviteGuestBody(string Mail, string DisplayName, string? DirectoryTenantId = null);
public sealed record AssignmentBody(Guid GuestId, Guid RoleId);
public sealed record DeletionValidationBody(bool GracePeriodReached);

/// <summary>Partial-Klasse, damit WebApplicationFactory&lt;Program&gt; in Integrationstests funktioniert.</summary>
public partial class Program;
