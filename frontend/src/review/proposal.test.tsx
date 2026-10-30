import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, vi } from 'vitest';
import { api, ApiError } from '../api/client';
import type { Proposal } from '../api/types';
import { en } from '../i18n/en';
import { ME, renderPage } from '../test/render';
import { ProposalPage, reasonError } from './ProposalPage';
import { loadTab, ReviewPage } from './ReviewPage';

const proposal = (over: Partial<Proposal> = {}): Proposal => ({
  id: 'p-1',
  title: 'Billing · payment plans',
  comment: 'From the release notes.',
  status: 'submitted',
  authorKind: 'user',
  author: { username: 'david', displayName: 'David Leroy' },
  onBehalfOf: null,
  createdAt: '2026-10-30T07:40:00Z',
  submittedAt: '2026-10-30T07:42:00Z',
  decidedBy: null,
  decidedAt: null,
  refusalReason: null,
  version: 1,
  outdated: false,
  ownerTeams: [{ id: 'team-finance', name: 'Team Finance', approvedBy: null, approvedAt: null }],
  requiresAdmin: false,
  canDecide: true,
  canEdit: false,
  changes: [
    {
      kind: 'application',
      targetId: 'billing',
      targetTitle: 'Billing',
      operation: 'update',
      baseVersion: 3,
      currentVersion: 3,
      outdated: false,
      before: { tech: '.NET' },
      after: { tech: '.NET 10' },
      conflicts: [],
      diff: [{ field: 'tech', label: 'Technology', kind: 'text', before: '.NET', after: '.NET 10' }],
    },
  ],
  ...over,
});

const open = (p: Proposal, me = ME.bob) => {
  vi.spyOn(api, 'proposal').mockResolvedValue(p);
  return renderPage(<ProposalPage />, { path: '/review/' + p.id, route: '/review/:id', me });
};

afterEach(() => vi.restoreAllMocks());

describe('reasonError', () => {
  it('requires 3 to 500 characters once trimmed', () => {
    expect(reasonError(en, '  ')).toBe(en.review.reasonRequired);
    expect(reasonError(en, ' no ')).toBe(en.review.reasonLength(3, 500));
    expect(reasonError(en, 'x'.repeat(501))).toBe(en.review.reasonLength(3, 500));
    expect(reasonError(en, 'Out of scope')).toBeNull();
  });
});

describe('ProposalPage', () => {
  it('shows the side-by-side comparison and the reason', async () => {
    open(proposal());
    expect(await screen.findByRole('heading', { level: 1, name: 'Billing · payment plans' })).toBeInTheDocument();
    expect(screen.getByText('Published · v3')).toBeInTheDocument();
    expect(screen.getByText('Based on the current version (v3)')).toBeInTheDocument();
    expect(screen.getByText('From the release notes.')).toBeInTheDocument();
  });

  it('refuses only with a reason, and sends it', async () => {
    const user = userEvent.setup();
    const refuse = vi
      .spyOn(api, 'refuseProposal')
      .mockResolvedValue(proposal({ status: 'refused', refusalReason: 'Not yet', canDecide: false }));
    open(proposal());
    await user.click(await screen.findByRole('button', { name: 'Refuse' }));
    expect(refuse).not.toHaveBeenCalled();
    const box = screen.getByLabelText('Reason for refusing');
    expect(box).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText(en.review.reasonRequired)).toBeInTheDocument();
    await user.type(box, '  Not yet  ');
    await user.click(screen.getByRole('button', { name: 'Refuse' }));
    expect(refuse).toHaveBeenCalledWith('p-1', 'Not yet');
    expect(await screen.findByText(/Refused by/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Accept and publish' })).toBeNull();
  });

  it('accepts, and reports an outdated proposal when the server answers 409', async () => {
    const user = userEvent.setup();
    const accept = vi.spyOn(api, 'acceptProposal').mockRejectedValue(new ApiError(409, 'Conflict', undefined, {}, {}));
    open(proposal());
    await user.click(await screen.findByRole('button', { name: 'Accept and publish' }));
    expect(accept).toHaveBeenCalledWith('p-1');
    expect(await screen.findByText(en.review.outdatedTitle)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Accept and publish' })).toBeDisabled();
  });

  it('lets the author update an outdated proposal from the latest version, or withdraw it', async () => {
    const user = userEvent.setup();
    const p = proposal({ outdated: true, canDecide: false, canEdit: true });
    p.changes[0] = { ...p.changes[0], outdated: true, currentVersion: 4 };
    const rebase = vi.spyOn(api, 'rebaseProposal').mockResolvedValue(
      proposal({
        canDecide: false,
        canEdit: true,
        changes: [{ ...p.changes[0], outdated: false, baseVersion: 4, conflicts: ['tech'] }],
      }),
    );
    const withdraw = vi.spyOn(api, 'withdrawProposal').mockResolvedValue(proposal({ status: 'withdrawn' }));
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    open(p, ME.david);
    expect(await screen.findByText('Written on v3, the record is now v4')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Accept and publish' })).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Update from latest' }));
    expect(rebase).toHaveBeenCalledWith('p-1');
    expect(await screen.findByText(en.review.conflictsTitle)).toBeInTheDocument();
    expect(screen.getByText('Changed on both sides')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Withdraw' }));
    expect(withdraw).toHaveBeenCalledWith('p-1');
    expect(await screen.findByText(en.review.withdrawnText)).toBeInTheDocument();
  });

  it('shows the refusal reason to the author', async () => {
    open(
      proposal({
        status: 'refused',
        canDecide: false,
        refusalReason: 'Accounting still runs on SAP.',
        decidedBy: { username: 'bob', displayName: 'Bob Durand' },
        decidedAt: '2026-10-13T08:20:00Z',
      }),
      ME.david,
    );
    expect(await screen.findByText('Accounting still runs on SAP.')).toBeInTheDocument();
    expect(screen.getByText(/Refused by Bob Durand/)).toBeInTheDocument();
  });
});

describe('ReviewPage', () => {
  it('loads the proposals of each tab', async () => {
    const list = vi.spyOn(api, 'proposals').mockResolvedValue([proposal()]);
    await loadTab('validate');
    expect(list).toHaveBeenLastCalledWith({ toValidate: true });
    await loadTab('mine');
    expect(list).toHaveBeenLastCalledWith({ mine: true });
    await loadTab('decided');
    expect(list).toHaveBeenCalledWith({ status: 'accepted' });
    expect(list).toHaveBeenCalledWith({ status: 'refused' });
  });

  it('lists the proposals to validate and switches tabs', async () => {
    const user = userEvent.setup();
    const list = vi
      .spyOn(api, 'proposals')
      .mockImplementation((f) =>
        Promise.resolve(f?.mine ? [] : [proposal(), proposal({ id: 'p-2', title: 'Flow · schedule', outdated: true })]),
      );
    renderPage(<ReviewPage />, { path: '/review', route: '/review', me: ME.bob });
    const tabs = await screen.findByRole('tablist');
    expect(within(tabs).getByRole('tab', { name: /To validate/ })).toHaveAttribute('aria-selected', 'true');
    expect(await screen.findByRole('link', { name: 'Flow · schedule' })).toHaveAttribute('href', '/review/p-2');
    expect(screen.getByText('Record changed since')).toBeInTheDocument();
    await user.click(within(tabs).getByRole('tab', { name: /My proposals/ }));
    await waitFor(() => expect(list).toHaveBeenLastCalledWith({ mine: true }));
    expect(await screen.findByText(/You have not proposed any change yet/)).toBeInTheDocument();
  });
});
