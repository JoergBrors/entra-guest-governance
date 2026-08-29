import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import {
  Title2, Text, Card, Badge, Spinner, Button, Textarea, makeStyles, tokens,
  MessageBar, MessageBarBody, Tooltip, Dialog, DialogTrigger, DialogSurface, DialogTitle,
  DialogBody, DialogContent, DialogActions,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { WorkloadScenario, WorkloadResource, ScenarioTemplateDto } from '../types/domain';

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px' },
  ruleRow: {
    display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center',
    padding: '8px 0', borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  meta: { color: tokens.colorNeutralForeground3 },
  importPanel: { display: 'flex', flexDirection: 'column', gap: '8px', maxWidth: '640px', marginTop: '24px' },
  divider: { marginTop: '32px', borderTop: `1px solid ${tokens.colorNeutralStroke2}`, paddingTop: '16px' },
});

/**
 * Szenario-Viewer für einen Workload: zeigt pro Szenario seine ScenarioResourceRules
 * (Ressource, freie Fields, optionale Bedingung) und erlaubt Deploy. Neue Szenarien
 * entstehen ausschließlich über den Upload eines vollständigen JSON-Templates (siehe
 * ScenarioImportExportService) — kein manuelles Anlage-Formular mehr, da Ressourcen und
 * Regeln frei im Template beschrieben werden und beim Import automatisch angelegt werden.
 */
export function ScenariosPage() {
  const styles = useStyles();
  const { workloadId } = useParams<{ workloadId: string }>();
  const [scenarios, setScenarios] = useState<WorkloadScenario[] | null>(null);
  const [resources, setResources] = useState<WorkloadResource[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [deployResult, setDeployResult] = useState<Record<string, string>>({});

  const [importJson, setImportJson] = useState('');
  const [importSummary, setImportSummary] = useState<string | null>(null);

  const reload = () => {
    if (!workloadId) return;
    api.listScenarios(workloadId).then(setScenarios).catch((e: Error) => setError(e.message));
    api.listWorkloads()
      .then((workloads) => {
        const workload = workloads.find((w) => w.id === workloadId);
        setResources(workload?.resources ?? []);
      })
      .catch((e: Error) => setError(e.message));
  };

  useEffect(reload, [workloadId]);

  const resourceLabel = (resourceId: string) => {
    const resource = resources.find((r) => r.id === resourceId);
    return resource ? `${resource.resourceType}:${resource.externalId ?? resource.id}` : resourceId;
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

  const handleImport = async () => {
    setError(null);
    setImportSummary(null);
    try {
      const template = JSON.parse(importJson) as ScenarioTemplateDto;
      const result = await api.importScenarios(template);
      const parts = [`${result.createdResourceNames.length} neue Ressource(n) angelegt`];
      if (result.errors.length > 0) parts.push(`${result.errors.length} Fehler: ${result.errors.join('; ')}`);
      setImportSummary(parts.join(', '));
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const handleExport = async (scenarioId: string) => {
    setError(null);
    try {
      const template = await api.exportScenario(scenarioId);
      setImportJson(JSON.stringify(template, null, 2));
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

  return (
    <div>
      <Title2>Scenarios</Title2>
      <Text>
        Ein Szenario besteht aus Ressourcen-Regeln — jede Regel bindet eine Ressource an
        freie fachliche Felder (z.B. Firma, Rolle) und optional eine eigene Bedingung
        (JSONLogic), wann diese Regel zutrifft.
      </Text>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

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

              <div style={{ display: 'flex', gap: '8px', marginTop: '12px', alignItems: 'center' }}>
                <Button size="small" onClick={() => handleDeploy(s.id)}>Deploy</Button>
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
            </Card>
          ))}
        </div>
      )}

      <div className={styles.importPanel}>
        <Text weight="semibold" block>Template hochladen</Text>
        <Textarea
          value={importJson}
          onChange={(_, d) => setImportJson(d.value)}
          rows={12}
          placeholder='{"workloadName":"...","scenarioName":"...","rules":[{"resourceName":"SG-FABRIKAM-DISPONENT","resourceType":"SecurityGroup","fields":{"Firma":"Fabrikam","Rolle":"Disponent"},"condition":null}]}'
        />
        <div style={{ display: 'flex', gap: '8px' }}>
          <Button appearance="primary" onClick={handleImport} disabled={!importJson}>Importieren</Button>
        </div>
        {importSummary && <Text size={200}>{importSummary}</Text>}
      </div>
    </div>
  );
}
