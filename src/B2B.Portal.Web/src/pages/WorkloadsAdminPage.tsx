import { useEffect, useState } from 'react';
import {
  Title2, Text, Card, Badge, Spinner, Button, Input, Field, makeStyles, tokens,
  MessageBar, MessageBarBody, Dialog, DialogTrigger, DialogSurface, DialogTitle,
  DialogBody, DialogContent, DialogActions,
} from '@fluentui/react-components';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { Workload, WorkloadAssignmentCounts } from '../types/domain';

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px' },
  section: { marginTop: '8px', display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' },
  editForm: { display: 'flex', gap: '8px', alignItems: 'flex-end', marginTop: '8px', flexWrap: 'wrap' },
  actions: { display: 'flex', gap: '8px', marginTop: '12px', flexWrap: 'wrap' },
  meta: { color: tokens.colorNeutralForeground3 },
  subheading: { marginTop: '16px', fontWeight: 600 },
});

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
  const [counts, setCounts] = useState<Record<string, WorkloadAssignmentCounts>>({});
  const [error, setError] = useState<string | null>(null);

  const [editingWorkloadId, setEditingWorkloadId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [editOwner, setEditOwner] = useState('');

  const [roleForm, setRoleForm] = useState<Record<string, { name: string; mappings: string }>>({});
  const [resourceForm, setResourceForm] = useState<Record<string, { type: string; externalId: string }>>({});

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
  };

  useEffect(reload, []);

  const startEdit = (w: Workload) => {
    setEditingWorkloadId(w.id);
    setEditName(w.name);
    setEditOwner(w.owner ?? '');
  };

  const saveEdit = async (workloadId: string) => {
    setError(null);
    try {
      await api.updateWorkload(workloadId, editName, editOwner || null);
      setEditingWorkloadId(null);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
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
      await api.createWorkloadRole(workloadId, form.name, mappings);
      setRoleForm((prev) => ({ ...prev, [workloadId]: { name: '', mappings: '' } }));
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
      await api.createWorkloadResource(workloadId, form.type, form.externalId || null);
      setResourceForm((prev) => ({ ...prev, [workloadId]: { type: '', externalId: '' } }));
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

  return (
    <div>
      <Title2>Workloads</Title2>
      <Text>Fachliche Zugriffskontexte dieses Platform-Tenants (Blueprint 6.1).</Text>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

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
                  <Input value={editOwner} onChange={(_, d) => setEditOwner(d.value)} />
                </Field>
                <Button appearance="primary" size="small" onClick={() => saveEdit(w.id)}>Speichern</Button>
                <Button size="small" onClick={() => setEditingWorkloadId(null)}>Abbrechen</Button>
              </div>
            ) : (
              <>
                <Text weight="semibold">{w.name}</Text>
                {!w.active && <Badge appearance="tint" color="danger" style={{ marginLeft: 8 }}>Inaktiv</Badge>}
                {w.owner && <Text className={styles.meta} block size={200}>Owner: {w.owner}</Text>}
                <Text className={styles.meta} block size={200}>
                  Nutzer: {counts[w.id] ? (
                    <>Aktiv {counts[w.id].active} / Inaktiv {counts[w.id].inactive}</>
                  ) : '…'}
                </Text>
              </>
            )}

            <div className={styles.section}>
              {w.roles.map((r) => (
                <Badge key={r.id} appearance="tint" color="brand">
                  Rolle: {r.name}
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
                  {res.resourceType}{res.externalId ? `:${res.externalId}` : ''}{!res.managed && ' (discovered)'}
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

            <Text className={styles.subheading} block size={200}>Rolle hinzufügen</Text>
            <div className={styles.editForm}>
              <Input
                placeholder="Rollenname"
                value={roleForm[w.id]?.name ?? ''}
                onChange={(_, d) => setRoleForm((prev) => ({ ...prev, [w.id]: { name: d.value, mappings: prev[w.id]?.mappings ?? '' } }))}
              />
              <Input
                placeholder="Ressourcen-IDs (Komma-getrennt, optional)"
                value={roleForm[w.id]?.mappings ?? ''}
                onChange={(_, d) => setRoleForm((prev) => ({ ...prev, [w.id]: { name: prev[w.id]?.name ?? '', mappings: d.value } }))}
              />
              <Button size="small" onClick={() => handleAddRole(w.id)}>Hinzufügen</Button>
            </div>

            <Text className={styles.subheading} block size={200}>Ressource hinzufügen</Text>
            <div className={styles.editForm}>
              <Input
                placeholder="ResourceType (z.B. SecurityGroup)"
                value={resourceForm[w.id]?.type ?? ''}
                onChange={(_, d) => setResourceForm((prev) => ({ ...prev, [w.id]: { type: d.value, externalId: prev[w.id]?.externalId ?? '' } }))}
              />
              <Input
                placeholder="ExternalId (optional)"
                value={resourceForm[w.id]?.externalId ?? ''}
                onChange={(_, d) => setResourceForm((prev) => ({ ...prev, [w.id]: { type: prev[w.id]?.type ?? '', externalId: d.value } }))}
              />
              <Button size="small" onClick={() => handleAddResource(w.id)}>Hinzufügen</Button>
            </div>

            <div className={styles.actions}>
              <Button appearance="secondary" size="small" onClick={() => navigate(`/workloads/${w.id}/scenarios`)}>
                Scenarios
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
