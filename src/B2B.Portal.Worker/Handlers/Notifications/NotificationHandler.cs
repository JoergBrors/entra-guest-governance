using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Worker.Processing;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Handlers.Notifications;

/// <summary>
/// Löst Template auf und versendet über IEmailProvider (MVP-Dokument Abschnitt 6
/// "Notification Worker"). Sender kommt aus Konfiguration, Template aus Payload —
/// kein Hardcoding im Handler. Protokolliert Sender, Recipient, Template,
/// CorrelationId und Workload-Kontext nachvollziehbar (siehe Verification-Prompt Punkt 10).
/// </summary>
public sealed class NotificationHandler(
    IEmailProvider emailProvider, string senderMailboxConfig, ILogger<NotificationHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.SendNotification;

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var payload = job.Payload;
        var recipient = payload.GetProperty("RecipientMail").GetString()!;
        var templateId = payload.GetProperty("TemplateId").GetString()!;
        var workloadContext = payload.TryGetProperty("WorkloadContext", out var w) ? w.GetString() : null;

        var templateData = new Dictionary<string, string>();
        if (payload.TryGetProperty("TemplateData", out var dataElement))
        {
            foreach (var prop in dataElement.EnumerateObject())
            {
                templateData[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }

        var message = new EmailMessage(
            SenderMailbox: senderMailboxConfig,
            RecipientMail: recipient,
            TemplateId: templateId,
            TemplateData: templateData,
            CorrelationId: job.CorrelationId,
            WorkloadContext: workloadContext,
            PlatformTenantId: job.PlatformTenantId);

        await emailProvider.SendAsync(message, ct);

        logger.LogInformation(
            "Notification verarbeitet: Sender={Sender} Recipient={Recipient} Template={Template} " +
            "Workload={Workload} CorrelationId={CorrelationId}",
            senderMailboxConfig, recipient, templateId, workloadContext, job.CorrelationId);
    }
}
