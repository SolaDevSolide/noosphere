import { screen } from '@testing-library/react';
import { vi } from 'vitest';
import { api } from '../api/client';
import type { PageSummary } from '../api/types';
import { ME, renderPage } from '../test/render';
import { RecordPages } from './RecordPages';
import { PagesProvider } from './store';

const summary = (id: string, title: string, extra: Partial<PageSummary> = {}): PageSummary => ({
  id,
  restricted: false,
  title,
  parentId: null,
  order: 0,
  attachedTo: null,
  ownerTeamId: null,
  tags: [],
  visibility: 'everyone',
  brokenLinks: 0,
  version: 1,
  updatedAt: '2026-10-30T00:00:00Z',
  ...extra,
});

describe('RecordPages', () => {
  afterEach(() => vi.restoreAllMocks());

  it('lists the attached pages and the pages that mention the record', async () => {
    vi.spyOn(api, 'pages').mockResolvedValue([
      summary('handbook', 'Billing handbook', { tags: ['overview'] }),
      summary('replay', 'Replay the import', { parentId: 'handbook', tags: ['runbook'], brokenLinks: 1 }),
      summary('glossary', 'Glossary', { ownerTeamId: 'team-data' }),
    ]);
    vi.spyOn(api, 'pageTree').mockResolvedValue([]);
    renderPage(
      <PagesProvider>
        <RecordPages
          kind="application"
          record={{
            id: 'billing',
            pages: [
              { id: 'handbook', title: 'Billing handbook' },
              { id: 'replay', title: 'Replay the import' },
            ],
            mentionedIn: [
              { id: 'replay', title: 'Replay the import' },
              { id: 'glossary', title: 'Glossary' },
            ],
            owningTeamId: 'team-finance',
            permissions: { publish: true, propose: true },
          }}
        />
      </PagesProvider>,
      { path: '/x', route: '/x', me: ME.bob },
    );
    expect(await screen.findByText('Pages · 2')).toBeInTheDocument();
    expect(await screen.findByText('1 broken link')).toBeInTheDocument();
    // A page both attached and mentioned is listed once, with the attached pages.
    expect(screen.getByText('Mentioned in · 1')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Glossary/ })).toHaveAttribute('href', '/pages/glossary');
    expect(screen.getByRole('link', { name: /New page attached/ })).toHaveAttribute(
      'href',
      expect.stringContaining('attachedId=billing'),
    );
  });
});
