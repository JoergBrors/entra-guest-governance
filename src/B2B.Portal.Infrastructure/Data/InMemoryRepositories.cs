using System.Collections.Concurrent;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Domain.ValueObjects;

namespace B2B.Portal.Infrastructure.Data;

/// <summary>Development-/PoC-Zeitquelle. In Produktion durch systemweite UTC-Quelle ersetzbar.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// InMemory-Repositories für Development/Tests (MVP-Dokument "InMemory/Development
/// repositories"). Jede Methode filtert zwingend nach TenantContext — das ist die
/// unterste Verteidigungslinie der Tenant-Isolation (Blueprint 8, "Daten"-Zeile).
/// Später austauschbar gegen Cosmos DB Adapter (Blueprint 19.2) ohne Änderung an
/// Application/Domain.
/// </summary>
public sealed class InMemoryGuestAccountRepository : IGuestAccountRepository
{
    private readonly ConcurrentDictionary<Guid, GuestAccount> _store = new();

    public Task<GuestAccount?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var guest);
        return Task.FromResult(guest is not null && tenant.Owns(guest.PlatformTenantId) ? guest : null);
    }

    public Task<GuestAccount?> GetByMailAsync(TenantContext tenant, string mail, CancellationToken ct) =>
        Task.FromResult(_store.Values.FirstOrDefault(
            g => tenant.Owns(g.PlatformTenantId) && string.Equals(g.Mail, mail, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<GuestAccount>> ListAsync(TenantContext tenant, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GuestAccount>>(
            _store.Values.Where(g => tenant.Owns(g.PlatformTenantId)).ToList());

    public Task UpsertAsync(GuestAccount guest, CancellationToken ct)
    {
        _store[guest.Id] = guest;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryWorkloadRepository : IWorkloadRepository
{
    private readonly ConcurrentDictionary<Guid, Workload> _store = new();

    public Task<Workload?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var w);
        return Task.FromResult(w is not null && tenant.Owns(w.PlatformTenantId) ? w : null);
    }

    public Task<IReadOnlyList<Workload>> ListAsync(TenantContext tenant, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Workload>>(
            _store.Values.Where(w => tenant.Owns(w.PlatformTenantId)).ToList());

    public Task UpsertAsync(Workload workload, CancellationToken ct)
    {
        _store[workload.Id] = workload;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        if (_store.TryGetValue(id, out var w) && tenant.Owns(w.PlatformTenantId))
        {
            _store.TryRemove(id, out _);
        }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryAssignmentRepository : IAssignmentRepository
{
    private readonly ConcurrentDictionary<Guid, GuestWorkloadAssignment> _store = new();

    public Task<GuestWorkloadAssignment?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var a);
        return Task.FromResult(a is not null && tenant.Owns(a.PlatformTenantId) ? a : null);
    }

    public Task<IReadOnlyList<GuestWorkloadAssignment>> ListByGuestAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GuestWorkloadAssignment>>(
            _store.Values.Where(a => tenant.Owns(a.PlatformTenantId) && a.GuestId == guestId).ToList());

    public Task<IReadOnlyList<GuestWorkloadAssignment>> ListActiveByGuestAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GuestWorkloadAssignment>>(
            _store.Values.Where(a => tenant.Owns(a.PlatformTenantId) && a.GuestId == guestId
                && a.Status is AssignmentStatus.Active or AssignmentStatus.Approved or AssignmentStatus.Requested)
                .ToList());

    public Task<IReadOnlyList<GuestWorkloadAssignment>> ListByWorkloadAsync(
        TenantContext tenant, Guid workloadId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GuestWorkloadAssignment>>(
            _store.Values.Where(a => tenant.Owns(a.PlatformTenantId) && a.WorkloadId == workloadId).ToList());

    public Task UpsertAsync(GuestWorkloadAssignment assignment, CancellationToken ct)
    {
        _store[assignment.Id] = assignment;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        if (_store.TryGetValue(id, out var a) && tenant.Owns(a.PlatformTenantId))
        {
            _store.TryRemove(id, out _);
        }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryReviewRepository : IReviewRepository
{
    private readonly ConcurrentDictionary<Guid, ReviewInstance> _store = new();

    public Task<ReviewInstance?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var r);
        return Task.FromResult(r is not null && tenant.Owns(r.PlatformTenantId) ? r : null);
    }

    public Task<IReadOnlyList<ReviewInstance>> ListOpenAsync(TenantContext tenant, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReviewInstance>>(
            _store.Values.Where(r => tenant.Owns(r.PlatformTenantId) && r.IsOpen).ToList());

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

    public Task<DirectoryOperation?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var j);
        return Task.FromResult(j is not null && tenant.Owns(j.PlatformTenantId) ? j : null);
    }

    public Task<IReadOnlyList<DirectoryOperation>> ListOpenSecurityRelevantAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DirectoryOperation>>(
            _store.Values.Where(j => tenant.Owns(j.PlatformTenantId)
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
        TenantContext tenant, Guid guestId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ResourceAccess>>(
            _store.Values.Where(a => tenant.Owns(a.PlatformTenantId) && a.GuestId == guestId).ToList());

    public Task UpsertAsync(ResourceAccess access, CancellationToken ct)
    {
        _store[access.Id] = access;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryWorkloadScenarioRepository : IWorkloadScenarioRepository
{
    private readonly ConcurrentDictionary<Guid, WorkloadScenario> _store = new();

    public Task<WorkloadScenario?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var s);
        return Task.FromResult(s is not null && tenant.Owns(s.PlatformTenantId) ? s : null);
    }

    public Task<IReadOnlyList<WorkloadScenario>> ListByWorkloadAsync(
        TenantContext tenant, Guid workloadId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WorkloadScenario>>(
            _store.Values.Where(s => tenant.Owns(s.PlatformTenantId) && s.WorkloadId == workloadId).ToList());

    public Task UpsertAsync(WorkloadScenario scenario, CancellationToken ct)
    {
        _store[scenario.Id] = scenario;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        if (_store.TryGetValue(id, out var s) && tenant.Owns(s.PlatformTenantId))
        {
            _store.TryRemove(id, out _);
        }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryExternalOrganizationRepository : IExternalOrganizationRepository
{
    private readonly ConcurrentDictionary<Guid, ExternalOrganization> _store = new();

    public Task<ExternalOrganization?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var o);
        return Task.FromResult(o is not null && tenant.Owns(o.PlatformTenantId) ? o : null);
    }

    public Task<ExternalOrganization?> GetByNameAsync(TenantContext tenant, string name, CancellationToken ct) =>
        Task.FromResult(_store.Values.FirstOrDefault(
            o => tenant.Owns(o.PlatformTenantId) && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<ExternalOrganization>> ListAsync(TenantContext tenant, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExternalOrganization>>(
            _store.Values.Where(o => tenant.Owns(o.PlatformTenantId)).ToList());

    public Task UpsertAsync(ExternalOrganization organization, CancellationToken ct)
    {
        _store[organization.Id] = organization;
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

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(TenantContext tenant, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AuditEvent>>(
            _store.Where(e => tenant.Owns(e.PlatformTenantId))
                .OrderByDescending(e => e.Timestamp)
                .Take(take)
                .ToList());
}
