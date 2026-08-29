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

export interface WorkloadAssignmentCounts {
  active: number;
  inactive: number;
}

export type AssignmentStatus =
  | 'Requested' | 'Approved' | 'Active' | 'PendingReview' | 'Expired' | 'Revoked' | 'Rejected' | 'Removed';

export interface GuestWorkloadAssignment {
  id: string;
  platformTenantId: string;
  guestId: string;
  workloadId: string;
  roleId: string;
  validFrom: string;
  validUntil?: string | null;
  status: AssignmentStatus;
  updatedAt: string;
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

// Eine ScenarioResourceRule bindet genau eine WorkloadResource an ein freies Set
// fachlicher Schlüssel (z.B. Firma, Rolle) und eine optionale Bedingung — die Bedingung
// gilt nur für diese eine Regel, nicht für das ganze Szenario. Siehe
// B2B.Portal.Domain.Entities.ScenarioResourceRule.
export interface ScenarioResourceRule {
  id: string;
  workloadScenarioId: string;
  resourceId: string;
  fields: Record<string, string>;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  condition?: any | null; // rohes JSONLogic-Dokument, siehe JsonLogicEvaluator (Backend)
}

export interface WorkloadScenario {
  id: string;
  platformTenantId: string;
  workloadId: string;
  name: string;
  rules: ScenarioResourceRule[];
  active: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ScenarioTemplateRuleDto {
  resourceName: string;
  resourceType: string;
  fields: Record<string, string>;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  condition?: any | null;
}

export interface ScenarioTemplateDto {
  workloadName: string;
  scenarioName: string;
  rules: ScenarioTemplateRuleDto[];
}

export interface ScenarioImportResult {
  scenarioId: string | null;
  createdResourceNames: string[];
  errors: string[];
}
