using System.Text.Json;
using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
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

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var payload = job.Payload;
        var mail = payload.GetProperty("Mail").GetString()!;
        var displayName = payload.GetProperty("DisplayName").GetString()!;

        var guest = await guestRepository.GetAsync(job.PlatformTenantId, Guid.Parse(job.EntityId), ct);
        if (guest is null)
        {
            logger.LogWarning("InviteGuest: GuestAccount {EntityId} nicht gefunden.", job.EntityId);
            return;
        }

        var entraObjectId = await guestDirectory.InviteGuestAsync(
            guest.DirectoryTenantId, mail, displayName, ct);

        guest.EntraObjectId = entraObjectId;
        await guestRepository.UpsertAsync(guest, ct);

        logger.LogInformation(
            "Guest {GuestId} eingeladen. EntraObjectId={EntraObjectId} CorrelationId={CorrelationId}",
            guest.Id, entraObjectId, job.CorrelationId);
    }
}

public sealed class ResendInvitationHandler(IGuestDirectory guestDirectory, ILogger<ResendInvitationHandler> logger)
    : IJobHandler
{
    public string JobType => JobTypes.ResendInvitation;

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var directoryTenantId = job.DirectoryTenantId ?? string.Empty;
        var entraObjectId = job.Payload.TryGetProperty("EntraObjectId", out var v) ? v.GetString() ?? "" : "";
        await guestDirectory.ResendInvitationAsync(directoryTenantId, entraObjectId, ct);
        logger.LogInformation("Invitation erneut gesendet für {EntraObjectId}", entraObjectId);
    }
}
