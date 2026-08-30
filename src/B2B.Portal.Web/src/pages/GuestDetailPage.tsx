import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Badge, Card, Spinner, Text, Title2, makeStyles } from '@fluentui/react-components';
import { api } from '../api/client';
import type { GuestAccount, MockEntraMembership, Workload, GuestWorkloadAssignment } from '../types/domain';

const useStyles = makeStyles({
  stack: { display: 'grid', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px', borderRadius: 'var(--card-radius)' },
});

export function GuestDetailPage() {
  const { guestId } = useParams<{ guestId: string }>();
  const styles = useStyles();
  const [guest, setGuest] = useState<GuestAccount | null>(null);
  const [assignments, setAssignments] = useState<GuestWorkloadAssignment[]>([]);
  const [workloads, setWorkloads] = useState<Workload[]>([]);
  const [memberships, setMemberships] = useState<MockEntraMembership[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!guestId) return;
    Promise.all([api.getGuest(guestId), api.listGuestAssignments(guestId), api.listWorkloads(), api.listMockEntraMemberships()])
      .then(([g, a, w, m]) => {
        setGuest(g);
        setAssignments(a);
        setWorkloads(w);
        setMemberships(m.filter((membership) => membership.entraObjectId === g.entraObjectId));
      })
      .catch((e: Error) => setError(e.message));
  }, [guestId]);

  if (error) return <Text>Fehler: {error}</Text>;
  if (!guest) return <Spinner label="Lade Gast..." />;

  const workloadName = (workloadId: string) => workloads.find((w) => w.id === workloadId)?.name ?? workloadId;
  const roleName = (assignment: GuestWorkloadAssignment) =>
    workloads.find((w) => w.id === assignment.workloadId)?.roles.find((r) => r.id === assignment.roleId)?.name ?? assignment.roleId;

  return (
    <div>
      <Title2>{guest.displayName}</Title2>
      <Text>{guest.mail}</Text>
      <div className={styles.stack}>
        <Card className={styles.card}>
          <Text weight="semibold" block>Stammdaten</Text>
          <Text block>Sponsor: {guest.sponsor ?? 'configuration required'}</Text>
          <Text block>UserType: <Badge>{guest.userType}</Badge></Text>
          <Text block>Account State: <Badge>{guest.accountState}</Badge></Text>
          <Text block>Entra Object ID: {guest.entraObjectId ?? 'integration pending'}</Text>
        </Card>
        <Card className={styles.card}>
          <Text weight="semibold" block>Zugeordnete Workloads</Text>
          {assignments.length === 0 && <Text>Keine Workload-Zuordnungen.</Text>}
          {assignments.map((assignment) => (
            <Text key={assignment.id} block>
              {workloadName(assignment.workloadId)} / {roleName(assignment)} / {assignment.status}
            </Text>
          ))}
        </Card>
        <Card className={styles.card}>
          <Text weight="semibold" block>Mock-Entra-Gruppen</Text>
          {memberships.length === 0 && <Text>Keine Gruppenmitgliedschaften.</Text>}
          {memberships.map((membership) => (
            <Text key={`${membership.groupId}-${membership.entraObjectId}`} block>
              {membership.groupName}
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
