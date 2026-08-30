using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Directory;

namespace B2B.Portal.Worker;

public sealed class ApplicationSignInSyncWorker(
    IConfiguration configuration,
    IGuestAccountRepository guestRepository,
    IWorkloadRepository workloadRepository,
    IAssignmentRepository assignmentRepository,
    MockEntraDirectoryStore mockEntraStore,
    ILogger<ApplicationSignInSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(10);
        await SyncAsync(stoppingToken);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncAsync(stoppingToken);
        }
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        // Erweiterung 2026-08-30 (Teil 3 "Multi-Tenant-Scanner"): vorher genau ein
        // hartkodierter Tenant (VITE_DEV_PLATFORM_TENANT_ID, eigentlich eine Frontend-
        // Env-Variable) — jetzt alle im Mock-Stamm bekannten Tenants. Fallback auf den alten
        // Default bleibt fuer den Fall, dass der Mock-Stamm noch komplett leer ist (frisch
        // resettete Cosmos-DB vor dem ersten Login/Seed).
        var tenantIds = mockEntraStore.ListKnownPlatformTenantIds();
        if (tenantIds.Count == 0)
        {
            tenantIds = [configuration["VITE_DEV_PLATFORM_TENANT_ID"] ?? "dev-tenant-a"];
        }

        foreach (var tenantId in tenantIds)
        {
            await SyncTenantAsync(tenantId, ct);
        }
    }

    private async Task SyncTenantAsync(string tenantId, CancellationToken ct)
    {
        try
        {
            var tenant = TenantContext.Create(tenantId);
            var guests = await guestRepository.ListAsync(tenant, ct);
            var entraObjectIdsByGuestId = guests
                .Where(guest => !string.IsNullOrWhiteSpace(guest.EntraObjectId))
                .ToDictionary(guest => guest.Id, guest => guest.EntraObjectId!);
            var workloads = await workloadRepository.ListAsync(tenant, ct);

            foreach (var workload in workloads.Where(w => !string.IsNullOrWhiteSpace(w.ApplicationExternalId)))
            {
                var assignments = await assignmentRepository.ListByWorkloadAsync(tenant, workload.Id, ct);
                foreach (var assignment in assignments.Where(a => a.Status is AssignmentStatus.Active or AssignmentStatus.Approved or AssignmentStatus.Requested))
                {
                    if (!entraObjectIdsByGuestId.TryGetValue(assignment.GuestId, out var entraObjectId))
                    {
                        continue;
                    }

                    var existing = mockEntraStore.ListApplicationSignIns(workload.ApplicationExternalId)
                        .Any(signIn => string.Equals(signIn.EntraObjectId, entraObjectId, StringComparison.OrdinalIgnoreCase));
                    if (existing)
                    {
                        continue;
                    }

                    mockEntraStore.UpsertApplicationSignIn(
                        workload.ApplicationExternalId!,
                        entraObjectId,
                        DateTimeOffset.UtcNow.AddDays(-Math.Abs(assignment.Id.GetHashCode() % 90)));
                }
            }

            logger.LogInformation("ApplicationSignInSyncWorker hat Mock-Entra-App-Logins fuer Tenant {Tenant} synchronisiert.", tenantId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ApplicationSignInSyncWorker konnte Mock-Entra-App-Logins fuer Tenant {Tenant} nicht synchronisieren.", tenantId);
        }
    }
}
