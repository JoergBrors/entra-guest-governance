namespace B2B.Portal.Domain.Entities;

/// <summary>
/// Unveränderlicher Nachweis (Blueprint 7 / 18.3 "Logging versus Audit"). Jede fachliche
/// Entscheidung und jede technische Operation erhält Correlation ID, Status und AuditEvent.
/// </summary>
public sealed class AuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string PlatformTenantId { get; init; }
    public required string Actor { get; init; }
    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public string? PolicyVersion { get; init; }
    public required string Result { get; init; } // z.B. "Allowed", "Blocked", "Success", "Failed"
    public Guid CorrelationId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? Details { get; init; }
}
