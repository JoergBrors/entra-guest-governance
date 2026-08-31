using System.Text.Json;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Email;
using B2B.Portal.Worker.Processing;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Handlers.Invitation;

/// <summary>
/// Verarbeitet InviteGuest / ResendInvitation / InvitationReminder (Blueprint 11
/// "Invitation Handling"). Ruft ausschließlich IGuestDirectory auf — keine direkten
/// Graph-Aufrufe im Handler.
/// </summary>
public sealed class InvitationHandler(
    IGuestDirectory guestDirectory, IGuestAccountRepository guestRepository, ILogger<InvitationHandler> logger)
    : IJobHandler
{
    public string JobType => JobTypes.InviteGuest;

    public async Task<string?> HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var payload = job.Payload;
        var mail = payload.GetProperty("Mail").GetString()!;
        var displayName = payload.GetProperty("DisplayName").GetString()!;

        var guest = await guestRepository.GetAsync(
            TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId), Guid.Parse(job.EntityId), ct);
        if (guest is null)
        {
            logger.LogWarning("InviteGuest: GuestAccount {EntityId} nicht gefunden.", job.EntityId);
            return $"GuestAccount {job.EntityId} nicht gefunden — keine Einladung versendet.";
        }

        var entraObjectId = await guestDirectory.InviteGuestAsync(
            guest.DirectoryTenantId, mail, displayName, ct);

        guest.EntraObjectId = entraObjectId;
        // Mock-Redemption-Link, deterministisch aus der GuestAccount-Id — KEIN echter Entra-
        // Redemption-Link. Ein echter DEV_INTEGRATION-Pfad wuerde stattdessen die von
        // Microsoft Graph beim Invite zurueckgegebene "inviteRedeemUrl" verwenden (Integration
        // pending, siehe docs/architecture/graph-integration.md).
        guest.InvitationRedemptionLink = $"https://mock-invite.local/redeem/{guest.Id}";
        await guestRepository.UpsertAsync(guest, ct);

        logger.LogInformation(
            "Guest {GuestId} eingeladen. EntraObjectId={EntraObjectId} CorrelationId={CorrelationId}",
            guest.Id, entraObjectId, job.CorrelationId);

        return $"{displayName} ({mail}) eingeladen — EntraObjectId={entraObjectId}, Redemption-Link erzeugt.";
    }
}

public sealed class ResendInvitationHandler(IGuestDirectory guestDirectory, ILogger<ResendInvitationHandler> logger)
    : IJobHandler
{
    public string JobType => JobTypes.ResendInvitation;

    public async Task<string?> HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var directoryTenantId = job.DirectoryTenantId ?? string.Empty;
        var entraObjectId = job.Payload.TryGetProperty("EntraObjectId", out var v) ? v.GetString() ?? "" : "";
        await guestDirectory.ResendInvitationAsync(directoryTenantId, entraObjectId, ct);
        logger.LogInformation("Invitation erneut gesendet für {EntraObjectId}", entraObjectId);
        return $"Einladung erneut gesendet an EntraObjectId={entraObjectId}.";
    }
}

/// <summary>
/// Verarbeitet einen einzelnen Reminder-Versand fuer eine offene Einladung (Erweiterung
/// 2026-08-30 "Invitation Reminder Worker"). Wird ausschliesslich vom periodischen
/// InvitationReminderWorker eingereiht — dieser Handler selbst entscheidet NICHT, ob/wann ein
/// Gast faellig ist (das macht der Scanner ueber ReminderPolicy + LastReminderStageSent),
/// sondern nur, WIE die konkrete Stufe versendet wird. Placeholder-Ersetzung ist bewusst
/// simple String-Ersetzung (kein Templating-Framework, siehe Aufgabenstellung) — TemplateBody
/// ist seit der Outlook-HTML-Erweiterung (2026-08-30, Teil 2) HTML, kein Plaintext mehr;
/// Platzhalterwerte werden daher beim Einsetzen in den Body HTML-encodiert (XSS-/Markup-
/// Injection-Schutz, da DisplayName/WorkloadName aus Nutzereingaben stammen), waehrend der
/// Betreff weiterhin reiner Text bleibt (Mail-Header, kein HTML-Kontext).
/// </summary>
public sealed class InvitationReminderHandler(
    IEmailProvider emailProvider,
    IGuestAccountRepository guestRepository,
    string senderMailboxConfig,
    ILogger<InvitationReminderHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.InvitationReminder;

    public async Task<string?> HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var payload = job.Payload;
        var stageNumber = payload.GetProperty("StageNumber").GetInt32();
        var templateId = payload.GetProperty("TemplateId").GetString()!;
        var templateSubject = payload.GetProperty("TemplateSubject").GetString()!;
        var templateBody = payload.GetProperty("TemplateBody").GetString()!;
        var workloadName = payload.TryGetProperty("WorkloadName", out var w) ? w.GetString() : null;
        var daysSinceInvite = payload.TryGetProperty("DaysSinceInvite", out var d) ? d.GetInt32() : 0;

        var tenant = TenantContext.Create(job.PlatformTenantId, job.DirectoryTenantId);
        var guest = await guestRepository.GetAsync(tenant, Guid.Parse(job.EntityId), ct);
        if (guest is null)
        {
            logger.LogWarning("InvitationReminder: GuestAccount {EntityId} nicht gefunden.", job.EntityId);
            return $"GuestAccount {job.EntityId} nicht gefunden — kein Reminder versendet.";
        }

        var templateData = new Dictionary<string, string>
        {
            ["DisplayName"] = guest.DisplayName,
            ["WorkloadName"] = workloadName ?? string.Empty,
            ["DaysSinceInvite"] = daysSinceInvite.ToString(),
            ["RedemptionLink"] = guest.InvitationRedemptionLink ?? string.Empty,
            // Subject/Body werden hier bereits mit denselben Platzhaltern gerendert mitgegeben
            // (siehe Placeholder-Konvention EmailMessage.TemplateData) — Renderer ist die
            // MockEmailProvider-Vorschau bzw. ein spaeterer echter Adapter.
            ["Subject"] = ReplacePlaceholders(templateSubject, guest, workloadName, daysSinceInvite, htmlEncode: false),
            ["Body"] = OutlookHtmlEmailRenderer.Render(
                ReplacePlaceholders(templateSubject, guest, workloadName, daysSinceInvite, htmlEncode: false),
                ReplacePlaceholders(templateBody, guest, workloadName, daysSinceInvite, htmlEncode: true)),
            ["ContentType"] = "text/html",
        };

        var message = new EmailMessage(
            SenderMailbox: senderMailboxConfig,
            RecipientMail: guest.Mail,
            TemplateId: templateId,
            TemplateData: templateData,
            CorrelationId: job.CorrelationId,
            WorkloadContext: workloadName,
            PlatformTenantId: job.PlatformTenantId);

        await emailProvider.SendAsync(message, ct);

        guest.LastReminderStageSent = stageNumber;
        guest.LastReminderSentAt = DateTimeOffset.UtcNow;
        await guestRepository.UpsertAsync(guest, ct);

        logger.LogInformation(
            "InvitationReminder Stufe {StageNumber} gesendet an {Recipient} (Guest {GuestId}, " +
            "CorrelationId={CorrelationId}).", stageNumber, guest.Mail, guest.Id, job.CorrelationId);

        return $"Reminder Stufe {stageNumber} ('{templateId}') gesendet an {guest.Mail} ({daysSinceInvite} Tage seit Einladung).";
    }

    private static string ReplacePlaceholders(
        string template, GuestAccount guest, string? workloadName, int daysSinceInvite, bool htmlEncode)
    {
        // RedemptionLink wird NIE HTML-encodiert, selbst im Body: er wird stets als href-
        // Attributwert bzw. sichtbarer Link-Text eingesetzt und ist eine vom Backend
        // deterministisch generierte URL (kein Nutzereingabe-Feld) — siehe
        // InvitationHandler.HandleAsync, wo der Link gesetzt wird.
        string Encode(string value) => htmlEncode ? System.Net.WebUtility.HtmlEncode(value) : value;

        return template
            .Replace("{{DisplayName}}", Encode(guest.DisplayName))
            .Replace("{{WorkloadName}}", Encode(workloadName ?? string.Empty))
            .Replace("{{DaysSinceInvite}}", daysSinceInvite.ToString())
            .Replace("{{RedemptionLink}}", guest.InvitationRedemptionLink ?? string.Empty);
    }
}
