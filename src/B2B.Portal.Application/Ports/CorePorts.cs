using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;

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

    /// <summary>
    /// Markiert einen Job für einen erneuten Versuch. Liefert den neuen, dauerhaften
    /// Attempt-Zähler zurück — die Queue-Implementierung führt diesen Zähler selbst
    /// (statt eines nicht-persistenten In-Prozess-Zählers im JobDispatcher), damit er
    /// einen Worker-Neustart oder mehrere Worker-Instanzen übersteht.
    /// </summary>
    Task<int> RetryAsync(Guid jobId, string error, CancellationToken ct);

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

    Task<IReadOnlyList<AuditEvent>> QueryAsync(TenantContext tenant, int take, CancellationToken ct);
}

/// <summary>Für deterministische Tests austauschbare Zeitquelle.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

// ---- Repositories -------------------------------------------------------
// Alle Repositories erzwingen Tenant-Isolation über TenantContext als Pflichtparameter
// (statt eines nackten String — der Kontext trägt zusätzlich DirectoryTenantId und die
// Owns(...)-Vergleichslogik, siehe B2B.Portal.Domain.ValueObjects.TenantContext).

public interface IGuestAccountRepository
{
    Task<GuestAccount?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct);

    /// <summary>E-Mail ist der fachlich eindeutige Schlüssel für einen Gast (siehe
    /// GuestImportService) — case-insensitive, da Mail-Adressen üblicherweise
    /// case-insensitiv behandelt werden.</summary>
    Task<GuestAccount?> GetByMailAsync(TenantContext tenant, string mail, CancellationToken ct);

    Task<IReadOnlyList<GuestAccount>> ListAsync(TenantContext tenant, CancellationToken ct);
    Task UpsertAsync(GuestAccount guest, CancellationToken ct);
}

public interface IWorkloadRepository
{
    Task<Workload?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct);
    Task<IReadOnlyList<Workload>> ListAsync(TenantContext tenant, CancellationToken ct);
    Task UpsertAsync(Workload workload, CancellationToken ct);

    /// <summary>Hartes Löschen — nur erlaubt, wenn WorkloadManagementService zuvor geprüft
    /// hat, dass keine aktiven Assignments mehr existieren.</summary>
    Task DeleteAsync(TenantContext tenant, Guid id, CancellationToken ct);
}

public interface IAssignmentRepository
{
    Task<GuestWorkloadAssignment?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct);

    Task<IReadOnlyList<GuestWorkloadAssignment>> ListByGuestAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct);
    Task<IReadOnlyList<GuestWorkloadAssignment>> ListActiveByGuestAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct);

    /// <summary>
    /// Alle Assignments eines Workload — Grundlage für Konsistenzprüfungen beim Löschen
    /// einer WorkloadRole/WorkloadResource/eines ganzen Workload (siehe
    /// WorkloadManagementService): eine Rolle/ein Workload mit aktiven Assignments darf
    /// nicht gelöscht werden, sonst hinge die Zuweisung an einer nicht mehr existierenden
    /// Rolle/einem nicht mehr existierenden Workload.
    /// </summary>
    Task<IReadOnlyList<GuestWorkloadAssignment>> ListByWorkloadAsync(
        TenantContext tenant, Guid workloadId, CancellationToken ct);

    Task UpsertAsync(GuestWorkloadAssignment assignment, CancellationToken ct);

    /// <summary>Hartes Löschen — nur für die Bereinigung historischer Assignments beim
    /// Hart-Löschen eines Workload (siehe WorkloadManagementService.DeleteWorkloadAsync),
    /// niemals für aktive Assignments (dafür existiert der Revoke-Fluss).</summary>
    Task DeleteAsync(TenantContext tenant, Guid id, CancellationToken ct);
}

public interface IReviewRepository
{
    Task<ReviewInstance?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct);
    Task<IReadOnlyList<ReviewInstance>> ListOpenAsync(TenantContext tenant, CancellationToken ct);
    Task UpsertAsync(ReviewInstance instance, CancellationToken ct);
}

public interface IJobRepository
{
    Task<DirectoryOperation?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct);
    Task<IReadOnlyList<DirectoryOperation>> ListOpenSecurityRelevantAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct);
    Task UpsertAsync(DirectoryOperation job, CancellationToken ct);
}

public interface IResourceAccessRepository
{
    Task<IReadOnlyList<ResourceAccess>> ListByGuestAsync(
        TenantContext tenant, Guid guestId, CancellationToken ct);
    Task UpsertAsync(ResourceAccess access, CancellationToken ct);
}

public interface IWorkloadScenarioRepository
{
    Task<WorkloadScenario?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct);
    Task<IReadOnlyList<WorkloadScenario>> ListByWorkloadAsync(
        TenantContext tenant, Guid workloadId, CancellationToken ct);
    Task UpsertAsync(WorkloadScenario scenario, CancellationToken ct);

    /// <summary>Hartes Löschen — Szenarien haben keine Fremdreferenzen (keine Assignments
    /// hängen direkt an einem Szenario), daher unproblematisch und jederzeit per
    /// Template-Import wiederherstellbar.</summary>
    Task DeleteAsync(TenantContext tenant, Guid id, CancellationToken ct);
}

public interface IExternalOrganizationRepository
{
    Task<ExternalOrganization?> GetAsync(TenantContext tenant, Guid id, CancellationToken ct);
    Task<ExternalOrganization?> GetByNameAsync(TenantContext tenant, string name, CancellationToken ct);
    Task<IReadOnlyList<ExternalOrganization>> ListAsync(TenantContext tenant, CancellationToken ct);
    Task UpsertAsync(ExternalOrganization organization, CancellationToken ct);
}

/// <summary>Ein Sheet als Zeilen von Rohwerten, gelesen ab der Kopfzeile — die technische
/// xlsx-Bibliothek (ClosedXML) bleibt bewusst in Infrastructure gekapselt, Application
/// kennt nur diese schmale Abstraktion (dieselbe Trennung wie bei allen anderen technischen
/// Adaptern: IGuestDirectory, IEmailProvider, ...).</summary>
public interface ISpreadsheetReader
{
    IReadOnlyList<string> GetSheetNames(Stream xlsxStream);

    /// <summary>Liest die Kopfzeile eines Sheets ab der gegebenen Spalte — endet an der
    /// ersten leeren Zelle.</summary>
    IReadOnlyList<string> ReadHeaderRow(Stream xlsxStream, string sheetName, int headerRowIndex, int dataStartColumnIndex);

    /// <summary>Liest alle Datenzeilen ab headerRowIndex+1 als Rohwerte (Spalten-Offset ab
    /// dataStartColumnIndex, 0-basiert) — endet an der ersten Zeile, deren erste Datenspalte
    /// leer ist.</summary>
    IReadOnlyList<IReadOnlyDictionary<int, string>> ReadDataRows(
        Stream xlsxStream, string sheetName, int headerRowIndex, int dataStartColumnIndex);
}
