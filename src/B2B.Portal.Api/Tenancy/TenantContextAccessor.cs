using B2B.Portal.Domain.ValueObjects;

namespace B2B.Portal.Api.Tenancy;

/// <summary>
/// Kapselt den Tenant-Kontext einer Request zentral (Blueprint 8 "Authentifizierung"-Zeile).
/// Im MVP/LOCAL_MOCK wird der Kontext aus einem vertrauenswürdigen Header gelesen, der in
/// DEV_INTEGRATION/AZURE_DEV durch die validierte Token-Claim-Extraktion ersetzt wird.
/// WICHTIG: In Produktion NIEMALS ungeprüft aus einem freien Client-Parameter übernehmen —
/// dieser Accessor ist die einzige Stelle, die den Kontext liefert, damit ein Wechsel auf
/// echte Token-Validierung an einer Stelle erfolgt.
/// </summary>
public interface ITenantContextAccessor
{
    TenantContext Current { get; }
}

public sealed class HeaderTenantContextAccessor(IHttpContextAccessor httpContextAccessor) : ITenantContextAccessor
{
    public TenantContext Current
    {
        get
        {
            var ctx = httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("Kein HttpContext verfügbar.");

            var platformTenantId = ctx.Request.Headers["X-Platform-Tenant-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(platformTenantId))
            {
                throw new UnauthorizedAccessException(
                    "X-Platform-Tenant-Id fehlt. In DEV_INTEGRATION/AZURE_DEV wird dieser Wert " +
                    "aus dem validierten Entra-Token abgeleitet, nicht aus einem freien Parameter.");
            }

            var directoryTenantId = ctx.Request.Headers["X-Directory-Tenant-Id"].FirstOrDefault();
            return TenantContext.Create(platformTenantId, directoryTenantId);
        }
    }
}
