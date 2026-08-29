using B2B.Portal.Application.Ports;
using B2B.Portal.Domain.Entities;

namespace B2B.Portal.Application.Services;

/// <summary>
/// Zentraler Zugang zum Schreiben von AuditEvents (Blueprint 16.2). Jede sicherheitsrelevante
/// Aktion soll über diesen Service protokolliert werden, statt AuditEvent direkt zu bauen,
/// damit Actor/Correlation/PolicyVersion konsistent gesetzt werden.
/// </summary>
public sealed class AuditService(IAuditWriter auditWriter, IClock clock)
{
    public Task RecordAsync(
        string platformTenantId,
        string actor,
        string action,
        string entityType,
        string entityId,
        string result,
        Guid correlationId,
        string? details = null,
        string? policyVersion = null,
        CancellationToken ct = default)
    {
        var evt = new AuditEvent
        {
            PlatformTenantId = platformTenantId,
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Result = result,
            CorrelationId = correlationId,
            PolicyVersion = policyVersion,
            Details = details,
            Timestamp = clock.UtcNow,
        };

        return auditWriter.WriteAsync(evt, ct);
    }
}
