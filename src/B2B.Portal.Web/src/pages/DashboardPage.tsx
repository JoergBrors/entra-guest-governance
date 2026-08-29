import { useEffect, useState } from 'react';
import { Card, Title2, Title3, Text, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { api } from '../api/client';
import type { GuestAccount, Workload, ReviewInstance } from '../types/domain';

const useStyles = makeStyles({
  grid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
    gap: '16px',
    marginTop: '16px',
  },
  card: {
    padding: '20px',
  },
  metric: {
    fontSize: '32px',
    fontWeight: 600,
    color: tokens.colorBrandForeground1,
  },
  modeCard: {
    padding: '12px 16px',
    marginTop: '8px',
    display: 'inline-block',
  },
});

/**
 * Dashboard (Blueprint 9): aktive Gäste, offene Reviews, Workloads. Löschkandidaten und
 * fehlgeschlagene Jobs sind für spätere Iterationen vorgesehen (siehe Blueprint 9
 * "Kernfunktionen" Dashboard-Zeile) und im MVP bewusst nicht erfunden simuliert.
 */
export function DashboardPage() {
  const styles = useStyles();
  const [guests, setGuests] = useState<GuestAccount[] | null>(null);
  const [workloads, setWorkloads] = useState<Workload[] | null>(null);
  const [reviews, setReviews] = useState<ReviewInstance[] | null>(null);
  const [mode, setMode] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.health().then((h) => setMode(h.mode)).catch(() => setMode(null));
    Promise.all([api.listGuests(), api.listWorkloads(), api.listOpenReviews()])
      .then(([g, w, r]) => {
        setGuests(g);
        setWorkloads(w);
        setReviews(r);
      })
      .catch((e: Error) => setError(e.message));
  }, []);

  if (error) {
    return <Text>Fehler beim Laden des Dashboards: {error}</Text>;
  }

  return (
    <div>
      <Title2>Dashboard</Title2>
      {mode && (
        <Card className={styles.modeCard}>
          <Text>
            Development-Modus: <strong>{mode}</strong>
            {mode === 'LOCAL_MOCK' && ' — keine externen Schreibzugriffe.'}
          </Text>
        </Card>
      )}

      <div className={styles.grid}>
        <Card className={styles.card}>
          <Title3>Gäste im Pool</Title3>
          {guests ? <div className={styles.metric}>{guests.length}</div> : <Spinner size="tiny" />}
        </Card>
        <Card className={styles.card}>
          <Title3>Workloads</Title3>
          {workloads ? <div className={styles.metric}>{workloads.length}</div> : <Spinner size="tiny" />}
        </Card>
        <Card className={styles.card}>
          <Title3>Offene Reviews</Title3>
          {reviews ? <div className={styles.metric}>{reviews.length}</div> : <Spinner size="tiny" />}
        </Card>
      </div>
    </div>
  );
}
