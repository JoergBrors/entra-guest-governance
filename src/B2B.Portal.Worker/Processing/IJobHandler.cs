using B2B.Portal.Domain.Entities;

namespace B2B.Portal.Worker.Processing;

/// <summary>
/// Handler-Schnittstelle (MVP-Dokument Abschnitt 5.2). Ein Handler ist für genau einen
/// JobType zuständig; der Dispatcher matcht anhand von <see cref="JobType"/>.
/// </summary>
public interface IJobHandler
{
    string JobType { get; }

    Task HandleAsync(JobEnvelope job, CancellationToken cancellationToken);
}
