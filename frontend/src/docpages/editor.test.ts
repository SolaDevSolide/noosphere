import { en } from '../i18n/en';
import { linkQuery, snippet, splitKind } from './PageEditor';
import { validateDraft, type PageDraft } from './PageSettings';

describe('editor helpers', () => {
  it('finds the link being typed before the caret', () => {
    const text = 'See [[flow:cust';
    expect(linkQuery(text, text.length)).toEqual({ start: 4, query: 'flow:cust' });
    expect(linkQuery('See [[crm]] done', 16)).toBeNull();
    expect(linkQuery('plain', 5)).toBeNull();
  });
  it('narrows the search to a kind', () => {
    expect(splitKind('flow:cust')).toEqual({ kinds: ['flow'], text: 'cust' });
    expect(splitKind('cust')).toEqual({ text: 'cust' });
    expect(splitKind('http:x')).toEqual({ text: 'http:x' });
  });
  it('inserts Markdown around a placeholder', () => {
    expect(snippet(en, 'b')).toEqual(['**', en.pages.snippets.bold, '**']);
    expect(snippet(en, 'callout')[0]).toContain('[!NOTE]');
  });
});

describe('validateDraft', () => {
  const draft = (d: Partial<PageDraft> = {}): PageDraft => ({
    title: 'Runbook',
    slug: 'runbook',
    slugTouched: false,
    body: '',
    parentId: '',
    attachedTo: null,
    ownerTeamId: 'team-finance',
    tags: [],
    visibility: 'everyone',
    ...d,
  });
  const opts = { isNew: true, ownerRequired: true, takenIds: new Set(['glossary']) };
  it('accepts a complete page', () => {
    expect(validateDraft(en, draft(), opts)).toEqual({});
  });
  it('flags the title, the slug and the owner', () => {
    const e = validateDraft(en, draft({ title: ' ', slug: 'Bad Slug', ownerTeamId: '' }), opts);
    expect(Object.keys(e).sort()).toEqual(['ps-slug', 'ps-team', 'ps-title']);
    expect(validateDraft(en, draft({ slug: 'glossary' }), opts)['ps-slug']).toBe(en.pages.errors.slugTaken);
  });
  it('accepts an attached record as owner and ignores the slug once published', () => {
    expect(validateDraft(en, draft({ ownerTeamId: '', attachedTo: { kind: 'application', id: 'crm' } }), opts)).toEqual(
      {},
    );
    expect(validateDraft(en, draft({ slug: 'Bad Slug' }), { ...opts, isNew: false })).toEqual({});
  });
});
