import { useEffect, useState } from 'react';
import {
  Title2, Text, Card, Badge, Spinner, Button, makeStyles, tokens,
} from '@fluentui/react-components';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { Workload } from '../types/domain';

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px' },
  section: { marginTop: '8px', display: 'flex', gap: '8px', flexWrap: 'wrap' },
  meta: { color: tokens.colorNeutralForeground3 },
});

/**
 * Admin/Governance-Ansicht "Workloads" (Blueprint 9): Rollen, Ressourcen, Owner.
 * Zeigt — anders als "Meine Workloads" — auch technische Ressourcen, da dies eine
 * administrative Ansicht ist.
 */
export function WorkloadsAdminPage() {
  const styles = useStyles();
  const navigate = useNavigate();
  const [workloads, setWorkloads] = useState<Workload[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.listWorkloads().then(setWorkloads).catch((e: Error) => setError(e.message));
  }, []);

  if (error) return <Text>Fehler: {error}</Text>;
  if (!workloads) return <Spinner label="Lade Workloads…" />;

  return (
    <div>
      <Title2>Workloads</Title2>
      <Text>Fachliche Zugriffskontexte dieses Platform-Tenants (Blueprint 6.1).</Text>

      <div className={styles.list}>
        {workloads.length === 0 && <Text>Noch keine Workloads angelegt.</Text>}
        {workloads.map((w) => (
          <Card key={w.id} className={styles.card}>
            <Text weight="semibold">{w.name}</Text>
            {w.owner && <Text className={styles.meta} block size={200}>Owner: {w.owner}</Text>}

            <div className={styles.section}>
              {w.roles.map((r) => (
                <Badge key={r.id} appearance="tint" color="brand">Rolle: {r.name}</Badge>
              ))}
              {w.resources.map((res) => (
                <Badge key={res.id} appearance="outline" color={res.managed ? 'success' : 'warning'}>
                  {res.resourceType}{!res.managed && ' (discovered)'}
                </Badge>
              ))}
            </div>

            <Button
              appearance="secondary"
              size="small"
              style={{ marginTop: '12px' }}
              onClick={() => navigate(`/workloads/${w.id}/scenarios`)}
            >
              Scenarios
            </Button>
          </Card>
        ))}
      </div>
    </div>
  );
}
