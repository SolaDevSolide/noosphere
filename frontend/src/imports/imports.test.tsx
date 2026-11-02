import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, vi } from 'vitest';
import { api, ApiError } from '../api/client';
import type { ImportEntry, ImportPlan } from '../api/types';
import { en } from '../i18n/en';
import { ImportExportPage } from '../pages/ImportExportPage';
import { ME, renderPage } from '../test/render';
import { leaveOut, planCsv, rows, sourceText, takeBack, tally } from './plan';

afterEach(() => vi.restoreAllMocks());

const T = en.imports;

const entry = (x: Partial<ImportEntry> & Pick<ImportEntry, 'index' | 'id' | 'name'>): ImportEntry => ({
  action: 'create',
  kind: 'application',
  matchedBy: null,
  changes: [],
  possibleMatches: [],
  source: { file: 'catalog-info.yaml', line: 10 + x.index, place: `documents[${x.index}]` },
  hasErrors: false,
  requires: [],
  ...x,
});

const plan = (x: Partial<ImportPlan> = {}): ImportPlan => {
  const entries = x.entries ?? [
    entry({ index: 0, kind: 'domain', id: 'prevention', name: 'Prevention' }),
    entry({
      index: 1,
      id: 'coaching',
      name: 'Coaching',
      requires: [0],
      changes: [{ field: 'domainId', label: 'Domain', kind: 'ref', before: null, after: 'prevention' }],
    }),
    entry({ index: 2, kind: 'flow', id: 'coaching-crm', name: 'Member lookup', requires: [1] }),
    entry({
      index: 3,
      action: 'update',
      id: 'crm',
      name: 'CRM',
      matchedBy: 'identifier',
      changes: [{ field: 'lifecycle', label: 'Lifecycle', kind: 'text', before: 'planned', after: 'live' }],
    }),
    entry({ index: 4, action: 'unchanged', id: 'billing', name: 'Billing', matchedBy: 'name' }),
    entry({
      index: 5,
      id: 'portal-v2',
      name: 'Customer portal 2',
      possibleMatches: [{ id: 'portal', name: 'Customer portal' }],
    }),
  ];
  return {
    id: '3f1c2b9e-5d0a-4c1e-9b7a-2e8f6d4c1a20',
    status: 'planned',
    username: 'alice',
    createdAt: '2026-11-02T10:00:00Z',
    expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
    closedAt: null,
    outcome: null,
    formats: ['backstage'],
    files: [{ name: 'catalog-info.yaml', format: 'backstage', size: 2200, sha256: 'ab', entries: entries.length }],
    counts: {
      create: entries.filter((e) => e.action === 'create').length,
      update: entries.filter((e) => e.action === 'update').length,
      unchanged: entries.filter((e) => e.action === 'unchanged').length,
      warnings: 1,
      errors: 0,
    },
    entries,
    warnings: [
      {
        message: "User 'jdupont' skipped: accounts are managed in Noosphere.",
        source: { file: 'catalog-info.yaml', line: 104, place: 'documents[9]' },
        entry: null,
        field: null,
      },
    ],
    errors: [],
    canApply: true,
    skipped: [],
    ...x,
  };
};

describe('import plan helpers', () => {
  const p = plan();
  it('leaves out an entry with every entry that requires it, and takes back what an entry requires', () => {
    const out = leaveOut(p.entries, new Set(), 0);
    expect([...out].sort((a, b) => a - b)).toEqual([0, 1, 2]);
    expect([...takeBack(p.entries, out, 2)].sort((a, b) => a - b)).toEqual([]);
    expect([...takeBack(p.entries, leaveOut(p.entries, new Set(), 0), 0)].sort((a, b) => a - b)).toEqual([1, 2]);
    // An unchanged entry cannot be left out.
    expect(leaveOut(p.entries, new Set(), 4).size).toBe(0);
  });
  it('counts what applying writes', () => {
    expect(tally(p, new Set([5]))).toEqual({ create: 3, update: 1, unchanged: 1, left: 1, changes: 4 });
  });
  it('shows the source of an entry and lists the messages about no entry after the entries', () => {
    expect(sourceText({ file: 'a.yaml', line: 4, place: 'documents[1]' })).toBe('a.yaml:4 · documents[1]');
    expect(sourceText({ file: 'Book.xlsx', line: null, place: 'Applications!A4' })).toBe('Book.xlsx · Applications!A4');
    expect(rows(p).map((r) => r.type)).toEqual(['entry', 'entry', 'entry', 'entry', 'entry', 'entry', 'warning']);
  });
  it('writes a CSV report that neutralises formulas', () => {
    const bad = plan({ entries: [entry({ index: 0, id: 'x', name: '=HYPERLINK("x")' })] });
    const csv = planCsv(bad, new Set([0]), T.csv);
    expect(csv.split('\r\n')[0]).toContain('Index,Action,Imported');
    expect(csv).toContain(`"'=HYPERLINK(""x"")"`);
    expect(csv.split('\r\n')[1]).toContain(',create,no,');
  });
});

describe('import page', () => {
  const file = new File(['kind: Component'], 'catalog-info.yaml', { type: 'text/yaml' });

  async function toPreview(p = plan()) {
    const planned = vi.spyOn(api, 'planImport').mockResolvedValue(p);
    renderPage(<ImportExportPage />, { path: '/import-export', route: '/import-export', me: ME.alice });
    await screen.findByRole('radiogroup', { name: T.sourceLabel });
    await userEvent.click(screen.getByRole('radio', { name: /OpenAPI/ }));
    await userEvent.upload(screen.getByLabelText(T.filesLabel, { selector: 'input' }), file);
    expect(screen.getByText('catalog-info.yaml')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: T.dryRun }));
    await screen.findByRole('heading', { name: T.previewH('OpenAPI 3.x') });
    expect(planned).toHaveBeenCalledWith([file], { format: 'openapi' });
  }

  it('starts with no file: the dry run waits for one', async () => {
    renderPage(<ImportExportPage />, { path: '/import-export', route: '/import-export', me: ME.bob });
    await screen.findByRole('radiogroup', { name: T.sourceLabel });
    expect(screen.getByRole('button', { name: T.dryRun })).toBeDisabled();
    // The exchange import stays for administrators.
    expect(screen.getByRole('radio', { name: /Noosphere JSON/ })).toBeDisabled();
  });

  it('tells guests they cannot import', async () => {
    renderPage(<ImportExportPage />, {
      path: '/import-export',
      route: '/import-export',
      me: { ...ME.bob, role: 'guest' },
    });
    expect(await screen.findByText(T.guestsCannot)).toBeInTheDocument();
    expect(screen.queryByRole('radiogroup')).not.toBeInTheDocument();
  });

  it('previews the plan, leaves out a line with what needs it, and applies the rest', async () => {
    await toPreview();
    expect(screen.getByText(T.previewSub('catalog-info.yaml', 6))).toBeInTheDocument();
    expect(screen.getByText('Customer portal')).toBeInTheDocument();
    expect(screen.getByText(/User 'jdupont' skipped/)).toBeInTheDocument();
    expect(screen.getByText(en.lifecycle.planned)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: T.importN(5) })).toBeEnabled();

    await userEvent.click(screen.getByRole('checkbox', { name: T.keep('Prevention') }));
    expect(screen.getByRole('checkbox', { name: T.keep('Coaching') })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: T.keep('Member lookup') })).not.toBeChecked();
    expect(screen.getByText(T.leftOutWith('Coaching'))).toBeInTheDocument();
    // Taking back the flow takes back what it needs.
    await userEvent.click(screen.getByRole('checkbox', { name: T.keep('Member lookup') }));
    expect(screen.getByRole('checkbox', { name: T.keep('Prevention') })).toBeChecked();
    await userEvent.click(screen.getByRole('checkbox', { name: T.keep('Customer portal 2') }));

    await userEvent.click(screen.getByRole('button', { name: T.filters.update + ' 1' }));
    expect(screen.queryByText('Member lookup')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: T.filters.all + ' 7' }));

    const applied = vi
      .spyOn(api, 'applyImport')
      .mockResolvedValue({ ...plan(), status: 'applied', canApply: false, skipped: [5] });
    await userEvent.click(screen.getByRole('button', { name: T.importN(4) }));
    const dialog = screen.getByRole('dialog', { name: T.confirmH });
    expect(within(dialog).getByText(T.leftOutCount(1))).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: T.importNow(4) }));
    expect(applied).toHaveBeenCalledWith(plan().id, [5]);
    expect(await screen.findByText(T.done(3, 1, 1, 1))).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: T.seeChanges }));
    expect(screen.getByRole('link', { name: 'CRM' })).toHaveAttribute('href', '/applications/crm');
  });

  it('asks for a new dry run when the catalogue changed or the plan expired', async () => {
    await toPreview();
    vi.spyOn(api, 'applyImport').mockRejectedValueOnce(
      new ApiError(409, 'Conflict', 'The catalogue changed since the dry run, so this plan may be wrong.'),
    );
    await userEvent.click(screen.getByRole('button', { name: T.importN(5) }));
    await userEvent.click(screen.getByRole('button', { name: T.importNow(5) }));
    expect(await screen.findByText(T.changedH)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: T.importN(5) })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: T.keep('CRM') })).toBeDisabled();

    await userEvent.click(screen.getByRole('button', { name: T.again }));
    await waitFor(() => expect(screen.queryByText(T.changedH)).not.toBeInTheDocument());
    vi.spyOn(api, 'applyImport').mockRejectedValueOnce(new ApiError(410, 'Gone', 'This import plan expired.'));
    await userEvent.click(await screen.findByRole('button', { name: T.importN(5) }));
    await userEvent.click(screen.getByRole('button', { name: T.importNow(5) }));
    expect(await screen.findByText(T.expiredH)).toBeInTheDocument();
  });

  it('shows the errors with their place and blocks the import', async () => {
    await toPreview(
      plan({
        canApply: false,
        counts: { create: 1, update: 0, unchanged: 0, warnings: 0, errors: 1 },
        entries: [entry({ index: 0, id: 'claims', name: 'Claims', hasErrors: true })],
        warnings: [],
        errors: [
          {
            message: 'application Claims: domainId: Unknown domain.',
            source: { file: 'catalog-info.yaml', line: 12, place: 'documents[0]' },
            entry: 0,
            field: 'domainId',
          },
          {
            message: 'metadata.name is required.',
            source: { file: 'catalog-info.yaml', line: 30, place: 'documents[1]' },
            entry: null,
            field: null,
          },
        ],
      }),
    );
    expect(screen.getByText(T.hasErrors(2 - 1))).toBeInTheDocument();
    expect(screen.getByText('metadata.name is required.')).toBeInTheDocument();
    expect(screen.getByText('catalog-info.yaml:30 · documents[1]')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: T.importN(1) })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: T.filters.error + ' 2' }));
    expect(screen.getByText('application Claims: domainId: Unknown domain.')).toBeInTheDocument();
  });

  it('keeps the files when the dry run is refused', async () => {
    vi.spyOn(api, 'planImport').mockRejectedValue(new ApiError(413, 'Too large'));
    renderPage(<ImportExportPage />, { path: '/import-export', route: '/import-export', me: ME.alice });
    await screen.findByRole('radiogroup', { name: T.sourceLabel });
    await userEvent.upload(screen.getByLabelText(T.filesLabel, { selector: 'input' }), file);
    await userEvent.click(screen.getByRole('button', { name: T.dryRun }));
    expect(await screen.findByText(T.tooBig)).toBeInTheDocument();
    expect(screen.getByText('catalog-info.yaml')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: T.remove('catalog-info.yaml') }));
    expect(screen.getByRole('button', { name: T.dryRun })).toBeDisabled();
  });
});
