import type { ReactNode } from 'react';
import { render, screen, within } from '@testing-library/react';
import { afterEach, vi } from 'vitest';
import { api } from '../api/client';
import type { Deployment, Deployments } from '../api/types';
import { LandscapeProvider } from '../explorer/landscape';
import { I18nProvider } from '../i18n';
import { en } from '../i18n/en';
import { memberLandscape } from '../test/fixture';
import { ApplicationDeployments, DeploymentHint, FlowDeployments, perEnd, statusChip } from './Deployments';

afterEach(() => vi.restoreAllMocks());

const D = en.deployments;

const dep = (x: Partial<Deployment>): Deployment => ({
  deploymentId: 'd-1',
  title: 'Billing 4.3',
  phase: 'preparation',
  outcome: null,
  postponed: false,
  plannedStart: '2026-11-04T20:00:00Z',
  plannedEnd: '2026-11-04T23:00:00Z',
  url: 'https://mep.example/meps/d-1',
  applicationIds: ['billing', 'crm'],
  environments: [{ name: 'Production', production: true }],
  updatedAt: '2026-10-30T08:00:00Z',
  receivedAt: '2026-10-30T08:00:00Z',
  source: { linkId: 'l-1', product: 'gestion-mep', instanceName: 'MEP Alizé', linkActive: true },
  ...x,
});

function show(ui: ReactNode) {
  vi.spyOn(api, 'landscape').mockResolvedValue(memberLandscape());
  return render(
    <I18nProvider initial="en">
      <LandscapeProvider>{ui}</LandscapeProvider>
    </I18nProvider>,
  );
}

const sample: Deployments = {
  upcoming: [dep({})],
  past: [
    dep({
      deploymentId: 'd-2',
      title: 'billing-api 4.2.3',
      phase: 'closed',
      outcome: 'success',
      applicationIds: ['billing'],
      url: 'https://ci.example/runs/2',
      source: { linkId: 'l-2', product: 'Release pipeline', instanceName: 'Alizé CI', linkActive: false },
    }),
  ],
};

describe('Deployments and changes', () => {
  it('lists upcoming and recent deployments, each opening in the application that sent it', async () => {
    show(<ApplicationDeployments appId="billing" deployments={sample} />);
    expect(screen.getByRole('heading', { name: D.title })).toBeInTheDocument();
    const next = screen.getByRole('link', { name: /Billing 4\.3/ });
    expect(next).toHaveAttribute('href', 'https://mep.example/meps/d-1');
    expect(next).toHaveAttribute('target', '_blank');
    expect(next).toHaveAttribute('title', D.openIn('MEP Alizé'));
    expect(within(next).getByText(D.production)).toBeInTheDocument();
    expect(within(next).getByText(D.phase.preparation)).toBeInTheDocument();
    expect(await within(next).findByText(/Also touches CRM/)).toBeInTheDocument();
    // A cut link keeps its history and the page it points to.
    const old = screen.getByRole('link', { name: /billing-api 4\.2\.3/ });
    expect(old).toHaveAttribute('href', 'https://ci.example/runs/2');
    expect(within(old).getByText(/link cut/)).toBeInTheDocument();
    expect(within(old).getByText(`${D.phase.closed} · ${D.outcome.success}`)).toBeInTheDocument();
    expect(
      screen.getByText(/Sent by 2 linked applications: MEP Alizé \(gestion-mep\) and Alizé CI/),
    ).toBeInTheDocument();
  });

  it('shows nothing without deployments', () => {
    const { container } = show(<ApplicationDeployments appId="billing" deployments={{ upcoming: [], past: [] }} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('hints the next planned deployment under the title', () => {
    show(<DeploymentHint deployments={sample} />);
    expect(screen.getByRole('link', { name: D.hint('4 Nov', 'Billing 4.3') })).toBeInTheDocument();
    expect(screen.getByText(D.hintFrom('MEP Alizé'))).toBeInTheDocument();
  });

  it('labels how a deployment ended', () => {
    expect(statusChip(en, dep({ phase: 'closed', outcome: 'rolled-back' })).cls).toBe('danger');
    expect(statusChip(en, dep({ phase: 'closed', outcome: 'success-with-incidents' })).cls).toBe('warn');
    expect(statusChip(en, dep({ phase: 'cancelled' }))).toEqual({ cls: 'muted', label: D.phase.cancelled });
  });

  it('keeps the upcoming and the last three past deployments of each end of a flow', () => {
    const past = [1, 2, 3, 4].map((i) => dep({ deploymentId: `p${i}`, phase: 'closed', applicationIds: ['crm'] }));
    const all = { upcoming: [dep({ applicationIds: ['billing'] })], past };
    expect(perEnd(all, 'crm').map((d) => d.deploymentId)).toEqual(['p1', 'p2', 'p3']);
    expect(perEnd(all, 'billing')).toHaveLength(1);
  });

  it('groups a flow by its ends, and asks nothing when the flow has no deployment', async () => {
    const spy = vi.spyOn(api, 'deployments').mockResolvedValue(sample);
    const { unmount } = show(
      <FlowDeployments flowId="f" fromId="crm" toId="billing" initial={{ upcoming: [], past: [] }} />,
    );
    expect(spy).not.toHaveBeenCalled();
    unmount();
    show(<FlowDeployments flowId="f" fromId="crm" toId="billing" initial={sample} />);
    expect(await screen.findByText(D.source('CRM'))).toBeInTheDocument();
    expect(screen.getByText(D.target('Billing'))).toBeInTheDocument();
    expect(spy).toHaveBeenCalledWith('flow', 'f');
  });
});
