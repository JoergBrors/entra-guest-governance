import { useEffect, useState } from 'react';
import { Button, Card, Field, Select, Text, Textarea, Title2, makeStyles } from '@fluentui/react-components';
import { api } from '../api/client';
import type { Workload } from '../types/domain';

const useStyles = makeStyles({
  card: { padding: '16px 20px', marginTop: '16px', borderRadius: 'var(--card-radius)' },
  form: { display: 'grid', gap: '12px', maxWidth: '520px' },
});

export function AccessRequestPage() {
  const styles = useStyles();
  const [workloads, setWorkloads] = useState<Workload[]>([]);
  const [workloadId, setWorkloadId] = useState('');
  const [roleId, setRoleId] = useState('');
  const [reason, setReason] = useState('');

  useEffect(() => {
    api.listMyWorkloads().then(setWorkloads).catch(() => setWorkloads([]));
  }, []);

  const selectedWorkload = workloads.find((workload) => workload.id === workloadId);

  return (
    <div>
      <Title2>Neuer Zugriff</Title2>
      <Card className={styles.card}>
        <div className={styles.form}>
          <Field label="1. Workload">
            <Select value={workloadId} onChange={(event) => { setWorkloadId(event.target.value); setRoleId(''); }}>
              <option value="">Workload auswählen</option>
              {workloads.map((workload) => <option key={workload.id} value={workload.id}>{workload.name}</option>)}
            </Select>
          </Field>
          <Field label="2. Rolle">
            <Select value={roleId} onChange={(event) => setRoleId(event.target.value)} disabled={!selectedWorkload}>
              <option value="">Rolle auswählen</option>
              {selectedWorkload?.roles.map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}
            </Select>
          </Field>
          <Field label="3. Begründung">
            <Textarea value={reason} onChange={(_, data) => setReason(data.value)} />
          </Field>
          <Text>4. Genehmiger-/Policy-Ergebnis: integration pending</Text>
          <Text>5. Zusammenfassung: {selectedWorkload?.name ?? 'TODO'} / {roleId || 'TODO'}</Text>
          <Button appearance="primary" disabled={!workloadId || !roleId || !reason}>
            6. Antrag senden
          </Button>
        </div>
      </Card>
    </div>
  );
}

