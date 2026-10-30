import { pageItems, withPages, type SearchGroup } from './searchIndex';

describe('page search results', () => {
  it('adds the pages group before the quick filters', () => {
    const pages = pageItems([
      { kind: 'application', id: 'crm', title: 'CRM', subtitle: '' },
      { kind: 'page', id: 'replay', title: 'Replay the import', subtitle: 'Billing handbook' },
    ]);
    expect(pages).toHaveLength(1);
    expect(pages[0]).toMatchObject({ kind: 'page', id: 'replay', sub: 'Billing handbook' });
    const groups: SearchGroup[] = [
      { kind: 'app', items: [] },
      { kind: 'filter', items: [] },
    ];
    expect(withPages(groups, pages).map((g) => g.kind)).toEqual(['app', 'page', 'filter']);
    expect(withPages(groups, [])).toBe(groups);
  });
});
