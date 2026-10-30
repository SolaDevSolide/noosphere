import type { PageListItem, PageSummary, PageTreeNode } from '../api/types';
import { fold } from '../domain/model';
import { memberModel } from '../test/fixture';
import {
  canPublishPage,
  dropTarget,
  filterTree,
  pageKind,
  pageOwner,
  pathOf,
  indexPages,
  relativeTime,
  resolveLink,
  slugify,
  wikiLinks,
} from './model';

const page = (id: string, parentId: string | null, order: number, extra: Partial<PageSummary> = {}): PageSummary => ({
  id,
  restricted: false,
  title: id.toUpperCase(),
  parentId,
  order,
  attachedTo: null,
  ownerTeamId: null,
  tags: [],
  visibility: 'everyone',
  brokenLinks: 0,
  version: 1,
  updatedAt: '2026-10-30T00:00:00Z',
  ...extra,
});

describe('wikiLinks', () => {
  it('reads links with labels, once each, and ignores code', () => {
    const body =
      'See [[application:crm]] and [[flow:billing-dwh|the export]].\n\n`[[team:hidden]]`\n\n```\n[[page:in-code]]\n```\n[[application:crm]]';
    expect(wikiLinks(body)).toEqual([
      { kind: 'application', id: 'crm', label: null },
      { kind: 'flow', id: 'billing-dwh', label: 'the export' },
    ]);
  });
});

describe('slugify', () => {
  it('folds accents and punctuation into a kebab-case id', () => {
    expect(slugify('Réseau Auto+ : runbook!')).toBe('reseau-auto-runbook');
  });
});

describe('resolveLink', () => {
  const m = memberModel();
  it('names records and pages, and flags missing ones', () => {
    const pages = indexPages([page('glossary', null, 0)]);
    expect(resolveLink(m, pages, 'application', 'crm')).toMatchObject({ exists: true, href: '/applications/crm' });
    expect(resolveLink(m, pages, 'flow', 'billing-dwh').title).toContain('→');
    expect(resolveLink(m, pages, 'page', 'glossary')).toMatchObject({ exists: true, title: 'GLOSSARY' });
    expect(resolveLink(m, pages, 'flow', 'billing-printer')).toMatchObject({ exists: false, href: null });
    expect(resolveLink(m, pages, 'page', 'nope').exists).toBe(false);
  });
});

describe('pageOwner', () => {
  const m = memberModel();
  it('uses the page team, else the owner of the attached record', () => {
    expect(pageOwner(m, { ownerTeamId: 'team-data', attachedTo: { kind: 'application', id: 'billing' } })).toBe(
      'team-data',
    );
    expect(pageOwner(m, { attachedTo: { kind: 'application', id: 'billing' } })).toBe('team-finance');
    expect(pageOwner(m, { attachedTo: { kind: 'domain', id: 'finance' } })).toBeNull();
    expect(pageOwner(m, {})).toBeNull();
  });
  it('lets admins and owning members publish', () => {
    expect(canPublishPage('admin', [], null)).toBe(true);
    expect(canPublishPage('member', ['team-finance'], 'team-finance')).toBe(true);
    expect(canPublishPage('member', ['team-crm'], 'team-finance')).toBe(false);
    expect(canPublishPage('member', ['team-crm'], null)).toBe(false);
    expect(canPublishPage('guest', ['team-finance'], 'team-finance')).toBe(false);
  });
});

describe('page helpers', () => {
  it('names the template kind from the tags', () => {
    expect(pageKind(['batch', 'runbook'])).toBe('runbook');
    expect(pageKind(['adr'])).toBe('adr');
    expect(pageKind(['batch'])).toBeNull();
  });
  it('builds the path of a page from its ancestors', () => {
    const pages = indexPages([page('a', null, 0), page('b', 'a', 0), page('c', 'b', 0)]);
    expect(pathOf(pages, 'c')).toBe('A › B');
  });
  it('keeps the branches that match the filter', () => {
    const node = (id: string, title: string, children: PageTreeNode[] = []): PageTreeNode => ({
      id,
      title,
      restricted: false,
      order: 0,
      attachedTo: null,
      children,
    });
    const tree = [node('f', 'Finance', [node('r', 'Replay import'), node('c', 'Calendar')]), node('g', 'Glossary')];
    const out = filterTree(tree, fold('replay'), fold);
    expect(out.map((n) => n.id)).toEqual(['f']);
    expect(out[0].children.map((n) => n.id)).toEqual(['r']);
  });
});

describe('dropTarget', () => {
  const pages: PageListItem[] = [
    page('a', null, 0),
    page('b', null, 10),
    page('c', null, 20),
    page('a1', 'a', 0),
    page('a2', 'a', 10),
  ];
  it('lands between two siblings', () => {
    expect(dropTarget(pages, 'c', 'b', 'before')).toEqual({ parentId: null, order: 5, renumber: [] });
    expect(dropTarget(pages, 'a', 'c', 'after')).toEqual({ parentId: null, order: 30, renumber: [] });
  });
  it('moves inside a page, as its last child', () => {
    expect(dropTarget(pages, 'c', 'a', 'inside')).toEqual({ parentId: 'a', order: 20, renumber: [] });
  });
  it('refuses to move a page under itself', () => {
    expect(dropTarget(pages, 'a', 'a1', 'inside')).toBeNull();
    expect(dropTarget(pages, 'a', 'a', 'after')).toBeNull();
  });
  it('numbers the siblings again when there is no room', () => {
    const tight = [page('x', null, 0), page('y', null, 1), page('z', null, 2)];
    const r = dropTarget(tight, 'z', 'y', 'before');
    expect(r?.order).toBe(20);
    expect(r?.renumber).toEqual([
      { id: 'x', order: 10 },
      { id: 'y', order: 30 },
    ]);
  });
});

describe('relativeTime', () => {
  const now = Date.parse('2026-10-30T12:00:00Z');
  it('says how long ago, in the reader language', () => {
    expect(relativeTime('2026-10-30T10:00:00Z', 'en', now)).toBe('2 hours ago');
    expect(relativeTime('2026-10-29T12:00:00Z', 'en', now)).toBe('Yesterday');
    expect(relativeTime('2026-10-29T12:00:00Z', 'fr', now)).toBe('Hier');
    expect(relativeTime('not a date', 'en', now)).toBe('');
  });
});
