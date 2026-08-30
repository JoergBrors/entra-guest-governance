import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Title2, Text, Badge, Input, Button, Field, Select, Spinner, makeStyles, MessageBar, MessageBarBody,
} from '@fluentui/react-components';
import { api } from '../api/client';
import { InvitationGuestList } from '../components/InvitationGuestList';
import type {
  GuestAccount, GuestAccountState, DeletionGateEvaluation, GuestWorkloadAssignment, Workload, WorkloadScenario,
} from '../types/domain';

const useStyles = makeStyles({
  form: {
    display: 'flex',
    gap: '12px',
    alignItems: 'flex-end',
    margin: '16px 0 24px',
  },
  filterBar: {
    display: 'flex',
    gap: '12px',
    alignItems: 'flex-end',
    margin: '0 0 16px',
    flexWrap: 'wrap',
  },
  assignmentRow: { display: 'flex', gap: '6px', flexWrap: 'wrap', alignItems: 'center', marginBottom: '4px' },
});

const ACCOUNT_STATES: GuestAccountState[] = [
  'Discovered', 'Invited', 'Active', 'Inactive', 'Blocked', 'OrphanCandidate', 'PendingRemoval', 'Disabled', 'Deleted',
];

const assignmentStatusColor: Record<string, 'success' | 'warning' | 'danger' | 'informative'> = {
  Active: 'success',
  Approved: 'success',
  Requested: 'informative',
  PendingReview: 'warning',
  Expired: 'warning',
  Revoked: 'danger',
  Rejected: 'danger',
  Removed: 'danger',
};

const activeAssignmentStatuses = new Set(['Active', 'Approved', 'Requested']);

/**
 * Admin/Governance-Ansicht "Guest Pool" (Blueprint 9): Suchen, Firma, Sponsor,
 * Attribute, Workloads, Actual Access, Review-/Invitation-Historie. Zeigt pro Gast alle
 * Workload-Zuweisungen (ein Gast kann mehrere Workloads haben) inkl. Unassign-Aktion —
 * notwendig, damit ein Workload durch Unassign aller Gäste löschbar wird (siehe
 * WorkloadsAdminPage "Endgültig löschen", nur bei 0 aktiven Zuweisungen erlaubt).
 */
export function GuestPoolPage() {
  const styles = useStyles();
  const [guests, setGuests] = useState<GuestAccount[] | null>(null);
  const [workloads, setWorkloads] = useState<Workload[]>([]);
  const [scenarios, setScenarios] = useState<WorkloadScenario[]>([]);
  const [assignments, setAssignments] = useState<Record<string, GuestWorkloadAssignment[]>>({});
  const [mail, setMail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [gateResult, setGateResult] = useState<Record<string, DeletionGateEvaluation>>({});

  // Erweiterung 2026-08-30 "Guest Pool Filter": Workload/Szenario/Status/Einladungsstatus.
  // Szenario-Auswahl ist auf den gewaehlten Workload beschraenkt (siehe onChange workloadFilter
  // unten) — ein Szenario ohne Workload ergibt keinen Sinn (Szenario haengt fachlich an genau
  // einem Workload, siehe WorkloadScenario.WorkloadId).
  const [workloadFilter, setWorkloadFilter] = useState('');
  const [scenarioFilter, setScenarioFilter] = useState('');
  const [stateFilter, setStateFilter] = useState('');
  const [invitationFilter, setInvitationFilter] = useState('');

  const reload = () => {
    api.listGuests({
      workloadId: workloadFilter || undefined,
      scenarioId: scenarioFilter || undefined,
      accountState: (stateFilter || undefined) as GuestAccountState | undefined,
      invitationStatus: (invitationFilter || undefined) as 'accepted' | 'pending' | undefined,
    }).then((gs) => {
      setGuests(gs);
      gs.forEach((g) => {
        api.listGuestAssignments(g.id)
          .then((a) => setAssignments((prev) => ({ ...prev, [g.id]: a })))
          .catch(() => undefined);
      });
    }).catch((e: Error) => setError(e.message));
    api.listWorkloads().then(setWorkloads).catch(() => undefined);
  };

  useEffect(() => {
    reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [workloadFilter, scenarioFilter, stateFilter, invitationFilter]);

  useEffect(() => {
    if (!workloadFilter) {
      setScenarios([]);
      setScenarioFilter('');
      return;
    }
    api.listScenarios(workloadFilter).then(setScenarios).catch(() => setScenarios([]));
  }, [workloadFilter]);

  const workloadName = (workloadId: string) => workloads.find((w) => w.id === workloadId)?.name ?? workloadId;
  const roleName = (workloadId: string, roleId: string) =>
    workloads.find((w) => w.id === workloadId)?.roles.find((r) => r.id === roleId)?.name ?? roleId;

  const handleInvite = async () => {
    if (!mail || !displayName) return;
    setBusy(true);
    setError(null);
    try {
      await api.inviteGuest(mail, displayName);
      setMail('');
      setDisplayName('');
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const handleValidateDeletion = async (guestId: string) => {
    try {
      const evaluation = await api.validateDeletion(guestId, /* gracePeriodReached */ true);
      setGateResult((prev) => ({ ...prev, [guestId]: evaluation }));
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleUnassign = async (guestId: string, assignmentId: string) => {
    setError(null);
    try {
      await api.revokeAssignment(assignmentId);
      const updated = await api.listGuestAssignments(guestId);
      setAssignments((prev) => ({ ...prev, [guestId]: updated }));
    } catch (e) {
      setError((e as Error).message);
    }
  };

  return (
    <div>
      <Title2>Guest Pool</Title2>
      <Text>Zentrale Gastidentitäten dieses Platform-Tenants (Blueprint 5.1).</Text>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.form}>
        <Field label="E-Mail">
          <Input value={mail} onChange={(_, d) => setMail(d.value)} placeholder="gast@firma.example" />
        </Field>
        <Field label="Anzeigename">
          <Input value={displayName} onChange={(_, d) => setDisplayName(d.value)} placeholder="Vorname Nachname" />
        </Field>
        <Button appearance="primary" disabled={busy || !mail || !displayName} onClick={handleInvite}>
          Gast einladen
        </Button>
      </div>

      <div className={styles.filterBar}>
        <Field label="Workload">
          <Select value={workloadFilter} onChange={(_, d) => setWorkloadFilter(d.value)}>
            <option value="">Alle Workloads</option>
            {workloads.map((w) => (
              <option key={w.id} value={w.id}>{w.name}</option>
            ))}
          </Select>
        </Field>
        <Field label="Szenario">
          <Select value={scenarioFilter} onChange={(_, d) => setScenarioFilter(d.value)} disabled={!workloadFilter}>
            <option value="">Alle Szenarien</option>
            {scenarios.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </Select>
        </Field>
        <Field label="Status">
          <Select value={stateFilter} onChange={(_, d) => setStateFilter(d.value)}>
            <option value="">Alle Status</option>
            {ACCOUNT_STATES.map((s) => (
              <option key={s} value={s}>{s}</option>
            ))}
          </Select>
        </Field>
        <Field label="Einladung">
          <Select value={invitationFilter} onChange={(_, d) => setInvitationFilter(d.value)}>
            <option value="">Alle</option>
            <option value="pending">Ausstehend</option>
            <option value="accepted">Angenommen</option>
          </Select>
        </Field>
      </div>

      {!guests ? (
        <Spinner label="Lade Gäste…" />
      ) : (
        <InvitationGuestList
          guests={guests}
          onGuestUpdated={reload}
          nameLink={(g) => <Link to={`/guest-pool/${g.id}`}>{g.displayName}</Link>}
          extraHeaderCells={['UserType', 'Workloads', 'Deletion Gate (Dry Run)']}
          renderExtraCells={(g) => [
            <Badge key="userType">{g.userType}</Badge>,
            <div key="workloads">
              {(assignments[g.id] ?? []).length === 0 && <Text size={200}>Keine Workloads</Text>}
              {(assignments[g.id] ?? []).map((a) => (
                <div key={a.id} className={styles.assignmentRow}>
                  <Badge color={assignmentStatusColor[a.status] ?? 'informative'}>
                    {workloadName(a.workloadId)} · {roleName(a.workloadId, a.roleId)} ({a.status})
                  </Badge>
                  {activeAssignmentStatuses.has(a.status) && (
                    <Button size="small" appearance="transparent" onClick={() => handleUnassign(g.id, a.id)}>
                      Unassign
                    </Button>
                  )}
                </div>
              ))}
            </div>,
            <div key="deletionGate">
              <Button size="small" onClick={() => handleValidateDeletion(g.id)}>
                Prüfen
              </Button>
              {gateResult[g.id] && (
                <div style={{ marginTop: 4 }}>
                  <Badge color={gateResult[g.id].result === 'Ready' ? 'success' : 'danger'}>
                    {gateResult[g.id].result}
                  </Badge>
                  {gateResult[g.id].blockers.length > 0 && (
                    <Text size={200} block>
                      Blocker: {gateResult[g.id].blockers.join(', ')}
                    </Text>
                  )}
                </div>
              )}
            </div>,
          ]}
        />
      )}
    </div>
  );
}
