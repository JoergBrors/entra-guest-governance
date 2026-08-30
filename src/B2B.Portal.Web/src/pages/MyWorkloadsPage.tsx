import { useEffect, useState } from 'react';
import { Card, Title2, Title3, Text, Badge, Spinner, makeStyles } from '@fluentui/react-components';
import { api } from '../api/client';
import type { Workload, GuestAccount } from '../types/domain';
import { InvitationGuestList } from '../components/InvitationGuestList';

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
  managedSection: {
    marginBottom: '28px',
    paddingBottom: '20px',
    borderBottom: '1px solid var(--border-color, #e1dfdd)',
  },
});

/**
 * User-Ansicht (Blueprint 9): zeigt AUSSCHLIESSLICH zugeordnete Workloads und die
 * eigene Rolle/Zugriffe. Keine Graph-Details, keine Entra Object IDs, keine
 * Connector-/Job-Informationen (Blueprint 9 "keine Graph-Details in der normalen
 * User-Ansicht").
 *
 * Erweiterung 2026-08-30 "Scoped Visibility fuer Workload-/Scenario-Owner": zusaetzlich, NUR
 * fuer WorkloadOwner/ScenarioManager (bzw. GovernanceAdmin), ein Abschnitt ueber der
 * bestehenden "Meine Workloads"-Liste mit den Gaesten der selbst verwalteten Workloads inkl.
 * Einladungsstatus/Reminder/Redemption-Link — serverseitig gescoped ueber
 * GET /api/me/managed-guests (dieselbe Scoping-Logik wie GET /api/me/workloads), damit ein
 * normaler User (ohne diese Rollen) hier weiterhin NICHTS zusaetzliches sieht.
 */
export function MyWorkloadsPage({ canManageWorkloads = false }: { canManageWorkloads?: boolean }) {
  const styles = useStyles();
  const [workloads, setWorkloads] = useState<Workload[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [managedGuests, setManagedGuests] = useState<GuestAccount[] | null>(null);

  useEffect(() => {
    api.listMyWorkloads()
      .then(setWorkloads)
      .catch((e: Error) => setError(e.message));
  }, []);

  useEffect(() => {
    if (!canManageWorkloads) {
      return;
    }
    api.listManagedGuests()
      .then(setManagedGuests)
      .catch(() => setManagedGuests([]));
  }, [canManageWorkloads]);

  if (error) {
    return <Text>Fehler beim Laden: {error}</Text>;
  }

  if (!workloads) {
    return <Spinner label="Lade Workloads…" />;
  }

  return (
    <div>
      {canManageWorkloads && (
        <div className={styles.managedSection}>
          <Title3>Gäste meiner Workloads</Title3>
          <Text>Einladungsstatus, Reminder und Mock-Redemption-Link für Gäste in Workloads/Szenarien, die du verwaltest.</Text>
          {!managedGuests ? (
            <Spinner label="Lade Gäste…" />
          ) : (
            <div style={{ marginTop: 12 }}>
              <InvitationGuestList guests={managedGuests} />
            </div>
          )}
        </div>
      )}

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
