using System.Text.Json;
using B2B.Portal.Domain.Enums;

namespace B2B.Portal.Domain.Entities;

/// <summary>
/// Technische Operation / DirectoryOperation (Blueprint 7, 10.3). Trägt Correlation ID,
/// Operationstyp, Zielentität und Desired-State-Hash für Audit, Idempotenz und Retry.
/// </summary>
public sealed class DirectoryOperation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlatformTenantId { get; init; }
    public string? DirectoryTenantId { get; init; }
    public required string JobType { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public string? TriggeredBy { get; init; }
    public Guid? WorkloadId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required string DesiredStateHash { get; init; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<JobLogEntry> Log { get; init; } = [];
}

/// <summary>
/// Ein Eintrag im Verlauf einer DirectoryOperation (Statuswechsel, Fehler) — fuer die
/// detaillierte Job-Log-Ansicht in der Jobs-Admin-UI (GET /api/jobs/{id}).
/// </summary>
public sealed record JobLogEntry(DateTimeOffset Timestamp, JobStatus Status, string? Message);

/// <summary>
/// Einheitlicher Job Envelope, wie er über die IJobQueue an den Worker übergeben wird
/// (Development-/MVP-Dokument, Abschnitt 5.1). Jeder Worker validiert den Tenant-Kontext
/// vor der Ausführung.
/// </summary>
public sealed record JobEnvelope(
    Guid JobId,
    string PlatformTenantId,
    string? DirectoryTenantId,
    string JobType,
    string EntityType,
    string EntityId,
    Guid CorrelationId,
    string DesiredStateHash,
    DateTimeOffset CreatedAt,
    JsonElement Payload)
{
    public static JobEnvelope Create(
        string platformTenantId,
        string? directoryTenantId,
        string jobType,
        string entityType,
        string entityId,
        string desiredStateHash,
        JsonElement payload,
        Guid? correlationId = null,
        Guid? jobId = null) =>
        new(
            jobId ?? Guid.NewGuid(),
            platformTenantId,
            directoryTenantId,
            jobType,
            entityType,
            entityId,
            correlationId ?? Guid.NewGuid(),
            desiredStateHash,
            DateTimeOffset.UtcNow,
            payload);
}

/// <summary>
/// Bekannte JobType-Werte (Blueprint 10.2 / MVP-Dokument 12). Als Konstanten statt
/// Magic Strings, damit Dispatcher und Handler konsistent registrieren/matchen.
/// </summary>
public static class JobTypes
{
    public const string InviteGuest = nameof(InviteGuest);
    public const string ResendInvitation = nameof(ResendInvitation);
    public const string InvitationReminder = nameof(InvitationReminder);
    public const string CreateGroup = nameof(CreateGroup);
    public const string CreateTeam = nameof(CreateTeam);
    public const string GrantWorkloadRole = nameof(GrantWorkloadRole);
    public const string RevokeWorkloadRole = nameof(RevokeWorkloadRole);
    public const string SynchronizeGuest = nameof(SynchronizeGuest);
    public const string RunDiscovery = nameof(RunDiscovery);
    public const string RunReconciliation = nameof(RunReconciliation);
    public const string StartReview = nameof(StartReview);
    public const string ApplyReviewDecision = nameof(ApplyReviewDecision);
    public const string ValidateDeletion = nameof(ValidateDeletion);
    public const string DisableGuest = nameof(DisableGuest);
    public const string DeleteGuest = nameof(DeleteGuest);
    public const string SendNotification = nameof(SendNotification);
    public const string DeployScenario = nameof(DeployScenario);
    public const string SyncWorkloadPatternResources = nameof(SyncWorkloadPatternResources);
}
