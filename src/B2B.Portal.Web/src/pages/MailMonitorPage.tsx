import { useEffect, useState } from 'react';
import {
  Title2, Text, Card, Badge, Spinner, makeStyles, tokens, MessageBar, MessageBarBody,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { MailSinkEntry } from '../types/domain';

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px', borderRadius: 'var(--card-radius)' },
  row: {
    display: 'grid',
    gridTemplateColumns: 'minmax(160px, auto) minmax(200px, 1fr) minmax(160px, auto) auto',
    gap: '12px',
    alignItems: 'center',
  },
  meta: { color: tokens.colorNeutralForeground3 },
  dataRow: {
    marginTop: '8px',
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
    gap: '4px',
    fontFamily: 'monospace',
    fontSize: tokens.fontSizeBase200,
  },
  bodyFrame: {
    width: '100%',
    height: '320px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
    marginTop: '8px',
    backgroundColor: '#ffffff',
  },
});

// TemplateData.Body/ContentType werden separat gerendert (HTML-Vorschau statt Rohtext-Zeile),
// die restlichen Platzhalterwerte bleiben als Debug-Rohdaten sichtbar.
const RENDERED_SEPARATELY_KEYS = new Set(['Body', 'ContentType', 'Subject']);

// Gleiches Polling-Muster wie JobsPage/WorkerOverviewPage.AUTO_REFRESH_INTERVAL_MS.
const AUTO_REFRESH_INTERVAL_MS = 5000;

/**
 * Admin-Seite "Mail Monitor" (Erweiterung 2026-08-30 "Mail Monitor"): zeigt den bisher
 * nirgends erreichbaren MockEmailProvider.Sink (siehe EmailProviders.cs) — LOCAL_MOCK-only,
 * GovernanceAdmin-only. Neueste E-Mails zuerst.
 */
export function MailMonitorPage() {
  const styles = useStyles();
  const [entries, setEntries] = useState<MailSinkEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = () => {
    api.listMailSink()
      .then(setEntries)
      .catch((e: Error) => setError(e.message));
  };

  useEffect(() => {
    reload();
    const interval = setInterval(reload, AUTO_REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, []);

  if (!entries) {
    return <Spinner label="Lade Mail-Sink…" />;
  }

  return (
    <div>
      <Title2>Mail Monitor</Title2>
      <Text>Mock-E-Mail-Vorschau (LOCAL_MOCK) — keine echten E-Mails werden versendet.</Text>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      <div className={styles.list}>
        {entries.length === 0 && (
          <Card className={styles.card}>
            <Text>Es wurden noch keine E-Mails gesendet.</Text>
          </Card>
        )}
        {entries.map((entry, i) => (
          <Card key={i} className={styles.card}>
            <div className={styles.row}>
              <div>
                <Text size={200} className={styles.meta} block>Gesendet</Text>
                <Text>{new Date(entry.sentAt).toLocaleString()}</Text>
              </div>
              <div>
                <Text size={200} className={styles.meta} block>Von → An</Text>
                <Text className={styles.meta}>{entry.senderMailbox} → {entry.recipientMail}</Text>
              </div>
              <div>
                <Text size={200} className={styles.meta} block>Workload-Kontext</Text>
                <Text>{entry.workloadContext ?? '—'}</Text>
              </div>
              <Badge appearance="tint" color="brand">{entry.templateId}</Badge>
            </div>
            {entry.templateData.Subject && (
              <Text weight="semibold" block style={{ marginTop: 8 }}>{entry.templateData.Subject}</Text>
            )}
            <div className={styles.dataRow}>
              <Text size={200} className={styles.meta}>CorrelationId: {entry.correlationId}</Text>
              {Object.entries(entry.templateData)
                .filter(([key]) => !RENDERED_SEPARATELY_KEYS.has(key))
                .map(([key, value]) => (
                  <Text size={200} key={key} className={styles.meta}>{key}: {value}</Text>
                ))}
            </div>
            {entry.templateData.ContentType === 'text/html' && entry.templateData.Body ? (
              <iframe
                title={`Mail an ${entry.recipientMail}`}
                className={styles.bodyFrame}
                srcDoc={entry.templateData.Body}
                sandbox=""
              />
            ) : entry.templateData.Body ? (
              <Text block style={{ marginTop: 8, whiteSpace: 'pre-wrap' }}>{entry.templateData.Body}</Text>
            ) : null}
          </Card>
        ))}
      </div>
    </div>
  );
}
