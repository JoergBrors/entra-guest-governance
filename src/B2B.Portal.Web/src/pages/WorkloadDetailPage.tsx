import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Badge, Button, Card, Spinner, Text, Title2, makeStyles } from '@fluentui/react-components';
import { api } from '../api/client';
import type { Workload } from '../types/domain';

const useStyles = makeStyles({
  stack: { display: 'grid', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px', borderRadius: 'var(--card-radius)' },
  row: { display: 'flex', gap: '8px', flexWrap: 'wrap', marginTop: '8px' },
});

export function WorkloadDetailPage() {
  const { workloadId } = useParams<{ workloadId: string }>();
  const navigate = useNavigate();
  const styles = useStyles();
  const [workload, setWorkload] = useState<Workload | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!workloadId) return;
    api.getWorkload(workloadId).then(setWorkload).catch((e: Error) => setError(e.message));
  }, [workloadId]);

  if (error) return <Text>Fehler: {error}</Text>;
  if (!workload) return <Spinner label="Lade Workload..." />;

  return (
    <div>
      <Title2>{workload.name}</Title2>
      <Text>Owner: {workload.owner ?? 'configuration required'}</Text>
      <div className={styles.stack}>
        <Card className={styles.card}>
          <Text weight="semibold" block>Rollen</Text>
          <div className={styles.row}>
            {workload.roles.length === 0 && <Text>Keine Rollen definiert.</Text>}
            {workload.roles.map((role) => <Badge key={role.id}>{role.name}</Badge>)}
          </div>
        </Card>
        <Card className={styles.card}>
          <Text weight="semibold" block>Eigene oder verwaltbare Ressourcen</Text>
          <div className={styles.row}>
            {workload.resources.length === 0 && <Text>Keine Ressourcen sichtbar.</Text>}
            {workload.resources.map((resource) => (
              <Badge key={resource.id} appearance="outline">
                {resource.resourceType}{resource.externalId ? `:${resource.externalId}` : ''}
              </Badge>
            ))}
          </div>
        </Card>
        <Card className={styles.card}>
          <Text weight="semibold" block>Szenarien</Text>
          <Button onClick={() => navigate(`/workloads/${workload.id}/scenarios`)}>Szenarien öffnen</Button>
        </Card>
      </div>
    </div>
  );
}

