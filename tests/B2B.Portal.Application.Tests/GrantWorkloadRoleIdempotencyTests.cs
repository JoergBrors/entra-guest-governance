using B2B.Portal.Application.Commands;
using B2B.Portal.Application.Services;
using B2B.Portal.Domain.Enums;
using B2B.Portal.Infrastructure.Data;
using B2B.Portal.Infrastructure.Queue;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace B2B.Portal.Application.Tests;

/// <summary>
/// Idempotenztest für GrantWorkloadRole (MVP-Dokument, Abschnitt "TESTS / QUALITY GATES").
/// Derselbe Grant darf keinen doppelten technischen Zustand erzeugen: ein zweiter Aufruf
/// mit identischem Gast/Workload/Rolle gibt das bestehende aktive Assignment zurück und
/// legt keinen zweiten Job an.
/// </summary>
public class GrantWorkloadRoleIdempotencyTests
{
    private static GrantWorkloadRoleCommandHandler BuildHandler(
        InMemoryAssignmentRepository assignmentRepo, LocalJobQueue queue, out InMemoryJobRepository jobRepo)
    {
        jobRepo = new InMemoryJobRepository();
        var clock = new SystemClock();
        var provisioning = new ProvisioningService(jobRepo, queue, clock);
        var auditService = new AuditService(new InMemoryAuditWriter(), clock);
        return new GrantWorkloadRoleCommandHandler(assignmentRepo, provisioning, auditService);
    }

    [Fact]
    public async Task HandleAsync_CalledTwice_DoesNotCreateSecondActiveAssignment()
    {
        var assignmentRepo = new InMemoryAssignmentRepository();
        var queue = new LocalJobQueue();
        var handler = BuildHandler(assignmentRepo, queue, out _);

        var request = new GrantWorkloadRoleRequest(
            "tenant-a", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "tester");

        var first = await handler.HandleAsync(request, CancellationToken.None);

        // Simuliere, dass der Worker das erste Assignment bereits aktiv gesetzt hat.
        first.Status = AssignmentStatus.Active;
        await assignmentRepo.UpsertAsync(first, CancellationToken.None);

        var second = await handler.HandleAsync(request, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);

        var allForGuest = await assignmentRepo.ListByGuestAsync("tenant-a", request.GuestId, CancellationToken.None);
        Assert.Single(allForGuest);
    }
}
