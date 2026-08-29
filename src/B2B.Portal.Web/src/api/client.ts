import type {
  GuestAccount, Workload, WorkloadRole, WorkloadResource, WorkloadAssignmentCounts,
  GuestWorkloadAssignment, ReviewInstance, AuditEvent, DeletionGateEvaluation,
  WorkloadScenario, ScenarioTemplateDto, ScenarioImportResult,
} from '../types/domain';

// API_BASE_URL und der Platform-Tenant kommen aus Vite-Env-Variablen (siehe .env.example
// im Repository-Root -> für das Frontend gespiegelt via VITE_-Präfix, kein Hardcoding).
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000';

// Im MVP/LOCAL_MOCK wird der Platform-Tenant clientseitig aus der lokalen Konfiguration
// gelesen. In DEV_INTEGRATION/AZURE_DEV übernimmt MSAL/Entra-Login diese Rolle — die
// Serverseite validiert den Tenant-Kontext ohnehin unabhängig vom Client (siehe
// B2B.Portal.Api ITenantContextAccessor).
const DEV_PLATFORM_TENANT_ID = import.meta.env.VITE_DEV_PLATFORM_TENANT_ID ?? 'dev-tenant-a';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-Platform-Tenant-Id': DEV_PLATFORM_TENANT_ID,
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

export const api = {
  health: () => request<{ status: string; mode: string }>('/health'),

  listGuests: () => request<GuestAccount[]>('/api/guest-accounts'),
  getGuest: (id: string) => request<GuestAccount>(`/api/guest-accounts/${id}`),
  listGuestAssignments: (guestId: string) =>
    request<GuestWorkloadAssignment[]>(`/api/guest-accounts/${guestId}/assignments`),

  listWorkloads: () => request<Workload[]>('/api/workloads'),

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
};
