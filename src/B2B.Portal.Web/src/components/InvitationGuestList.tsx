import type { ReactNode } from 'react';
import { useState } from 'react';
import {
  Badge, Button, Table, TableHeader, TableRow, TableHeaderCell, TableBody, TableCell, Text, makeStyles,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { GuestAccount } from '../types/domain';
import { invitationStatusOf } from '../types/domain';

const useStyles = makeStyles({
  link: { wordBreak: 'break-all', fontFamily: 'monospace', fontSize: '12px' },
  meta: { color: 'var(--nav-fg, #605e5c)' },
  actions: { display: 'flex', gap: '6px', flexWrap: 'wrap' },
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
 * Gemeinsame Tabellen-Ansicht "Gast + Einladungsstatus" (Erweiterung 2026-08-30 "Guest Pool
 * Filter/Invitation Reminder") — wird sowohl im Guest Pool (Admin, volle Liste) als auch auf
 * MyWorkloadsPage (Owner/ScenarioManager, serverseitig auf eigene Workloads beschraenkt)
 * verwendet, damit beide Ansichten exakt dieselbe Darstellung von Einladungsstatus,
 * Reminder-Anzahl und Mock-Redemption-Link zeigen. onGuestUpdated wird nach einem
 * erfolgreichen Resend aufgerufen, damit der Aufrufer bei Bedarf neu laden kann — diese
 * Komponente haelt selbst keinen Gast-Zustand.
 *
 * extraHeaderCells/renderExtraCells sind bewusst generische Slots (statt fester
 * Guest-Pool-Props wie "assignments"/"onUnassign") — GuestPoolPage nutzt sie fuer
 * UserType/Workload-Zuweisungen/Deletion-Gate, MyWorkloadsPage laesst sie einfach weg. Damit
 * bleibt diese Komponente die einzige Stelle, die Basis-Spalten+Aktionen kennt, statt dass
 * beide Seiten eine eigene Tabelle pflegen (siehe Bug: GuestPoolPage hatte vor dieser
 * Erweiterung eine komplett duplizierte Tabellenimplementierung).
 * nameLink wandelt den Anzeigenamen optional in einen Link um (GuestPoolPage verlinkt auf die
 * Detailseite, MyWorkloadsPage nicht).
 */
export function InvitationGuestList({
  guests, onGuestUpdated, extraHeaderCells, renderExtraCells, nameLink,
}: {
  guests: GuestAccount[];
  onGuestUpdated?: () => void;
  extraHeaderCells?: ReactNode[];
  renderExtraCells?: (guest: GuestAccount) => ReactNode[];
  nameLink?: (guest: GuestAccount) => ReactNode;
}) {
  const styles = useStyles();
  const [busyGuestId, setBusyGuestId] = useState<string | null>(null);
  const [copiedGuestId, setCopiedGuestId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const resend = async (guestId: string) => {
    setBusyGuestId(guestId);
    setError(null);
    try {
      await api.resendInvitation(guestId);
      onGuestUpdated?.();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusyGuestId(null);
    }
  };

  const copyLink = async (guestId: string, link: string) => {
    try {
      await navigator.clipboard.writeText(link);
      setCopiedGuestId(guestId);
      setTimeout(() => setCopiedGuestId((prev) => (prev === guestId ? null : prev)), 2000);
    } catch {
      // clipboard-Zugriff kann in restriktiven Browserkontexten fehlschlagen (z.B. kein
      // sicherer Kontext) — der Link steht ohnehin als Text daneben, kein Fallback noetig.
    }
  };

  if (guests.length === 0) {
    return <Text>Keine Gäste gefunden.</Text>;
  }

  return (
    <>
      {error && <Text style={{ color: 'var(--colorPaletteRedForeground1, #c50f1f)' }} block>{error}</Text>}
      <Table>
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Anzeigename</TableHeaderCell>
            <TableHeaderCell>E-Mail</TableHeaderCell>
            <TableHeaderCell>Status</TableHeaderCell>
            <TableHeaderCell>Einladung</TableHeaderCell>
            <TableHeaderCell>Reminder gesendet</TableHeaderCell>
            <TableHeaderCell>Redemption-Link (Mock)</TableHeaderCell>
            <TableHeaderCell>Aktionen</TableHeaderCell>
            {extraHeaderCells?.map((cell, i) => <TableHeaderCell key={i}>{cell}</TableHeaderCell>)}
          </TableRow>
        </TableHeader>
        <TableBody>
          {guests.map((g) => {
            const invitationStatus = invitationStatusOf(g.accountState);
            return (
              <TableRow key={g.id}>
                <TableCell>{nameLink ? nameLink(g) : g.displayName}</TableCell>
                <TableCell>{g.mail}</TableCell>
                <TableCell>
                  <Badge color={stateColor[g.accountState] ?? 'informative'}>{g.accountState}</Badge>
                </TableCell>
                <TableCell>
                  <Badge color={invitationStatus === 'accepted' ? 'success' : 'warning'}>
                    {invitationStatus === 'accepted' ? 'Angenommen' : 'Ausstehend'}
                  </Badge>
                </TableCell>
                <TableCell>
                  {g.lastReminderStageSent ? (
                    <Text>
                      Stufe {g.lastReminderStageSent}
                      {g.lastReminderSentAt && (
                        <Text className={styles.meta} size={200} block>
                          {new Date(g.lastReminderSentAt).toLocaleString()}
                        </Text>
                      )}
                    </Text>
                  ) : (
                    <Text className={styles.meta} size={200}>Keine</Text>
                  )}
                </TableCell>
                <TableCell>
                  {g.invitationRedemptionLink ? (
                    <Text className={styles.link}>{g.invitationRedemptionLink}</Text>
                  ) : (
                    <Text className={styles.meta} size={200}>—</Text>
                  )}
                </TableCell>
                <TableCell>
                  <div className={styles.actions}>
                    {invitationStatus === 'pending' && g.entraObjectId && (
                      <Button
                        size="small"
                        appearance="secondary"
                        disabled={busyGuestId === g.id}
                        onClick={() => resend(g.id)}
                      >
                        {busyGuestId === g.id ? 'Sendet…' : 'Erneut einladen'}
                      </Button>
                    )}
                    {g.invitationRedemptionLink && (
                      <Button size="small" appearance="secondary" onClick={() => copyLink(g.id, g.invitationRedemptionLink!)}>
                        {copiedGuestId === g.id ? 'Kopiert ✓' : 'Link kopieren'}
                      </Button>
                    )}
                  </div>
                </TableCell>
                {renderExtraCells?.(g).map((cell, i) => <TableCell key={i}>{cell}</TableCell>)}
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </>
  );
}
