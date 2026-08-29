using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.Services;

namespace B2B.Portal.Application.Services;

/// <summary>
/// Governance Core / Lifecycle-Orchestrierung (Blueprint 14, 16.2 LifecycleService).
/// Einzige Stelle, die eine Gastlöschung tatsächlich freigeben darf. Nutzt die reine
/// Fachlogik aus <see cref="DeletionGateEvaluator"/> und reichert sie mit dem
/// tatsächlichen Actual State (Repositories + Live Check über IGuestDirectory) an.
/// </summary>
public sealed class LifecycleService(
    IGuestAccountRepository guestRepository,
    IAssignmentRepository assignmentRepository,
    IResourceAccessRepository resourceAccessRepository,
    IJobRepository jobRepository,
    IReviewRepository reviewRepository,
    IGuestDirectory guestDirectory,
    AuditService auditService)
{
    /// <summary>
    /// Führt das Deletion Gate im Dry-Run aus: prüft alle Blocker inklusive Live Validation,
    /// verändert aber niemals den Gast-Status (MVP-Dokument: "ValidateDeletion (Dry Run)").
    /// </summary>
    public async Task<DeletionGateEvaluation> EvaluateDeletionAsync(
        string platformTenantId, Guid guestId, bool gracePeriodReached, Guid correlationId, CancellationToken ct)
    {
        var guest = await guestRepository.GetAsync(platformTenantId, guestId, ct)
            ?? throw new InvalidOperationException($"GuestAccount {guestId} nicht gefunden.");

        var activeAssignments = await assignmentRepository.ListActiveByGuestAsync(platformTenantId, guestId, ct);
        var unclassified = (await resourceAccessRepository.ListByGuestAsync(platformTenantId, guestId, ct))
            .Count(a => a.Classification == AccessClassification.Unclassified);
        var openJobs = await jobRepository.ListOpenSecurityRelevantAsync(platformTenantId, guestId, ct);
        var openReviews = (await reviewRepository.ListOpenAsync(platformTenantId, ct)).Count;

        var connectorError = false;
        var liveAccessFound = false;

        // Live Check nur ausführen, wenn die vorgelagerten Blocker bereits frei sind —
        // spart unnötige Graph-Aufrufe und spiegelt das Blueprint-Flussdiagramm.
        if (activeAssignments.Count == 0 && unclassified == 0 && openJobs.Count == 0 && openReviews == 0
            && gracePeriodReached && !string.IsNullOrEmpty(guest.EntraObjectId))
        {
            try
            {
                liveAccessFound = await guestDirectory.HasRelevantAccessAsync(
                    guest.DirectoryTenantId, guest.EntraObjectId!, ct);
            }
            catch
            {
                // Konservativ: Connectorfehler blockiert, "kein Zugriff" wird nie angenommen
                // (Blueprint 14.4).
                connectorError = true;
            }
        }

        var evaluation = DeletionGateEvaluator.Evaluate(new DeletionGateInput(
            ActiveWorkloadReferences: activeAssignments.Count,
            UnclassifiedAccessCount: unclassified,
            OpenSecurityRelevantJobs: openJobs.Count,
            OpenReviews: openReviews,
            GracePeriodReached: gracePeriodReached,
            ConnectorError: connectorError,
            LiveCheckFoundRelevantAccess: liveAccessFound));

        await auditService.RecordAsync(
            platformTenantId, actor: "system:lifecycle-service", action: "ValidateDeletion",
            entityType: nameof(GuestAccount), entityId: guestId.ToString(),
            result: evaluation.Result.ToString(), correlationId: correlationId,
            details: string.Join(',', evaluation.Blockers), ct: ct);

        return evaluation;
    }
}
