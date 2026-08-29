using System.Text.Json;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Infrastructure.Queue;
using B2B.Portal.Worker.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace B2B.Portal.Integration.Tests;

/// <summary>
/// Worker-Dispatcher-Test (MVP-Dokument, TESTS / QUALITY GATES). Prüft, dass ein Job
/// vom Dispatcher an den passenden Handler geroutet wird, dass unbekannte JobTypes
/// DeadLetter erhalten, und dass Jobs ohne PlatformTenantId abgelehnt werden
/// (Tenant-Isolation im Worker, Blueprint 8).
/// </summary>
public class JobDispatcherTests
{
    private sealed class RecordingHandler : IJobHandler
    {
        public string JobType => "TestJobType";
        public int CallCount { get; private set; }

        public Task HandleAsync(JobEnvelope job, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IJobHandler
    {
        public string JobType => "AlwaysFails";
        public Task HandleAsync(JobEnvelope job, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    private static JobEnvelope BuildJob(string jobType, string? platformTenantId = "tenant-a") =>
        JobEnvelope.Create(
            platformTenantId ?? string.Empty, "dir-a", jobType, "TestEntity", Guid.NewGuid().ToString(),
            "hash", JsonSerializer.SerializeToElement(new { }));

    [Fact]
    public async Task ProcessNextAsync_RoutesJobToMatchingHandler()
    {
        var queue = new LocalJobQueue();
        var handler = new RecordingHandler();
        var dispatcher = new JobDispatcher([handler], queue, NullLogger<JobDispatcher>.Instance);

        await queue.EnqueueAsync(BuildJob("TestJobType"), CancellationToken.None);

        var processed = await dispatcher.ProcessNextAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ProcessNextAsync_UnknownJobType_GoesToDeadLetter()
    {
        var queue = new LocalJobQueue();
        var dispatcher = new JobDispatcher([], queue, NullLogger<JobDispatcher>.Instance);

        await queue.EnqueueAsync(BuildJob("UnknownType"), CancellationToken.None);
        await dispatcher.ProcessNextAsync(CancellationToken.None);

        Assert.Single(queue.DeadLetters);
    }

    [Fact]
    public async Task ProcessNextAsync_MissingPlatformTenantId_GoesToDeadLetter()
    {
        var queue = new LocalJobQueue();
        var handler = new RecordingHandler();
        var dispatcher = new JobDispatcher([handler], queue, NullLogger<JobDispatcher>.Instance);

        await queue.EnqueueAsync(BuildJob("TestJobType", platformTenantId: ""), CancellationToken.None);
        await dispatcher.ProcessNextAsync(CancellationToken.None);

        Assert.Single(queue.DeadLetters);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ProcessNextAsync_FailingHandler_RetriesBeforeDeadLetter()
    {
        var queue = new LocalJobQueue();
        var dispatcher = new JobDispatcher([new ThrowingHandler()], queue, NullLogger<JobDispatcher>.Instance);

        await queue.EnqueueAsync(BuildJob("AlwaysFails"), CancellationToken.None);

        // 3 Versuche (MaxRetries) bevor DeadLetter greift.
        await dispatcher.ProcessNextAsync(CancellationToken.None);
        await dispatcher.ProcessNextAsync(CancellationToken.None);
        await dispatcher.ProcessNextAsync(CancellationToken.None);

        Assert.Single(queue.DeadLetters);
    }
}
