import type {
  GuestAccount, Workload, WorkloadRole, WorkloadResource, WorkloadAssignmentCounts,
  GuestWorkloadAssignment, ReviewInstance, AuditEvent, DeletionGateEvaluation,
  WorkloadScenario, ScenarioTemplateDto, ScenarioImportResult,
  GuestImportInspectResult, GuestImportColumnMapping, GuestImportResult,
  UiConfiguration,
  MockEntraGroup, MockEntraMembership, MockEntraUser,
} from '../types/domain';

// API_BASE_URL und der Platform-Tenant kommen aus Vite-Env-Variablen (siehe .env.example
// im Repository-Root -> für das Frontend gespiegelt via VITE_-Präfix, kein Hardcoding).
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000';

// Im MVP/LOCAL_MOCK wird der Platform-Tenant clientseitig aus der lokalen Konfiguration
// gelesen. In DEV_INTEGRATION/AZURE_DEV übernimmt MSAL/Entra-Login diese Rolle — die
// Serverseite validiert den Tenant-Kontext ohnehin unabhängig vom Client (siehe
// B2B.Portal.Api ITenantContextAccessor).
const DEV_PLATFORM_TENANT_ID = import.meta.env.VITE_DEV_PLATFORM_TENANT_ID ?? 'dev-tenant-a';
const DEV_PORTAL_USER_MAIL = import.meta.env.VITE_DEV_PORTAL_USER_MAIL ?? 'admin@platform.example';
const DEV_PORTAL_ROLES = import.meta.env.VITE_DEV_PORTAL_ROLES ?? 'GovernanceAdmin,User,Reviewer';

function devThemeHeader(): Record<string, string> {
  const themeId = localStorage.getItem('portal-theme-id');
  return themeId ? { 'X-Portal-Theme-Id': themeId } : {};
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-Platform-Tenant-Id': DEV_PLATFORM_TENANT_ID,
      'X-Portal-User-Mail': DEV_PORTAL_USER_MAIL,
      'X-Portal-Roles': DEV_PORTAL_ROLES,
      ...devThemeHeader(),
      ...(init?.headers ?? {}),
    },
  });

  if (!response.ok) {
    const fallback = `API-Fehler ${response.status} bei ${path}`;
    let message = fallback;
    try {
      const body = (await response.json()) as { error?: string };
      message = body.error ?? fallback;
    } catch {
      // Antwort war kein JSON (z.B. leerer Body) -> Fallback-Meldung behalten.
    }
    throw new Error(message);
  }

  if (response.status === 202 || response.status === 204 || response.headers.get('content-length') === '0') {
    return undefined as T;
  }

  return (await response.json()) as T;
}

/** Wie request(), aber ohne Content-Type-Header — der Browser setzt bei FormData die
 * multipart/form-data-Boundary selbst; ein manuell gesetzter Content-Type ohne Boundary
 * würde das Parsen serverseitig brechen. Für den Excel-Gäste-Import (erster Datei-Upload
 * im Projekt). */
async function requestForm<T>(path: string, form: FormData): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: 'POST',
    headers: {
      'X-Platform-Tenant-Id': DEV_PLATFORM_TENANT_ID,
      'X-Portal-User-Mail': DEV_PORTAL_USER_MAIL,
      'X-Portal-Roles': DEV_PORTAL_ROLES,
      ...devThemeHeader(),
    },
    body: form,
  });

  if (!response.ok) {
    const fallback = `API-Fehler ${response.status} bei ${path}`;
    let message = fallback;
    try {
      const body = (await response.json()) as { error?: string };
      message = body.error ?? fallback;
    } catch {
      // Antwort war kein JSON -> Fallback-Meldung behalten.
    }
    throw new Error(message);
  }

  return (await response.json()) as T;
}

function mappingToFormValue(mapping: GuestImportColumnMapping): string {
  const columnToField: Record<string, string> = {};
  for (const [key, value] of Object.entries(mapping.columnToField)) {
    columnToField[key] = value;
  }
  return JSON.stringify({
    sheetName: mapping.sheetName,
    headerRowIndex: mapping.headerRowIndex,
    dataStartColumnIndex: mapping.dataStartColumnIndex,
    columnToField,
  });
}

export const api = {
  health: () => request<{ status: string; mode: string }>('/health'),
  uiConfiguration: () => request<UiConfiguration>('/api/ui/configuration'),
  myNavigation: () => request<{ items: string[] }>('/api/me/navigation'),
  listMockEntraUsers: () => request<MockEntraUser[]>('/api/dev/mock-entra/users'),
  listMockEntraGroups: () => request<MockEntraGroup[]>('/api/dev/mock-entra/groups'),
  listMockEntraMemberships: () => request<MockEntraMembership[]>('/api/dev/mock-entra/memberships'),

  listGuests: () => request<GuestAccount[]>('/api/guest-accounts'),
  getGuest: (id: string) => request<GuestAccount>(`/api/guest-accounts/${id}`),
  listGuestAssignments: (guestId: string) =>
    request<GuestWorkloadAssignment[]>(`/api/guest-accounts/${guestId}/assignments`),

  listWorkloads: () => request<Workload[]>('/api/workloads'),
  listMyWorkloads: () => request<Workload[]>('/api/me/workloads'),
  getWorkload: (id: string) => request<Workload>(`/api/workloads/${id}`),
  createWorkload: (name: string, owner: string | null, templateId?: string | null) =>
    request<Workload>('/api/workloads', {
      method: 'POST',
      body: JSON.stringify({ name, owner, templateId }),
    }),

  updateWorkload: (workloadId: string, name: string, owner: string | null) =>
    request<Workload>(`/api/workloads/${workloadId}`, {
      method: 'PUT',
      body: JSON.stringify({ name, owner }),
    }),

  deactivateWorkload: (workloadId: string) =>
    request<void>(`/api/workloads/${workloadId}`, { method: 'DELETE' }),

  reactivateWorkload: (workloadId: string) =>
    request<void>(`/api/workloads/${workloadId}/reactivate`, { method: 'POST' }),

  deleteWorkloadPermanently: (workloadId: string) =>
    request<void>(`/api/workloads/${workloadId}/permanent`, { method: 'DELETE' }),

  getWorkloadAssignmentCounts: (workloadId: string) =>
    request<WorkloadAssignmentCounts>(`/api/workloads/${workloadId}/assignment-counts`),

  createWorkloadRole: (workloadId: string, name: string, resourceMappings: string[]) =>
    request<WorkloadRole>(`/api/workloads/${workloadId}/roles`, {
      method: 'POST',
      body: JSON.stringify({ name, resourceMappings }),
    }),

  updateWorkloadRole: (workloadId: string, roleId: string, name: string, resourceMappings: string[]) =>
    request<WorkloadRole>(`/api/workloads/${workloadId}/roles/${roleId}`, {
      method: 'PUT',
      body: JSON.stringify({ name, resourceMappings }),
    }),

  deleteWorkloadRole: (workloadId: string, roleId: string) =>
    request<void>(`/api/workloads/${workloadId}/roles/${roleId}`, { method: 'DELETE' }),

  createWorkloadResource: (workloadId: string, resourceType: string, externalId: string | null) =>
    request<WorkloadResource>(`/api/workloads/${workloadId}/resources`, {
      method: 'POST',
      body: JSON.stringify({ resourceType, externalId }),
    }),

  updateWorkloadResource: (workloadId: string, resourceId: string, resourceType: string, externalId: string | null) =>
    request<WorkloadResource>(`/api/workloads/${workloadId}/resources/${resourceId}`, {
      method: 'PUT',
      body: JSON.stringify({ resourceType, externalId }),
    }),

  deleteWorkloadResource: (workloadId: string, resourceId: string) =>
    request<void>(`/api/workloads/${workloadId}/resources/${resourceId}`, { method: 'DELETE' }),

  listOpenReviews: () => request<ReviewInstance[]>('/api/reviews'),
  decideReviewItem: (reviewInstanceId: string, reviewItemId: string, decision: 'Keep' | 'Remove' | 'Escalated') =>
    request<void>(`/api/reviews/${reviewInstanceId}/items/${reviewItemId}/decision`, {
      method: 'POST',
      body: JSON.stringify({ decision }),
    }),

  listAuditEvents: () => request<AuditEvent[]>('/api/audit-events'),

  inviteGuest: (mail: string, displayName: string, directoryTenantId?: string) =>
    request<GuestAccount>('/api/guests/invite', {
      method: 'POST',
      body: JSON.stringify({ mail, displayName, directoryTenantId }),
    }),

  grantWorkloadRole: (workloadId: string, guestId: string, roleId: string) =>
    request(`/api/workloads/${workloadId}/assignments`, {
      method: 'POST',
      body: JSON.stringify({ guestId, roleId }),
    }),

  revokeAssignment: (assignmentId: string) =>
    request(`/api/assignments/${assignmentId}/revoke`, { method: 'POST' }),

  validateDeletion: (guestId: string, gracePeriodReached: boolean) =>
    request<DeletionGateEvaluation>(`/api/deletion-candidates/${guestId}/validate`, {
      method: 'POST',
      body: JSON.stringify({ gracePeriodReached }),
    }),

  listScenarios: (workloadId: string) =>
    request<WorkloadScenario[]>(`/api/workloads/${workloadId}/scenarios`),

  deployScenario: (scenarioId: string) =>
    request<WorkloadScenario>(`/api/scenarios/${scenarioId}/deploy`, { method: 'POST' }),

  importScenarios: (template: ScenarioTemplateDto) =>
    request<ScenarioImportResult>('/api/scenarios/import', {
      method: 'POST',
      body: JSON.stringify(template),
    }),

  exportScenario: (scenarioId: string) =>
    request<ScenarioTemplateDto>(`/api/scenarios/${scenarioId}/export`),

  deleteScenario: (scenarioId: string) =>
    request<void>(`/api/scenarios/${scenarioId}`, { method: 'DELETE' }),

  inspectGuestImportFile: (file: File, sheetName: string | null, headerRowIndex: number, dataStartColumnIndex: number) => {
    const form = new FormData();
    form.append('file', file);
    if (sheetName) form.append('sheetName', sheetName);
    form.append('headerRowIndex', String(headerRowIndex));
    form.append('dataStartColumnIndex', String(dataStartColumnIndex));
    return requestForm<GuestImportInspectResult>('/api/guest-import/inspect', form);
  },

  previewGuestImport: (file: File, mapping: GuestImportColumnMapping) => {
    const form = new FormData();
    form.append('file', file);
    form.append('mapping', mappingToFormValue(mapping));
    return requestForm<GuestImportResult>('/api/guest-import/preview', form);
  },

  commitGuestImport: (file: File, mapping: GuestImportColumnMapping) => {
    const form = new FormData();
    form.append('file', file);
    form.append('mapping', mappingToFormValue(mapping));
    return requestForm<GuestImportResult>('/api/guest-import/commit', form);
  },
};
