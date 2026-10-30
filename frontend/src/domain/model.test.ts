import {
  EMPTY_FILTERS,
  adjacency,
  appStats,
  fold,
  lineage,
  makeMatcher,
  matches,
  pairs,
  shortestPath,
  visibleApps,
} from './model';
import { memberModel } from '../test/fixture';
import { isRestrictedFlow } from '../api/types';

describe('landscape model', () => {
  const m = memberModel();

  it('orders applications by domain order', () => {
    const idx = m.appIds.map((id) => m.domainIndex.get(m.apps.get(id)!.domainId)!);
    expect([...idx].sort((a, b) => a - b)).toEqual(idx);
  });

  it('matches restricted flows on their type only', () => {
    const r = m.flows.find(isRestrictedFlow)!;
    expect(matches(r, { ...EMPTY_FILTERS, type: [r.type] })).toBe(true);
    expect(matches(r, { ...EMPTY_FILTERS, sens: ['sensitive'] })).toBe(false);
  });

  it('hides flows that touch a hidden domain', () => {
    const ok = makeMatcher(m, { ...EMPTY_FILTERS, hidden: ['finance'] });
    const touching = m.flows.filter((f) => [f.fromId, f.toId].some((id) => m.apps.get(id)?.domainId === 'finance'));
    expect(touching.length).toBeGreaterThan(0);
    expect(touching.some(ok)).toBe(false);
  });

  it('drops applications without matching flows when asked', () => {
    const all = visibleApps(m, EMPTY_FILTERS).length;
    const some = visibleApps(m, { ...EMPTY_FILTERS, type: ['event'], isolated: true });
    expect(some.length).toBeLessThan(all);
    for (const id of some) expect(appStats(m, id, { ...EMPTY_FILTERS, type: ['event'] }).total).toBeGreaterThan(0);
  });

  it('finds a shortest path between two applications', () => {
    const adj = adjacency(m, pairs(m, EMPTY_FILTERS));
    const p = shortestPath(adj, 'idp', 'acct');
    expect(p?.[0]).toBe('idp');
    expect(p?.at(-1)).toBe('acct');
    expect(shortestPath(adjacency(m, pairs(m, { ...EMPTY_FILTERS, type: ['event'] })), 'idp', 'acct')).toBeNull();
  });

  it('builds the lineage of a data object from its source', () => {
    const lg = lineage(m, 'customer')!;
    expect(lg.src).toBe('crm');
    expect(lg.depth.get('crm')).toBe(0);
    expect([...lg.depth.values()].some((d) => d < 0)).toBe(true);
    expect(lg.carry.every((f) => f.dataObjectIds.includes('customer'))).toBe(true);
  });

  it('folds accents for search', () => {
    expect(fold('Alizé Évènement')).toBe('alize evenement');
  });
});
