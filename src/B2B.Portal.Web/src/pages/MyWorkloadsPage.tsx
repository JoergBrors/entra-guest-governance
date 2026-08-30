import { useEffect, useState } from 'react';
import { Card, Title2, Text, Badge, Spinner, makeStyles } from '@fluentui/react-components';
import { api } from '../api/client';
import type { Workload } from '../types/domain';

const useStyles = makeStyles({
  list: {
    display: 'flex',
    flexDirection: 'column',
    gap: '12px',
    marginTop: '16px',
  },
  card: {
    padding: '16px 20px',
  },
  roleRow: {
    display: 'flex',
    gap: '8px',
    marginTop: '8px',
    flexWrap: 'wrap',
  },
});

/**
 * User-Ansicht (Blueprint 9): zeigt AUSSCHLIESSLICH zugeordnete Workloads und die
 * eigene Rolle/Zugriffe. Keine Graph-Details, keine Entra Object IDs, keine
 * Connector-/Job-Informationen (Blueprint 9 "keine Graph-Details in der normalen
 * User-Ansicht").
 */
export function MyWorkloadsPage() {
  const styles = useStyles();
  const [workloads, setWorkloads] = useState<Workload[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.listMyWorkloads()
      .then(setWorkloads)
      .catch((e: Error) => setError(e.message));
  }, []);

  if (error) {
    return <Text>Fehler beim Laden: {error}</Text>;
  }

  if (!workloads) {
    return <Spinner label="Lade Workloads…" />;
  }

  return (
    <div>
      <Title2>Meine Workloads</Title2>
      <Text>Fachliche Rollen und Zugriffe, die dir aktuell zugewiesen sind.</Text>

      <div className={styles.list}>
        {workloads.length === 0 && <Text>Aktuell sind dir keine Workloads zugeordnet.</Text>}
        {workloads.map((w) => (
          <Card key={w.id} className={styles.card}>
            <Text weight="semibold">{w.name}</Text>
            <div className={styles.roleRow}>
              {w.roles.length === 0 && <Text size={200}>Keine Rollen definiert.</Text>}
              {w.roles.map((r) => (
                <Badge key={r.id} appearance="tint" color="brand">
                  {r.name}
                </Badge>
              ))}
            </div>
          </Card>
        ))}
      </div>
    </div>
  );
}
