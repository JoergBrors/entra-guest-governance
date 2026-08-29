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
  reason?: string | null;
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

// ---- Excel-Gäste-Import (Phase 4) ------------------------------------------

/** Reservierte Zielschlüssel im Spalten-Mapping — jeder andere Wert wird als freier
 * ScenarioResourceRule.Fields-Schlüssel behandelt (siehe GuestImportReservedFields,
 * Backend). */
export const GUEST_IMPORT_RESERVED_FIELDS = ['Mail', 'DisplayName', 'Workload', 'Szenario'] as const;

export interface GuestImportInspectResult {
  sheetNames: string[];
  columnHeaders: string[];
}

export interface GuestImportColumnMapping {
  sheetName: string;
  headerRowIndex: number;
  dataStartColumnIndex: number;
  /** Spalten-Offset (0-basiert, ab dataStartColumnIndex) -> Zielschlüssel. */
  columnToField: Record<number, string>;
}

export interface GuestImportRowWarning {
  message: string;
}

export interface GuestImportForeignWorkloadImpact {
  workloadId: string;
  workloadName: string;
  assignmentId: string;
  reason: string;
}

export interface GuestImportRowResult {
  rowNumber: number;
  mail: string;
  displayName: string;
  isNewGuest: boolean;
  dataChanged: boolean;
  matchedRoleNames: string[];
  warnings: GuestImportRowWarning[];
  foreignWorkloadImpacts: GuestImportForeignWorkloadImpact[];
}

export interface GuestImportResult {
  rows: GuestImportRowResult[];
  newGuestCount: number;
  updatedGuestCount: number;
  assignmentCount: number;
  warningCount: number;
}

export interface UiConfiguration {
  platformTenantId?: string | null;
  themeId: string;
  branding: {
    productName: string;
  };
  user: {
    mail: string;
    roles: string[];
  };
}
