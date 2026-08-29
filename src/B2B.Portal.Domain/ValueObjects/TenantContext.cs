namespace B2B.Portal.Domain.ValueObjects;

/// <summary>
/// Unveränderlicher Tenant-Kontext. Wird serverseitig aus validierten Tokens abgeleitet
/// (siehe Blueprint 8 "Mandantenfähigkeit und Tenant-Isolation") und niemals blind aus
/// Client-Parametern übernommen. Jede Entität, jede Query, jeder Job trägt diesen Kontext.
/// </summary>
public sealed record TenantContext(string PlatformTenantId, string? DirectoryTenantId)
{
    public static TenantContext Create(string platformTenantId, string? directoryTenantId = null)
    {
        if (string.IsNullOrWhiteSpace(platformTenantId))
        {
            throw new ArgumentException("PlatformTenantId ist verpflichtend.", nameof(platformTenantId));
        }

        return new TenantContext(platformTenantId, directoryTenantId);
    }

    /// <summary>
    /// Vergleicht, ob ein Zielobjekt zum selben Platform-Tenant gehört. Grundlage für
    /// Tenant-Isolation in Repositories und Domain Services.
    /// </summary>
    public bool Owns(string platformTenantId) =>
        string.Equals(PlatformTenantId, platformTenantId, StringComparison.Ordinal);
}

/// <summary>
/// Correlation-Kontext, der über API, Domain, Job, Worker und Connector durchgezogen wird
/// (Blueprint Anhang A, Regel 9).
/// </summary>
public sealed record CorrelationContext(Guid CorrelationId, string? CausationId = null)
{
    public static CorrelationContext New() => new(Guid.NewGuid());
}
