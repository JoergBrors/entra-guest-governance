import { useEffect, useState } from 'react';
import type { DragEvent } from 'react';
import { useParams } from 'react-router-dom';
import {
  Title2, Text, Card, Badge, Spinner, Button, Textarea, makeStyles, tokens,
  MessageBar, MessageBarBody, Tooltip, Dialog, DialogTrigger, DialogSurface, DialogTitle,
  DialogBody, DialogContent, DialogActions, Field, Input, Select, Table, TableBody,
  TableCell, TableHeader, TableHeaderCell, TableRow,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { Workload, WorkloadScenario, WorkloadResource, ScenarioTemplateDto, ScenarioUser } from '../types/domain';

type EditorMode = 'gui' | 'json';

interface GuiRule {
  id: string;
  resourceId: string;
  fieldsText: string;
  conditionText: string;
}

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px' },
  ruleRow: {
    display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center',
    padding: '8px 0', borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  meta: { color: tokens.colorNeutralForeground3 },
  importPanel: { display: 'flex', flexDirection: 'column', gap: '10px', maxWidth: '980px', marginTop: '24px' },
  modeBar: { display: 'flex', gap: '8px', flexWrap: 'wrap' },
  editorGrid: {
    display: 'grid',
    gridTemplateColumns: 'minmax(220px, 280px) minmax(320px, 1fr)',
    gap: '12px',
    alignItems: 'start',
  },
  palette: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px',
    padding: '10px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
  },
  paletteItem: {
    cursor: 'grab',
    padding: '8px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  dropZone: {
    minHeight: '120px',
    display: 'flex',
    flexDirection: 'column',
    gap: '10px',
    padding: '10px',
    border: `1px dashed ${tokens.colorBrandStroke1}`,
    borderRadius: '6px',
  },
  ruleEditor: {
    display: 'grid',
    gridTemplateColumns: 'minmax(180px, 240px) minmax(220px, 1fr) minmax(180px, 280px) auto',
    gap: '8px',
    alignItems: 'end',
    padding: '10px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
    backgroundColor: tokens.colorNeutralBackground2,
  },
  inlineForm: { display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'end' },
});

export function ScenariosPage() {
  const styles = useStyles();
  const { workloadId } = useParams<{ workloadId: string }>();
  const [scenarios, setScenarios] = useState<WorkloadScenario[] | null>(null);
  const [workload, setWorkload] = useState<Workload | null>(null);
  const [resources, setResources] = useState<WorkloadResource[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [deployResult, setDeployResult] = useState<Record<string, string>>({});
  const [scenarioUsers, setScenarioUsers] = useState<Record<string, ScenarioUser[]>>({});

  const [mode, setMode] = useState<EditorMode>('gui');
  const [importJson, setImportJson] = useState('');
  const [importSummary, setImportSummary] = useState<string | null>(null);
  const [guiScenarioName, setGuiScenarioName] = useState('');
  const [guiRules, setGuiRules] = useState<GuiRule[]>([]);
  const [selectedResourceId, setSelectedResourceId] = useState('');
  const [dragResourceId, setDragResourceId] = useState<string | null>(null);

  const reload = () => {
    if (!workloadId) return;
    api.listScenarios(workloadId).then(setScenarios).catch((e: Error) => setError(e.message));
    api.listWorkloads()
      .then((workloads) => {
        const current = workloads.find((w) => w.id === workloadId);
        setWorkload(current ?? null);
        setResources(current?.resources ?? []);
      })
      .catch((e: Error) => setError(e.message));
  };

  useEffect(reload, [workloadId]);

  const resourceLabel = (resourceId: string) => {
    const resource = resources.find((r) => r.id === resourceId);
    return resource ? `${resource.resourceType}:${resource.externalId ?? resource.id}` : resourceId;
  };

  const addGuiRule = (resourceId: string) => {
    if (!resourceId) return;
    setGuiRules((prev) => [
      ...prev,
      { id: newId(), resourceId, fieldsText: '', conditionText: '' },
    ]);
    setSelectedResourceId('');
  };

  const handleDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    addGuiRule(event.dataTransfer.getData('text/resource-id') || dragResourceId || '');
    setDragResourceId(null);
  };

  const buildTemplateFromGui = (): ScenarioTemplateDto => {
    if (!workload) throw new Error('Workload ist noch nicht geladen.');
    if (!guiScenarioName.trim()) throw new Error('Bitte einen Szenario-Namen angeben.');
    if (guiRules.length === 0) throw new Error('Bitte mindestens eine Gruppe in das Szenario ziehen.');

    return {
      workloadName: workload.name,
      scenarioName: guiScenarioName.trim(),
      rules: guiRules.map((rule) => {
        const resource = resources.find((r) => r.id === rule.resourceId);
        if (!resource) throw new Error(`Ressource ${rule.resourceId} ist nicht mehr im Workload vorhanden.`);
        return {
          resourceName: resource.externalId ?? resource.id,
          resourceType: resource.resourceType,
          fields: parseFields(rule.fieldsText),
          condition: parseCondition(rule.conditionText),
        };
      }),
    };
  };

  const applyJsonToGui = () => {
    if (!importJson.trim()) {
      setMode('gui');
      return;
    }

    const template = JSON.parse(importJson) as ScenarioTemplateDto;
    const rules = template.rules.map((rule) => {
      const resource = resources.find((candidate) =>
        candidate.resourceType === rule.resourceType
        && (candidate.externalId === rule.resourceName || candidate.id === rule.resourceName));
      if (!resource) {
        throw new Error(`Template-Ressource ${rule.resourceType}:${rule.resourceName} ist nicht im Workload vorhanden.`);
      }
      return {
        id: newId(),
        resourceId: resource.id,
        fieldsText: formatFields(rule.fields),
        conditionText: rule.condition ? JSON.stringify(rule.condition) : '',
      };
    });

    setGuiScenarioName(template.scenarioName ?? '');
    setGuiRules(rules);
    setMode('gui');
  };

  const switchMode = (nextMode: EditorMode) => {
    if (nextMode === mode) return;
    setError(null);
    try {
      if (nextMode === 'json') {
        setImportJson(JSON.stringify(buildTemplateFromGui(), null, 2));
        setMode('json');
      } else {
        applyJsonToGui();
      }
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleDeploy = async (scenarioId: string) => {
    setError(null);
    try {
      await api.deployScenario(scenarioId);
      setDeployResult((prev) => ({ ...prev, [scenarioId]: 'Deployment ausgelöst' }));
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const loadScenarioUsers = async (scenarioId: string) => {
    if (!workloadId) return;
    setError(null);
    try {
      const users = await api.listScenarioUsers(workloadId, scenarioId);
      setScenarioUsers((prev) => ({ ...prev, [scenarioId]: users }));
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const importTemplate = async (template: ScenarioTemplateDto) => {
    const result = await api.importScenarios(template);
    const parts = ['Import abgeschlossen'];
    if (result.createdResourceNames.length > 0) parts.push(`${result.createdResourceNames.length} neue Ressource(n) angelegt`);
    if (result.errors.length > 0) parts.push(`${result.errors.length} Fehler: ${result.errors.join('; ')}`);
    setImportSummary(parts.join(', '));
    reload();
  };

  const handleGuiSave = async () => {
    setError(null);
    setImportSummary(null);
    try {
      const template = buildTemplateFromGui();
      setImportJson(JSON.stringify(template, null, 2));
      await importTemplate(template);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleImport = async () => {
    setError(null);
    setImportSummary(null);
    try {
      const template = JSON.parse(importJson) as ScenarioTemplateDto;
      await importTemplate(template);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleExport = async (scenarioId: string) => {
    setError(null);
    try {
      const template = await api.exportScenario(scenarioId);
      setImportJson(JSON.stringify(template, null, 2));
      setMode('json');
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleDelete = async (scenarioId: string) => {
    setError(null);
    try {
      await api.deleteScenario(scenarioId);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  if (!workloadId) return <Text>Kein Workload angegeben.</Text>;
  if (workload?.isDefault) {
    return (
      <div>
        <Title2>Scenarios</Title2>
        <Text>Der Default-Workload ist der Anker fuer alle Gruppen und erlaubt keine Szenarien.</Text>
      </div>
    );
  }

  return (
    <div>
      <Title2>Scenarios</Title2>
      <Text>
        Ein Szenario besteht aus Ressourcen-Regeln. Es dürfen nur Gruppen verwendet werden,
        die am aktuellen Workload hängen.
      </Text>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      <Card className={styles.card} style={{ marginTop: 16 }}>
        <Text weight="semibold" block>Verwendbare Gruppen dieses Workloads</Text>
        {resources.length === 0 && <Text>Keine Gruppen am Workload hinterlegt.</Text>}
        {resources.map((resource) => (
          <Badge key={resource.id} appearance="outline" color="brand" style={{ marginRight: 6, marginTop: 6 }}>
            {resource.resourceType}:{resource.externalId ?? resource.id}
          </Badge>
        ))}
      </Card>

      {!scenarios ? (
        <Spinner label="Lade Szenarien…" />
      ) : (
        <div className={styles.list}>
          {scenarios.length === 0 && <Text>Noch keine Szenarien für diesen Workload.</Text>}
          {scenarios.map((s) => (
            <Card key={s.id} className={styles.card}>
              <Text weight="semibold">{s.name}</Text>
              <Text className={styles.meta} block size={200}>{s.rules.length} Regel(n)</Text>

              {s.rules.map((rule) => (
                <div key={rule.id} className={styles.ruleRow}>
                  <Badge appearance="tint" color="brand">{resourceLabel(rule.resourceId)}</Badge>
                  {Object.entries(rule.fields).map(([key, value]) => (
                    <Badge key={key} appearance="outline" color="informative">{key}: {value}</Badge>
                  ))}
                  {rule.condition ? (
                    <Tooltip content={JSON.stringify(rule.condition)} relationship="label">
                      <Badge appearance="outline" color="warning">Bedingung</Badge>
                    </Tooltip>
                  ) : (
                    <Badge appearance="outline">immer aktiv</Badge>
                  )}
                </div>
              ))}

              <div style={{ display: 'flex', gap: '8px', marginTop: '12px', alignItems: 'center', flexWrap: 'wrap' }}>
                <Button size="small" onClick={() => handleDeploy(s.id)}>Deploy</Button>
                <Button size="small" appearance="secondary" onClick={() => loadScenarioUsers(s.id)}>
                  User prüfen
                </Button>
                <Button size="small" appearance="secondary" onClick={() => handleExport(s.id)}>
                  Als Template exportieren
                </Button>
                <Dialog>
                  <DialogTrigger disableButtonEnhancement>
                    <Button size="small" appearance="secondary">Löschen</Button>
                  </DialogTrigger>
                  <DialogSurface>
                    <DialogBody>
                      <DialogTitle>Szenario löschen?</DialogTitle>
                      <DialogContent>
                        "{s.name}" wird endgültig gelöscht. Kann jederzeit per Template neu
                        importiert werden.
                      </DialogContent>
                      <DialogActions>
                        <DialogTrigger disableButtonEnhancement>
                          <Button appearance="secondary">Abbrechen</Button>
                        </DialogTrigger>
                        <DialogTrigger disableButtonEnhancement>
                          <Button appearance="primary" onClick={() => handleDelete(s.id)}>Löschen</Button>
                        </DialogTrigger>
                      </DialogActions>
                    </DialogBody>
                  </DialogSurface>
                </Dialog>
                {deployResult[s.id] && <Text size={200}>{deployResult[s.id]}</Text>}
              </div>
              {scenarioUsers[s.id] && (
                <ScenarioUsersTable users={scenarioUsers[s.id]} applicationName={workload?.applicationExternalId ?? null} />
              )}
            </Card>
          ))}
        </div>
      )}

      <div className={styles.importPanel}>
        <Text weight="semibold" block>Szenario definieren</Text>
        <div className={styles.modeBar}>
          <Button appearance={mode === 'gui' ? 'primary' : 'secondary'} onClick={() => switchMode('gui')}>GUI</Button>
          <Button appearance={mode === 'json' ? 'primary' : 'secondary'} onClick={() => switchMode('json')}>JSON</Button>
        </div>

        {mode === 'gui' ? (
          <>
            <Field label="Szenario-Name">
              <Input value={guiScenarioName} onChange={(_, d) => setGuiScenarioName(d.value)} placeholder="z.B. Fabrikam Disponenten" />
            </Field>
            <div className={styles.editorGrid}>
              <div className={styles.palette}>
                <Text weight="semibold">Workload-Gruppen</Text>
                {resources.map((resource) => (
                  <div
                    key={resource.id}
                    className={styles.paletteItem}
                    draggable
                    onDragStart={(event) => {
                      event.dataTransfer.setData('text/resource-id', resource.id);
                      setDragResourceId(resource.id);
                    }}
                  >
                    <Text size={200}>{resource.resourceType}</Text>
                    <Text weight="semibold" block>{resource.externalId ?? resource.id}</Text>
                  </div>
                ))}
                <div className={styles.inlineForm}>
                  <Select value={selectedResourceId} onChange={(event) => setSelectedResourceId(event.target.value)}>
                    <option value="">Gruppe auswählen</option>
                    {resources.map((resource) => (
                      <option key={resource.id} value={resource.id}>{resourceLabel(resource.id)}</option>
                    ))}
                  </Select>
                  <Button size="small" onClick={() => addGuiRule(selectedResourceId)} disabled={!selectedResourceId}>Hinzufügen</Button>
                </div>
              </div>

              <div
                className={styles.dropZone}
                onDragOver={(event) => event.preventDefault()}
                onDrop={handleDrop}
              >
                {guiRules.length === 0 && <Text>Gruppen hier ablegen oder links auswählen.</Text>}
                {guiRules.map((rule) => (
                  <div key={rule.id} className={styles.ruleEditor}>
                    <Field label="Gruppe">
                      <Select
                        value={rule.resourceId}
                        onChange={(event) => setGuiRules((prev) => prev.map((item) =>
                          item.id === rule.id ? { ...item, resourceId: event.target.value } : item))}
                      >
                        {resources.map((resource) => (
                          <option key={resource.id} value={resource.id}>{resourceLabel(resource.id)}</option>
                        ))}
                      </Select>
                    </Field>
                    <Field label="Felder">
                      <Textarea
                        rows={2}
                        value={rule.fieldsText}
                        onChange={(_, d) => setGuiRules((prev) => prev.map((item) =>
                          item.id === rule.id ? { ...item, fieldsText: d.value } : item))}
                        placeholder="Firma=Fabrikam; Rolle=Reader"
                      />
                    </Field>
                    <Field label="Bedingung">
                      <Textarea
                        rows={2}
                        value={rule.conditionText}
                        onChange={(_, d) => setGuiRules((prev) => prev.map((item) =>
                          item.id === rule.id ? { ...item, conditionText: d.value } : item))}
                        placeholder='{"==":[{"var":"Firma"},"Fabrikam"]}'
                      />
                    </Field>
                    <Button
                      size="small"
                      appearance="secondary"
                      onClick={() => setGuiRules((prev) => prev.filter((item) => item.id !== rule.id))}
                    >
                      Entfernen
                    </Button>
                  </div>
                ))}
              </div>
            </div>
            <div className={styles.inlineForm}>
              <Button appearance="primary" onClick={handleGuiSave} disabled={!guiScenarioName || guiRules.length === 0}>Speichern</Button>
              <Button appearance="secondary" onClick={() => switchMode('json')} disabled={!guiScenarioName || guiRules.length === 0}>
                In JSON übernehmen
              </Button>
            </div>
          </>
        ) : (
          <>
            <Textarea
              value={importJson}
              onChange={(_, d) => setImportJson(d.value)}
              rows={12}
              placeholder='{"workloadName":"...","scenarioName":"...","rules":[{"resourceName":"SG-FABRIKAM-DISPONENT","resourceType":"SecurityGroup","fields":{"Firma":"Fabrikam","Rolle":"Disponent"},"condition":null}]}'
            />
            <div className={styles.inlineForm}>
              <Button appearance="primary" onClick={handleImport} disabled={!importJson}>Importieren</Button>
              <Button appearance="secondary" onClick={() => switchMode('gui')} disabled={!importJson}>In GUI übernehmen</Button>
            </div>
          </>
        )}
        {importSummary && <Text size={200}>{importSummary}</Text>}
      </div>
    </div>
  );
}

function parseFields(value: string): Record<string, string> {
  const fields: Record<string, string> = {};
  value
    .split(/[\n;]/)
    .map((part) => part.trim())
    .filter(Boolean)
    .forEach((part) => {
      const separatorIndex = part.includes('=') ? part.indexOf('=') : part.indexOf(':');
      if (separatorIndex < 1) {
        fields[part] = 'true';
        return;
      }
      const key = part.slice(0, separatorIndex).trim();
      const fieldValue = part.slice(separatorIndex + 1).trim();
      if (key) fields[key] = fieldValue;
    });
  return fields;
}

function formatFields(fields: Record<string, string>): string {
  return Object.entries(fields).map(([key, value]) => `${key}=${value}`).join('; ');
}

function parseCondition(value: string) {
  const trimmed = value.trim();
  return trimmed ? JSON.parse(trimmed) : null;
}

function newId(): string {
  return globalThis.crypto?.randomUUID?.() ?? Math.random().toString(36).slice(2);
}

function ScenarioUsersTable({ users, applicationName }: { users: ScenarioUser[]; applicationName: string | null }) {
  const active = users.filter((user) => user.active).length;
  return (
    <div style={{ marginTop: 12 }}>
      <Text weight="semibold" block>
        User: Aktiv {active} / Inaktiv {users.length - active}
      </Text>
      <Table size="extra-small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Name</TableHeaderCell>
            <TableHeaderCell>UserType</TableHeaderCell>
            <TableHeaderCell>Rolle</TableHeaderCell>
            <TableHeaderCell>Status</TableHeaderCell>
            <TableHeaderCell>Last login</TableHeaderCell>
            <TableHeaderCell>{applicationName ? 'App last login' : 'App'}</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {users.map((user) => (
            <TableRow key={`${user.guestId}-${user.roleName}`}>
              <TableCell>
                <Text weight="semibold" block>{user.displayName}</Text>
                <Text size={200}>{user.mail}</Text>
              </TableCell>
              <TableCell>{user.userType}</TableCell>
              <TableCell>{user.roleName}</TableCell>
              <TableCell>
                <Badge appearance="tint" color={user.active ? 'success' : 'subtle'}>{user.assignmentStatus}</Badge>
              </TableCell>
              <TableCell>{formatDate(user.lastLoginAt)}</TableCell>
              <TableCell>{applicationName ? formatDate(user.applicationLastLoginAt) : 'Keine Workload-App'}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function formatDate(value?: string | null): string {
  return value ? new Date(value).toLocaleString() : 'nie';
}
