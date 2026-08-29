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
    private readonly ConcurrentDictionary<Guid, int> _attempts = new();
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

    public Task<int> RetryAsync(Guid jobId, string error, CancellationToken ct)
    {
        var attempt = _attempts.AddOrUpdate(jobId, 1, (_, current) => current + 1);

        if (_inFlight.TryRemove(jobId, out var job))
        {
            RetryCounter++;
            _pending.Enqueue(job);
        }

        return Task.FromResult(attempt);
    }

    public Task DeadLetterAsync(Guid jobId, string error, CancellationToken ct)
    {
        // Der Job kann bereits per RetryAsync zurueck nach _pending verschoben worden sein,
        // wenn der Dispatcher zunaechst RetryAsync aufruft (um den neuen Attempt-Zaehler zu
        // erhalten) und danach anhand dieses Zaehlers doch DeadLetterAsync waehlt. Daher hier
        // aus beiden moeglichen Fundorten entfernen, nicht nur aus _inFlight.
        JobEnvelope? job = null;
        if (_inFlight.TryRemove(jobId, out var inFlightJob))
        {
            job = inFlightJob;
        }
        else
        {
            var remaining = new List<JobEnvelope>();
            while (_pending.TryDequeue(out var candidate))
            {
                if (candidate.JobId == jobId) { job = candidate; }
                else { remaining.Add(candidate); }
            }
            foreach (var r in remaining) { _pending.Enqueue(r); }
        }

        if (job is not null)
        {
            _deadLetters[jobId] = (job, error);
        }

        return Task.CompletedTask;
    }
}
