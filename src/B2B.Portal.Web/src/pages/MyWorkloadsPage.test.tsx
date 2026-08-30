import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MyWorkloadsPage } from './MyWorkloadsPage';
import { api } from '../api/client';
import type { Workload } from '../types/domain';

// Blueprint 9: "keine Graph-Details in der normalen User-Ansicht" — dieser Test stellt
// sicher, dass die User-Ansicht ("Meine Workloads") nur Workload-Namen und Rollen
// rendert, aber die technischen Ressourcen (resourceType/externalId) NICHT anzeigt.
//
// @fluentui/react-components wird hier mit schlanken HTML-Stand-ins gemockt: die
// transitive Abhängigkeit "tabster" liefert derzeit ein CJS-Bundle, dessen benannte
// Exporte unter Vitest/Node-ESM nicht statisch auflösbar sind (bekanntes
// Ökosystem-Interop-Problem, unabhängig vom Portal-Code). Für einen reinen
// Render-/Inhalts-Test genügen einfache Ersatzkomponenten.
vi.mock('@fluentui/react-components', () => ({
  Card: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  Title2: ({ children }: { children: React.ReactNode }) => <h2>{children}</h2>,
  Text: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
  Badge: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
  Spinner: ({ label }: { label?: string }) => <div>{label ?? 'Laden…'}</div>,
  makeStyles: () => () => ({}),
}));

vi.mock('../api/client', () => ({
  api: { listMyWorkloads: vi.fn() },
}));

const sampleWorkload: Workload = {
  id: 'wl-1',
  platformTenantId: 'tenant-a',
  name: 'SAP S/4 Projekt',
  owner: 'owner@contoso.example',
  templateId: null,
  active: true,
  isDefault: false,
  administrativeUnitExternalId: null,
  resourceNamePatterns: [],
  roles: [{ id: 'role-1', workloadId: 'wl-1', name: 'Reader', resourceMappings: [] }],
  resources: [{ id: 'res-1', workloadId: 'wl-1', resourceType: 'SecurityGroup', externalId: 'SG-SAP-READER', managed: true }],
};

describe('MyWorkloadsPage', () => {
  beforeEach(() => {
    vi.mocked(api.listMyWorkloads).mockResolvedValue([sampleWorkload]);
  });

  it('zeigt Workload-Namen und Rollen, aber keine technischen Ressourcendetails', async () => {
    render(<MyWorkloadsPage />);

    await waitFor(() => expect(screen.getByText('SAP S/4 Projekt')).toBeInTheDocument());

    expect(screen.getByText('Reader')).toBeInTheDocument();
    expect(screen.queryByText(/SecurityGroup/)).not.toBeInTheDocument();
    expect(screen.queryByText(/SG-SAP-READER/)).not.toBeInTheDocument();
  });

  it('zeigt einen Hinweis, wenn keine Workloads zugeordnet sind', async () => {
    vi.mocked(api.listMyWorkloads).mockResolvedValue([]);

    render(<MyWorkloadsPage />);

    await waitFor(() =>
      expect(screen.getByText('Aktuell sind dir keine Workloads zugeordnet.')).toBeInTheDocument(),
    );
  });
});
