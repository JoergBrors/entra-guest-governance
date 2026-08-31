import { useEffect, useState } from 'react';
import {
  Title2, Text, Card, Badge, Spinner, makeStyles, tokens, Button, MessageBar, MessageBarBody,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { ReviewInstance } from '../types/domain';

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px' },
  itemRow: { display: 'flex', justifyContent: 'space-between', padding: '4px 0', alignItems: 'flex-start', gap: '12px' },
  reason: { color: tokens.colorNeutralForeground3, maxWidth: '480px', textAlign: 'right' },
});

const decisionColor: Record<string, 'success' | 'danger' | 'warning' | 'informative'> = {
  Keep: 'success',
  Remove: 'danger',
  Escalated: 'warning',
  Pending: 'informative',
};

/**
 * Admin/Governance-Ansicht "Reviews" (Blueprint 9, 13.2 "Interne Review Engine").
 * Zeigt laufende Review-Instanzen mit Snapshot-Items. Keep/Remove-Entscheidungen werden
 * im MVP über die Worker-Jobs ApplyReviewDecision angewandt (siehe
 * B2B.Portal.Worker.Handlers.Reviews) — die Web-Aktion dafür ist ein nächster
 * Ausbauschritt (siehe docs/architecture/mvp-test-report.md).
 */
export function ReviewsPage() {
  const styles = useStyles();
  const [reviews, setReviews] = useState<ReviewInstance[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [busyItemId, setBusyItemId] = useState<string | null>(null);
  const [startingDiscovery, setStartingDiscovery] = useState(false);

  const reload = () => {
    api.listOpenReviews().then(setReviews).catch((e: Error) => setError(e.message));
  };

  useEffect(reload, []);

  const decide = async (reviewInstanceId: string, reviewItemId: string, decision: 'Keep' | 'Remove' | 'Escalated') => {
    setBusyItemId(reviewItemId);
    setError(null);
    try {
      await api.decideReviewItem(reviewInstanceId, reviewItemId, decision);
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusyItemId(null);
    }
  };

  const startDiscoveryReview = async () => {
    setError(null);
    setInfo(null);
    setStartingDiscovery(true);
    try {
      await api.triggerDiscoveryReview();
      setInfo('Discovery-Review eingereiht — erscheint hier, sobald der Worker sie verarbeitet hat.');
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setStartingDiscovery(false);
    }
  };

  if (!reviews) return <Spinner label="Lade Reviews…" />;

  return (
    <div>
      <Title2>Reviews</Title2>
      <Text>
        Laufende interne Review-Instanzen (Snapshot, Keep/Remove). Discovery-Items zeigen
        entdeckte Gruppenmitgliedschaften ohne formale Workload-Zuweisung (Actual State) —
        Keep/Remove klassifiziert diese nur, ändert aber nie automatisch einen Zugriff.
      </Text>
      <div style={{ marginTop: 8 }}>
        <Button size="small" appearance="secondary" disabled={startingDiscovery} onClick={startDiscoveryReview}>
          Discovery-Review jetzt starten
        </Button>
      </div>
      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}
      {info && (
        <MessageBar intent="success" style={{ marginTop: 12 }}>
          <MessageBarBody>{info}</MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.list}>
        {reviews.length === 0 && <Text>Keine offenen Reviews.</Text>}
        {reviews.map((r) => (
          <Card key={r.id} className={styles.card}>
            <Text weight="semibold">Review {r.id.slice(0, 8)}</Text>
            <Text size={200} block>Provider: {r.provider} · gestartet: {new Date(r.startedAt).toLocaleString('de-DE')}</Text>
            {r.items.map((item) => (
              <div key={item.id} className={styles.itemRow}>
                <Text size={200}>
                  {item.resourceAccessId
                    ? `Discovery: ${item.resourceAccessId.slice(0, 8)}`
                    : `Assignment ${item.assignmentId?.slice(0, 8) ?? '—'}`}
                </Text>
                {item.reason && <Text size={200} className={styles.reason}>{item.reason}</Text>}
                <Badge color={decisionColor[item.decision]}>{item.decision}</Badge>
                {item.decision === 'Pending' && (
                  <div>
                    <Button size="small" disabled={busyItemId === item.id} onClick={() => decide(r.id, item.id, 'Keep')}>
                      Keep
                    </Button>
                    <Button size="small" disabled={busyItemId === item.id} onClick={() => decide(r.id, item.id, 'Remove')}>
                      Remove
                    </Button>
                    {!item.resourceAccessId && (
                      <Button size="small" disabled={busyItemId === item.id} onClick={() => decide(r.id, item.id, 'Escalated')}>
                        Escalate
                      </Button>
                    )}
                  </div>
                )}
              </div>
            ))}
          </Card>
        ))}
      </div>
    </div>
  );
}
