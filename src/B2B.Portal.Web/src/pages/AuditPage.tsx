import { useEffect, useState } from 'react';
import {
  Title2, Text, Table, TableHeader, TableRow, TableHeaderCell, TableBody, TableCell,
  Badge, Spinner,
} from '@fluentui/react-components';
import { api } from '../api/client';
import type { AuditEvent } from '../types/domain';

/**
 * Admin/Governance-Ansicht "Audit" (Blueprint 9, 18.3). Nachvollziehbare fachliche und
 * technische Ereignisse — Rohdaten aus Graph werden hier NICHT durchgereicht
 * (Blueprint 18.3 "Graph-Rohantworten werden nicht ungefiltert gespeichert").
 */
export function AuditPage() {
  const [events, setEvents] = useState<AuditEvent[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.listAuditEvents().then(setEvents).catch((e: Error) => setError(e.message));
  }, []);

  if (error) return <Text>Fehler: {error}</Text>;
  if (!events) return <Spinner label="Lade Audit-Events…" />;

  return (
    <div>
      <Title2>Audit</Title2>
      <Text>Correlation-basierter Nachweis sicherheitsrelevanter Aktionen.</Text>

      <Table style={{ marginTop: 16 }}>
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Zeitpunkt</TableHeaderCell>
            <TableHeaderCell>Akteur</TableHeaderCell>
            <TableHeaderCell>Aktion</TableHeaderCell>
            <TableHeaderCell>Entität</TableHeaderCell>
            <TableHeaderCell>Ergebnis</TableHeaderCell>
            <TableHeaderCell>Correlation ID</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {events.length === 0 && (
            <TableRow>
              <TableCell colSpan={6}>Noch keine Audit-Events.</TableCell>
            </TableRow>
          )}
          {events.map((e) => (
            <TableRow key={e.id}>
              <TableCell>{new Date(e.timestamp).toLocaleString('de-DE')}</TableCell>
              <TableCell>{e.actor}</TableCell>
              <TableCell>{e.action}</TableCell>
              <TableCell>{e.entityType} {e.entityId.slice(0, 8)}</TableCell>
              <TableCell>
                <Badge color={e.result.toLowerCase().includes('block') ? 'danger' : 'success'}>
                  {e.result}
                </Badge>
              </TableCell>
              <TableCell>
                <Text size={200} font="monospace">{e.correlationId.slice(0, 8)}</Text>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
