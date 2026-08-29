// Spiegelt die zentralen Backend-Entitäten (siehe B2B.Portal.Domain). Bewusst schlank
// gehalten — die normale User-Ansicht zeigt ohnehin nur zugeordnete Workloads und Rollen,
// keine Graph-Details (Blueprint 9 "Webportal und Bedienmodell").

export type GuestAccountState =
  | 'Discovered' | 'Invited' | 'Active' | 'Inactive' | 'Blocked'
  | 'OrphanCandidate' | 'PendingRemoval' | 'Disabled' | 'Deleted';

export interface GuestAccount {
  id: string;
  platformTenantId: string;
  directoryTenantId: string;
  entraObjectId?: string | null;
  mail: string;
  displayName: string;
  externalOrganizationId?: string | null;
  sponsor?: string | null;
  accountState: GuestAccountState;
  createdAt: string;
  updatedAt: string;
}

export interface WorkloadRole {
  id: string;
  workloadId: string;
  name: string;
  resourceMappings: string[];
}

export interface WorkloadResource {
  id: string;
  workloadId: string;
  resourceType: string;
  externalId?: string | null;
  managed: boolean;
}

export interface Workload {
  id: string;
  platformTenantId: string;
  name: string;
  owner?: string | null;
  templateId?: string | null;
  active: boolean;
  roles: WorkloadRole[];
  resources: WorkloadResource[];
}

export type ReviewDecision = 'Pending' | 'Keep' | 'Remove' | 'Escalated';

export interface ReviewItem {
  id: string;
  reviewInstanceId: string;
  assignmentId: string;
  decision: ReviewDecision;
  decidedBy?: string | null;
  decidedAt?: string | null;
}

export interface ReviewInstance {
  id: string;
  platformTenantId: string;
  reviewDefinitionId: string;
  provider: 'Auto' | 'Internal' | 'EntraNative';
  startedAt: string;
  completedAt?: string | null;
  items: ReviewItem[];
}

export interface AuditEvent {
  id: string;
  platformTenantId: string;
  actor: string;
  action: string;
  entityType: string;
  entityId: string;
  policyVersion?: string | null;
  result: string;
  correlationId: string;
  timestamp: string;
  details?: string | null;
}

export type DeletionGateResult = 'Blocked' | 'Ready';

export interface DeletionGateEvaluation {
  result: DeletionGateResult;
  blockers: string[];
}
