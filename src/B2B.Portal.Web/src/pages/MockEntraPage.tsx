import { useEffect, useMemo, useState } from 'react';
import {
  Badge, Card, Spinner, Table, TableBody, TableCell, TableHeader, TableHeaderCell,
  TableRow, Text, Title2, Title3, makeStyles,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { MockEntraGroup, MockEntraMembership, MockEntraUser } from '../types/domain';

const useStyles = makeStyles({
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(360px, 1fr))', gap: '16px', marginTop: '16px' },
  card: { padding: '16px 20px', borderRadius: 'var(--card-radius)' },
  full: { gridColumn: '1 / -1' },
});

export function MockEntraPage() {
  const styles = useStyles();
  const [users, setUsers] = useState<MockEntraUser[] | null>(null);
  const [groups, setGroups] = useState<MockEntraGroup[] | null>(null);
  const [memberships, setMemberships] = useState<MockEntraMembership[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      api.listMockEntraUsers(),
      api.listMockEntraGroups(),
      api.listMockEntraMemberships(),
    ])
      .then(([u, g, m]) => {
        setUsers(u);
        setGroups(g);
        setMemberships(m);
      })
      .catch((e: Error) => setError(e.message));
  }, []);

  const userById = useMemo(
    () => new Map((users ?? []).map((user) => [user.objectId, user])),
    [users],
  );

  if (error) return <Text>Fehler: {error}</Text>;
  if (!users || !groups || !memberships) return <Spinner label="Lade Mock Entra..." />;

  return (
    <div>
      <Title2>Mock Entra Portal</Title2>
      <Text>Development-only Sicht auf lokalen Benutzer-, Gruppen- und Membership-Stamm.</Text>

      <div className={styles.grid}>
        <Card className={styles.card}>
          <Title3>Benutzer</Title3>
          <Table size="extra-small">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Firma</TableHeaderCell>
                <TableHeaderCell>Sponsor</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {users.map((user) => (
                <TableRow key={user.objectId}>
                  <TableCell>
                    <Text weight="semibold" block>{user.displayName}</Text>
                    <Text size={200}>{user.mail}</Text>
                  </TableCell>
                  <TableCell>{user.companyName}</TableCell>
                  <TableCell>{user.sponsor}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>

        <Card className={styles.card}>
          <Title3>Gruppen</Title3>
          <Table size="extra-small">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Typ</TableHeaderCell>
                <TableHeaderCell>Workload</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {groups.map((group) => (
                <TableRow key={group.objectId}>
                  <TableCell>{group.displayName}</TableCell>
                  <TableCell><Badge>{group.groupType}</Badge></TableCell>
                  <TableCell>{group.workloadName ?? 'configuration required'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>

        <Card className={`${styles.card} ${styles.full}`}>
          <Title3>Mitgliedschaften</Title3>
          <Table size="extra-small">
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Gruppe</TableHeaderCell>
                <TableHeaderCell>Benutzer</TableHeaderCell>
                <TableHeaderCell>Object ID</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {memberships.map((membership) => {
                const user = userById.get(membership.entraObjectId);
                return (
                  <TableRow key={`${membership.groupId}-${membership.entraObjectId}`}>
                    <TableCell>{membership.groupName}</TableCell>
                    <TableCell>{user ? `${user.displayName} (${user.mail})` : membership.entraObjectId}</TableCell>
                    <TableCell>{membership.entraObjectId}</TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Card>
      </div>
    </div>
  );
}

