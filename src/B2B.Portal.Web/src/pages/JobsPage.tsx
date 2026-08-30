import { useEffect, useState } from 'react';
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
});

export function JobsPage() {
  const styles = useStyles();
  const [jobs, setJobs] = useState<JobStatusResponse[] | null>(null);
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = () => {
    api.listJobs()
      .then(setJobs)
      .catch((e: Error) => setError(e.message));
  };

  useEffect(reload, []);

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

  if (!jobs) {
    return <Spinner label="Lade Jobs..." />;
  }

  return (
    <div>
      <Title2>Jobs</Title2>
      <Text>Worker-Jobs im erlaubten Kontext: Governance Admin sieht alle Jobs, Workload Owner nur Jobs ihrer Workloads.</Text>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.list}>
        {jobs.length === 0 && (
          <Card className={styles.card}>
            <Text>Keine sichtbaren Jobs vorhanden.</Text>
          </Card>
        )}
        {jobs.map((job) => {
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
                </div>
              </div>
              {selected && (
                <div className={styles.details}>
                  <Text className={styles.meta}>Entity: {job.entityType}:{job.entityId}</Text>
                  <Text className={styles.meta}>Retry: {job.retryCount}</Text>
                  <Text className={styles.meta}>Erstellt: {new Date(job.createdAt).toLocaleString()}</Text>
                  <Text className={styles.meta}>Aktualisiert: {new Date(job.updatedAt).toLocaleString()}</Text>
                  {job.lastError && <Text className={styles.meta}>Meldung: {job.lastError}</Text>}
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
