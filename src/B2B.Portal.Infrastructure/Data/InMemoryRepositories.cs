using System.Collections.Concurrent;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;

namespace B2B.Portal.Infrastructure.Data;

/// <summary>Development-/PoC-Zeitquelle. In Produktion durch systemweite UTC-Quelle ersetzbar.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// InMemory-Repositories für Development/Tests (MVP-Dokument "InMemory/Development
/// repositories"). Jede Methode filtert zwingend nach platformTenantId — das ist die
/// unterste Verteidigungslinie der Tenant-Isolation (Blueprint 8, "Daten"-Zeile).
/// Später austauschbar gegen Cosmos DB Adapter (Blueprint 19.2) ohne Änderung an
/// Application/Domain.
/// </summary>
public sealed class InMemoryGuestAccountRepository : IGuestAccountRepository
{
    private readonly ConcurrentDictionary<Guid, GuestAccount> _store = new();

    public Task<GuestAccount?> GetAsync(string platformTenantId, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var guest);
        return Task.FromResult(guest is not null && guest.PlatformTenantId == platformTenantId ? guest : null);
    }

    public Task<IReadOnlyList<GuestAccount>> ListAsync(string platformTenantId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GuestAccount>>(
            _store.Values.Where(g => g.PlatformTenantId == platformTenantId).ToList());

    public Task UpsertAsync(GuestAccount guest, CancellationToken ct)
    {
        _store[guest.Id] = guest;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryWorkloadRepository : IWorkloadRepository
{
    private readonly ConcurrentDictionary<Guid, Workload> _store = new();

    public Task<Workload?> GetAsync(string platformTenantId, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var w);
        return Task.FromResult(w is not null && w.PlatformTenantId == platformTenantId ? w : null);
    }

    public Task<IReadOnlyList<Workload>> ListAsync(string platformTenantId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Workload>>(
            _store.Values.Where(w => w.PlatformTenantId == platformTenantId).ToList());

    public Task UpsertAsync(Workload workload, CancellationToken ct)
    {
        _store[workload.Id] = workload;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryAssignmentRepository : IAssignmentRepository
{
    private readonly ConcurrentDictionary<Guid, GuestWorkloadAssignment> _store = new();

    public Task<IReadOnlyList<GuestWorkloadAssignment>> ListByGuestAsync(
        string platformTenantId, Guid guestId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GuestWorkloadAssignment>>(
            _store.Values.Where(a => a.PlatformTenantId == platformTenantId && a.GuestId == guestId).ToList());

    public Task<IReadOnlyList<GuestWorkloadAssignment>> ListActiveByGuestAsync(
        string platformTenantId, Guid guestId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GuestWorkloadAssignment>>(
            _store.Values.Where(a => a.PlatformTenantId == platformTenantId && a.GuestId == guestId
                && a.Status is AssignmentStatus.Active or AssignmentStatus.Approved or AssignmentStatus.Requested)
                .ToList());

    public Task UpsertAsync(GuestWorkloadAssignment assignment, CancellationToken ct)
    {
        _store[assignment.Id] = assignment;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryReviewRepository : IReviewRepository
{
    private readonly ConcurrentDictionary<Guid, ReviewInstance> _store = new();

    public Task<ReviewInstance?> GetAsync(string platformTenantId, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var r);
        return Task.FromResult(r is not null && r.PlatformTenantId == platformTenantId ? r : null);
    }

    public Task<IReadOnlyList<ReviewInstance>> ListOpenAsync(string platformTenantId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReviewInstance>>(
            _store.Values.Where(r => r.PlatformTenantId == platformTenantId && r.IsOpen).ToList());

    public Task UpsertAsync(ReviewInstance instance, CancellationToken ct)
    {
        _store[instance.Id] = instance;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryJobRepository : IJobRepository
{
    private readonly ConcurrentDictionary<Guid, DirectoryOperation> _store = new();

    private static readonly HashSet<string> SecurityRelevantJobTypes =
    [
        JobTypes.GrantWorkloadRole, JobTypes.RevokeWorkloadRole, JobTypes.DisableGuest,
        JobTypes.DeleteGuest, JobTypes.ValidateDeletion,
    ];

    public Task<DirectoryOperation?> GetAsync(string platformTenantId, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var j);
        return Task.FromResult(j is not null && j.PlatformTenantId == platformTenantId ? j : null);
    }

    public Task<IReadOnlyList<DirectoryOperation>> ListOpenSecurityRelevantAsync(
        string platformTenantId, Guid guestId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DirectoryOperation>>(
            _store.Values.Where(j => j.PlatformTenantId == platformTenantId
                && j.EntityId == guestId.ToString()
                && SecurityRelevantJobTypes.Contains(j.JobType)
                && j.Status is JobStatus.Pending or JobStatus.Running or JobStatus.Retry)
                .ToList());

    public Task UpsertAsync(DirectoryOperation job, CancellationToken ct)
    {
        _store[job.Id] = job;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryResourceAccessRepository : IResourceAccessRepository
{
    private readonly ConcurrentDictionary<Guid, ResourceAccess> _store = new();

    public Task<IReadOnlyList<ResourceAccess>> ListByGuestAsync(
        string platformTenantId, Guid guestId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ResourceAccess>>(
            _store.Values.Where(a => a.PlatformTenantId == platformTenantId && a.GuestId == guestId).ToList());

    public Task UpsertAsync(ResourceAccess access, CancellationToken ct)
    {
        _store[access.Id] = access;
        return Task.CompletedTask;
    }
}

/// <summary>In-Memory AuditWriter — Audit Events sind tenantgebunden und werden gefiltert.</summary>
public sealed class InMemoryAuditWriter : IAuditWriter
{
    private readonly ConcurrentQueue<AuditEvent> _store = new();

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct)
    {
        _store.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(string platformTenantId, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AuditEvent>>(
            _store.Where(e => e.PlatformTenantId == platformTenantId)
                .OrderByDescending(e => e.Timestamp)
                .Take(take)
                .ToList());
}
