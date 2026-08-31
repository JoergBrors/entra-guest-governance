using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;

namespace B2B.Portal.Application.Ports;

/// <summary>
/// Abstraktion für die Job Queue. PoC/Development nutzt CosmosJobQueue, Produktion z.B.
/// Azure Service Bus (Blueprint 19.4 "IJobQueue: CosmosJobQueue (PoC) / ServiceBusJobQueue").
/// </summary>
public interface IJobQueue
{
    Task EnqueueAsync(JobEnvelope job, CancellationToken ct);

    Task<JobEnvelope?> DequeueAsync(CancellationToken ct);

    Task CompleteAsync(Guid jobId, CancellationToken ct);
    Task CancelAsync(Guid jobId, CancellationToken ct);

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
    string? WorkloadContext,
    // Erweiterung 2026-08-30 "Mail Monitor": noetig, damit ein persistenter Mail-Sink
    // (CosmosMailSinkRepository) prozessuebergreifend nach Tenant filtern kann — ohne dieses
    // Feld haette IEmailProvider.SendAsync keinen Tenant-Kontext zum Schreiben.
    string PlatformTenantId);

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
    Task<IReadOnlyList<DirectoryOperation>> ListAsync(TenantContext tenant, CancellationToken ct);
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

/// <summary>
/// Persistenz-DTO fuer einen Mock-Entra-Benutzer (Erweiterung 2026-08-30: Cosmos-Speicherung
/// statt reinem In-Memory-Singleton, siehe
/// B2B.Portal.Infrastructure.Directory.MockEntraDirectoryStore/MockEntraUser). Bewusst als
/// eigener, schmaler DTO-Typ in Application/Ports (statt Referenz auf den Infrastructure-Typ
/// MockEntraUser) definiert — Application darf Infrastructure nicht referenzieren
/// (B2B.Portal.Architecture.Tests). Die Infrastructure-Implementierung
/// (CosmosMockEntraUserRepository) mappt zwischen MockEntraUser und diesem DTO.
/// </summary>
public sealed record MockEntraUserRecord(
    string ObjectId,
    string UserPrincipalName,
    string Mail,
    string DisplayName,
    string GivenName,
    string Surname,
    string CompanyName,
    string Department,
    string JobTitle,
    string Sponsor,
    string AccountEnabled,
    string UserType,
    IReadOnlyList<string> PortalRoles,
    string PlatformTenantId,
    DateTimeOffset? LastLoginAt = null);

/// <summary>
/// Persistenz fuer Mock-Entra-Benutzer (Erweiterung 2026-08-30: Cosmos-Speicherung statt
/// reinem In-Memory-Singleton). Bewusst schmal gehalten — nur was fuer Login-Lookup,
/// Startup-Hydration und Rollen-Persistenz gebraucht wird (siehe MockEntraDirectoryStore,
/// das diesen Port fuer UpsertUser/Startup-Hydration nutzt).
/// </summary>
public interface IMockEntraUserRepository
{
    /// <summary>Alle Benutzer ueber ALLE Tenants (fuer die Startup-Hydration des In-Memory
    /// Stores, bevor ein Tenant-Kontext ueberhaupt bekannt ist — analog zum Cold-Start-Problem
    /// bei /api/dev/mock-entra/login-users).</summary>
    Task<IReadOnlyList<MockEntraUserRecord>> ListAllAsync(CancellationToken ct);

    Task<IReadOnlyList<MockEntraUserRecord>> ListAsync(TenantContext tenant, CancellationToken ct);

    Task UpsertAsync(MockEntraUserRecord user, CancellationToken ct);
}

/// <summary>DTO fuer eine Mock-Entra-Gruppe (siehe MockEntraUserRecord-Kommentar: Application
/// darf Infrastructure nicht referenzieren, daher dieses eigene Record statt MockEntraGroup).
/// </summary>
public sealed record MockEntraGroupRecord(
    string ObjectId,
    string DisplayName,
    string MailNickname,
    string Description,
    IReadOnlyList<string> GroupTypes,
    bool MailEnabled,
    bool SecurityEnabled,
    IReadOnlyList<string> ResourceProvisioningOptions);

/// <summary>DTO fuer eine Mock-Entra-Gruppenmitgliedschaft.</summary>
public sealed record MockEntraMembershipRecord(string GroupId, string EntraObjectId);

/// <summary>DTO fuer eine Mock-Entra-Anwendung (App-Registrierung).</summary>
public sealed record MockEntraApplicationRecord(
    string ObjectId,
    string AppId,
    string DisplayName,
    IReadOnlyList<MockEntraApplicationRoleRecord> AppRoles);

public sealed record MockEntraApplicationRoleRecord(string Id, string Value, string DisplayName, string Description);

/// <summary>DTO fuer einen Mock-Entra-Anwendungs-Sign-in.</summary>
public sealed record MockEntraApplicationSignInRecord(string AppId, string EntraObjectId, DateTimeOffset LastLoginAt);

/// <summary>
/// Persistenz fuer den restlichen Mock-Entra-Bestand (Gruppen, Mitgliedschaften, Anwendungen,
/// Anwendungs-Sign-ins) — Ergaenzung zu IMockEntraUserRepository (Erweiterung 2026-08-31:
/// vorher lebten Gruppen/Memberships/Applications/AppSignIns ausschliesslich im In-Memory
/// MockEntraDirectoryStore-Singleton und gingen bei jedem Prozessneustart (API wie Worker)
/// verloren — Gruppen liessen sich zwar teilweise aus persistierten WorkloadResource-Eintraegen
/// rekonstruieren (siehe MockEntraDirectoryStore.HydrateFromWorkloadsAndGuestsAsync), aber
/// eigenstaendig angelegte oder manuell administrierte Gruppen/Mitgliedschaften (z.B. ueber
/// /api/dev/mock-entra/groups) nicht. Bewusst ohne TenantContext-Parameter — Gruppen/
/// Anwendungen sind im Mock-Entra-Stamm (anders als Users) nicht tenant-gebunden (siehe
/// MockEntraGroup/MockEntraApplication in MockGuestDirectory.cs), Cosmos-seitig laufen die
/// Queries daher Cross-Partition wie IMockEntraUserRepository.ListAllAsync.
/// </summary>
public interface IMockEntraDirectoryRepository
{
    Task<IReadOnlyList<MockEntraGroupRecord>> ListGroupsAsync(CancellationToken ct);

    Task UpsertGroupAsync(MockEntraGroupRecord group, CancellationToken ct);

    Task DeleteGroupAsync(string objectId, CancellationToken ct);

    Task<IReadOnlyList<MockEntraMembershipRecord>> ListMembershipsAsync(CancellationToken ct);

    Task UpsertMembershipAsync(MockEntraMembershipRecord membership, CancellationToken ct);

    Task DeleteMembershipAsync(string groupId, string entraObjectId, CancellationToken ct);

    Task DeleteMembershipsByGroupAsync(string groupId, CancellationToken ct);

    Task<IReadOnlyList<MockEntraApplicationRecord>> ListApplicationsAsync(CancellationToken ct);

    Task UpsertApplicationAsync(MockEntraApplicationRecord application, CancellationToken ct);

    Task DeleteApplicationAsync(string objectId, CancellationToken ct);

    Task<IReadOnlyList<MockEntraApplicationSignInRecord>> ListApplicationSignInsAsync(CancellationToken ct);

    Task UpsertApplicationSignInAsync(MockEntraApplicationSignInRecord signIn, CancellationToken ct);
}

/// <summary>
/// Persistenz fuer die Erinnerungs-Policy fuer offene Einladungen (Erweiterung 2026-08-30
/// "Invitation Reminder Worker"). Genau eine Policy pro PlatformTenantId — GetAsync liefert
/// null, solange der Tenant noch keine eigene Policy konfiguriert hat (Worker/Scanner
/// behandeln das als "keine Reminder aktiv", siehe InvitationReminderWorker).
/// </summary>
public interface IReminderPolicyRepository
{
    Task<ReminderPolicy?> GetAsync(TenantContext tenant, CancellationToken ct);

    Task UpsertAsync(ReminderPolicy policy, CancellationToken ct);
}

/// <summary>
/// Persistenter Log der ueber IEmailProvider versendeten (Mock-)Mails (Mail Monitor,
/// Erweiterung 2026-08-30). Noetig, weil API und Worker getrennte Prozesse mit jeweils
/// eigenem In-Memory-Zustand sind — ein rein prozesslokaler Sink im API-Prozess wuerde nie die
/// tatsaechlich vom Worker-Prozess versendeten Mails zeigen (derselbe Grund, aus dem
/// MockEntraUser/Job-Status bereits frueher von InMemory auf Cosmos migriert wurden).
/// </summary>
public interface IMailSinkRepository
{
    Task AppendAsync(TenantContext tenant, EmailMessage message, DateTimeOffset sentAt, CancellationToken ct);

    Task<IReadOnlyList<(EmailMessage Message, DateTimeOffset SentAt)>> ListAsync(TenantContext tenant, int take, CancellationToken ct);
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
