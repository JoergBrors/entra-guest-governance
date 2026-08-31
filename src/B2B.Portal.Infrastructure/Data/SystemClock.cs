using B2B.Portal.Application.Ports;

namespace B2B.Portal.Infrastructure.Data;

/// <summary>Systemweite UTC-Zeitquelle (IClock-Implementierung).</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
