using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Directory;

namespace B2B.Portal.Worker;

/// <summary>
/// Periodischer Scanner fuer Workload-Gruppen-Patterns (Erweiterung 2026-08-30, Teil 5
/// "Automatischer Pattern-Sync"), modelliert exakt nach InvitationReminderWorker/
/// ApplicationSignInSyncWorker (BackgroundService + PeriodicTimer, gleiches
/// 10-Minuten-Intervall, gleicher Multi-Tenant-Scan ueber
/// MockEntraDirectoryStore.ListKnownPlatformTenantIds).
///
/// Vorher wurde SyncWorkloadPatternResources (siehe
/// Handlers/Workloads/SyncWorkloadPatternResourcesHandler.cs) ausschliesslich beim Erstellen/
/// Bearbeiten eines Workloads in der API ausgeloest (EnqueuePatternSyncJobAsync,
/// Program.cs) — neu im Mock-Entra-Stamm hinzugekommene Gruppen, die auf ein bereits
/// bestehendes Pattern passen, wurden dadurch nie automatisch erfasst, solange niemand den
/// Workload erneut speicherte. Dieser Worker schliesst die Luecke: er reiht fuer jeden
/// Workload mit gesetzten ResourceNamePatterns periodisch erneut einen Sync-Job ein.
///
/// Wiederholung: jeder 10-Minuten-Zyklus reiht bewusst einen NEUEN Job pro Workload mit
/// Patterns ein, auch wenn ein frueherer Zyklus bereits erfolgreich war — sonst wuerden nach
/// dem ERSTEN erfolgreichen Sync neu im Mock-Entra-Stamm hinzugekommene Gruppen nie
/// automatisch erfasst (genau der urspruengliche Zweck dieses Workers). Der Handler selbst
/// (SyncWorkloadPatternResourcesHandler) ist idempotent — haengt nur tatsaechlich neue
/// Treffer als Ressource an, ein wiederholter Lauf mit unveraendertem Gruppenbestand aendert
/// nichts. Einzige Sperre: ein Job desselben Typs+Workloads, der noch Pending/Running ist,
/// wird nicht doppelt eingereiht (Schutz gegen sich stauende Jobs bei einem langsamen/
/// haengenden Handler) — ein Success-Job blockiert den naechsten Zyklus NICHT.
/// </summary>
public sealed class WorkloadPatternSyncWorker(
    IConfiguration configuration,
    IWorkloadRepository workloadRepository,
    IJobRepository jobRepository,
    MockEntraDirectoryStore mockEntraStore,
    ProvisioningService provisioningService,
    IWorkerControlRepository workerControlRepository,
    ILogger<WorkloadPatternSyncWorker> logger)
    : PeriodicWorkerBase(nameof(WorkloadPatternSyncWorker), TimeSpan.FromMinutes(10), workerControlRepository, logger)
{
    private static readonly HashSet<JobStatus> InFlightStatuses = [JobStatus.Pending, JobStatus.Running];

    protected override async Task<string?> RunOnceAsync(CancellationToken ct)
    {
        // Multi-Tenant-Scan wie InvitationReminderWorker/ApplicationSignInSyncWorker — siehe
        // identischer Kommentar dort.
        var tenantIds = mockEntraStore.ListKnownPlatformTenantIds();
        if (tenantIds.Count == 0)
        {
            tenantIds = [configuration["VITE_DEV_PLATFORM_TENANT_ID"] ?? "dev-tenant-a"];
        }

        var summaries = new List<string>();
        foreach (var tenantId in tenantIds)
        {
            summaries.Add(await ScanTenantAsync(tenantId, ct));
        }
        return string.Join(" | ", summaries);
    }

    private async Task<string> ScanTenantAsync(string tenantId, CancellationToken ct)
    {
        var tenant = TenantContext.Create(tenantId);
        var workloads = await workloadRepository.ListAsync(tenant, ct);
        var patternWorkloads = workloads.Where(w => w.Active && w.ResourceNamePatterns.Count > 0).ToList();
        if (patternWorkloads.Count == 0)
        {
            return $"Tenant {tenantId}: keine aktiven Workloads mit ResourceNamePatterns.";
        }

        var existingJobs = await jobRepository.ListAsync(tenant, ct);
        var enqueuedCount = 0;

        foreach (var workload in patternWorkloads)
        {
            // Exakt dieselbe Hash-Bildung wie EnqueuePatternSyncJobAsync (Program.cs) —
            // dort KEIN DesiredStateHasher.Hash(...) (SHA256), sondern ein roher
            // "{workloadId}:{patterns}"-String. Muss identisch bleiben, damit ein manuell
            // (API, Workload speichern) und ein periodisch (dieser Worker) ausgeloester Job
            // fuer dasselbe Pattern-Set als derselbe "Desired State" erkannt werden.
            var hash = $"{workload.Id}:{string.Join('|', workload.ResourceNamePatterns)}";

            var alreadyInFlight = existingJobs.Any(j =>
                j.JobType == JobTypes.SyncWorkloadPatternResources
                && j.WorkloadId == workload.Id
                && InFlightStatuses.Contains(j.Status));
            if (alreadyInFlight)
            {
                continue;
            }

            var correlationId = Guid.NewGuid();
            await provisioningService.EnqueueJobAsync(
                tenant.PlatformTenantId, tenant.DirectoryTenantId, JobTypes.SyncWorkloadPatternResources,
                nameof(Workload), workload.Id.ToString(), hash,
                new
                {
                    WorkloadId = workload.Id,
                    ResourceNamePatterns = workload.ResourceNamePatterns.ToArray(),
                    Actor = "WorkloadPatternSyncWorker",
                },
                correlationId, ct,
                triggeredBy: "WorkloadPatternSyncWorker", workloadId: workload.Id);

            logger.LogInformation(
                "SyncWorkloadPatternResources-Job fuer Workload {WorkloadId} ({WorkloadName}) periodisch eingereiht.",
                workload.Id, workload.Name);
            enqueuedCount++;
        }

        return $"Tenant {tenantId}: {patternWorkloads.Count} Workload(s) mit Patterns geprueft, {enqueuedCount} Sync-Job(s) eingereiht.";
    }
}
