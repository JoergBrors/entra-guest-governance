using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Directory;

namespace B2B.Portal.Worker;

/// <summary>
/// Periodischer Discovery-Abgleich (Erweiterung 2026-08-31 "EntraId-Persistenz + Discovery-
/// Reconciliation"): prueft alle 10 Minuten je bekanntem Tenant per
/// MockEntraDirectoryStore.ReconcileWorkloadResourcesAsync, ob alle von Workloads
/// referenzierten Ressourcen (WorkloadResource.ExternalId, immer eine Entra-Object-ID) im
/// Mock-Entra-Verzeichnis (Container "entraid") noch existieren — meldet Abweichungen nur als
/// Warnung, repariert sie NICHT automatisch (siehe Kommentar an
/// MockEntraDirectoryStore.ReconcileWorkloadResourcesAsync: eine fehlende Gruppe ist nach der
/// Umstellung auf einen dedizierten, garantiert persistenten Verzeichnis-Container ein echtes
/// Datenproblem, kein Timing-Artefakt mehr, das automatisch geheilt werden sollte).
///
/// Modelliert exakt nach ApplicationSignInSyncWorker/InvitationReminderWorker/
/// WorkloadPatternSyncWorker (PeriodicWorkerBase, gleiches 10-Minuten-Intervall, gleicher
/// Multi-Tenant-Scan ueber MockEntraDirectoryStore.ListKnownPlatformTenantIds).
/// Zusaetzlich manuell ausloesbar ueber POST /api/dev/discovery/reconcile (B2B.Portal.Api),
/// da dieser BackgroundService selbst nur im B2B.Portal.Worker-Prozess laeuft und von dort
/// nicht direkt per HTTP erreichbar ist.
/// </summary>
public sealed class DiscoveryReconciliationWorker(
    IConfiguration configuration,
    IWorkloadRepository workloadRepository,
    MockEntraDirectoryStore mockEntraStore,
    IWorkerControlRepository workerControlRepository,
    ILogger<DiscoveryReconciliationWorker> logger)
    : PeriodicWorkerBase(nameof(DiscoveryReconciliationWorker), TimeSpan.FromMinutes(10), workerControlRepository, logger)
{
    protected override async Task<string?> RunOnceAsync(CancellationToken ct)
    {
        var tenantIds = mockEntraStore.ListKnownPlatformTenantIds();
        if (tenantIds.Count == 0)
        {
            tenantIds = [configuration["VITE_DEV_PLATFORM_TENANT_ID"] ?? "dev-tenant-a"];
        }

        var summaries = new List<string>();
        foreach (var tenantId in tenantIds)
        {
            var missingCount = await mockEntraStore.ReconcileWorkloadResourcesAsync(
                TenantContext.Create(tenantId), workloadRepository, logger, ct);

            logger.LogInformation(
                "DiscoveryReconciliationWorker: Tenant {Tenant} abgeglichen, {MissingCount} Workload-Ressource(n) " +
                "ohne bekannte Verzeichnis-Gruppe.",
                tenantId, missingCount);
            summaries.Add($"Tenant {tenantId}: {missingCount} Workload-Ressource(n) ohne bekannte Verzeichnis-Gruppe.");
        }
        return string.Join(" | ", summaries);
    }
}
