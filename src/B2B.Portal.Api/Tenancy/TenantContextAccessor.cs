using B2B.Portal.Domain.ValueObjects;
using B2B.Portal.Infrastructure.Auth;

namespace B2B.Portal.Api.Tenancy;

/// <summary>
/// Kapselt den Tenant-Kontext einer Request zentral (Blueprint 8 "Authentifizierung"-Zeile).
/// WICHTIG: In Produktion NIEMALS ungeprüft aus einem freien Client-Parameter übernehmen —
/// dieser Accessor ist die einzige Stelle, die den Kontext liefert, damit ein Wechsel auf
/// echte Token-Validierung an einer Stelle erfolgt.
/// </summary>
public interface ITenantContextAccessor
{
    TenantContext Current { get; }
}

/// <summary>
/// Liest den PlatformTenantId-Claim aus dem validierten JWT (HttpContext.User) statt aus
/// dem frueheren freien X-Platform-Tenant-Id-Header. Ersetzt HeaderTenantContextAccessor,
/// das Interface bleibt unveraendert. DirectoryTenantId bleibt vorerst optional/leer —
/// in LOCAL_MOCK gibt es keinen echten Directory-Tenant, DEV_INTEGRATION/AZURE_DEV leiten
/// ihn spaeter ebenfalls aus dem Token ab.
/// </summary>
public sealed class ClaimsTenantContextAccessor(IHttpContextAccessor httpContextAccessor) : ITenantContextAccessor
{
    public TenantContext Current
    {
        get
        {
            var ctx = httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("Kein HttpContext verfügbar.");
            var user = ctx.User;

            if (user.Identity is not { IsAuthenticated: true })
            {
                throw new UnauthorizedAccessException(
                    "Kein gueltiges Bearer-Token. Login ueber POST /api/auth/mock/login " +
                    "(EntraIdMock) bzw. den konfigurierten Identity Provider.");
            }

            var platformTenantId = user.FindFirst(PortalJwtClaimTypes.PlatformTenantId)?.Value;
            if (string.IsNullOrWhiteSpace(platformTenantId))
            {
                throw new UnauthorizedAccessException(
                    $"Token enthaelt keinen {PortalJwtClaimTypes.PlatformTenantId}-Claim.");
            }

            return TenantContext.Create(platformTenantId, directoryTenantId: null);
        }
    }
}
