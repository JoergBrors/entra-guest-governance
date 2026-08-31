using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Directory;

namespace B2B.Portal.Worker;

/// <summary>
/// Periodischer Scanner fuer offene Einladungen (Erweiterung 2026-08-30 "Invitation Reminder
/// Worker"), modelliert exakt nach ApplicationSignInSyncWorker (BackgroundService +
/// PeriodicTimer, gleiches 10-Minuten-Intervall). Sucht Gaeste im Zustand Invited, deren
/// Einladung laenger als die naechste faellige ReminderStage.DaysAfterInvite zurueckliegt, und
/// reiht pro Gast+Stufe genau einen InvitationReminder-Job ein — Idempotenz ueber
/// GuestAccount.LastReminderStageSent (siehe InvitationReminderHandler, der dieses Feld nach
/// erfolgreichem Versand fortschreibt): eine Stufe N wird nur ausgeloest, wenn
/// LastReminderStageSent null (fuer Stufe 1) oder gleich N-1 ist.
/// </summary>
public sealed class InvitationReminderWorker(
    IConfiguration configuration,
    IGuestAccountRepository guestRepository,
    IReminderPolicyRepository reminderPolicyRepository,
    IAssignmentRepository assignmentRepository,
    IWorkloadRepository workloadRepository,
    MockEntraDirectoryStore mockEntraStore,
    ProvisioningService provisioningService,
    IWorkerControlRepository workerControlRepository,
    ILogger<InvitationReminderWorker> logger)
    : PeriodicWorkerBase(nameof(InvitationReminderWorker), TimeSpan.FromMinutes(10), workerControlRepository, logger)
{
    protected override async Task<string?> RunOnceAsync(CancellationToken ct)
    {
        // Erweiterung 2026-08-30 (Teil 3 "Multi-Tenant-Scanner"): siehe identischer Kommentar
        // in ApplicationSignInSyncWorker.SyncAsync — alle bekannten Tenants statt eines
        // hartkodierten, mit Fallback auf den alten Default bei leerem Mock-Stamm.
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

        var policy = await reminderPolicyRepository.GetAsync(tenant, ct);
        if (policy is null || policy.Stages.Count == 0)
        {
            logger.LogDebug("InvitationReminderWorker: keine ReminderPolicy fuer Tenant {Tenant} konfiguriert.", tenantId);
            return $"Tenant {tenantId}: keine ReminderPolicy konfiguriert.";
        }

        var orderedStages = policy.Stages.OrderBy(s => s.StageNumber).ToList();
        var guests = await guestRepository.ListAsync(tenant, ct);
        var invitedGuests = guests.Where(g => g.AccountState == GuestAccountState.Invited).ToList();
        var now = DateTimeOffset.UtcNow;
        var remindersSent = 0;

        foreach (var guest in invitedGuests)
        {
            // Naechste faellige Stufe: Stufe 1, wenn noch nie gesendet, sonst die direkt
            // auf LastReminderStageSent folgende Stufe (nie eine Stufe ueberspringen).
            var nextStageNumber = (guest.LastReminderStageSent ?? 0) + 1;
            var nextStage = orderedStages.FirstOrDefault(s => s.StageNumber == nextStageNumber);
            if (nextStage is null)
            {
                continue;
            }

            var daysSinceInvite = (int)(now - guest.CreatedAt).TotalDays;
            if (daysSinceInvite < nextStage.DaysAfterInvite)
            {
                continue;
            }

            var workloadName = await ResolveWorkloadNameAsync(tenant, guest.Id, ct);

            var correlationId = Guid.NewGuid();
            var hash = DesiredStateHasher.Hash(
                "InvitationReminder", guest.Id.ToString(), nextStage.StageNumber.ToString(), correlationId.ToString());
            var payload = new
            {
                StageNumber = nextStage.StageNumber,
                nextStage.TemplateId,
                nextStage.TemplateSubject,
                nextStage.TemplateBody,
                WorkloadName = workloadName,
                DaysSinceInvite = daysSinceInvite,
            };

            await provisioningService.EnqueueJobAsync(
                tenant.PlatformTenantId, tenant.DirectoryTenantId, JobTypes.InvitationReminder,
                nameof(GuestAccount), guest.Id.ToString(), hash, payload, correlationId, ct,
                triggeredBy: "InvitationReminderWorker");

            logger.LogInformation(
                "InvitationReminder-Job fuer Gast {GuestId} Stufe {StageNumber} eingereiht (DaysSinceInvite={Days}).",
                guest.Id, nextStage.StageNumber, daysSinceInvite);
            remindersSent++;
        }

        return $"Tenant {tenantId}: {invitedGuests.Count} eingeladene(r) Gast/Gaeste geprueft, {remindersSent} Reminder-Job(s) eingereiht.";
    }

    private async Task<string?> ResolveWorkloadNameAsync(TenantContext tenant, Guid guestId, CancellationToken ct)
    {
        var assignments = await assignmentRepository.ListActiveByGuestAsync(tenant, guestId, ct);
        var firstAssignment = assignments.FirstOrDefault();
        if (firstAssignment is null)
        {
            return null;
        }

        var workload = await workloadRepository.GetAsync(tenant, firstAssignment.WorkloadId, ct);
        return workload?.Name;
    }
}
