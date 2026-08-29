using B2B.Portal.Domain.Entities;

namespace B2B.Portal.Application.Ports;

/// <summary>
/// Abstraktion für die Job Queue. PoC nutzt LocalJobQueue, Produktion z.B.
/// Azure Service Bus (Blueprint 19.4 "IJobQueue: CosmosJobQueue (PoC) / ServiceBusJobQueue").
/// </summary>
public interface IJobQueue
{
    Task EnqueueAsync(JobEnvelope job, CancellationToken ct);

    Task<JobEnvelope?> DequeueAsync(CancellationToken ct);

    Task CompleteAsync(Guid jobId, CancellationToken ct);

    Task RetryAsync(Guid jobId, string error, CancellationToken ct);

    Task DeadLetterAsync(Guid jobId, string error, CancellationToken ct);
}

/// <summary>
/// E-Mail ist ein eigener technischer Provider, kein Bestandteil der Fachlogik
/// (MVP-Dokument Abschnitt 6). LOCAL_MOCK rendert eine Vorschau statt zu senden.
/// </summary>
public sealed record EmailMessage(
    string SenderMailbox,
    string RecipientMail,
    string TemplateId,
    IReadOnlyDictionary<string, string> TemplateData,
    Guid CorrelationId,
    string? WorkloadContext);

public interface IEmailProvider
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Schreibt unveränderliche AuditEvents (Blueprint 18.3).</summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken ct);

    Task<IReadOnlyList<AuditEvent>> QueryAsync(string platformTenantId, int take, CancellationToken ct);
}

/// <summary>Für deterministische Tests austauschbare Zeitquelle.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

// ---- Repositories -------------------------------------------------------
// Alle Repositories erzwingen Tenant-Isolation über platformTenantId als Pflichtparameter.

public interface IGuestAccountRepository
{
    Task<GuestAccount?> GetAsync(string platformTenantId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<GuestAccount>> ListAsync(string platformTenantId, CancellationToken ct);
    Task UpsertAsync(GuestAccount guest, CancellationToken ct);
}

public interface IWorkloadRepository
{
    Task<Workload?> GetAsync(string platformTenantId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<Workload>> ListAsync(string platformTenantId, CancellationToken ct);
    Task UpsertAsync(Workload workload, CancellationToken ct);
}

public interface IAssignmentRepository
{
    Task<IReadOnlyList<GuestWorkloadAssignment>> ListByGuestAsync(
        string platformTenantId, Guid guestId, CancellationToken ct);
    Task<IReadOnlyList<GuestWorkloadAssignment>> ListActiveByGuestAsync(
        string platformTenantId, Guid guestId, CancellationToken ct);
    Task UpsertAsync(GuestWorkloadAssignment assignment, CancellationToken ct);
}

public interface IReviewRepository
{
    Task<ReviewInstance?> GetAsync(string platformTenantId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<ReviewInstance>> ListOpenAsync(string platformTenantId, CancellationToken ct);
    Task UpsertAsync(ReviewInstance instance, CancellationToken ct);
}

public interface IJobRepository
{
    Task<DirectoryOperation?> GetAsync(string platformTenantId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<DirectoryOperation>> ListOpenSecurityRelevantAsync(
        string platformTenantId, Guid guestId, CancellationToken ct);
    Task UpsertAsync(DirectoryOperation job, CancellationToken ct);
}

public interface IResourceAccessRepository
{
    Task<IReadOnlyList<ResourceAccess>> ListByGuestAsync(
        string platformTenantId, Guid guestId, CancellationToken ct);
    Task UpsertAsync(ResourceAccess access, CancellationToken ct);
}
