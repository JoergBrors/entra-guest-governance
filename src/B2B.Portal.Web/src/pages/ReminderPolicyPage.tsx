import { useEffect, useState } from 'react';
import {
  Title2, Text, Card, Button, Input, Field, Textarea, Spinner, makeStyles, tokens,
  MessageBar, MessageBarBody,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { ReminderStage } from '../types/domain';

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px', borderRadius: 'var(--card-radius)' },
  row: { display: 'flex', gap: '12px', flexWrap: 'wrap', alignItems: 'flex-end' },
  actions: { display: 'flex', gap: '8px', marginTop: '12px' },
  meta: { color: tokens.colorNeutralForeground3 },
  reorder: { display: 'flex', gap: '4px', flexDirection: 'column' },
  previewFrame: {
    width: '100%',
    height: '420px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
    marginTop: '8px',
    backgroundColor: '#ffffff',
  },
});

let nextTempId = -1;

/**
 * Admin-Seite "Erinnerungs-Policy" (Erweiterung 2026-08-30 "Invitation Reminder Worker"):
 * voll konfigurierbare, geordnete Stufenliste — kein hartkodierter Default. Genau eine Policy
 * pro Tenant (siehe ReminderPolicy.cs), Stufen werden komplett per PUT ersetzt (kein
 * partielles Patchen einzelner Stufen).
 */
export function ReminderPolicyPage() {
  const styles = useStyles();
  const [stages, setStages] = useState<ReminderStage[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  // Vorschau-HTML je Stufe (Index -> gerendertes Outlook-HTML), nur bei Bedarf per Button
  // geladen statt bei jedem Tastendruck — Vorschau ruft einen Server-Roundtrip auf (derselbe
  // Renderer wie beim echten Versand, siehe OutlookHtmlEmailRenderer, Backend).
  const [previewByIndex, setPreviewByIndex] = useState<Record<number, string>>({});
  const [previewLoading, setPreviewLoading] = useState<number | null>(null);

  useEffect(() => {
    api.getReminderPolicy()
      .then((policy) => setStages([...policy.stages].sort((a, b) => a.stageNumber - b.stageNumber)))
      .catch((e: Error) => setError(e.message));
  }, []);

  const renumber = (list: ReminderStage[]) =>
    list.map((s, i) => ({ ...s, stageNumber: i + 1 }));

  const addStage = () => {
    setStages((prev) => renumber([
      ...(prev ?? []),
      {
        stageNumber: 0,
        daysAfterInvite: 7,
        templateId: `reminder-stage-${nextTempId}`,
        templateSubject: 'Erinnerung: Ihre Einladung wartet noch',
        // HTML statt Plaintext (Erweiterung 2026-08-30 (Teil 2) "Outlook-HTML-Templates") —
        // nur der innere Inhalt, das Outlook-Tabellengeruest wird serverseitig automatisch
        // drumherum gebaut (siehe OutlookHtmlEmailRenderer).
        templateBody: '<p>Hallo {{DisplayName}},</p><p>Ihre Einladung für <strong>{{WorkloadName}}</strong> ist seit {{DaysSinceInvite}} Tagen offen.</p><p><a href="{{RedemptionLink}}" style="color:#0f6cbd;">Einladung jetzt annehmen</a></p>',
      },
    ]));
    nextTempId -= 1;
  };

  const previewStage = async (index: number) => {
    const stage = stages?.[index];
    if (!stage) return;
    setPreviewLoading(index);
    setError(null);
    try {
      const result = await api.previewReminderStage(stage.templateSubject, stage.templateBody);
      setPreviewByIndex((prev) => ({ ...prev, [index]: result.renderedHtml }));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setPreviewLoading(null);
    }
  };

  const removeStage = (index: number) => {
    setStages((prev) => (prev ? renumber(prev.filter((_, i) => i !== index)) : prev));
  };

  const moveStage = (index: number, direction: -1 | 1) => {
    setStages((prev) => {
      if (!prev) return prev;
      const target = index + direction;
      if (target < 0 || target >= prev.length) return prev;
      const next = [...prev];
      [next[index], next[target]] = [next[target], next[index]];
      return renumber(next);
    });
  };

  const updateStage = (index: number, patch: Partial<ReminderStage>) => {
    setStages((prev) => prev?.map((s, i) => (i === index ? { ...s, ...patch } : s)) ?? prev);
  };

  const save = async () => {
    if (!stages) return;
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      const policy = await api.updateReminderPolicy(stages);
      setStages([...policy.stages].sort((a, b) => a.stageNumber - b.stageNumber));
      setInfo('Erinnerungs-Policy gespeichert.');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  if (!stages) {
    return <Spinner label="Lade Erinnerungs-Policy…" />;
  }

  return (
    <div>
      <Title2>Erinnerungs-Policy</Title2>
      <Text>
        Mehrstufige Erinnerungs-Mails für offene Einladungen. Der periodische Worker
        (InvitationReminderWorker) sendet Stufe für Stufe, sobald die Einladung länger als die
        konfigurierte Anzahl Tage offen ist — eine Stufe wird nie zweimal an denselben Gast gesendet.
      </Text>

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
        {stages.length === 0 && (
          <Card className={styles.card}>
            <Text>Keine Stufen konfiguriert — Erinnerungen sind aktuell deaktiviert.</Text>
          </Card>
        )}
        {stages.map((stage, index) => (
          <Card key={index} className={styles.card}>
            <Text weight="semibold" block>Stufe {stage.stageNumber}</Text>
            <div className={styles.row} style={{ marginTop: 8 }}>
              <Field label="Tage nach Einladung">
                <Input
                  type="number"
                  value={String(stage.daysAfterInvite)}
                  onChange={(_, d) => updateStage(index, { daysAfterInvite: Number(d.value) || 0 })}
                />
              </Field>
              <Field label="Template-ID">
                <Input value={stage.templateId} onChange={(_, d) => updateStage(index, { templateId: d.value })} />
              </Field>
              <div className={styles.reorder}>
                <Button size="small" disabled={index === 0} onClick={() => moveStage(index, -1)}>↑</Button>
                <Button size="small" disabled={index === stages.length - 1} onClick={() => moveStage(index, 1)}>↓</Button>
              </div>
              <Button size="small" appearance="secondary" onClick={() => removeStage(index)}>Entfernen</Button>
            </div>
            <div className={styles.row} style={{ marginTop: 8 }}>
              <Field label="Betreff" style={{ flexGrow: 1 }}>
                <Input
                  value={stage.templateSubject}
                  onChange={(_, d) => updateStage(index, { templateSubject: d.value })}
                />
              </Field>
            </div>
            <div className={styles.row} style={{ marginTop: 8 }}>
              <Field
                label="HTML-Inhalt (Platzhalter: {{DisplayName}}, {{WorkloadName}}, {{DaysSinceInvite}}, {{RedemptionLink}}) — wird automatisch in ein Outlook-kompatibles E-Mail-Geruest eingebettet"
                style={{ flexGrow: 1 }}
              >
                <Textarea
                  value={stage.templateBody}
                  onChange={(_, d) => updateStage(index, { templateBody: d.value })}
                  rows={4}
                  style={{ fontFamily: 'monospace' }}
                />
              </Field>
            </div>
            <div className={styles.actions}>
              <Button
                size="small"
                appearance="secondary"
                disabled={previewLoading === index}
                onClick={() => previewStage(index)}
              >
                {previewLoading === index ? 'Rendert…' : 'Outlook-Vorschau anzeigen'}
              </Button>
            </div>
            {previewByIndex[index] !== undefined && (
              <iframe
                title={`Vorschau Stufe ${stage.stageNumber}`}
                className={styles.previewFrame}
                srcDoc={previewByIndex[index]}
                sandbox=""
              />
            )}
          </Card>
        ))}
      </div>

      <div className={styles.actions}>
        <Button appearance="secondary" onClick={addStage}>Stufe hinzufügen</Button>
        <Button appearance="primary" disabled={busy} onClick={save}>Speichern</Button>
      </div>
    </div>
  );
}
