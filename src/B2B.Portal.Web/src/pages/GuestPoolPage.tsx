import { useEffect, useState } from 'react';
import {
  Title2, Text, Table, TableHeader, TableRow, TableHeaderCell, TableBody, TableCell,
  Badge, Input, Button, Field, Spinner, makeStyles, MessageBar, MessageBarBody,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { GuestAccount, DeletionGateEvaluation } from '../types/domain';

const useStyles = makeStyles({
  form: {
    display: 'flex',
    gap: '12px',
    alignItems: 'flex-end',
    margin: '16px 0 24px',
  },
});

const stateColor: Record<string, 'success' | 'warning' | 'danger' | 'informative'> = {
  Active: 'success',
  Invited: 'informative',
  Discovered: 'informative',
  OrphanCandidate: 'warning',
  PendingRemoval: 'warning',
  Blocked: 'danger',
  Disabled: 'danger',
  Deleted: 'danger',
  Inactive: 'warning',
};

/**
 * Admin/Governance-Ansicht "Guest Pool" (Blueprint 9): Suchen, Firma, Sponsor,
 * Attribute, Workloads, Actual Access, Review-/Invitation-Historie. Im MVP zunächst
 * Liste + Invite + Deletion-Gate-Dry-Run (Blueprint 22 "Live Validation").
 */
export function GuestPoolPage() {
  const styles = useStyles();
  const [guests, setGuests] = useState<GuestAccount[] | null>(null);
  const [mail, setMail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [gateResult, setGateResult] = useState<Record<string, DeletionGateEvaluation>>({});

  const reload = () => api.listGuests().then(setGuests).catch((e: Error) => setError(e.message));

  useEffect(() => {
    reload();
  }, []);

  const handleInvite = async () => {
    if (!mail || !displayName) return;
    setBusy(true);
    setError(null);
    try {
      await api.inviteGuest(mail, displayName);
      setMail('');
      setDisplayName('');
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const handleValidateDeletion = async (guestId: string) => {
    try {
      const evaluation = await api.validateDeletion(guestId, /* gracePeriodReached */ true);
      setGateResult((prev) => ({ ...prev, [guestId]: evaluation }));
    } catch (e) {
      setError((e as Error).message);
    }
  };

  return (
    <div>
      <Title2>Guest Pool</Title2>
      <Text>Zentrale Gastidentitäten dieses Platform-Tenants (Blueprint 5.1).</Text>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.form}>
        <Field label="E-Mail">
          <Input value={mail} onChange={(_, d) => setMail(d.value)} placeholder="gast@firma.example" />
        </Field>
        <Field label="Anzeigename">
          <Input value={displayName} onChange={(_, d) => setDisplayName(d.value)} placeholder="Vorname Nachname" />
        </Field>
        <Button appearance="primary" disabled={busy || !mail || !displayName} onClick={handleInvite}>
          Gast einladen
        </Button>
      </div>

      {!guests ? (
        <Spinner label="Lade Gäste…" />
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Anzeigename</TableHeaderCell>
              <TableHeaderCell>E-Mail</TableHeaderCell>
              <TableHeaderCell>Status</TableHeaderCell>
              <TableHeaderCell>Deletion Gate (Dry Run)</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {guests.map((g) => (
              <TableRow key={g.id}>
                <TableCell>{g.displayName}</TableCell>
                <TableCell>{g.mail}</TableCell>
                <TableCell>
                  <Badge color={stateColor[g.accountState] ?? 'informative'}>{g.accountState}</Badge>
                </TableCell>
                <TableCell>
                  <Button size="small" onClick={() => handleValidateDeletion(g.id)}>
                    Prüfen
                  </Button>
                  {gateResult[g.id] && (
                    <div style={{ marginTop: 4 }}>
                      <Badge color={gateResult[g.id].result === 'Ready' ? 'success' : 'danger'}>
                        {gateResult[g.id].result}
                      </Badge>
                      {gateResult[g.id].blockers.length > 0 && (
                        <Text size={200} block>
                          Blocker: {gateResult[g.id].blockers.join(', ')}
                        </Text>
                      )}
                    </div>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
