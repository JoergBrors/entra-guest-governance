using B2B.Portal.Api.Tenancy;
using B2B.Portal.Application.Commands;
using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
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
    .WithOrigins(builder.Configuration["WEB_BASE_URL"] ?? "http://localhost:5301")
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

// ---- Dev-Only Seed (nur LOCAL_MOCK) ---------------------------------------
// Erzeugt aussagekräftige Demo-/Mockdaten: einen Workload mit mehreren Rollen und eine
// konfigurierbare Anzahl Gäste inkl. Assignments, direkt über die vorhandenen Repositories
// und den ProvisioningService (also über denselben Pfad wie die echten Commands, nur ohne
// 500 einzelne HTTP-Requests). Bewusst nur unter LOCAL_MOCK aktiv — kein Produktionscode,
// keine Graph-Schreibzugriffe (siehe README "Drei Development-Modi").
if (mode == "LOCAL_MOCK")
{
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

// ---- Request-DTOs ----------------------------------------------------------
public sealed record InviteGuestBody(string Mail, string DisplayName, string? DirectoryTenantId = null);
public sealed record AssignmentBody(Guid GuestId, Guid RoleId);
public sealed record DeletionValidationBody(bool GracePeriodReached);
public sealed record SeedLargeWorkloadBody(int? GuestCount, string? WorkloadName);

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
