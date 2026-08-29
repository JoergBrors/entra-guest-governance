import { useState } from 'react';
import {
  Title2, Text, Card, Badge, Button, Input, Field, Dropdown, Option, Table, TableHeader,
  TableRow, TableHeaderCell, TableBody, TableCell, makeStyles, tokens, MessageBar, MessageBarBody,
} from '@fluentui/react-components';
import { api } from '../api/client';
import { GUEST_IMPORT_RESERVED_FIELDS } from '../types/domain';
import type { GuestImportColumnMapping, GuestImportResult } from '../types/domain';

const useStyles = makeStyles({
  step: { marginTop: '24px' },
  form: { display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap', marginTop: '8px' },
  mappingRow: { display: 'flex', gap: '12px', alignItems: 'center', marginTop: '8px' },
  actions: { display: 'flex', gap: '8px', marginTop: '16px' },
  warning: { color: tokens.colorPaletteRedForeground1 },
  summary: { display: 'flex', gap: '16px', marginTop: '12px', flexWrap: 'wrap' },
});

/**
 * Excel-Gäste-Import (Phase 4): Datei hochladen -> Sheet/Startzeile/-spalte wählen -> Spalten
 * auf Zielschlüssel mappen -> Vorschau (Dry-Run, keine Schreibzugriffe) -> Import ausführen.
 * Freie (nicht-reservierte) Zielschlüssel werden zu ScenarioResourceRule.Fields-Schlüsseln
 * fürs Regel-Matching (siehe GuestImportService, Backend).
 */
export function GuestImportPage() {
  const styles = useStyles();
  const [file, setFile] = useState<File | null>(null);
  const [sheetNames, setSheetNames] = useState<string[]>([]);
  const [selectedSheet, setSelectedSheet] = useState('');
  const [headerRowIndex, setHeaderRowIndex] = useState(1);
  const [dataStartColumnIndex, setDataStartColumnIndex] = useState(1);
  const [columnHeaders, setColumnHeaders] = useState<string[]>([]);
  const [columnToField, setColumnToField] = useState<Record<number, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [previewResult, setPreviewResult] = useState<GuestImportResult | null>(null);
  const [commitResult, setCommitResult] = useState<GuestImportResult | null>(null);
  const [busy, setBusy] = useState(false);

  const mapping = (): GuestImportColumnMapping => ({
    sheetName: selectedSheet,
    headerRowIndex,
    dataStartColumnIndex,
    columnToField,
  });

  const handleInspect = async () => {
    if (!file) return;
    setError(null);
    setBusy(true);
    try {
      const result = await api.inspectGuestImportFile(file, selectedSheet || null, headerRowIndex, dataStartColumnIndex);
      setSheetNames(result.sheetNames);
      setSelectedSheet((prev) => prev || result.sheetNames[0] || '');
      setColumnHeaders(result.columnHeaders);
      setColumnToField({});
      setPreviewResult(null);
      setCommitResult(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const handlePreview = async () => {
    if (!file) return;
    setError(null);
    setBusy(true);
    try {
      const result = await api.previewGuestImport(file, mapping());
      setPreviewResult(result);
      setCommitResult(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const handleCommit = async () => {
    if (!file) return;
    setError(null);
    setBusy(true);
    try {
      const result = await api.commitGuestImport(file, mapping());
      setCommitResult(result);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const resultToShow = commitResult ?? previewResult;

  return (
    <div>
      <Title2>Gäste-Import (Excel)</Title2>
      <Text>
        Excel-Datei hochladen, Spalten auf Mail/DisplayName/Workload/Szenario sowie freie
        fachliche Schlüssel (z.B. "Rolle") mappen, Vorschau prüfen, dann importieren.
      </Text>

      {error && (
        <MessageBar intent="error" style={{ marginTop: 12 }}>
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      <Card className={styles.step}>
        <Text weight="semibold" block>1. Datei &amp; Startposition</Text>
        <div className={styles.form}>
          <Field label="Excel-Datei (.xlsx)">
            <input
              type="file"
              accept=".xlsx"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            />
          </Field>
          <Field label="Kopfzeile (Zeile)">
            <Input
              type="number"
              value={String(headerRowIndex)}
              onChange={(_, d) => setHeaderRowIndex(Number(d.value) || 1)}
              style={{ width: 80 }}
            />
          </Field>
          <Field label="Erste Datenspalte">
            <Input
              type="number"
              value={String(dataStartColumnIndex)}
              onChange={(_, d) => setDataStartColumnIndex(Number(d.value) || 1)}
              style={{ width: 80 }}
            />
          </Field>
          <Button appearance="primary" disabled={!file || busy} onClick={handleInspect}>
            Datei einlesen
          </Button>
        </div>

        {sheetNames.length > 0 && (
          <Field label="Sheet" style={{ marginTop: 12, maxWidth: 240 }}>
            <Dropdown
              value={selectedSheet}
              selectedOptions={[selectedSheet]}
              onOptionSelect={(_, d) => setSelectedSheet(d.optionValue ?? selectedSheet)}
            >
              {sheetNames.map((name) => (
                <Option key={name} value={name}>{name}</Option>
              ))}
            </Dropdown>
          </Field>
        )}
      </Card>

      {columnHeaders.length > 0 && (
        <Card className={styles.step}>
          <Text weight="semibold" block>2. Spalten-Mapping</Text>
          <Text size={200} block>
            Reservierte Zielschlüssel: {GUEST_IMPORT_RESERVED_FIELDS.join(', ')}. Alles
            andere wird zum freien fachlichen Schlüssel (Fields), gegen den
            Szenario-Regeln gematcht werden.
          </Text>
          {columnHeaders.map((header, index) => (
            <div key={index} className={styles.mappingRow}>
              <Text style={{ minWidth: 160 }}>{header || `Spalte ${index + 1}`}</Text>
              <Text>→</Text>
              <Input
                list="guest-import-field-options"
                placeholder="Zielschlüssel (z.B. Mail, Rolle, ...)"
                value={columnToField[index] ?? ''}
                onChange={(_, d) => setColumnToField((prev) => ({ ...prev, [index]: d.value }))}
              />
            </div>
          ))}
          <datalist id="guest-import-field-options">
            {GUEST_IMPORT_RESERVED_FIELDS.map((f) => <option key={f} value={f} />)}
          </datalist>

          <div className={styles.actions}>
            <Button appearance="primary" disabled={busy || !selectedSheet} onClick={handlePreview}>
              Vorschau
            </Button>
            <Button
              disabled={busy || !previewResult || previewResult.warningCount === previewResult.rows.length}
              onClick={handleCommit}
            >
              Import ausführen
            </Button>
          </div>
        </Card>
      )}

      {resultToShow && (
        <Card className={styles.step}>
          <Text weight="semibold" block>{commitResult ? 'Import-Ergebnis' : 'Vorschau'}</Text>
          <div className={styles.summary}>
            <Badge appearance="tint" color="brand">{resultToShow.newGuestCount} neue Gäste</Badge>
            <Badge appearance="tint" color="informative">{resultToShow.updatedGuestCount} aktualisiert</Badge>
            <Badge appearance="tint" color="success">{resultToShow.assignmentCount} Zuweisung(en)</Badge>
            {resultToShow.warningCount > 0 && (
              <Badge appearance="tint" color="danger">{resultToShow.warningCount} Warnung(en)</Badge>
            )}
          </div>

          <Table style={{ marginTop: 12 }}>
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Zeile</TableHeaderCell>
                <TableHeaderCell>Mail</TableHeaderCell>
                <TableHeaderCell>Neu/Aktualisiert</TableHeaderCell>
                <TableHeaderCell>Zuweisungen</TableHeaderCell>
                <TableHeaderCell>Warnungen</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {resultToShow.rows.map((row) => (
                <TableRow key={row.rowNumber}>
                  <TableCell>{row.rowNumber}</TableCell>
                  <TableCell>{row.mail || '—'}</TableCell>
                  <TableCell>{row.isNewGuest ? 'Neu' : row.dataChanged ? 'Aktualisiert' : '—'}</TableCell>
                  <TableCell>{row.matchedRoleNames.join(', ') || '—'}</TableCell>
                  <TableCell className={styles.warning}>
                    {row.warnings.map((w) => <div key={w.message}>{w.message}</div>)}
                    {row.foreignWorkloadImpacts.map((impact) => (
                      <div key={impact.assignmentId}>
                        Review markiert für Workload &quot;{impact.workloadName}&quot;
                      </div>
                    ))}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}
    </div>
  );
}
