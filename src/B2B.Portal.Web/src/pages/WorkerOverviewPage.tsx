import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Badge, Button, Card, MessageBar, MessageBarBody, Spinner, Text, Title2, Title3,
  makeStyles, tokens,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { JobStatusResponse, WorkerControlState } from '../types/domain';

// Erklaert je periodischem Worker, WAS er tut und wie oft — ergaenzt die reine
// Job-Typ-Statistik oben um die tatsaechlichen BackgroundServices (B2B.Portal.Worker), die
// im Hintergrund selbststaendig Jobs einreihen bzw. den Mock-Entra-Bestand abgleichen.
const WORKER_DESCRIPTIONS: Record<string, string> = {
  ApplicationSignInSyncWorker: 'Synchronisiert Mock-Entra-App-Anmeldungen fuer aktive Assignments (alle 10 Min).',
  InvitationReminderWorker: 'Scannt offene Einladungen und reiht faellige Reminder-Jobs ein (alle 10 Min).',
  WorkloadPatternSyncWorker: 'Gleicht Workload-Gruppen-Pattern gegen das Mock-Entra-Verzeichnis ab (alle 10 Min).',
  DiscoveryReconciliationWorker: 'Prueft, ob alle von Workloads referenzierten Gruppen im Verzeichnis noch existieren (alle 10 Min).',
};

const useStyles = makeStyles({
  list: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '16px' },
  card: { padding: '16px 20px', borderRadius: 'var(--card-radius)' },
  row: {
    display: 'grid',
    gridTemplateColumns: 'minmax(200px, 1.2fr) repeat(3, minmax(90px, auto)) minmax(160px, auto) auto',
    gap: '12px',
    alignItems: 'center',
  },
  workerRow: {
    display: 'grid',
    gridTemplateColumns: 'minmax(220px, 1fr) minmax(140px, auto) minmax(80px, auto) minmax(120px, auto)',
    gridTemplateAreas: '"name lastrun result triggeredby" "summary summary summary summary" "actions actions actions actions"',
    gap: '8px 12px',
    alignItems: 'start',
  },
  stat: { display: 'flex', flexDirection: 'column', gap: '2px' },
  statCount: { fontSize: tokens.fontSizeBase500 },
  meta: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', gap: '8px', flexWrap: 'wrap', justifyContent: 'flex-end' },
  errorLink: { cursor: 'pointer' },
});

// Gleiches Intervall wie JobsPage.AUTO_REFRESH_INTERVAL_MS — beide Seiten zeigen denselben
// zugrundeliegenden Job-Bestand, daher identisches Polling-Verhalten.
const AUTO_REFRESH_INTERVAL_MS = 5000;

// Job-Typen mit generischem "Jetzt ausführen"-Trigger — die einzigen ohne fachlichen
// Kontext-Parameter (kein Guest/Workload/Role aus einem bestehenden Flow). Alle anderen
// Job-Typen entstehen ausschliesslich kontextuell (z.B. GrantWorkloadRole aus der
// Workloads-Admin-Seite) und werden hier nur read-only mit Statistik angezeigt.
const TRIGGERABLE_JOB_TYPES = new Set(['RunDiscovery', 'RunReconciliation']);

// Erklaert pro Job-Typ, WAS ihn tatsaechlich ausloest — ohne das bleibt in der reinen
// Zahlen-Uebersicht unklar, ob ein Job automatisch, aus einem UI-Flow oder manuell entsteht.
// Nur Job-Typen mit einem tatsaechlich existierenden Trigger-Pfad im Code sind hier gelistet
// (siehe JobTypes in B2B.Portal.Domain/Entities/Job.cs fuer alle 17 Konstanten — die uebrigen
// haben aktuell KEINEN Ausloeser im Code, siehe Fallback-Text unten).
const JOB_TYPE_TRIGGER_DESCRIPTIONS: Record<string, string> = {
  InviteGuest: 'Gäste-Import / "Gast einladen" (Guest Pool, Admin)',
  ResendInvitation: 'Erneutes Senden einer offenen Einladung (Guest Pool, Admin)',
  GrantWorkloadRole: '"Gast zuweisen" auf der Workloads-Admin-Seite',
  RevokeWorkloadRole: '"Unassign" im Guest Pool oder bei Review-Entscheidung "Remove"',
  DeployScenario: '"Deploy" auf einem Workload-Szenario (Scenarios-Seite)',
  SyncWorkloadPatternResources: 'Automatisch bei Workload erstellen/bearbeiten, wenn Gruppen-Pattern gesetzt sind',
  ApplyReviewDecision: 'Review-Entscheidung (Keep/Remove) auf der Reviews-Seite',
  StartReview: 'Start einer Review-Instanz (turnusmäßig oder aus dem Review-Flow)',
  ValidateDeletion: 'Deletion-Gate-Prüfung vor Disable/Delete eines Gasts',
  DisableGuest: 'Governance-Freigabe zum Deaktivieren eines Gasts',
  DeleteGuest: 'Governance-Freigabe zum endgültigen Löschen eines Gasts (nur mit ALLOW_GUEST_DELETE)',
  SendNotification: 'Automatisch als Folge-Job anderer Aktionen (z. B. nach Invite/Grant)',
  RunDiscovery: 'Manuell über "Jetzt ausführen" (unten) oder künftig automatisch/turnusmäßig',
  RunReconciliation: 'Manuell über "Jetzt ausführen" (unten) oder künftig automatisch/turnusmäßig',
};
const NO_TRIGGER_YET = 'Kein Trigger-Pfad im Code vorhanden — dieser Job-Typ kann aktuell nicht entstehen.';

interface JobTypeSummary {
  jobType: string;
  total: number;
  success: number;
  error: number;
  pending: number;
  lastUpdatedAt: string;
}

function summarize(jobs: JobStatusResponse[]): JobTypeSummary[] {
  const byType = new Map<string, JobStatusResponse[]>();
  for (const job of jobs) {
    const list = byType.get(job.jobType) ?? [];
    list.push(job);
    byType.set(job.jobType, list);
  }

  return [...byType.entries()]
    .map(([jobType, list]) => ({
      jobType,
      total: list.length,
      success: list.filter((j) => j.status === 'Success').length,
      error: list.filter((j) => j.status === 'Failed' || j.status === 'DeadLetter').length,
      pending: list.filter((j) => j.status === 'Pending' || j.status === 'Running' || j.status === 'Retry').length,
      lastUpdatedAt: list.reduce((latest, j) => (j.updatedAt > latest ? j.updatedAt : latest), list[0].updatedAt),
    }))
    .sort((a, b) => a.jobType.localeCompare(b.jobType));
}

/**
 * Worker/Trigger-Uebersicht (Erweiterung 2026-08-30): aggregiert die bestehenden Jobs (GET
 * /api/jobs) PRO JOB-TYP statt pro Einzeljob wie JobsPage. Zusaetzlich: "Jetzt ausführen" fuer
 * kontextlose Job-Typen (Discovery/Reconciliation) und ein Klick auf die Fehlerzahl springt
 * gefiltert in JobsPage (?jobType=X&status=Failed) — die beiden Seiten sind dadurch verlinkt,
 * kein separates Job-Listen-UI wird hier dupliziert.
 */
export function WorkerOverviewPage() {
  const styles = useStyles();
  const navigate = useNavigate();
  const [jobs, setJobs] = useState<JobStatusResponse[] | null>(null);
  const [workers, setWorkers] = useState<WorkerControlState[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [triggering, setTriggering] = useState<string | null>(null);
  const [workerActionPending, setWorkerActionPending] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const reload = () => {
    api.listJobs()
      .then(setJobs)
      .catch((e: Error) => setError(e.message));
    api.listWorkers()
      .then(setWorkers)
      .catch((e: Error) => setError(e.message));
  };

  useEffect(() => {
    reload();
    const interval = setInterval(reload, AUTO_REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, []);

  const handlePauseWorker = async (workerName: string) => {
    setError(null);
    setWorkerActionPending(workerName);
    try {
      await api.pauseWorker(workerName);
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorkerActionPending(null);
    }
  };

  const handleResumeWorker = async (workerName: string) => {
    setError(null);
    setWorkerActionPending(workerName);
    try {
      await api.resumeWorker(workerName);
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorkerActionPending(null);
    }
  };

  const handleTriggerWorker = async (workerName: string) => {
    setError(null);
    setInfo(null);
    setWorkerActionPending(workerName);
    try {
      const result = await api.triggerWorker(workerName);
      setInfo(result.message);
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorkerActionPending(null);
    }
  };

  const runDiscovery = async () => {
    setError(null);
    setInfo(null);
    setTriggering('RunDiscovery');
    try {
      const job = await api.triggerDiscovery();
      setInfo(`Discovery-Job ${job.id} eingereiht.`);
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setTriggering(null);
    }
  };

  const runReconciliation = async () => {
    setError(null);
    setInfo(null);
    setTriggering('RunReconciliation');
    try {
      const result = await api.triggerReconciliation();
      setInfo(`${result.queuedJobCount} Reconciliation-Job(s) eingereiht.`);
      reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setTriggering(null);
    }
  };

  const openFailedInJobs = (jobType: string) => {
    navigate(`/jobs?jobType=${encodeURIComponent(jobType)}&status=Failed`);
  };

  if (!jobs) {
    return <Spinner label="Lade Worker-Übersicht..." />;
  }

  const summaries = summarize(jobs);

  return (
    <div>
      <Title2>Worker</Title2>
      <Text>
        Übersicht pro Job-Typ (Trigger): wie oft ein Job-Typ gelaufen ist, mit welchem Ergebnis, und
        wo möglich ein manueller Anstoß. Details zu einzelnen Jobs stehen weiterhin unter{' '}
        <Button appearance="transparent" size="small" onClick={() => navigate('/jobs')}>Jobs</Button>.
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

      <Title3 style={{ marginTop: 24, display: 'block' }}>Periodische Worker</Title3>
      <Text size={200} className={styles.meta}>
        Hintergrundprozesse in B2B.Portal.Worker, die selbststaendig alle 10 Minuten laufen —
        pausierbar/fortsetzbar (uebersteht einen Neustart) und manuell sofort auslösbar.
      </Text>
      <div className={styles.list}>
        {!workers && <Spinner size="tiny" label="Lade Worker-Status…" />}
        {workers?.map((worker) => (
          <Card key={worker.workerName} className={styles.card}>
            <div className={styles.workerRow}>
              <div style={{ gridArea: 'name' }}>
                <Text weight="semibold" block>
                  {worker.workerName}{' '}
                  {worker.isPaused && (
                    <Badge appearance="tint" color="warning" style={{ marginLeft: 4 }}>Pausiert</Badge>
                  )}
                </Text>
                <Text size={200} className={styles.meta}>
                  {WORKER_DESCRIPTIONS[worker.workerName] ?? ''}
                </Text>
                {worker.isPaused && worker.pausedBy && (
                  <Text size={200} className={styles.meta} block>
                    Pausiert von {worker.pausedBy} am {worker.pausedAt ? new Date(worker.pausedAt).toLocaleString() : ''}
                  </Text>
                )}
              </div>
              <div className={styles.stat} style={{ gridArea: 'lastrun' }}>
                <Text size={200} className={styles.meta}>Letzter Lauf</Text>
                <Text size={200}>
                  {worker.lastRunCompletedAt ? new Date(worker.lastRunCompletedAt).toLocaleString() : 'Noch nie gelaufen'}
                </Text>
              </div>
              <div className={styles.stat} style={{ gridArea: 'result' }}>
                <Text size={200} className={styles.meta}>Ergebnis</Text>
                {worker.lastRunSucceeded === true && <Badge appearance="tint" color="success">OK</Badge>}
                {worker.lastRunSucceeded === false && <Badge appearance="tint" color="danger">Fehler</Badge>}
                {worker.lastRunSucceeded == null && <Text size={200}>—</Text>}
              </div>
              <div className={styles.stat} style={{ gridArea: 'triggeredby' }}>
                <Text size={200} className={styles.meta}>Ausgeloest von</Text>
                <Text size={200}>{worker.lastTriggeredBy ?? '—'}</Text>
              </div>
              <div className={styles.stat} style={{ gridArea: 'summary' }}>
                <Text size={200} className={styles.meta}>Was wurde getan</Text>
                <Text size={200}>{worker.lastRunSummary ?? '—'}</Text>
              </div>
              <div className={styles.actions} style={{ gridArea: 'actions' }}>
                <Button
                  size="small"
                  disabled={workerActionPending === worker.workerName}
                  onClick={() => handleTriggerWorker(worker.workerName)}
                >
                  Jetzt ausführen
                </Button>
                {worker.isPaused ? (
                  <Button
                    size="small"
                    appearance="primary"
                    disabled={workerActionPending === worker.workerName}
                    onClick={() => handleResumeWorker(worker.workerName)}
                  >
                    Fortsetzen
                  </Button>
                ) : (
                  <Button
                    size="small"
                    appearance="secondary"
                    disabled={workerActionPending === worker.workerName}
                    onClick={() => handlePauseWorker(worker.workerName)}
                  >
                    Pausieren
                  </Button>
                )}
              </div>
            </div>
          </Card>
        ))}
      </div>

      <Title3 style={{ marginTop: 24, display: 'block' }}>Job-Typen</Title3>
      <div className={styles.list}>
        {summaries.length === 0 && (
          <Card className={styles.card}>
            <Text>Es sind noch keine Jobs gelaufen.</Text>
          </Card>
        )}
        {summaries.map((summary) => (
          <Card key={summary.jobType} className={styles.card}>
            <div className={styles.row}>
              <div>
                <Text weight="semibold" block>{summary.jobType}</Text>
                <Text size={200} className={styles.meta}>
                  {JOB_TYPE_TRIGGER_DESCRIPTIONS[summary.jobType] ?? NO_TRIGGER_YET}
                </Text>
              </div>
              <div className={styles.stat}>
                <Text size={200} className={styles.meta}>Gesamt</Text>
                <Text className={styles.statCount}>{summary.total}</Text>
              </div>
              <div className={styles.stat}>
                <Text size={200} className={styles.meta}>Erfolgreich</Text>
                <Text className={styles.statCount} style={{ color: tokens.colorPaletteGreenForeground1 }}>
                  {summary.success}
                </Text>
              </div>
              <div className={styles.stat}>
                <Text size={200} className={styles.meta}>Fehler</Text>
                {summary.error > 0 ? (
                  <Badge
                    appearance="tint"
                    color="danger"
                    className={styles.errorLink}
                    onClick={() => openFailedInJobs(summary.jobType)}
                    title="Fehlgeschlagene Jobs dieses Typs in der Jobs-Übersicht anzeigen"
                  >
                    {summary.error}
                  </Badge>
                ) : (
                  <Text className={styles.statCount}>0</Text>
                )}
              </div>
              <div className={styles.stat}>
                <Text size={200} className={styles.meta}>Letzte Aktivität</Text>
                <Text size={200}>{new Date(summary.lastUpdatedAt).toLocaleString()}</Text>
              </div>
              <div className={styles.actions}>
                {summary.error > 0 && (
                  <Button size="small" appearance="secondary" onClick={() => openFailedInJobs(summary.jobType)}>
                    Fehler ansehen
                  </Button>
                )}
                {TRIGGERABLE_JOB_TYPES.has(summary.jobType) && (
                  <Button
                    size="small"
                    appearance="primary"
                    disabled={triggering !== null}
                    onClick={() => (summary.jobType === 'RunDiscovery' ? runDiscovery() : runReconciliation())}
                  >
                    Jetzt ausführen
                  </Button>
                )}
              </div>
            </div>
          </Card>
        ))}

        {[...TRIGGERABLE_JOB_TYPES].filter((t) => !summaries.some((s) => s.jobType === t)).map((jobType) => (
          <Card key={jobType} className={styles.card}>
            <div className={styles.row}>
              <div>
                <Text weight="semibold" block>{jobType}</Text>
                <Text size={200} className={styles.meta}>{JOB_TYPE_TRIGGER_DESCRIPTIONS[jobType]}</Text>
              </div>
              <div className={styles.stat}>
                <Text size={200} className={styles.meta}>Gesamt</Text>
                <Text className={styles.statCount}>0</Text>
              </div>
              <div />
              <div />
              <Text size={200} className={styles.meta}>Noch nie gelaufen</Text>
              <div className={styles.actions}>
                <Button
                  size="small"
                  appearance="primary"
                  disabled={triggering !== null}
                  onClick={() => (jobType === 'RunDiscovery' ? runDiscovery() : runReconciliation())}
                >
                  Jetzt ausführen
                </Button>
              </div>
            </div>
          </Card>
        ))}
      </div>
    </div>
  );
}
