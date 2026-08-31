import { useEffect, useState } from 'react';
import {
  Title2, Text, Card, Badge, Spinner, Button, Input, Field, makeStyles, tokens, Checkbox,
  MessageBar, MessageBarBody, Dialog, DialogTrigger, DialogSurface, DialogTitle,
  DialogBody, DialogContent, DialogActions, Select,
} from '@fluentui/react-components';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type {
  GuestAccount, MockEntraGroup, MockEntraUser, Workload, WorkloadAssignmentCounts,
  JobStatusResponse, MockEntraApplication, WorkloadResource, WorkloadRole,
} from '../types/domain';

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px' },
  section: { marginTop: '8px', display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' },
  editForm: { display: 'flex', gap: '8px', alignItems: 'flex-end', marginTop: '8px', flexWrap: 'wrap' },
  editPanel: {
    display: 'flex',
    gap: '8px',
    alignItems: 'flex-end',
    marginTop: '8px',
    padding: '10px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
    flexWrap: 'wrap',
  },
  checkboxList: { display: 'flex', gap: '8px', flexWrap: 'wrap', maxWidth: '900px' },
  validationResult: {
    display: 'flex',
    flexDirection: 'column',
    gap: '6px',
    marginTop: '8px',
    padding: '10px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
  },
  actions: { display: 'flex', gap: '8px', marginTop: '12px', flexWrap: 'wrap' },
  meta: { color: tokens.colorNeutralForeground3 },
  subheading: { marginTop: '16px', fontWeight: 600 },
});

interface PatternValidationResult {
  patterns: string[];
  matches: MockEntraGroup[];
  errors: string[];
}

interface PatternSyncState {
  jobId: string;
  status: JobStatusResponse['status'];
  lastError?: string | null;
}

/**
 * Admin/Governance-Ansicht "Workloads" (Blueprint 9): Rollen, Ressourcen, Owner —
 * editier- und löschbar. Workload-Löschen ist standardmäßig Soft-Delete (Active=false,
 * siehe WorkloadManagementService) — Assignments/Szenarien bleiben erhalten und ein
 * deaktivierter Workload ist über "Reaktivieren" wieder aktivierbar. "Endgültig löschen"
 * (Hart-Löschen) ist erst möglich, sobald keine aktiven Zuweisungen mehr existieren.
 * Rollen/Ressourcen werden hart entfernt, aber nur wenn keine aktiven Assignments bzw.
 * Rollen-/Szenario-Referenzen mehr darauf zeigen (Konsistenzprüfung im Backend, Fehler wird
 * hier angezeigt).
 */
export function WorkloadsAdminPage() {
  const styles = useStyles();
  const navigate = useNavigate();
  const [workloads, setWorkloads] = useState<Workload[] | null>(null);
  const [guests, setGuests] = useState<GuestAccount[]>([]);
  const [mockUsers, setMockUsers] = useState<MockEntraUser[]>([]);
  const [mockGroups, setMockGroups] = useState<MockEntraGroup[]>([]);
  const [mockApplications, setMockApplications] = useState<MockEntraApplication[]>([]);
  const [counts, setCounts] = useState<Record<string, WorkloadAssignmentCounts>>({});
  const [error, setError] = useState<string | null>(null);

  const [editingWorkloadId, setEditingWorkloadId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [editOwner, setEditOwner] = useState('');
  const [editAdministrativeUnit, setEditAdministrativeUnit] = useState('');
  const [editApplicationExternalId, setEditApplicationExternalId] = useState('');
  const [editPatterns, setEditPatterns] = useState('');

  const [roleForm, setRoleForm] = useState<Record<string, { name: string; mappings: string; applicationId: string; applicationRoleId: string }>>({});
  const [resourceForm, setResourceForm] = useState<Record<string, { type: string; externalId: string; displayName: string }>>({});
  const [editingRole, setEditingRole] = useState<{ workloadId: string; roleId: string; name: string; applicationId: string; applicationRoleId: string; resourceMappings: string[] } | null>(null);
  const [editingResource, setEditingResource] = useState<{ workloadId: string; resourceId: string; resourceType: string; externalId: string; displayName: string } | null>(null);
  const [attachGroupForm, setAttachGroupForm] = useState<Record<string, string>>({});
  const [patternValidation, setPatternValidation] = useState<Record<string, PatternValidationResult>>({});
  const [newWorkloadName, setNewWorkloadName] = useState('');
  const [newWorkloadOwner, setNewWorkloadOwner] = useState('');
  const [newAdministrativeUnit, setNewAdministrativeUnit] = useState('');
  const [newApplicationExternalId, setNewApplicationExternalId] = useState('');
  const [newPatterns, setNewPatterns] = useState('');
  const [assignForm, setAssignForm] = useState<Record<string, { guestId: string; roleId: string }>>({});
  const [patternSyncStatus, setPatternSyncStatus] = useState<Record<string, PatternSyncState>>({});

  const reload = () => {
    api.listWorkloads()
      .then((ws) => {
        setWorkloads(ws);
        ws.forEach((w) => {
          api.getWorkloadAssignmentCounts(w.id)
            .then((c) => setCounts((prev) => ({ ...prev, [w.id]: c })))
            .catch(() => undefined);
        });
      })
      .catch((e: Error) => setError(e.message));
    api.listGuests().then(setGuests).catch(() => setGuests([]));
    api.listMockEntraLoginUsers().then(setMockUsers).catch(() => setMockUsers([]));
    api.listMockEntraGroups().then(setMockGroups).catch(() => setMockGroups([]));
    api.listMockEntraApplications().then(setMockApplications).catch(() => setMockApplications([]));
  };

  useEffect(reload, []);

  const startEdit = (w: Workload) => {
    setEditingWorkloadId(w.id);
    setEditName(w.name);
    setEditOwner(w.owner ?? '');
    setEditAdministrativeUnit(w.administrativeUnitExternalId ?? '');
    setEditApplicationExternalId(w.applicationExternalId ?? '');
    setEditPatterns((w.resourceNamePatterns ?? []).join(', '));
  };

  const saveEdit = async (workloadId: string) => {
    setError(null);
    try {
      const result = await api.updateWorkload(workloadId, editName, editOwner || null, editAdministrativeUnit || null, editApplicationExternalId || null, splitList(editPatterns));
      setEditingWorkloadId(null);
      if (result.patternSyncJobId) {
        await pollPatternSync(workloadId, result.patternSyncJobId);
      } else {
        reload();
      }
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const pollPatternSync = async (workloadId: string, jobId: string) => {
    setPatternSyncStatus((prev) => ({ ...prev, [workloadId]: { jobId, status: 'Pending' } }));
    for (let attempt = 0; attempt < 30; attempt++) {
      await delay(1000);
      const status = await api.getJobStatus(jobId);
      setPatternSyncStatus((prev) => ({
        ...prev,
        [workloadId]: { jobId, status: status.status, lastError: status.lastError },
      }));
      if (['Success', 'Failed', 'DeadLetter'].includes(status.status)) {
        reload();
        return;
      }
    }
    reload();
  };

  const handleDeactivate = async (workloadId: string) => {
    setError(null);
    try {
      await api.deactivateWorkload(workloadId);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleReactivate = async (workloadId: string) => {
    setError(null);
    try {
      await api.reactivateWorkload(workloadId);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleDeletePermanently = async (workloadId: string) => {
    setError(null);
    try {
      await api.deleteWorkloadPermanently(workloadId);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleAddRole = async (workloadId: string) => {
    setError(null);
    const form = roleForm[workloadId];
    if (!form?.name) return;
    try {
      const mappings = form.mappings.split(',').map((s) => s.trim()).filter(Boolean);
      await api.createWorkloadRole(
        workloadId,
        form.name,
        mappings,
        form.applicationId || null,
        form.applicationRoleId || null,
      );
      setRoleForm((prev) => ({ ...prev, [workloadId]: { name: '', mappings: '', applicationId: '', applicationRoleId: '' } }));
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const validatePatternInput = (key: string, value: string) => {
    const patterns = splitList(value);
    const errors: string[] = [];
    const matches = mockGroups.filter((group) => patterns.some((pattern) => {
      try {
        return matchesPattern(group.displayName, pattern);
      } catch (e) {
        errors.push(`${pattern}: ${(e as Error).message}`);
        return false;
      }
    }));

    setPatternValidation((prev) => ({
      ...prev,
      [key]: {
        patterns,
        matches: uniqueGroups(matches),
        errors: [...new Set(errors)],
      },
    }));
  };

  const startRoleEdit = (workloadId: string, role: WorkloadRole, workloadApplicationId?: string | null) => {
    setEditingRole({
      workloadId,
      roleId: role.id,
      name: role.name,
      applicationId: role.applicationId ?? workloadApplicationId ?? '',
      applicationRoleId: role.applicationRoleId ?? '',
      resourceMappings: [...role.resourceMappings],
    });
  };

  const handleSaveRole = async () => {
    if (!editingRole?.name) return;
    setError(null);
    try {
      await api.updateWorkloadRole(
        editingRole.workloadId,
        editingRole.roleId,
        editingRole.name,
        editingRole.resourceMappings,
        editingRole.applicationId || null,
        editingRole.applicationRoleId || null,
      );
      setEditingRole(null);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleDeleteRole = async (workloadId: string, roleId: string) => {
    setError(null);
    try {
      await api.deleteWorkloadRole(workloadId, roleId);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleAddResource = async (workloadId: string) => {
    setError(null);
    const form = resourceForm[workloadId];
    if (!form?.type) return;
    try {
      await api.createWorkloadResource(workloadId, form.type, form.externalId || null, form.displayName || null);
      setResourceForm((prev) => ({ ...prev, [workloadId]: { type: '', externalId: '', displayName: '' } }));
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const startResourceEdit = (workloadId: string, resource: WorkloadResource) => {
    setEditingResource({
      workloadId,
      resourceId: resource.id,
      resourceType: resource.resourceType,
      externalId: resource.externalId ?? '',
      displayName: resource.displayName ?? '',
    });
  };

  const handleSaveResource = async () => {
    if (!editingResource?.resourceType) return;
    setError(null);
    try {
      await api.updateWorkloadResource(
        editingResource.workloadId,
        editingResource.resourceId,
        editingResource.resourceType,
        editingResource.externalId || null,
        editingResource.displayName || null,
      );
      setEditingResource(null);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleAttachGroup = async (workloadId: string) => {
    const groupId = attachGroupForm[workloadId];
    const group = mockGroups.find((g) => g.objectId === groupId);
    if (!group) return;
    setError(null);
    try {
      // externalId ist immer die stabile Entra-Object-ID (group.objectId), niemals der
      // Anzeigename — der wandert separat als displayName mit (siehe WorkloadResource-Typ).
      await api.attachWorkloadResource(
        workloadId,
        group.resourceProvisioningOptions.includes('Team') ? 'Team' : group.groupTypes.includes('Unified') ? 'M365Group' : 'SecurityGroup',
        group.objectId,
        group.displayName,
      );
      setAttachGroupForm((prev) => ({ ...prev, [workloadId]: '' }));
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleCreateWorkload = async () => {
    if (!newWorkloadName) return;
    setError(null);
    try {
      const result = await api.createWorkload(newWorkloadName, newWorkloadOwner || null, null, false, newAdministrativeUnit || null, newApplicationExternalId || null, splitList(newPatterns));
      setNewWorkloadName('');
      setNewWorkloadOwner('');
      setNewAdministrativeUnit('');
      setNewApplicationExternalId('');
      setNewPatterns('');
      if (result.patternSyncJobId) {
        await pollPatternSync(result.workload.id, result.patternSyncJobId);
      } else {
        reload();
      }
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleAssignGuest = async (workloadId: string) => {
    const form = assignForm[workloadId];
    if (!form?.guestId || !form.roleId) return;
    setError(null);
    try {
      await api.grantWorkloadRole(workloadId, form.guestId, form.roleId);
      setAssignForm((prev) => ({ ...prev, [workloadId]: { guestId: '', roleId: '' } }));
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleDeleteResource = async (workloadId: string, resourceId: string) => {
    setError(null);
    try {
      await api.deleteWorkloadResource(workloadId, resourceId);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  if (error && !workloads) return <Text>Fehler: {error}</Text>;
  if (!workloads) return <Spinner label="Lade Workloads…" />;
  const memberUsers = mockUsers.filter((user) => user.userType === 'Member');

  return (
    <div>
      <Title2>Workloads</Title2>
      <Text>Fachliche Zugriffskontexte dieses Platform-Tenants (Blueprint 6.1).</Text>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      <Card className={styles.card} style={{ marginTop: 16 }}>
        <Text weight="semibold" block>Workload erstellen</Text>
        <div className={styles.editForm}>
          <Field label="Name">
            <Input value={newWorkloadName} onChange={(_, d) => setNewWorkloadName(d.value)} />
          </Field>
          <Field label="Owner">
            <Select value={newWorkloadOwner} onChange={(event) => setNewWorkloadOwner(event.target.value)}>
              <option value="">Owner auswählen</option>
              {memberUsers.map((user) => <option key={user.objectId} value={user.mail}>{user.displayName} ({user.mail})</option>)}
            </Select>
          </Field>
          <Field label="Administrative Unit">
            <Input value={newAdministrativeUnit} onChange={(_, d) => setNewAdministrativeUnit(d.value)} placeholder="z.B. AU-FSM-APPS" />
          </Field>
          <Field label="Application">
            <Select value={newApplicationExternalId} onChange={(event) => setNewApplicationExternalId(event.target.value)}>
              <option value="">Keine Application</option>
              {mockApplications.map((application) => (
                <option key={application.objectId} value={application.appId}>{application.displayName} ({application.appId})</option>
              ))}
            </Select>
          </Field>
          <Field label="Gruppen-Pattern">
            <Input value={newPatterns} onChange={(_, d) => setNewPatterns(d.value)} placeholder="FSM APP*, regex:^FSM APP.* - TEST$" />
          </Field>
          <Button appearance="secondary" onClick={() => validatePatternInput('new', newPatterns)} disabled={!newPatterns}>
            Pattern prüfen
          </Button>
          <Button appearance="primary" onClick={handleCreateWorkload} disabled={!newWorkloadName}>
            Erstellen
          </Button>
        </div>
        {patternValidation.new && (
          <PatternValidationView result={patternValidation.new} styles={styles} />
        )}
      </Card>

      <div className={styles.list}>
        {workloads.length === 0 && <Text>Noch keine Workloads angelegt.</Text>}
        {workloads.map((w) => (
          <Card key={w.id} className={styles.card}>
            {editingWorkloadId === w.id ? (
              <div className={styles.editForm}>
                <Field label="Name">
                  <Input value={editName} onChange={(_, d) => setEditName(d.value)} />
                </Field>
                <Field label="Owner">
                  <Select value={editOwner} onChange={(event) => setEditOwner(event.target.value)}>
                    <option value="">Owner auswählen</option>
                    {memberUsers.map((user) => <option key={user.objectId} value={user.mail}>{user.displayName} ({user.mail})</option>)}
                  </Select>
                </Field>
                <Field label="Administrative Unit">
                  <Input value={editAdministrativeUnit} onChange={(_, d) => setEditAdministrativeUnit(d.value)} />
                </Field>
                <Field label="Application">
                  <Select value={editApplicationExternalId} onChange={(event) => setEditApplicationExternalId(event.target.value)}>
                    <option value="">Keine Application</option>
                    {mockApplications.map((application) => (
                      <option key={application.objectId} value={application.appId}>{application.displayName} ({application.appId})</option>
                    ))}
                  </Select>
                </Field>
                <Field label="Gruppen-Pattern">
                  <Input value={editPatterns} onChange={(_, d) => setEditPatterns(d.value)} />
                </Field>
                <Button appearance="secondary" size="small" onClick={() => validatePatternInput(w.id, editPatterns)} disabled={!editPatterns}>
                  Pattern prüfen
                </Button>
                <Button appearance="primary" size="small" onClick={() => saveEdit(w.id)}>Speichern</Button>
                <Button size="small" onClick={() => setEditingWorkloadId(null)}>Abbrechen</Button>
                {patternValidation[w.id] && (
                  <PatternValidationView result={patternValidation[w.id]} styles={styles} />
                )}
              </div>
            ) : (
              <>
                <Text weight="semibold">{w.name}</Text>
                {!w.active && <Badge appearance="tint" color="danger" style={{ marginLeft: 8 }}>Inaktiv</Badge>}
                {w.owner && <Text className={styles.meta} block size={200}>Owner: {w.owner}</Text>}
                {w.isDefault && <Badge appearance="tint" color="warning">Default-Anker, keine Szenarien</Badge>}
                {w.administrativeUnitExternalId && <Text className={styles.meta} block size={200}>Administrative Unit: {w.administrativeUnitExternalId}</Text>}
                {w.applicationExternalId && <Text className={styles.meta} block size={200}>Application: {applicationName(w.applicationExternalId, mockApplications)}</Text>}
                {(w.resourceNamePatterns ?? []).length > 0 && (
                  <div className={styles.section}>
                    <Text className={styles.meta} size={200}>Pattern: {w.resourceNamePatterns.join(', ')}</Text>
                    <Button size="small" appearance="secondary" onClick={() => startEdit(w)}>Pattern bearbeiten</Button>
                    <Button size="small" appearance="secondary" onClick={() => validatePatternInput(w.id, w.resourceNamePatterns.join(', '))}>
                      Pattern prüfen
                    </Button>
                  </div>
                )}
                {patternValidation[w.id] && (
                  <PatternValidationView result={patternValidation[w.id]} styles={styles} />
                )}
                {patternSyncStatus[w.id] && (
                  <PatternSyncStatusView state={patternSyncStatus[w.id]} />
                )}
                <Text className={styles.meta} block size={200} title="Aktiv/Inaktiv basieren auf der tatsächlichen Gruppenmitgliedschaft im Verzeichnis, nicht nur auf formalen Zuweisungen.">
                  Nutzer: {counts[w.id] ? (
                    <>Aktiv {counts[w.id].active} / Inaktiv {counts[w.id].inactive}</>
                  ) : '…'}
                </Text>
                {counts[w.id]?.sharedWith != null && counts[w.id]!.sharedWith!.length > 0 && (
                  <Text className={styles.meta} block size={200} style={{ color: tokens.colorPaletteMarigoldForeground1 }}>
                    Ressource(n) geteilt mit: {counts[w.id]!.sharedWith!.map((s) => `${s.resourceDisplayName} → ${s.otherWorkloadNames.join(', ')}`).join('; ')}
                    {' '}— manche gezählten Nutzer stammen ggf. aus diesen Workloads, siehe{' '}
                    <Button appearance="transparent" size="small" onClick={() => navigate('/reviews')}>Reviews</Button>
                  </Text>
                )}
              </>
            )}

            <div className={styles.section}>
              {w.roles.map((r) => (
                <Badge key={r.id} appearance="tint" color="brand">
                  Rolle: {r.name}{r.applicationRoleId ? ` (${applicationRoleName(r.applicationId ?? w.applicationExternalId, r.applicationRoleId, mockApplications)})` : ''}
                  <Button
                    appearance="transparent"
                    size="small"
                    style={{ minWidth: 0, padding: '0 4px', marginLeft: 4 }}
                    onClick={() => startRoleEdit(w.id, r, w.applicationExternalId)}
                  >
                    Bearbeiten
                  </Button>
                  <Button
                    appearance="transparent"
                    size="small"
                    style={{ minWidth: 0, padding: '0 4px', marginLeft: 4 }}
                    onClick={() => handleDeleteRole(w.id, r.id)}
                  >
                    ×
                  </Button>
                </Badge>
              ))}
              {w.resources.map((res) => (
                <Badge key={res.id} appearance="outline" color={res.managed ? 'success' : 'warning'}>
                  {res.resourceType}:{res.displayName ?? res.externalId ?? res.id}{resourceMatchesWorkloadPattern(res, w) && ' (Pattern-Treffer)'}{!res.managed && ' (discovered)'}
                  <Button
                    appearance="transparent"
                    size="small"
                    style={{ minWidth: 0, padding: '0 4px', marginLeft: 4 }}
                    onClick={() => startResourceEdit(w.id, res)}
                  >
                    Bearbeiten
                  </Button>
                  <Button
                    appearance="transparent"
                    size="small"
                    style={{ minWidth: 0, padding: '0 4px', marginLeft: 4 }}
                    onClick={() => handleDeleteResource(w.id, res.id)}
                  >
                    ×
                  </Button>
                </Badge>
              ))}
            </div>

            {editingRole?.workloadId === w.id && (
              <div className={styles.editPanel}>
                <Field label="Rollenname">
                  <Input
                    value={editingRole.name}
                    onChange={(_, d) => setEditingRole((prev) => prev ? { ...prev, name: d.value } : prev)}
                  />
                </Field>
                {w.applicationExternalId && (
                  <>
                    <Field label="Application">
                      <Select
                        value={editingRole.applicationId || w.applicationExternalId}
                        onChange={(event) => setEditingRole((prev) => prev ? { ...prev, applicationId: event.target.value, applicationRoleId: '' } : prev)}
                      >
                        {mockApplications.map((application) => (
                          <option key={application.objectId} value={application.appId}>{application.displayName}</option>
                        ))}
                      </Select>
                    </Field>
                    <Field label="App-Rolle">
                      <Select
                        value={editingRole.applicationRoleId}
                        onChange={(event) => setEditingRole((prev) => prev ? { ...prev, applicationRoleId: event.target.value } : prev)}
                      >
                        <option value="">App-Rolle auswählen</option>
                        {applicationRoles(editingRole.applicationId || w.applicationExternalId, mockApplications).map((role) => (
                          <option key={role.id} value={role.id}>{role.displayName} ({role.value})</option>
                        ))}
                      </Select>
                    </Field>
                  </>
                )}
                <Field label="Zugeordnete Ressourcen">
                  <div className={styles.checkboxList}>
                    {w.resources.length === 0 && <Text size={200}>Keine Ressourcen am Workload.</Text>}
                    {w.resources.map((resource) => (
                      <Checkbox
                        key={resource.id}
                        label={`${resource.resourceType}:${resource.displayName ?? resource.externalId ?? resource.id}`}
                        checked={editingRole.resourceMappings.includes(resource.id)}
                        onChange={(_, data) => setEditingRole((prev) => {
                          if (!prev) return prev;
                          const next = data.checked
                            ? [...new Set([...prev.resourceMappings, resource.id])]
                            : prev.resourceMappings.filter((id) => id !== resource.id);
                          return { ...prev, resourceMappings: next };
                        })}
                      />
                    ))}
                  </div>
                </Field>
                <Button appearance="primary" size="small" onClick={handleSaveRole} disabled={!editingRole.name}>Speichern</Button>
                <Button size="small" onClick={() => setEditingRole(null)}>Abbrechen</Button>
              </div>
            )}

            {editingResource?.workloadId === w.id && (
              <div className={styles.editPanel}>
                <Field label="ResourceType">
                  <Input
                    value={editingResource.resourceType}
                    onChange={(_, d) => setEditingResource((prev) => prev ? { ...prev, resourceType: d.value } : prev)}
                  />
                </Field>
                <Field label="ExternalId (Entra Object ID)">
                  <Input
                    value={editingResource.externalId}
                    onChange={(_, d) => setEditingResource((prev) => prev ? { ...prev, externalId: d.value } : prev)}
                  />
                </Field>
                <Field label="Anzeigename">
                  <Input
                    value={editingResource.displayName}
                    onChange={(_, d) => setEditingResource((prev) => prev ? { ...prev, displayName: d.value } : prev)}
                  />
                </Field>
                <Button appearance="primary" size="small" onClick={handleSaveResource} disabled={!editingResource.resourceType}>Speichern</Button>
                <Button size="small" onClick={() => setEditingResource(null)}>Abbrechen</Button>
              </div>
            )}

            <Text className={styles.subheading} block size={200}>Rolle hinzufügen</Text>
            <div className={styles.editForm}>
              <Input
                placeholder="Rollenname"
                value={roleForm[w.id]?.name ?? ''}
                onChange={(_, d) => setRoleForm((prev) => ({
                  ...prev,
                  [w.id]: {
                    name: d.value,
                    mappings: prev[w.id]?.mappings ?? '',
                    applicationId: prev[w.id]?.applicationId ?? w.applicationExternalId ?? '',
                    applicationRoleId: prev[w.id]?.applicationRoleId ?? '',
                  },
                }))}
              />
              {w.applicationExternalId && (
                <Select
                  value={roleForm[w.id]?.applicationRoleId ?? ''}
                  onChange={(event) => setRoleForm((prev) => ({
                    ...prev,
                    [w.id]: {
                      name: prev[w.id]?.name ?? '',
                      mappings: prev[w.id]?.mappings ?? '',
                      applicationId: w.applicationExternalId ?? '',
                      applicationRoleId: event.target.value,
                    },
                  }))}
                >
                  <option value="">App-Rolle auswählen</option>
                  {applicationRoles(w.applicationExternalId, mockApplications).map((role) => (
                    <option key={role.id} value={role.id}>{role.displayName} ({role.value})</option>
                  ))}
                </Select>
              )}
              <Input
                placeholder="Ressourcen-IDs (Komma-getrennt, optional)"
                value={roleForm[w.id]?.mappings ?? ''}
                onChange={(_, d) => setRoleForm((prev) => ({
                  ...prev,
                  [w.id]: {
                    name: prev[w.id]?.name ?? '',
                    mappings: d.value,
                    applicationId: prev[w.id]?.applicationId ?? w.applicationExternalId ?? '',
                    applicationRoleId: prev[w.id]?.applicationRoleId ?? '',
                  },
                }))}
              />
              <Button size="small" onClick={() => handleAddRole(w.id)}>Hinzufügen</Button>
            </div>

            <Text className={styles.subheading} block size={200}>Gruppe aus Mock Entra anhängen</Text>
            <div className={styles.editForm}>
              <Select
                value={attachGroupForm[w.id] ?? ''}
                onChange={(event) => setAttachGroupForm((prev) => ({ ...prev, [w.id]: event.target.value }))}
              >
                <option value="">Gruppe auswählen</option>
                {mockGroups.map((group) => (
                  <option key={group.objectId} value={group.objectId}>{group.displayName}</option>
                ))}
              </Select>
              <Button size="small" onClick={() => handleAttachGroup(w.id)} disabled={!attachGroupForm[w.id]}>
                Anhängen
              </Button>
            </div>

            <Text className={styles.subheading} block size={200}>Ressource hinzufügen</Text>
            <div className={styles.editForm}>
              <Input
                placeholder="ResourceType (z.B. SecurityGroup)"
                value={resourceForm[w.id]?.type ?? ''}
                onChange={(_, d) => setResourceForm((prev) => ({ ...prev, [w.id]: { type: d.value, externalId: prev[w.id]?.externalId ?? '', displayName: prev[w.id]?.displayName ?? '' } }))}
              />
              <Input
                placeholder="ExternalId / Entra Object ID (optional)"
                value={resourceForm[w.id]?.externalId ?? ''}
                onChange={(_, d) => setResourceForm((prev) => ({ ...prev, [w.id]: { type: prev[w.id]?.type ?? '', externalId: d.value, displayName: prev[w.id]?.displayName ?? '' } }))}
              />
              <Input
                placeholder="Anzeigename (optional)"
                value={resourceForm[w.id]?.displayName ?? ''}
                onChange={(_, d) => setResourceForm((prev) => ({ ...prev, [w.id]: { type: prev[w.id]?.type ?? '', externalId: prev[w.id]?.externalId ?? '', displayName: d.value } }))}
              />
              <Button size="small" onClick={() => handleAddResource(w.id)}>Hinzufügen</Button>
            </div>

            <Text className={styles.subheading} block size={200}>Gast zuweisen</Text>
            <div className={styles.editForm}>
              <Field label="Gast">
                <Select
                  value={assignForm[w.id]?.guestId ?? ''}
                  onChange={(event) => setAssignForm((prev) => ({
                    ...prev,
                    [w.id]: { guestId: event.target.value, roleId: prev[w.id]?.roleId ?? '' },
                  }))}
                >
                  <option value="">Gast auswählen</option>
                  {guests.map((guest) => <option key={guest.id} value={guest.id}>{guest.displayName} ({guest.mail})</option>)}
                </Select>
              </Field>
              <Field label="Rolle">
                <Select
                  value={assignForm[w.id]?.roleId ?? ''}
                  onChange={(event) => setAssignForm((prev) => ({
                    ...prev,
                    [w.id]: { guestId: prev[w.id]?.guestId ?? '', roleId: event.target.value },
                  }))}
                >
                  <option value="">Rolle auswählen</option>
                  {w.roles.map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}
                </Select>
              </Field>
              <Button
                size="small"
                onClick={() => handleAssignGuest(w.id)}
                disabled={!assignForm[w.id]?.guestId || !assignForm[w.id]?.roleId}
              >
                Zuweisen
              </Button>
            </div>

            <div className={styles.actions}>
              <Button appearance="secondary" size="small" disabled={w.isDefault} onClick={() => navigate(`/workloads/${w.id}/scenarios`)}>
                Scenarios
              </Button>
              <Button appearance="secondary" size="small" onClick={() => navigate(`/workloads/${w.id}`)}>
                Details
              </Button>
              {editingWorkloadId !== w.id && (
                <Button size="small" onClick={() => startEdit(w)}>Bearbeiten</Button>
              )}
              {w.active ? (
                <Dialog>
                  <DialogTrigger disableButtonEnhancement>
                    <Button size="small" appearance="secondary">Deaktivieren</Button>
                  </DialogTrigger>
                  <DialogSurface>
                    <DialogBody>
                      <DialogTitle>Workload deaktivieren?</DialogTitle>
                      <DialogContent>
                        "{w.name}" wird deaktiviert (Active=false) und verschwindet aus aktiven Listen.
                        Zuweisungen und Szenarien bleiben erhalten.
                      </DialogContent>
                      <DialogActions>
                        <DialogTrigger disableButtonEnhancement>
                          <Button appearance="secondary">Abbrechen</Button>
                        </DialogTrigger>
                        <DialogTrigger disableButtonEnhancement>
                          <Button appearance="primary" onClick={() => handleDeactivate(w.id)}>Deaktivieren</Button>
                        </DialogTrigger>
                      </DialogActions>
                    </DialogBody>
                  </DialogSurface>
                </Dialog>
              ) : (
                <Button size="small" appearance="secondary" onClick={() => handleReactivate(w.id)}>
                  Reaktivieren
                </Button>
              )}
              <Dialog>
                <DialogTrigger disableButtonEnhancement>
                  <Button size="small" appearance="secondary" disabled={(counts[w.id]?.active ?? 1) > 0}>
                    Endgültig löschen
                  </Button>
                </DialogTrigger>
                <DialogSurface>
                  <DialogBody>
                    <DialogTitle>Workload endgültig löschen?</DialogTitle>
                    <DialogContent>
                      "{w.name}" wird unwiderruflich gelöscht, inklusive aller Szenarien und der
                      historischen (inaktiven) Zuweisungen. Diese Aktion kann nicht rückgängig
                      gemacht werden.
                    </DialogContent>
                    <DialogActions>
                      <DialogTrigger disableButtonEnhancement>
                        <Button appearance="secondary">Abbrechen</Button>
                      </DialogTrigger>
                      <DialogTrigger disableButtonEnhancement>
                        <Button appearance="primary" onClick={() => handleDeletePermanently(w.id)}>
                          Endgültig löschen
                        </Button>
                      </DialogTrigger>
                    </DialogActions>
                  </DialogBody>
                </DialogSurface>
              </Dialog>
            </div>
          </Card>
        ))}
      </div>
    </div>
  );
}

function splitList(value: string): string[] {
  return value.split(/[,\n;]/).map((item) => item.trim()).filter(Boolean);
}

function PatternValidationView({
  result,
  styles,
}: {
  result: PatternValidationResult;
  styles: ReturnType<typeof useStyles>;
}) {
  return (
    <div className={styles.validationResult}>
      <Text size={200}>
        Pattern: {result.patterns.length === 0 ? 'keine' : result.patterns.join(', ')}
      </Text>
      {result.errors.length > 0 && (
        <MessageBar intent="error">
          <MessageBarBody>{result.errors.join('; ')}</MessageBarBody>
        </MessageBar>
      )}
      <Text size={200} weight="semibold">Treffer: {result.matches.length}</Text>
      <div className={styles.section}>
        {result.matches.length === 0 && <Text size={200}>Keine Mock-Entra-Gruppen gefunden.</Text>}
        {result.matches.map((group) => (
          <Badge key={group.objectId} appearance="outline" color="success">
            {group.displayName}
          </Badge>
        ))}
      </div>
    </div>
  );
}

function PatternSyncStatusView({ state }: { state: PatternSyncState }) {
  const running = ['Pending', 'Running', 'Retry'].includes(state.status);
  return (
    <MessageBar intent={state.status === 'Success' ? 'success' : state.status === 'DeadLetter' || state.status === 'Failed' ? 'error' : 'info'} style={{ marginTop: 8 }}>
      <MessageBarBody>
        {running && <Spinner size="tiny" style={{ marginRight: 8 }} />}
        Pattern-Sync: {state.status} ({state.jobId})
        {state.lastError ? ` - ${state.lastError}` : ''}
      </MessageBarBody>
    </MessageBar>
  );
}

function uniqueGroups(groups: MockEntraGroup[]): MockEntraGroup[] {
  const seen = new Set<string>();
  return groups.filter((group) => {
    if (seen.has(group.objectId)) return false;
    seen.add(group.objectId);
    return true;
  });
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}

function matchesPattern(value: string, pattern: string): boolean {
  if (pattern.toLowerCase().startsWith('regex:')) {
    return new RegExp(pattern.slice('regex:'.length), 'i').test(value);
  }

  if (pattern.length >= 2 && pattern.startsWith('/') && pattern.endsWith('/')) {
    return new RegExp(pattern.slice(1, -1), 'i').test(value);
  }

  const expression = `^${escapeRegex(pattern).replace(/\\\*/g, '.*').replace(/\\\?/g, '.')}$`;
  return new RegExp(expression, 'i').test(value);
}

function escapeRegex(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function applicationName(appId: string, applications: MockEntraApplication[]): string {
  const application = applications.find((item) => item.appId === appId || item.objectId === appId);
  return application ? `${application.displayName} (${application.appId})` : appId;
}

function applicationRoles(appId: string, applications: MockEntraApplication[]) {
  return applications.find((item) => item.appId === appId || item.objectId === appId)?.appRoles ?? [];
}

function applicationRoleName(appId: string | null | undefined, roleId: string, applications: MockEntraApplication[]): string {
  const role = applicationRoles(appId ?? '', applications).find((item) => item.id === roleId || item.value === roleId);
  return role ? role.displayName : roleId;
}

function resourceMatchesWorkloadPattern(resource: WorkloadResource, workload: Workload): boolean {
  // Patterns werden von Admins gegen Anzeigenamen geschrieben (z.B. "SG-MERIDIAN-*"), nie
  // gegen die opake Object-ID — daher Abgleich gegen displayName, nicht externalId.
  if (!resource.displayName) return false;
  return (workload.resourceNamePatterns ?? []).some((pattern) => {
    try {
      return matchesPattern(resource.displayName!, pattern);
    } catch {
      return false;
    }
  });
}
