import type {
  GuestAccount, Workload, WorkloadRole, WorkloadResource, WorkloadAssignmentCounts,
  GuestWorkloadAssignment, ReviewInstance, AuditEvent, DeletionGateEvaluation,
  WorkloadScenario, ScenarioTemplateDto, ScenarioImportResult,
  GuestImportInspectResult, GuestImportColumnMapping, GuestImportResult,
  UiConfiguration,
  JobStatusResponse,
  MockEntraApplication, MockEntraApplicationSignIn, MockEntraGroup, MockEntraMembership, MockEntraUser,
  ScenarioUser,
  WorkloadMutationResponse,
  GuestAccountState,
  ReminderPolicy, ReminderStage, ReminderStagePreview, MailSinkEntry,
  WorkerControlState,
} from '../types/domain';
import { getToken } from '../auth/token';

// API_BASE_URL kommt aus Vite-Env-Variablen (siehe .env.example im Repository-Root ->
// für das Frontend gespiegelt via VITE_-Präfix, kein Hardcoding).
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000';

// Erweiterung 2026-08-30: Identität/Tenant kommen nicht mehr aus freien X-Portal-*-Headern,
// sondern aus dem serverseitig validierten JWT (siehe auth/token.ts, B2B.Portal.Api
// ClaimsPortalUserContextAccessor/ClaimsTenantContextAccessor). Ohne Token wird kein
// Authorization-Header gesendet — der Server antwortet dann mit 401, die App-Routing-Ebene
// (App.tsx) leitet in diesem Fall auf /login um.
function authHeader(): Record<string, string> {
  const token = getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

function devThemeHeader(): Record<string, string> {
  const themeId = localStorage.getItem('portal-theme-id');
  return themeId ? { 'X-Portal-Theme-Id': themeId } : {};
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...authHeader(),
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

  // 204 hat per HTTP-Spezifikation nie einen Body. 202 KANN einen Body haben (z.B. dieser
  // Trigger-Endpoint liefert { message, state } zurueck) — vorher wurde hier pauschal
  // undefined zurueckgegeben, was bei jedem 202-Response MIT Body zu "Cannot read properties
  // of undefined" beim Aufrufer fuehrte (siehe WorkerOverviewPage.handleTriggerWorker).
  // content-length fehlt bei manchen Antworten (z.B. chunked/ohne expliziten Header), daher
  // zusaetzlich versuchen zu parsen und nur bei leerem Body wirklich undefined liefern.
  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  if (!text) {
    return undefined as T;
  }
  return JSON.parse(text) as T;
}

/** Wie request(), aber ohne Content-Type-Header — der Browser setzt bei FormData die
 * multipart/form-data-Boundary selbst; ein manuell gesetzter Content-Type ohne Boundary
 * würde das Parsen serverseitig brechen. Für den Excel-Gäste-Import (erster Datei-Upload
 * im Projekt). */
async function requestForm<T>(path: string, form: FormData): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: 'POST',
    headers: {
      ...authHeader(),
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
  listJobs: () => request<JobStatusResponse[]>('/api/jobs'),
  getJobStatus: (jobId: string) => request<JobStatusResponse>(`/api/jobs/${jobId}`),
  stopJob: (jobId: string) => request<JobStatusResponse>(`/api/jobs/${jobId}/stop`, { method: 'POST' }),
  restartJob: (jobId: string) => request<JobStatusResponse>(`/api/jobs/${jobId}/restart`, { method: 'POST' }),
  triggerDiscovery: () => request<JobStatusResponse>('/api/jobs/trigger/discovery', { method: 'POST' }),
  triggerReconciliation: () =>
    request<{ queuedJobCount: number; jobIds: string[] }>('/api/jobs/trigger/reconciliation', { method: 'POST' }),
  listWorkers: () => request<WorkerControlState[]>('/api/dev/workers'),
  pauseWorker: (workerName: string) =>
    request<WorkerControlState>(`/api/dev/workers/${encodeURIComponent(workerName)}/pause`, { method: 'POST' }),
  resumeWorker: (workerName: string) =>
    request<WorkerControlState>(`/api/dev/workers/${encodeURIComponent(workerName)}/resume`, { method: 'POST' }),
  triggerWorker: (workerName: string) =>
    request<{ message: string; state: WorkerControlState }>(`/api/dev/workers/${encodeURIComponent(workerName)}/trigger`, { method: 'POST' }),
  uiConfiguration: () => request<UiConfiguration>('/api/ui/configuration'),
  myNavigation: () => request<{ items: string[] }>('/api/me/navigation'),
  mockLogin: (mail: string) =>
    request<{ token: string; mail: string; roles: string[]; platformTenantId: string }>('/api/auth/mock/login', {
      method: 'POST',
      body: JSON.stringify({ mail }),
    }),
  mockLogout: () => request<void>('/api/auth/mock/logout', { method: 'POST' }),
  listMockEntraUsers: () => request<MockEntraUser[]>('/api/dev/mock-entra/users'),
  listMockEntraLoginUsers: () => request<MockEntraUser[]>('/api/dev/mock-entra/login-users'),
  upsertMockEntraUser: (user: Partial<MockEntraUser> & Pick<MockEntraUser, 'mail' | 'displayName'>) =>
    request<MockEntraUser>(user.objectId ? `/api/dev/mock-entra/users/${user.objectId}` : '/api/dev/mock-entra/users', {
      method: user.objectId ? 'PUT' : 'POST',
      body: JSON.stringify(user),
    }),
  deleteMockEntraUser: (objectId: string) =>
    request<void>(`/api/dev/mock-entra/users/${objectId}`, { method: 'DELETE' }),

  listMockEntraGroups: () => request<MockEntraGroup[]>('/api/dev/mock-entra/groups'),
  upsertMockEntraGroup: (group: Partial<MockEntraGroup> & Pick<MockEntraGroup, 'displayName' | 'mailEnabled' | 'securityEnabled'>) =>
    request<MockEntraGroup>(group.objectId ? `/api/dev/mock-entra/groups/${group.objectId}` : '/api/dev/mock-entra/groups', {
      method: group.objectId ? 'PUT' : 'POST',
      body: JSON.stringify(group),
    }),
  deleteMockEntraGroup: (objectId: string) =>
    request<void>(`/api/dev/mock-entra/groups/${objectId}`, { method: 'DELETE' }),

  listMockEntraApplications: () => request<MockEntraApplication[]>('/api/dev/mock-entra/applications'),
  upsertMockEntraApplication: (application: Partial<MockEntraApplication> & Pick<MockEntraApplication, 'displayName'>) =>
    request<MockEntraApplication>(application.objectId ? `/api/dev/mock-entra/applications/${application.objectId}` : '/api/dev/mock-entra/applications', {
      method: application.objectId ? 'PUT' : 'POST',
      body: JSON.stringify(application),
    }),
  deleteMockEntraApplication: (objectId: string) =>
    request<void>(`/api/dev/mock-entra/applications/${objectId}`, { method: 'DELETE' }),

  listMockEntraMemberships: () => request<MockEntraMembership[]>('/api/dev/mock-entra/memberships'),
  upsertMockEntraMembership: (groupId: string, entraObjectId: string) =>
    request<void>('/api/dev/mock-entra/memberships', {
      method: 'POST',
      body: JSON.stringify({ groupId, entraObjectId }),
    }),
  deleteMockEntraMembership: (groupId: string, entraObjectId: string) =>
    request<void>('/api/dev/mock-entra/memberships', {
      method: 'DELETE',
      body: JSON.stringify({ groupId, entraObjectId }),
    }),
  removeAllMockEntraGroupMembers: (groupId: string) =>
    request<{ removed: number }>(`/api/dev/mock-entra/groups/${encodeURIComponent(groupId)}/members`, { method: 'DELETE' }),
  listMockEntraApplicationSignIns: (appId?: string | null) =>
    request<MockEntraApplicationSignIn[]>(`/api/dev/mock-entra/application-signins${appId ? `?appId=${encodeURIComponent(appId)}` : ''}`),

  // Erweiterung 2026-08-30 "Guest Pool Filter": optionale Filter werden als Query-Parameter
  // angehaengt und serverseitig ausgewertet (siehe FilterGuestsAsync, Program.cs).
  listGuests: (filter?: {
    workloadId?: string; scenarioId?: string; accountState?: GuestAccountState; invitationStatus?: 'accepted' | 'pending';
  }) => {
    const params = new URLSearchParams();
    if (filter?.workloadId) params.set('workloadId', filter.workloadId);
    if (filter?.scenarioId) params.set('scenarioId', filter.scenarioId);
    if (filter?.accountState) params.set('accountState', filter.accountState);
    if (filter?.invitationStatus) params.set('invitationStatus', filter.invitationStatus);
    const query = params.toString();
    return request<GuestAccount[]>(`/api/guest-accounts${query ? `?${query}` : ''}`);
  },
  getGuest: (id: string) => request<GuestAccount>(`/api/guest-accounts/${id}`),
  listGuestAssignments: (guestId: string) =>
    request<GuestWorkloadAssignment[]>(`/api/guest-accounts/${guestId}/assignments`),
  resendInvitation: (guestId: string) =>
    request<{ jobId: string }>(`/api/guest-accounts/${guestId}/resend-invitation`, { method: 'POST' }),

  // Erweiterung 2026-08-30 "Scoped Visibility fuer Workload-/Scenario-Owner": dieselbe
  // gefilterte Gaesteliste wie listGuests, aber serverseitig auf die eigenen Workloads
  // beschraenkt (siehe GET /api/me/managed-guests) — kein GovernanceAdmin noetig.
  listManagedGuests: () => request<GuestAccount[]>('/api/me/managed-guests'),

  listWorkloads: () => request<Workload[]>('/api/workloads'),
  listMyWorkloads: () => request<Workload[]>('/api/me/workloads'),
  getWorkload: (id: string) => request<Workload>(`/api/workloads/${id}`),
  createWorkload: (
    name: string,
    owner: string | null,
    templateId?: string | null,
    isDefault = false,
    administrativeUnitExternalId?: string | null,
    applicationExternalId?: string | null,
    resourceNamePatterns?: string[],
  ) =>
    request<WorkloadMutationResponse>('/api/workloads', {
      method: 'POST',
      body: JSON.stringify({ name, owner, templateId, isDefault, administrativeUnitExternalId, applicationExternalId, resourceNamePatterns }),
    }),

  updateWorkload: (
    workloadId: string,
    name: string,
    owner: string | null,
    administrativeUnitExternalId?: string | null,
    applicationExternalId?: string | null,
    resourceNamePatterns?: string[],
  ) =>
    request<WorkloadMutationResponse>(`/api/workloads/${workloadId}`, {
      method: 'PUT',
      body: JSON.stringify({ name, owner, administrativeUnitExternalId, applicationExternalId, resourceNamePatterns }),
    }),

  deactivateWorkload: (workloadId: string) =>
    request<void>(`/api/workloads/${workloadId}`, { method: 'DELETE' }),

  reactivateWorkload: (workloadId: string) =>
    request<void>(`/api/workloads/${workloadId}/reactivate`, { method: 'POST' }),

  deleteWorkloadPermanently: (workloadId: string) =>
    request<void>(`/api/workloads/${workloadId}/permanent`, { method: 'DELETE' }),

  getWorkloadAssignmentCounts: (workloadId: string) =>
    request<WorkloadAssignmentCounts>(`/api/workloads/${workloadId}/assignment-counts`),

  createWorkloadRole: (workloadId: string, name: string, resourceMappings: string[], applicationId?: string | null, applicationRoleId?: string | null) =>
    request<WorkloadRole>(`/api/workloads/${workloadId}/roles`, {
      method: 'POST',
      body: JSON.stringify({ name, applicationId, applicationRoleId, resourceMappings }),
    }),

  updateWorkloadRole: (workloadId: string, roleId: string, name: string, resourceMappings: string[], applicationId?: string | null, applicationRoleId?: string | null) =>
    request<WorkloadRole>(`/api/workloads/${workloadId}/roles/${roleId}`, {
      method: 'PUT',
      body: JSON.stringify({ name, applicationId, applicationRoleId, resourceMappings }),
    }),

  deleteWorkloadRole: (workloadId: string, roleId: string) =>
    request<void>(`/api/workloads/${workloadId}/roles/${roleId}`, { method: 'DELETE' }),

  createWorkloadResource: (workloadId: string, resourceType: string, externalId: string | null, displayName?: string | null) =>
    request<WorkloadResource>(`/api/workloads/${workloadId}/resources`, {
      method: 'POST',
      body: JSON.stringify({ resourceType, externalId, displayName }),
    }),

  updateWorkloadResource: (workloadId: string, resourceId: string, resourceType: string, externalId: string | null, displayName?: string | null) =>
    request<WorkloadResource>(`/api/workloads/${workloadId}/resources/${resourceId}`, {
      method: 'PUT',
      body: JSON.stringify({ resourceType, externalId, displayName }),
    }),

  deleteWorkloadResource: (workloadId: string, resourceId: string) =>
    request<void>(`/api/workloads/${workloadId}/resources/${resourceId}`, { method: 'DELETE' }),

  listOpenReviews: () => request<ReviewInstance[]>('/api/reviews'),
  decideReviewItem: (reviewInstanceId: string, reviewItemId: string, decision: 'Keep' | 'Remove' | 'Escalated') =>
    request<void>(`/api/reviews/${reviewInstanceId}/items/${reviewItemId}/decision`, {
      method: 'POST',
      body: JSON.stringify({ decision }),
    }),

  attachWorkloadResource: (workloadId: string, resourceType: string, externalId: string, displayName?: string | null) =>
    request<WorkloadResource>(`/api/workloads/${workloadId}/resources/attach`, {
      method: 'POST',
      body: JSON.stringify({ resourceType, externalId, displayName }),
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

  listScenarioUsers: (workloadId: string, scenarioId: string) =>
    request<ScenarioUser[]>(`/api/workloads/${workloadId}/scenarios/${scenarioId}/users`),

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

  getReminderPolicy: () => request<ReminderPolicy>('/api/reminder-policy'),
  updateReminderPolicy: (stages: ReminderStage[]) =>
    request<ReminderPolicy>('/api/reminder-policy', {
      method: 'PUT',
      body: JSON.stringify({ stages }),
    }),
  previewReminderStage: (templateSubject: string, templateBody: string) =>
    request<ReminderStagePreview>('/api/reminder-policy/preview', {
      method: 'POST',
      body: JSON.stringify({ templateSubject, templateBody }),
    }),

  listMailSink: () => request<MailSinkEntry[]>('/api/dev/mail-sink'),
};
