// Token-Handling fuer den EntraIdMock-Login (Erweiterung 2026-08-30: Ablösung der freien
// X-Portal-*-Header durch ein echtes JWT). sessionStorage statt localStorage — Schliessen
// des Tabs beendet die Session bewusst, das entspricht normaler Session-Semantik und behebt
// den urspruenglichen Bug (Sign-out ohne sichtbare Wirkung, weil client.ts sofort wieder auf
// einen Default-Dev-User zurueckfiel).
const STORAGE_KEY = 'portal-jwt';

export interface PortalTokenClaims {
  mail: string;
  roles: string[];
  platformTenantId: string;
}

export function storeToken(token: string): void {
  sessionStorage.setItem(STORAGE_KEY, token);
}

export function clearToken(): void {
  sessionStorage.removeItem(STORAGE_KEY);
}

export function getToken(): string | null {
  return sessionStorage.getItem(STORAGE_KEY);
}

// MockJwtIssuer stellt Rollen ueber System.Security.Claims.ClaimTypes.Role aus (siehe
// src/B2B.Portal.Infrastructure/Auth/MockJwtIssuer.cs). ClaimTypes.Role ist die volle URI
// "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" — JwtSecurityTokenHandler
// mappt sie NICHT auf die kurze Form "role" (verifiziert per echtem Token-Payload-Dump).
// Bug: Admin-Rolle kam im Frontend nicht an, weil hier nur "role" geprueft wurde. email kommt
// dagegen als kurzer Key "email" im Payload an (der Server setzt PayloadJson/Claims direkt,
// kein automatisches URI-Mapping fuer email) — beide Formen werden trotzdem akzeptiert, falls
// sich das Serialisierungsverhalten je nach Handler-Version aendert.
const ROLE_CLAIM_KEYS = ['role', 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
const EMAIL_CLAIM_KEYS = ['email', 'http://schemas.microsoft.com/ws/2008/06/identity/claims/emailaddress'];

function firstClaim(json: Record<string, unknown>, keys: string[]): unknown {
  for (const key of keys) {
    if (json[key] !== undefined) return json[key];
  }
  return undefined;
}

/** Leichtgewichtiges Base64Url-Payload-Decoding ohne Signaturpruefung — die Signatur wird
 * ohnehin serverseitig bei jedem Request geprueft, hier geht es nur darum, mail/roles/tenant
 * fuer die UI (AppLayout-Props) aus dem bereits vertrauten Token zu extrahieren. */
export function decodeToken(token: string): PortalTokenClaims | null {
  try {
    const payloadSegment = token.split('.')[1];
    if (!payloadSegment) return null;
    const base64 = payloadSegment.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const json = JSON.parse(atob(padded)) as Record<string, unknown>;

    const emailClaim = firstClaim(json, EMAIL_CLAIM_KEYS);
    const mail = typeof emailClaim === 'string' ? emailClaim : null;
    const platformTenantId = typeof json.platformTenantId === 'string' ? json.platformTenantId : null;
    if (!mail || !platformTenantId) return null;

    const roleClaim = firstClaim(json, ROLE_CLAIM_KEYS);
    const roles = Array.isArray(roleClaim)
      ? roleClaim.filter((r): r is string => typeof r === 'string')
      : typeof roleClaim === 'string' ? [roleClaim] : [];

    return { mail, roles, platformTenantId };
  } catch {
    return null;
  }
}

export function getCurrentClaims(): PortalTokenClaims | null {
  const token = getToken();
  return token ? decodeToken(token) : null;
}
