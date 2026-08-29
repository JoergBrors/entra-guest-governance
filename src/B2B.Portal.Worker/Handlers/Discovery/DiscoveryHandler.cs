using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Worker.Processing;
using Microsoft.Extensions.Logging;

namespace B2B.Portal.Worker.Handlers.Discovery;

/// <summary>
/// Initial Audit / Discovery (Blueprint 12.1). Liest Gäste + Memberships über
/// IGuestDirectory und legt entdeckte Zugriffe als Unclassified an, bis eine
/// bewusste Klassifizierung/Workload-Zuordnung erfolgt (Blueprint 12.2).
/// </summary>
public sealed class DiscoveryHandler(
    IGuestDirectory guestDirectory,
    IGuestAccountRepository guestRepository,
    IResourceAccessRepository resourceAccessRepository,
    ILogger<DiscoveryHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.RunDiscovery;

    public async Task HandleAsync(JobEnvelope job, CancellationToken ct)
    {
        var directoryTenantId = job.DirectoryTenantId ?? string.Empty;
        var guests = await guestDirectory.ListGuestsAsync(directoryTenantId, ct);

        foreach (var snapshot in guests)
        {
            var existing = (await guestRepository.ListAsync(job.PlatformTenantId, ct))
                .FirstOrDefault(g => g.EntraObjectId == snapshot.EntraObjectId);

            var guest = existing ?? new GuestAccount
            {
                PlatformTenantId = job.PlatformTenantId,
                DirectoryTenantId = directoryTenantId,
                EntraObjectId = snapshot.EntraObjectId,
                Mail = snapshot.Mail,
                DisplayName = snapshot.DisplayName,
            };

            if (existing is null)
            {
                guest.TransitionTo(GuestAccountState.Discovered);
                await guestRepository.UpsertAsync(guest, ct);
            }

            var memberships = await guestDirectory.ListMembershipsAsync(directoryTenantId, snapshot.EntraObjectId, ct);
            foreach (var m in memberships)
            {
                await resourceAccessRepository.UpsertAsync(new ResourceAccess
                {
                    PlatformTenantId = job.PlatformTenantId,
                    GuestId = guest.Id,
                    ResourceType = "Group",
                    ExternalResourceId = m.GroupId,
                    Classification = AccessClassification.Unclassified,
                }, ct);
            }
        }

        logger.LogInformation(
            "Discovery abgeschlossen: {Count} Gäste für Tenant {DirectoryTenantId}. CorrelationId={CorrelationId}",
            guests.Count, directoryTenantId, job.CorrelationId);
    }
}
