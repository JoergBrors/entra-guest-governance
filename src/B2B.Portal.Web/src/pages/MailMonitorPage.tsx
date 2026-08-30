import { useEffect, useMemo, useState } from 'react';
import {
  Title2, Text, Card, Badge, Button, Input, Field, Select, Spinner, makeStyles, tokens,
  MessageBar, MessageBarBody,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { MailSinkEntry } from '../types/domain';

const useStyles = makeStyles({
  filterBar: {
    display: 'flex',
    gap: '12px',
    alignItems: 'flex-end',
    margin: '16px 0',
    flexWrap: 'wrap',
  },
  list: { display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '8px' },
  card: { padding: '12px 20px', borderRadius: 'var(--card-radius)' },
  row: {
    display: 'grid',
    gridTemplateColumns: 'minmax(140px, auto) minmax(180px, 1fr) minmax(220px, 1.4fr) minmax(140px, auto) auto',
    gap: '12px',
    alignItems: 'center',
  },
  meta: { color: tokens.colorNeutralForeground3 },
  details: {
    marginTop: '12px',
    paddingTop: '12px',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
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

// TemplateData.Body/ContentType/Subject werden separat gerendert (HTML-Vorschau statt
// Rohtext-Zeile), die restlichen Platzhalterwerte bleiben als Debug-Rohdaten sichtbar.
const RENDERED_SEPARATELY_KEYS = new Set(['Body', 'ContentType', 'Subject']);

// Gleiches Polling-Muster wie JobsPage/WorkerOverviewPage.AUTO_REFRESH_INTERVAL_MS.
const AUTO_REFRESH_INTERVAL_MS = 5000;

/**
 * Admin-Seite "Mail Monitor" (Erweiterung 2026-08-30, Teil 4 "Filterbare Liste + Details"):
 * zeigt den Cosmos-persistierten Mail-Sink (siehe CosmosMailSinkRepository) als kompakte,
 * filterbare Liste — Details (voller HTML-Body, Rohdaten) werden erst auf Klick pro Zeile
 * aufgeklappt, statt wie zuvor immer alle Mails vollstaendig inline zu rendern (bei vielen
 * Mails sonst schnell unuebersichtlich). LOCAL_MOCK-only, GovernanceAdmin-only.
 */
export function MailMonitorPage() {
  const styles = useStyles();
  const [entries, setEntries] = useState<MailSinkEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);

  const [recipientFilter, setRecipientFilter] = useState('');
  const [templateFilter, setTemplateFilter] = useState('');
  const [workloadFilter, setWorkloadFilter] = useState('');
  const [fromFilter, setFromFilter] = useState('');
  const [toFilter, setToFilter] = useState('');

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

  const templateOptions = useMemo(
    () => [...new Set((entries ?? []).map((e) => e.templateId))].sort(),
    [entries],
  );
  const workloadOptions = useMemo(
    () => [...new Set((entries ?? []).map((e) => e.workloadContext).filter((w): w is string => !!w))].sort(),
    [entries],
  );

  const filteredEntries = useMemo(() => {
    if (!entries) return null;
    const from = fromFilter ? new Date(fromFilter).getTime() : null;
    // toFilter ist ein Datum ohne Uhrzeit — bis Ende des Tages einschliessen.
    const to = toFilter ? new Date(toFilter).getTime() + 24 * 60 * 60 * 1000 - 1 : null;
    return entries.filter((e) => {
      if (recipientFilter && !e.recipientMail.toLowerCase().includes(recipientFilter.toLowerCase())) return false;
      if (templateFilter && e.templateId !== templateFilter) return false;
      if (workloadFilter && e.workloadContext !== workloadFilter) return false;
      const sentAtMs = new Date(e.sentAt).getTime();
      if (from !== null && sentAtMs < from) return false;
      if (to !== null && sentAtMs > to) return false;
      return true;
    });
  }, [entries, recipientFilter, templateFilter, workloadFilter, fromFilter, toFilter]);

  const clearFilters = () => {
    setRecipientFilter('');
    setTemplateFilter('');
    setWorkloadFilter('');
    setFromFilter('');
    setToFilter('');
  };

  const hasActiveFilters = !!(recipientFilter || templateFilter || workloadFilter || fromFilter || toFilter);

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

      <div className={styles.filterBar}>
        <Field label="Empfänger">
          <Input
            value={recipientFilter}
            onChange={(_, d) => setRecipientFilter(d.value)}
            placeholder="name@firma.example"
          />
        </Field>
        <Field label="Template">
          <Select value={templateFilter} onChange={(_, d) => setTemplateFilter(d.value)}>
            <option value="">Alle Templates</option>
            {templateOptions.map((t) => <option key={t} value={t}>{t}</option>)}
          </Select>
        </Field>
        <Field label="Workload-Kontext">
          <Select value={workloadFilter} onChange={(_, d) => setWorkloadFilter(d.value)}>
            <option value="">Alle</option>
            {workloadOptions.map((w) => <option key={w} value={w}>{w}</option>)}
          </Select>
        </Field>
        <Field label="Von">
          <Input type="date" value={fromFilter} onChange={(_, d) => setFromFilter(d.value)} />
        </Field>
        <Field label="Bis">
          <Input type="date" value={toFilter} onChange={(_, d) => setToFilter(d.value)} />
        </Field>
        {hasActiveFilters && (
          <Button appearance="secondary" onClick={clearFilters}>Filter zurücksetzen</Button>
        )}
      </div>

      <Text size={200} className={styles.meta}>
        {filteredEntries?.length ?? 0} von {entries.length} E-Mail(s)
      </Text>

      <div className={styles.list}>
        {filteredEntries?.length === 0 && (
          <Card className={styles.card}>
            <Text>{entries.length === 0 ? 'Es wurden noch keine E-Mails gesendet.' : 'Keine E-Mails entsprechen den Filtern.'}</Text>
          </Card>
        )}
        {filteredEntries?.map((entry, i) => {
          const selected = selectedIndex === i;
          return (
            <Card key={i} className={styles.card}>
              <div className={styles.row}>
                <Text size={200} className={styles.meta}>{new Date(entry.sentAt).toLocaleString()}</Text>
                <Text>{entry.recipientMail}</Text>
                <Text className={styles.meta}>{entry.templateData.Subject ?? '—'}</Text>
                <Badge appearance="tint" color="brand">{entry.templateId}</Badge>
                <Button size="small" appearance="secondary" onClick={() => setSelectedIndex(selected ? null : i)}>
                  {selected ? 'Details ausblenden' : 'Details'}
                </Button>
              </div>
              {selected && (
                <div className={styles.details}>
                  <div className={styles.row} style={{ gridTemplateColumns: '1fr 1fr' }}>
                    <div>
                      <Text size={200} className={styles.meta} block>Von → An</Text>
                      <Text>{entry.senderMailbox} → {entry.recipientMail}</Text>
                    </div>
                    <div>
                      <Text size={200} className={styles.meta} block>Workload-Kontext</Text>
                      <Text>{entry.workloadContext ?? '—'}</Text>
                    </div>
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
                </div>
              )}
            </Card>
          );
        })}
      </div>
    </div>
  );
}
