using System.Collections.Concurrent;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;

namespace B2B.Portal.Infrastructure.Queue;

/// <summary>
/// Thread-sichere lokale Queue für Development/Tests (MVP-Dokument "LocalJobQueue").
/// Später austauschbar gegen Azure Service Bus / CosmosJobQueue (Blueprint 19.4),
/// ohne dass Application/Domain sich ändern müssen — reines IJobQueue-Implementation-Detail.
/// </summary>
public sealed class LocalJobQueue : IJobQueue
{
    private readonly ConcurrentQueue<JobEnvelope> _pending = new();
    private readonly ConcurrentDictionary<Guid, JobEnvelope> _inFlight = new();
    private readonly ConcurrentDictionary<Guid, (JobEnvelope Job, string Error)> _deadLetters = new();
    public int RetryCounter { get; private set; }

    public IReadOnlyCollection<(JobEnvelope Job, string Error)> DeadLetters => _deadLetters.Values.ToArray();

    public Task EnqueueAsync(JobEnvelope job, CancellationToken ct)
    {
        _pending.Enqueue(job);
        return Task.CompletedTask;
    }

    public Task<JobEnvelope?> DequeueAsync(CancellationToken ct)
    {
        if (_pending.TryDequeue(out var job))
        {
            _inFlight[job.JobId] = job;
            return Task.FromResult<JobEnvelope?>(job);
        }

        return Task.FromResult<JobEnvelope?>(null);
    }

    public Task CompleteAsync(Guid jobId, CancellationToken ct)
    {
        _inFlight.TryRemove(jobId, out _);
        return Task.CompletedTask;
    }

    public Task RetryAsync(Guid jobId, string error, CancellationToken ct)
    {
        if (_inFlight.TryRemove(jobId, out var job))
        {
            RetryCounter++;
            _pending.Enqueue(job);
        }

        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(Guid jobId, string error, CancellationToken ct)
    {
        if (_inFlight.TryRemove(jobId, out var job))
        {
            _deadLetters[jobId] = (job, error);
        }

        return Task.CompletedTask;
    }
}
