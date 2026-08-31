import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Badge, Button, Card, MessageBar, MessageBarBody, Spinner, Text, Title2,
  makeStyles, tokens,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { JobStatusResponse } from '../types/domain';

const useStyles = makeStyles({
  summary: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(120px, 1fr))',
    gap: '12px',
    marginTop: '16px',
  },
  summaryCard: {
    padding: '12px 16px',
    borderRadius: 'var(--card-radius)',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  summaryCount: { fontSize: tokens.fontSizeHero700 },
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px', borderRadius: 'var(--card-radius)' },
  row: {
    display: 'grid',
    gridTemplateColumns: 'minmax(220px, 1.3fr) minmax(180px, 1fr) minmax(180px, 1fr) auto',
    gap: '12px',
    alignItems: 'center',
  },
  details: {
    marginTop: '12px',
    padding: '10px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
    gap: '8px',
  },
  meta: { color: tokens.colorNeutralForeground3, overflowWrap: 'anywhere' },
  actions: { display: 'flex', gap: '8px', flexWrap: 'wrap', justifyContent: 'flex-end' },
  log: {
    gridColumn: '1 / -1',
    marginTop: '8px',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  logEntry: {
    display: 'grid',
    gridTemplateColumns: 'minmax(150px, auto) minmax(80px, auto) 1fr',
    gap: '8px',
    fontFamily: 'monospace',
    fontSize: tokens.fontSizeBase200,
    padding: '2px 0',
    borderBottom: `1px solid ${tokens.colorNeutralStroke3}`,
  },
});

const AUTO_REFRESH_INTERVAL_MS = 5000;

export function JobsPage() {
  const styles = useStyles();
  const [jobs, setJobs] = useState<JobStatusResponse[] | null>(null);
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [searchParams, setSearchParams] = useSearchParams();

  // Filter aus der URL (?jobType=X&status=Y) — gesetzt, wenn von der Worker-Uebersicht
  // (WorkerOverviewPage) in die fehlgeschlagenen Jobs eines Typs hineingelinkt wird. Die
  // beiden Seiten "interagieren" dadurch statt zwei getrennte Ansichten zu sein.
  const jobTypeFilter = searchParams.get('jobType');
  const statusFilter = searchParams.get('status');

  const reload = () => {
    api.listJobs()
      .then(setJobs)
      .catch((e: Error) => setError(e.message));
  };

  // Automatische Aktualisierung statt reinem manuellem Reload: die Uebersicht dient auch als
  // Worker-Statusanzeige (laufen gerade Jobs, wie viele haengen fest) — ohne Polling wuerde
  // "wird gerade verarbeitet" sofort veralten, sobald der Worker den naechsten Job zieht.
  useEffect(() => {
    reload();
    const interval = setInterval(reload, AUTO_REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, []);

  const stopJob = async (jobId: string) => {
    setError(null);
    try {
      const updated = await api.stopJob(jobId);
      setJobs((prev) => prev?.map((job) => job.id === updated.id ? updated : job) ?? prev);
      setSelectedJobId(updated.id);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const restartJob = async (jobId: string) => {
    setError(null);
    try {
      const created = await api.restartJob(jobId);
      setJobs((prev) => (prev ? [created, ...prev] : prev));
      setSelectedJobId(created.id);
    } catch (e) {
      setError((e as Error).message);
    }
  };

  const clearFilter = () => setSearchParams({});

  if (!jobs) {
    return <Spinner label="Lade Jobs..." />;
  }

  const visibleJobs = jobs.filter((job) => {
    if (jobTypeFilter && job.jobType !== jobTypeFilter) return false;
    if (statusFilter && job.status !== statusFilter) return false;
    return true;
  });

  const runningCount = jobs.filter((j) => j.status === 'Running').length;
  const pendingCount = jobs.filter((j) => j.status === 'Pending').length;
  const failedCount = jobs.filter((j) => j.status === 'Failed' || j.status === 'DeadLetter').length;
  const lastActivity = jobs.reduce<string | null>(
    (latest, job) => (!latest || job.updatedAt > latest ? job.updatedAt : latest), null);
  // Kein separater "Worker läuft/gestoppt"-Status: die API hat kein Prozess-Handle auf den
  // Worker (eigenstaendiger .NET-Host-Prozess, siehe launch.json/docker-compose.yml) — ob der
  // Worker aktiv ist, zeigt sich indirekt daran, ob Pending/Running-Jobs zeitnah fortschreiten.

  return (
    <div>
      <Title2>Jobs</Title2>
      <Text>Worker-Jobs im erlaubten Kontext: Governance Admin sieht alle Jobs, Workload Owner nur Jobs ihrer Workloads.</Text>

      {(jobTypeFilter || statusFilter) && (
        <MessageBar intent="info" style={{ marginTop: 12 }}>
          <MessageBarBody>
            Gefiltert auf {jobTypeFilter ? `Typ "${jobTypeFilter}"` : 'alle Typen'}
            {statusFilter ? `, Status "${statusFilter}"` : ''}.{' '}
            <Button size="small" appearance="transparent" onClick={clearFilter}>Filter zurücksetzen</Button>
          </MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.summary}>
        <Card className={styles.summaryCard}>
          <Text size={200}>Laufend</Text>
          <Text className={styles.summaryCount} weight="semibold">{runningCount}</Text>
        </Card>
        <Card className={styles.summaryCard}>
          <Text size={200}>Wartend</Text>
          <Text className={styles.summaryCount} weight="semibold">{pendingCount}</Text>
        </Card>
        <Card className={styles.summaryCard}>
          <Text size={200}>Fehlgeschlagen</Text>
          <Text className={styles.summaryCount} weight="semibold" style={failedCount > 0 ? { color: tokens.colorPaletteRedForeground1 } : undefined}>
            {failedCount}
          </Text>
        </Card>
        <Card className={styles.summaryCard}>
          <Text size={200}>Letzte Aktivität</Text>
          <Text weight="semibold">{lastActivity ? new Date(lastActivity).toLocaleTimeString() : '—'}</Text>
        </Card>
      </div>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.list}>
        {visibleJobs.length === 0 && (
          <Card className={styles.card}>
            <Text>Keine sichtbaren Jobs vorhanden.</Text>
          </Card>
        )}
        {visibleJobs.map((job) => {
          const selected = selectedJobId === job.id;
          return (
            <Card key={job.id} className={styles.card}>
              <div className={styles.row}>
                <div>
                  <Text weight="semibold" block>{job.jobType}</Text>
                  <Text className={styles.meta} size={200}>{job.id}</Text>
                </div>
                <div>
                  <Text size={200} block>Workload</Text>
                  <Text className={styles.meta}>{job.workloadName ?? job.workloadId ?? 'Globaler Admin-Job'}</Text>
                </div>
                <div>
                  <Text size={200} block>Ausgelöst von</Text>
                  <Text className={styles.meta}>{job.triggeredBy ?? 'unbekannt'}</Text>
                </div>
                <div className={styles.actions}>
                  <Badge appearance="tint" color={statusColor(job.status)}>{job.status}</Badge>
                  <Button size="small" appearance="secondary" onClick={() => setSelectedJobId(selected ? null : job.id)}>
                    Details
                  </Button>
                  <Button size="small" appearance="secondary" disabled={!canStop(job)} onClick={() => stopJob(job.id)}>
                    Stop
                  </Button>
                  <Button size="small" appearance="secondary" disabled={!canRestart(job)} onClick={() => restartJob(job.id)}>
                    Restart
                  </Button>
                </div>
              </div>
              {selected && (
                <div className={styles.details}>
                  <Text className={styles.meta}>Entity: {job.entityType}:{job.entityId}</Text>
                  <Text className={styles.meta}>Retry: {job.retryCount}</Text>
                  <Text className={styles.meta}>Erstellt: {new Date(job.createdAt).toLocaleString()}</Text>
                  <Text className={styles.meta}>Aktualisiert: {new Date(job.updatedAt).toLocaleString()}</Text>
                  {job.lastError && <Text className={styles.meta}>Meldung: {job.lastError}</Text>}
                  <div className={styles.log}>
                    <Text weight="semibold" size={200}>Verlauf</Text>
                    {job.log.length === 0 && <Text className={styles.meta} size={200}>Kein Log vorhanden.</Text>}
                    {job.log.map((entry, i) => (
                      <div key={i} className={styles.logEntry}>
                        <Text size={200}>{new Date(entry.timestamp).toLocaleString()}</Text>
                        <Badge appearance="tint" color={statusColor(entry.status)} size="small">{entry.status}</Badge>
                        <Text size={200} className={styles.meta}>{entry.message ?? '—'}</Text>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </Card>
          );
        })}
      </div>
    </div>
  );
}

function canStop(job: JobStatusResponse): boolean {
  return ['Pending', 'Running', 'Retry'].includes(job.status);
}

function canRestart(job: JobStatusResponse): boolean {
  return job.status === 'Failed' || job.status === 'DeadLetter';
}

function statusColor(status: JobStatusResponse['status']) {
  switch (status) {
    case 'Success':
      return 'success';
    case 'DeadLetter':
    case 'Failed':
      return 'danger';
    case 'Cancelled':
      return 'warning';
    default:
      return 'brand';
  }
}
