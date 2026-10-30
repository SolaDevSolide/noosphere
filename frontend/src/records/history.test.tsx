import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, vi } from 'vitest';
import { api } from '../api/client';
import type { HistoryEntry } from '../api/types';
import { LandscapeProvider } from '../explorer/landscape';
import { I18nProvider } from '../i18n';
import { memberLandscape } from '../test/fixture';
import { ToastProvider } from '../ui/Toast';
import { comparePair, HistoryPanel, togglePick } from './HistoryPanel';

const entries: HistoryEntry[] = [
  {
    version: 3,
    operation: 'update',
    author: { username: 'bob', displayName: 'Bob Durand' },
    authorKind: 'user',
    createdAt: '2026-10-21T09:12:00Z',
    summary: 'Moved to PostgreSQL',
    proposalId: null,
  },
  {
    version: 2,
    operation: 'update',
    author: { username: 'david', displayName: 'David Leroy' },
    authorKind: 'user',
    createdAt: '2026-09-20T14:30:00Z',
    summary: 'Mandates listed',
    proposalId: 'p-4',
  },
  {
    version: 1,
    operation: 'create',
    author: null,
    authorKind: 'user',
    createdAt: '2026-09-01T08:00:00Z',
    summary: null,
    proposalId: null,
  },
];

const panel = (canPublish: boolean, onRestored = vi.fn(), onClose = vi.fn()) => {
  vi.spyOn(api, 'landscape').mockResolvedValue(memberLandscape());
  vi.spyOn(api, 'history').mockResolvedValue(entries);
  render(
    <I18nProvider initial="en">
      <MemoryRouter>
        <ToastProvider>
          <LandscapeProvider>
            <HistoryPanel
              kind="application"
              id="billing"
              name="Billing"
              canPublish={canPublish}
              canPropose
              onClose={onClose}
              onRestored={onRestored}
            />
          </LandscapeProvider>
        </ToastProvider>
      </MemoryRouter>
    </I18nProvider>,
  );
  return { onRestored, onClose };
};

afterEach(() => vi.restoreAllMocks());

describe('version picking', () => {
  it('keeps the last two picks, older first', () => {
    expect(togglePick([], 3)).toEqual([3]);
    expect(togglePick([3, 1], 2)).toEqual([1, 2]);
    expect(togglePick([3, 1], 3)).toEqual([1]);
    expect(comparePair([3, 1])).toEqual([1, 3]);
    expect(comparePair([3])).toBeNull();
  });
});

describe('HistoryPanel', () => {
  it('lists the versions with author, date and the current one', async () => {
    panel(true);
    const dlg = await screen.findByRole('dialog', { name: 'History' });
    expect(await within(dlg).findByText(/Moved to PostgreSQL/)).toBeInTheDocument();
    expect(within(dlg).getByText('Current')).toBeInTheDocument();
    expect(within(dlg).getByText(/David Leroy/)).toBeInTheDocument();
    expect(within(dlg).getByRole('link', { name: 'Proposal' })).toHaveAttribute('href', '/review/p-4');
    expect(within(dlg).getAllByRole('button', { name: /Restore version/ })).toHaveLength(2);
  });

  it('compares two picked versions', async () => {
    const user = userEvent.setup();
    const compare = vi.spyOn(api, 'compare').mockResolvedValue({
      from: 1,
      to: 3,
      diff: [{ field: 'tech', label: 'Technology', kind: 'text', before: '.NET', after: '.NET 10' }],
    });
    panel(true);
    const btn = await screen.findByRole('button', { name: 'Compare' });
    expect(btn).toBeDisabled();
    await user.click(screen.getByRole('checkbox', { name: 'Compare version 3' }));
    await user.click(screen.getByRole('checkbox', { name: 'Compare version 1' }));
    await user.click(screen.getByRole('button', { name: 'Compare v1 and v3' }));
    expect(compare).toHaveBeenCalledWith('application', 'billing', 1, 3);
    expect(await screen.findByText('Changes from v1 to v3')).toBeInTheDocument();
    expect(screen.getByText('Version 1')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Back to the versions/ }));
    expect(screen.getByText('Current')).toBeInTheDocument();
  });

  it('restores after a confirmation', async () => {
    const user = userEvent.setup();
    const restore = vi.spyOn(api, 'restore').mockResolvedValue({ outcome: 'published', version: 4, proposalId: null });
    const { onRestored } = panel(true);
    await user.click(await screen.findByRole('button', { name: 'Restore version 2' }));
    expect(restore).not.toHaveBeenCalled();
    const confirm = screen.getByRole('alertdialog');
    expect(confirm).toHaveTextContent('Restoring publishes v2 again as v4.');
    await user.click(within(confirm).getByRole('button', { name: 'Restore' }));
    expect(restore).toHaveBeenCalledWith('application', 'billing', 2);
    expect(onRestored).toHaveBeenCalled();
  });

  it('sends a proposal when the user does not own the record', async () => {
    const user = userEvent.setup();
    vi.spyOn(api, 'restore').mockResolvedValue({ outcome: 'proposed', version: null, proposalId: 'p-7' });
    const { onRestored } = panel(false);
    await user.click(await screen.findByRole('button', { name: 'Restore version 1' }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('sends a proposal to its owners');
    await user.click(screen.getByRole('button', { name: 'Send proposal' }));
    expect(await screen.findByText('Restore proposed')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'View the proposal' })).toHaveAttribute('href', '/review/p-7');
    expect(onRestored).not.toHaveBeenCalled();
  });

  it('closes with Escape', async () => {
    const user = userEvent.setup();
    const { onClose } = panel(true);
    await screen.findByText(/Moved to PostgreSQL/);
    await user.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalled();
  });
});
