import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Badge, Button, Card, MessageBar, MessageBarBody, Spinner, Text, Title2,
  makeStyles, tokens,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { JobStatusResponse } from '../types/domain';

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px', borderRadius: 'var(--card-radius)' },
  row: {
    display: 'grid',
    gridTemplateColumns: 'minmax(200px, 1.2fr) repeat(3, minmax(90px, auto)) minmax(160px, auto) auto',
    gap: '12px',
    alignItems: 'center',
  },
  stat: { display: 'flex', flexDirection: 'column', gap: '2px' },
  statCount: { fontSize: tokens.fontSizeBase500 },
  meta: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', gap: '8px', flexWrap: 'wrap', justifyContent: 'flex-end' },
  errorLink: { cursor: 'pointer' },
});

// Gleiches Intervall wie JobsPage.AUTO_REFRESH_INTERVAL_MS — beide Seiten zeigen denselben
// zugrundeliegenden Job-Bestand, daher identisches Polling-Verhalten.
const AUTO_REFRESH_INTERVAL_MS = 5000;

// Job-Typen mit generischem "Jetzt ausführen"-Trigger — die einzigen ohne fachlichen
// Kontext-Parameter (kein Guest/Workload/Role aus einem bestehenden Flow). Alle anderen
// Job-Typen entstehen ausschliesslich kontextuell (z.B. GrantWorkloadRole aus der
// Workloads-Admin-Seite) und werden hier nur read-only mit Statistik angezeigt.
const TRIGGERABLE_JOB_TYPES = new Set(['RunDiscovery', 'RunReconciliation']);

interface JobTypeSummary {
  jobType: string;
  total: number;
  success: number;
  error: number;
  pending: number;
  lastUpdatedAt: string;
}

function summarize(jobs: JobStatusResponse[]): JobTypeSummary[] {
  const byType = new Map<string, JobStatusResponse[]>();
  for (const job of jobs) {
    const list = byType.get(job.jobType) ?? [];
    list.push(job);
    byType.set(job.jobType, list);
  }

  return [...byType.entries()]
    .map(([jobType, list]) => ({
      jobType,
      total: list.length,
      success: list.filter((j) => j.status === 'Success').length,
      error: list.filter((j) => j.status === 'Failed' || j.status === 'DeadLetter').length,
      pending: list.filter((j) => j.status === 'Pending' || j.status === 'Running' || j.status === 'Retry').length,
      lastUpdatedAt: list.reduce((latest, j) => (j.updatedAt > latest ? j.updatedAt : latest), list[0].updatedAt),
    }))
    .sort((a, b) => a.jobType.localeCompare(b.jobType));
}

/**
 * Worker/Trigger-Uebersicht (Erweiterung 2026-08-30): aggregiert die bestehenden Jobs (GET
 * /api/jobs) PRO JOB-TYP statt pro Einzeljob wie JobsPage. Zusaetzlich: "Jetzt ausführen" fuer
 * kontextlose Job-Typen (Discovery/Reconciliation) und ein Klick auf die Fehlerzahl springt
 * gefiltert in JobsPage (?jobType=X&status=Failed) — die beiden Seiten sind dadurch verlinkt,
 * kein separates Job-Listen-UI wird hier dupliziert.
 */
export function WorkerOverviewPage() {
  const styles = useStyles();
  const navigate = useNavigate();
  const [jobs, setJobs] = useState<JobStatusResponse[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [triggering, setTriggering] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const reload = () => {
    api.listJobs()
      .then(setJobs)
      .catch((e: Error) => setError(e.message));
  };

  useEffect(() => {
    reload();
    const interval = setInterval(reload, AUTO_REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, []);

  const runDiscovery = async () => {
    setError(null);
    setInfo(null);
    setTriggering('RunDiscovery');
    try {
      const job = await api.triggerDiscovery();
      setInfo(`Discovery-Job ${job.id} eingereiht.`);
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setTriggering(null);
    }
  };

  const runReconciliation = async () => {
    setError(null);
    setInfo(null);
    setTriggering('RunReconciliation');
    try {
      const result = await api.triggerReconciliation();
      setInfo(`${result.queuedJobCount} Reconciliation-Job(s) eingereiht.`);
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setTriggering(null);
    }
  };

  const openFailedInJobs = (jobType: string) => {
    navigate(`/jobs?jobType=${encodeURIComponent(jobType)}&status=Failed`);
  };

  if (!jobs) {
    return <Spinner label="Lade Worker-Übersicht..." />;
  }

  const summaries = summarize(jobs);

  return (
    <div>
      <Title2>Worker</Title2>
      <Text>
        Übersicht pro Job-Typ (Trigger): wie oft ein Job-Typ gelaufen ist, mit welchem Ergebnis, und
        wo möglich ein manueller Anstoß. Details zu einzelnen Jobs stehen weiterhin unter{' '}
        <Button appearance="transparent" size="small" onClick={() => navigate('/jobs')}>Jobs</Button>.
      </Text>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      {info && (
        <MessageBar intent="success" style={{ marginTop: 12 }}>
          <MessageBarBody>{info}</MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.list}>
        {summaries.length === 0 && (
          <Card className={styles.card}>
            <Text>Es sind noch keine Jobs gelaufen.</Text>
          </Card>
        )}
        {summaries.map((summary) => (
          <Card key={summary.jobType} className={styles.card}>
            <div className={styles.row}>
              <Text weight="semibold">{summary.jobType}</Text>
              <div className={styles.stat}>
                <Text size={200} className={styles.meta}>Gesamt</Text>
                <Text className={styles.statCount}>{summary.total}</Text>
              </div>
              <div className={styles.stat}>
                <Text size={200} className={styles.meta}>Erfolgreich</Text>
                <Text className={styles.statCount} style={{ color: tokens.colorPaletteGreenForeground1 }}>
                  {summary.success}
                </Text>
              </div>
              <div className={styles.stat}>
                <Text size={200} className={styles.meta}>Fehler</Text>
                {summary.error > 0 ? (
                  <Badge
                    appearance="tint"
                    color="danger"
                    className={styles.errorLink}
                    onClick={() => openFailedInJobs(summary.jobType)}
                    title="Fehlgeschlagene Jobs dieses Typs in der Jobs-Übersicht anzeigen"
                  >
                    {summary.error}
                  </Badge>
                ) : (
                  <Text className={styles.statCount}>0</Text>
                )}
              </div>
              <div className={styles.stat}>
                <Text size={200} className={styles.meta}>Letzte Aktivität</Text>
                <Text size={200}>{new Date(summary.lastUpdatedAt).toLocaleString()}</Text>
              </div>
              <div className={styles.actions}>
                {summary.error > 0 && (
                  <Button size="small" appearance="secondary" onClick={() => openFailedInJobs(summary.jobType)}>
                    Fehler ansehen
                  </Button>
                )}
                {TRIGGERABLE_JOB_TYPES.has(summary.jobType) && (
                  <Button
                    size="small"
                    appearance="primary"
                    disabled={triggering !== null}
                    onClick={() => (summary.jobType === 'RunDiscovery' ? runDiscovery() : runReconciliation())}
                  >
                    Jetzt ausführen
                  </Button>
                )}
              </div>
            </div>
          </Card>
        ))}

        {[...TRIGGERABLE_JOB_TYPES].filter((t) => !summaries.some((s) => s.jobType === t)).map((jobType) => (
          <Card key={jobType} className={styles.card}>
            <div className={styles.row}>
              <Text weight="semibold">{jobType}</Text>
              <div className={styles.stat}>
                <Text size={200} className={styles.meta}>Gesamt</Text>
                <Text className={styles.statCount}>0</Text>
              </div>
              <div />
              <div />
              <Text size={200} className={styles.meta}>Noch nie gelaufen</Text>
              <div className={styles.actions}>
                <Button
                  size="small"
                  appearance="primary"
                  disabled={triggering !== null}
                  onClick={() => (jobType === 'RunDiscovery' ? runDiscovery() : runReconciliation())}
                >
                  Jetzt ausführen
                </Button>
              </div>
            </div>
          </Card>
        ))}
      </div>
    </div>
  );
}
