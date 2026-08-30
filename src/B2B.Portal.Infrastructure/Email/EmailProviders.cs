using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Infrastructure.Email;

/// <summary>
/// Rendert eine strukturierte E-Mail-Vorschau in Log/Test-Sink statt zu versenden
/// (MVP-Dokument Abschnitt 6: "LOCAL_MOCK rendert eine E-Mail-Vorschau"). Der In-Memory-Sink
/// bleibt fuer schnelle Tests bestehen (MockEmailProviderTests), ist aber NICHT die Quelle des
/// Mail Monitors (GET /api/dev/mail-sink, Erweiterung 2026-08-30): API und Worker sind
/// getrennte Prozesse mit getrenntem In-Memory-Zustand — ein rein prozesslokaler Sink im
/// Worker (wo die meisten Mails tatsaechlich versendet werden, z.B. InvitationReminder) waere
/// fuer den im API-Prozess laufenden Monitor-Endpoint nie sichtbar (Bug, live beobachtet: Mail
/// im Worker-Log sichtbar versendet, aber GET /api/dev/mail-sink zeigte []). Daher zusaetzlich
/// Persistenz ueber IMailSinkRepository (Cosmos, discovery-Container) — derselbe Grund, aus
/// dem Job-Status/Mock-Entra-User bereits frueher von InMemory auf Cosmos migriert wurden.
/// </summary>
public sealed class MockEmailProvider(ILogger<MockEmailProvider> logger, IMailSinkRepository mailSinkRepository) : IEmailProvider
{
    private readonly List<EmailMessage> _sink = [];
    public IReadOnlyList<EmailMessage> Sink => _sink;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sink.Add(message);
        logger.LogInformation(
            "[MOCK EMAIL PREVIEW] Sender={Sender} Recipient={Recipient} Template={Template} " +
            "CorrelationId={CorrelationId} WorkloadContext={WorkloadContext} Data={@TemplateData}",
            message.SenderMailbox, message.RecipientMail, message.TemplateId,
            message.CorrelationId, message.WorkloadContext, message.TemplateData);

        var tenant = TenantContext.Create(message.PlatformTenantId);
        await mailSinkRepository.AppendAsync(tenant, message, DateTimeOffset.UtcNow, ct);
    }
}

/// <summary>
/// Adapterstruktur für Microsoft Graph sendMail über eine Shared Mailbox
/// (Blueprint 15, MVP-Dokument Abschnitt 6). Absenderadresse kommt ausschließlich aus
/// Konfiguration (<paramref name="sharedMailbox"/>) — kein Hardcoding. Solange kein
/// Dev-Tenant / App Registration vorliegt, wirft dieser Adapter bewusst statt einen
/// Graph-Call zu erfinden; er ist der explizite "nächste Schritt" für DEV_INTEGRATION
/// (siehe docs/architecture/mvp-test-report.md).
/// </summary>
public sealed class GraphSharedMailboxEmailProvider(string sharedMailbox, bool allowGraphWrites) : IEmailProvider
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (!allowGraphWrites)
        {
            throw new InvalidOperationException(
                "ALLOW_GRAPH_WRITES=false — GraphSharedMailboxEmailProvider darf in " +
                "LOCAL_MOCK nicht senden. Aktiviere DEV_INTEGRATION mit dediziertem Dev-Tenant.");
        }

        if (string.IsNullOrWhiteSpace(sharedMailbox))
        {
            throw new InvalidOperationException(
                "NOTIFICATIONS_SHARED_MAILBOX ist nicht konfiguriert. " +
                "Integration pending: Adresse muss als Tenant-Konfiguration gesetzt werden.");
        }

        // Integration pending: der eigentliche Microsoft Graph SDK sendMail-Aufruf gegen
        // /users/{sharedMailbox}/sendMail wird ergänzt, sobald eine Dev App Registration
        // mit Mail.Send-Berechtigung für den Ziel-Tenant vorliegt. Es werden keine
        // Tenant-/Client-IDs erfunden.
        throw new NotImplementedException(
            "Graph sendMail Integration pending — siehe mvp-test-report.md, " +
            "offene Integrationstests.");
    }
}
