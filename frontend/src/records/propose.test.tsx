import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, vi } from 'vitest';
import { api } from '../api/client';
import type { Application, Proposal, RecordExtras } from '../api/types';
import { applications } from '../mocks/alize';
import { ME, renderPage } from '../test/render';
import { ApplicationRecord } from './ApplicationRecord';
import { permissionsFor } from './permissions';

const billing = applications.find((a) => a.id === 'billing')!;
const sent = { id: 'p-9', title: 'Billing · update', status: 'submitted' } as Proposal;

const open = (permissions: RecordExtras['permissions'], me = ME.david) => {
  const app: Application & RecordExtras = { ...billing, version: 3, owningTeamId: 'team-finance', permissions };
  vi.spyOn(api, 'application').mockResolvedValue(app);
  return renderPage(<ApplicationRecord />, { path: '/applications/billing', route: '/applications/:id', me });
};

afterEach(() => vi.restoreAllMocks());

describe('permissionsFor', () => {
  it('follows the server, or the ownership rule when it says nothing', () => {
    expect(permissionsFor('member', ['a'], 'b', { publish: true, propose: true })).toEqual({
      publish: true,
      propose: true,
    });
    expect(permissionsFor('admin', [], null)).toEqual({ publish: true, propose: true });
    expect(permissionsFor('member', ['team-crm'], 'team-crm')).toEqual({ publish: true, propose: true });
    expect(permissionsFor('member', ['team-crm'], 'team-finance')).toEqual({ publish: false, propose: true });
    expect(permissionsFor('member', ['team-crm'], null)).toEqual({ publish: false, propose: true });
    expect(permissionsFor('guest', [], 'team-crm')).toEqual({ publish: false, propose: false });
  });
});

describe('editing a record', () => {
  it('lets a non-owner suggest a change, sent as a proposal with a comment', async () => {
    const user = userEvent.setup();
    const create = vi.spyOn(api, 'createProposal').mockResolvedValue(sent);
    const update = vi.spyOn(api, 'updateApplication');
    open({ publish: false, propose: true });
    await user.click(await screen.findByRole('button', { name: 'Suggest a change' }));
    expect(screen.queryByRole('button', { name: 'Edit' })).toBeNull();
    expect(screen.getByText(/Team Finance owns this record/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Propose instead')).toBeNull();
    const tech = screen.getByLabelText('Technology');
    await user.clear(tech);
    await user.type(tech, '.NET 10');
    await user.type(screen.getByLabelText('Why this change?'), 'Release notes 4.3');
    await user.click(screen.getByRole('button', { name: 'Send proposal' }));
    expect(update).not.toHaveBeenCalled();
    expect(create).toHaveBeenCalledTimes(1);
    const body = create.mock.calls[0][0];
    expect(body).toMatchObject({ title: 'Billing · update', comment: 'Release notes 4.3', submit: true });
    expect(body.changes).toHaveLength(1);
    expect(body.changes[0]).toMatchObject({
      kind: 'application',
      targetId: 'billing',
      operation: 'update',
      baseVersion: 3,
    });
    expect(body.changes[0].after).toMatchObject({ id: 'billing', version: 3, tech: '.NET 10', name: 'Billing' });
    expect(await screen.findByRole('link', { name: 'View the proposal' })).toHaveAttribute('href', '/review/p-9');
  });

  it('requires a proposal title', async () => {
    const user = userEvent.setup();
    const create = vi.spyOn(api, 'createProposal').mockResolvedValue(sent);
    open({ publish: false, propose: true });
    await user.click(await screen.findByRole('button', { name: 'Suggest a change' }));
    await user.clear(screen.getByLabelText(/Proposal title/));
    await user.click(screen.getByRole('button', { name: 'Send proposal' }));
    expect(create).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/Proposal title/)).toHaveAttribute('aria-invalid', 'true');
  });

  it('publishes directly for an owner, unless "Propose instead" is ticked', async () => {
    const user = userEvent.setup();
    const update = vi.spyOn(api, 'updateApplication').mockResolvedValue({ ...billing, version: 4 });
    const create = vi.spyOn(api, 'createProposal').mockResolvedValue(sent);
    open({ publish: true, propose: true }, ME.bob);
    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    const instead = screen.getByLabelText('Propose instead');
    expect(instead).not.toBeChecked();
    expect(screen.queryByLabelText(/Proposal title/)).toBeNull();
    await user.click(instead);
    expect(screen.getByRole('heading', { name: 'Suggesting a change' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Send proposal' }));
    expect(create).toHaveBeenCalledTimes(1);
    expect(update).not.toHaveBeenCalled();
    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(update).toHaveBeenCalledTimes(1);
  });

  it('keeps guests read-only', async () => {
    open({ publish: false, propose: false }, { ...ME.david, role: 'guest', teams: [] });
    expect(await screen.findByText(/Shared with you by/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Suggest a change' })).toBeNull();
    expect(screen.getByRole('button', { name: 'History' })).toBeInTheDocument();
  });
});
