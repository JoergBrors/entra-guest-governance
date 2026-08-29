using B2B.Portal.Worker.Processing;

namespace B2B.Portal.Worker;

/// <summary>
/// .NET 10 Worker Service (BackgroundService), der die IJobQueue pollt und Jobs an den
/// JobDispatcher übergibt (MVP-Dokument Abschnitt 5 "Worker-Abbildung in .NET 10").
/// In Produktion durch eine ereignisbasierte Verarbeitung (z.B. Service Bus Trigger)
/// ersetzbar, ohne Dispatcher/Handler anzufassen.
/// </summary>
public sealed class PollingWorker(JobDispatcher dispatcher, ILogger<PollingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("B2B.Portal.Worker gestartet (LOCAL_MOCK-fähig, Job-Polling aktiv).");

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await dispatcher.ProcessNextAsync(stoppingToken);
            if (!processed)
            {
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
