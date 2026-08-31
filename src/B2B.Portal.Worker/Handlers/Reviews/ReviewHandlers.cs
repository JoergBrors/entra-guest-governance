using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Worker.Processing;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Handlers.Reviews;

/// <summary>
/// Startet eine interne Review-Instanz mit Snapshot der zu prüfenden Assignments
/// (Blueprint 13.2 "Interne Review Engine"). Ein laufender ReviewInstance wechselt
/// seinen Provider nicht nachträglich (Anhang A, Regel 11) — Provider wird beim Start
/// fixiert.
///
/// Zwei Modi ueber das Payload (Erweiterung 2026-08-31 "Discovery-Sichtbarkeit ueber
/// Review"): Payload mit "GuestId" -&gt; klassischer Assignment-Review (ein Gast, dessen
/// aktive Zuweisungen geprueft werden sollen). Payload mit "Scope": "discovery" (kein
/// GuestId) -&gt; Discovery-Review: nimmt ALLE tenant-weiten Unclassified ResourceAccess als
/// ReviewItems auf (WorkloadResource.ExternalId wird gegen ResourceAccess.ExternalResourceId
/// gematcht, um in der Reason den betroffenen Workload zu nennen, falls bekannt) — macht
/// Blueprint-12-Drift ("Nutzer ist tatsaechlich Mitglied einer Gruppe, aber keine formale
/// Workload-Zuweisung existiert dafuer") sichtbar, OHNE selbst irgendetwas zu aendern (reiner
/// Snapshot, wie der bestehende Assignment-Pfad auch).
/// </summary>
public sealed class StartReviewHandler(
    IReviewRepository reviewRepository,
    IAssignmentRepository assignmentRepository,
    IResourceAccessRepository resourceAccessRepository,
    IWorkloadRepository workloadRepository,
    ILogger<StartReviewHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.StartReview;

    public async Task<string?> HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var reviewDefinitionId = job.Payload.GetProperty("ReviewDefinitionId").GetGuid();
        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);

        var isDiscoveryReview = job.Payload.TryGetProperty("Scope", out var scopeValue)
            && string.Equals(scopeValue.GetString(), "discovery", StringComparison.OrdinalIgnoreCase);

        return isDiscoveryReview
            ? await StartDiscoveryReviewAsync(tenant, reviewDefinitionId, job, ct)
            : await StartAssignmentReviewAsync(tenant, reviewDefinitionId, job, ct);
    }

    private async Task<string?> StartAssignmentReviewAsync(
        TenantContext tenant, Guid reviewDefinitionId, JobEnvelope job, CancellationToken ct)
    {
        var guestId = job.Payload.GetProperty("GuestId").GetGuid();
        var assignments = await assignmentRepository.ListActiveByGuestAsync(tenant, guestId, ct);

        var instance = new ReviewInstance
        {
            PlatformTenantId = job.PlatformTenantId,
            ReviewDefinitionId = reviewDefinitionId,
            Provider = GovernanceProvider.Internal,
        };

        foreach (var a in assignments)
        {
            instance.Items.Add(new ReviewItem
            {
                ReviewInstanceId = instance.Id,
                AssignmentId = a.Id,
            });
        }

        await reviewRepository.UpsertAsync(instance, ct);

        logger.LogInformation(
            "Review {ReviewInstanceId} gestartet mit {ItemCount} Items. CorrelationId={CorrelationId}",
            instance.Id, instance.Items.Count, job.CorrelationId);

        return $"Review {instance.Id} gestartet fuer ReviewDefinition {reviewDefinitionId}, Guest {guestId}: " +
            $"{instance.Items.Count} Assignment(s) als Items aufgenommen.";
    }

    private async Task<string?> StartDiscoveryReviewAsync(
        TenantContext tenant, Guid reviewDefinitionId, JobEnvelope job, CancellationToken ct)
    {
        var unclassified = await resourceAccessRepository.ListUnclassifiedByTenantAsync(tenant, ct);

        var instance = new ReviewInstance
        {
            PlatformTenantId = job.PlatformTenantId,
            ReviewDefinitionId = reviewDefinitionId,
            Provider = GovernanceProvider.Internal,
        };

        if (unclassified.Count > 0)
        {
            // Cross-Reference (Erweiterung 2026-08-31): WorkloadResource.ExternalId ist immer
            // die stabile Entra-Object-ID (siehe WorkloadResource-Kommentar), genau wie
            // ResourceAccess.ExternalResourceId (DiscoveryHandler schreibt dort
            // DirectoryGroupMembership.GroupId hinein) — beide vergleichbar, um dem Admin in
            // der Reason zu sagen, WELCHER Workload betroffen ist, statt nur eine rohe
            // Object-ID zu zeigen.
            var workloads = await workloadRepository.ListAsync(tenant, ct);
            var workloadByExternalId = workloads
                .SelectMany(w => w.Resources.Select(r => (Workload: w, Resource: r)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Resource.ExternalId))
                .GroupBy(x => x.Resource.ExternalId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var access in unclassified)
            {
                var reason = workloadByExternalId.TryGetValue(access.ExternalResourceId, out var match)
                    ? $"Mitglied von {match.Resource.ResourceType}:{match.Resource.DisplayName ?? match.Resource.ExternalId} " +
                        $"(Workload '{match.Workload.Name}') — keine Workload-Zuweisung fuer diesen Gast gefunden."
                    : $"Mitglied von {access.ResourceType}:{access.ExternalResourceId} — keinem bekannten Workload zugeordnet.";

                instance.Items.Add(new ReviewItem
                {
                    ReviewInstanceId = instance.Id,
                    ResourceAccessId = access.Id,
                    Reason = reason,
                });
            }
        }

        await reviewRepository.UpsertAsync(instance, ct);

        logger.LogInformation(
            "Discovery-Review {ReviewInstanceId} gestartet mit {ItemCount} Items. CorrelationId={CorrelationId}",
            instance.Id, instance.Items.Count, job.CorrelationId);

        return $"Discovery-Review {instance.Id} gestartet: {instance.Items.Count} Unclassified ResourceAccess-Eintrag(e) als Items aufgenommen.";
    }
}

/// <summary>
/// Wendet eine Review-Entscheidung an (Blueprint 13.2). Verhalten haengt vom Item-Typ ab
/// (Erweiterung 2026-08-31 "Discovery-Sichtbarkeit ueber Review", siehe ReviewItem-Kommentar):
/// - Assignment-Item (AssignmentId gesetzt): Remove führt zu einem RevokeWorkloadRole-Job und
///   entfernt NUR den Workload-Zugriff — niemals die Gastidentität (Anhang A, Regel 3).
/// - Discovery-Item (ResourceAccessId gesetzt): Keep/Remove aendert AUSSCHLIESSLICH die
///   Classification des ResourceAccess auf Classified — es wird NIE automatisch ein
///   RevokeWorkloadRole-Job oder eine GuestWorkloadAssignment erzeugt, da fuer ein Discovery-
///   Item per Definition keine formale Zuweisung existiert (sonst waere es ein Assignment-
///   Item). "Remove" bedeutet hier organisatorisch "als geprueft und zu entfernen markiert" —
///   das tatsaechliche Entfernen aus der Gruppe bleibt eine bewusste, separate Admin-Aktion
///   im Mock-Entra-Verzeichnis (Anhang A Regel 4: Desired State und Actual State sind
///   getrennt, Reconciliation/Discovery loesen NIE automatisch Provisionierung/Loeschung aus).
/// </summary>
public sealed class ApplyReviewDecisionHandler(
    IReviewRepository reviewRepository,
    IAssignmentRepository assignmentRepository,
    IResourceAccessRepository resourceAccessRepository,
    B2B.Portal.Application.Services.ProvisioningService provisioningService,
    B2B.Portal.Application.Services.AuditService auditService,
    ILogger<ApplyReviewDecisionHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.ApplyReviewDecision;

    public async Task<string?> HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var reviewInstanceId = Guid.Parse(job.EntityId);
        var reviewItemId = job.Payload.GetProperty("ReviewItemId").GetGuid();
        var decision = Enum.Parse<ReviewDecision>(job.Payload.GetProperty("Decision").GetString()!);
        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);

        var instance = await reviewRepository.GetAsync(tenant, reviewInstanceId, ct);
        var item = instance?.Items.FirstOrDefault(i => i.Id == reviewItemId);
        if (instance is null || item is null)
        {
            logger.LogWarning("ApplyReviewDecision: ReviewItem {ReviewItemId} nicht gefunden.", reviewItemId);
            return $"ReviewItem {reviewItemId} in ReviewInstance {reviewInstanceId} nicht gefunden — keine Entscheidung angewendet.";
        }

        item.Decision = decision;
        item.DecidedBy = job.Payload.TryGetProperty("Actor", out var actorValue)
            ? actorValue.GetString()
            : "system:review-handler";
        item.DecidedAt = DateTimeOffset.UtcNow;
        await reviewRepository.UpsertAsync(instance, ct);

        string resultMessage;
        if (item.ResourceAccessId is Guid resourceAccessId)
        {
            resultMessage = await ApplyDiscoveryDecisionAsync(tenant, resourceAccessId, item, decision, ct);
        }
        else
        {
            resultMessage = await ApplyAssignmentDecisionAsync(job, item, decision, ct);
        }

        await auditService.RecordAsync(
            job.PlatformTenantId,
            item.DecidedBy ?? "system:review-handler",
            "ApplyReviewDecision",
            nameof(ReviewItem),
            item.Id.ToString(),
            decision.ToString(),
            job.CorrelationId,
            details: item.ResourceAccessId is not null
                ? $"ReviewInstance={instance.Id};ResourceAccess={item.ResourceAccessId}"
                : $"ReviewInstance={instance.Id};Assignment={item.AssignmentId}",
            ct: ct);

        logger.LogInformation(
            "ReviewItem {ReviewItemId} entschieden: {Decision}. CorrelationId={CorrelationId}",
            reviewItemId, decision, job.CorrelationId);

        return resultMessage;
    }

    private async Task<string> ApplyDiscoveryDecisionAsync(
        TenantContext tenant, Guid resourceAccessId, ReviewItem item, ReviewDecision decision, CancellationToken ct)
    {
        var access = await resourceAccessRepository.GetAsync(tenant, resourceAccessId, ct);
        if (access is null)
        {
            return $"ResourceAccess {resourceAccessId} nicht gefunden — keine Klassifizierung vorgenommen.";
        }

        access.Classification = AccessClassification.Classified;
        await resourceAccessRepository.UpsertAsync(access, ct);

        return $"Discovery-Item (ResourceAccess {resourceAccessId}, {access.ResourceType}:{access.ExternalResourceId}) " +
            $"entschieden: {decision} von {item.DecidedBy}. Als Classified markiert — " +
            "KEIN automatischer Zugriffsentzug (Actual/Desired State bleiben getrennt, siehe Anhang A Regel 4); " +
            (decision == ReviewDecision.Remove
                ? "ein tatsaechliches Entfernen aus der Gruppe erfordert eine bewusste Admin-Aktion im Mock-Entra-Verzeichnis."
                : "Zugriff bleibt unveraendert bestehen.");
    }

    private async Task<string> ApplyAssignmentDecisionAsync(
        JobEnvelope job, ReviewItem item, ReviewDecision decision, CancellationToken ct)
    {
        var assignmentId = item.AssignmentId!.Value;
        if (decision == ReviewDecision.Remove)
        {
            var assignment = await assignmentRepository.GetAsync(
                TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId), assignmentId, ct);
            await provisioningService.EnqueueJobAsync(
                job.PlatformTenantId, job.DirectoryTenantId, JobTypes.RevokeWorkloadRole,
                nameof(GuestWorkloadAssignment), assignmentId.ToString(),
                desiredStateHash: $"revoke-{assignmentId}",
                new
                {
                    GuestId = assignment?.GuestId ?? Guid.Empty,
                    WorkloadId = assignment?.WorkloadId ?? Guid.Empty,
                    RoleId = assignment?.RoleId ?? Guid.Empty,
                },
                job.CorrelationId,
                ct,
                triggeredBy: item.DecidedBy,
                workloadId: assignment?.WorkloadId);
        }

        return $"ReviewItem (Assignment {assignmentId}) entschieden: {decision} von {item.DecidedBy}" +
            (decision == ReviewDecision.Remove ? " — RevokeWorkloadRole-Job eingereiht." : ".");
    }
}
