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

    const mail = typeof json.email === 'string' ? json.email : null;
    const platformTenantId = typeof json.platformTenantId === 'string' ? json.platformTenantId : null;
    if (!mail || !platformTenantId) return null;

    const roleClaim = json.role;
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
