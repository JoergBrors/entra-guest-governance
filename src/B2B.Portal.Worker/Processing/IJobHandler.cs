using B2B.Portal.Domain.Entities;

namespace B2B.Portal.Worker.Processing;

/// <summary>
/// Handler-Schnittstelle (MVP-Dokument Abschnitt 5.2). Ein Handler ist für genau einen
/// JobType zuständig; der Dispatcher matcht anhand von <see cref="JobType"/>.
///
/// HandleAsync liefert eine kurze, menschenlesbare Ergebnis-Zusammenfassung zurueck
/// (Erweiterung 2026-08-31 "Job/Worker-Audit — Detail-Logging"), z.B. "1 Ressource(n)
/// gewaehrt: SecurityGroup:TEST" — der JobDispatcher schreibt diese als Message in den
/// JobLogEntry beim Success-Status, damit die Job-Detailansicht (GET /api/jobs/{id}) nicht
/// nur den Status, sondern auch WAS der Job tatsaechlich getan hat zeigt. Null/leer ist
/// erlaubt fuer Handler, bei denen es nichts Sinnvolles zusammenzufassen gibt.
/// </summary>
public interface IJobHandler
{
    string JobType { get; }

    Task<string?> HandleAsync(JobEnvelope job, CancellationToken cancellationToken);
}
