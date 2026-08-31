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
/// </summary>
public sealed class StartReviewHandler(
    IReviewRepository reviewRepository,
    IAssignmentRepository assignmentRepository,
    ILogger<StartReviewHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.StartReview;

    public async Task<string?> HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var reviewDefinitionId = job.Payload.GetProperty("ReviewDefinitionId").GetGuid();
        var guestId = job.Payload.GetProperty("GuestId").GetGuid();

        var assignments = await assignmentRepository.ListActiveByGuestAsync(
            TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId), guestId, ct);

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
}

/// <summary>
/// Wendet eine Review-Entscheidung an (Blueprint 13.2). Remove führt zu einem
/// RevokeWorkloadRole-Job und entfernt NUR den Workload-Zugriff — niemals die
/// Gastidentität (Anhang A, Regel 3).
/// </summary>
public sealed class ApplyReviewDecisionHandler(
    IReviewRepository reviewRepository,
    IAssignmentRepository assignmentRepository,
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

        var instance = await reviewRepository.GetAsync(
            TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId), reviewInstanceId, ct);
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

        if (decision == ReviewDecision.Remove)
        {
            var assignment = await assignmentRepository.GetAsync(
                TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId), item.AssignmentId, ct);
            await provisioningService.EnqueueJobAsync(
                job.PlatformTenantId, job.DirectoryTenantId, JobTypes.RevokeWorkloadRole,
                nameof(GuestWorkloadAssignment), item.AssignmentId.ToString(),
                desiredStateHash: $"revoke-{item.AssignmentId}",
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

        await auditService.RecordAsync(
            job.PlatformTenantId,
            item.DecidedBy ?? "system:review-handler",
            "ApplyReviewDecision",
            nameof(ReviewItem),
            item.Id.ToString(),
            decision.ToString(),
            job.CorrelationId,
            details: $"ReviewInstance={instance.Id};Assignment={item.AssignmentId}",
            ct: ct);

        logger.LogInformation(
            "ReviewItem {ReviewItemId} entschieden: {Decision}. CorrelationId={CorrelationId}",
            reviewItemId, decision, job.CorrelationId);

        return $"ReviewItem {reviewItemId} (Assignment {item.AssignmentId}) entschieden: {decision} von {item.DecidedBy}" +
            (decision == ReviewDecision.Remove ? " — RevokeWorkloadRole-Job eingereiht." : ".");
    }
}
