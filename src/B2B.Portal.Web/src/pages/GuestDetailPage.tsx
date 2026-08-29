import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Badge, Card, Spinner, Text, Title2, makeStyles } from '@fluentui/react-components';
import { api } from '../api/client';
import type { GuestAccount, GuestWorkloadAssignment } from '../types/domain';

const useStyles = makeStyles({
  stack: { display: 'grid', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px', borderRadius: 'var(--card-radius)' },
});

export function GuestDetailPage() {
  const { guestId } = useParams<{ guestId: string }>();
  const styles = useStyles();
  const [guest, setGuest] = useState<GuestAccount | null>(null);
  const [assignments, setAssignments] = useState<GuestWorkloadAssignment[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!guestId) return;
    Promise.all([api.getGuest(guestId), api.listGuestAssignments(guestId)])
      .then(([g, a]) => {
        setGuest(g);
        setAssignments(a);
      })
      .catch((e: Error) => setError(e.message));
  }, [guestId]);

  if (error) return <Text>Fehler: {error}</Text>;
  if (!guest) return <Spinner label="Lade Gast..." />;

  return (
    <div>
      <Title2>{guest.displayName}</Title2>
      <Text>{guest.mail}</Text>
      <div className={styles.stack}>
        <Card className={styles.card}>
          <Text weight="semibold" block>Stammdaten</Text>
          <Text block>Sponsor: {guest.sponsor ?? 'configuration required'}</Text>
          <Text block>Account State: <Badge>{guest.accountState}</Badge></Text>
          <Text block>Entra Object ID: {guest.entraObjectId ?? 'integration pending'}</Text>
        </Card>
        <Card className={styles.card}>
          <Text weight="semibold" block>Zugeordnete Workloads</Text>
          {assignments.length === 0 && <Text>Keine Workload-Zuordnungen.</Text>}
          {assignments.map((assignment) => (
            <Text key={assignment.id} block>
              {assignment.workloadId} / {assignment.roleId} / {assignment.status}
            </Text>
          ))}
        </Card>
        <Card className={styles.card}>
          <Text weight="semibold" block>Audit / Reviews / Compliance</Text>
          <Text>integration pending</Text>
        </Card>
      </div>
    </div>
  );
}

