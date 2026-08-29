using B2B.Portal.Application.Ports;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Entities;

namespace B2B.Portal.Application.Commands;

public sealed record InviteGuestRequest(
    string PlatformTenantId, string DirectoryTenantId, string Mail, string DisplayName, string Actor);

/// <summary>
/// Command-Handler für POST /api/guests/invite (Blueprint 16.1). Speichert den fachlichen
/// Zustand (GuestAccount discovered/invited) und plant anschließend den technischen
/// InviteGuest-Job — das Frontend führt keine langlaufende Graph-Operation direkt aus
/// (Blueprint 10.1).
/// </summary>
public sealed class InviteGuestCommandHandler(
    IGuestAccountRepository guestRepository,
    ProvisioningService provisioningService,
    AuditService auditService,
    IClock clock)
{
    public async Task<GuestAccount> HandleAsync(InviteGuestRequest request, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();

        var guest = new GuestAccount
        {
            PlatformTenantId = request.PlatformTenantId,
            DirectoryTenantId = request.DirectoryTenantId,
            Mail = request.Mail,
            DisplayName = request.DisplayName,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };
        guest.TransitionTo(Domain.Enums.GuestAccountState.Invited);

        await guestRepository.UpsertAsync(guest, ct);

        var hash = DesiredStateHasher.Hash("InviteGuest", guest.Id.ToString(), request.Mail);
        await provisioningService.EnqueueJobAsync(
            request.PlatformTenantId, request.DirectoryTenantId, JobTypes.InviteGuest,
            nameof(GuestAccount), guest.Id.ToString(), hash,
            new { guest.Mail, guest.DisplayName }, correlationId, ct);

        await auditService.RecordAsync(
            request.PlatformTenantId, request.Actor, "InviteGuest", nameof(GuestAccount),
            guest.Id.ToString(), "Accepted", correlationId, ct: ct);

        return guest;
    }
}
