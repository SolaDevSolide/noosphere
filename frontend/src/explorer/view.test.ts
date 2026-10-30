import { parseLocation } from './view';
import { safeNext } from '../pages/LoginPage';
import { appLayout, flowLayout, lineageLayout } from './lens/layouts';
import { memberModel } from '../test/fixture';

describe('explorer locations', () => {
  it('maps paths to views', () => {
    expect(parseLocation('/applications/billing/lens', '')).toEqual({
      view: { kind: 'app', id: 'billing' },
      mode: 'cards',
    });
    expect(parseLocation('/flows/a%20b/lens', '').view).toEqual({ kind: 'flow', id: 'a b' });
    expect(parseLocation('/data-objects/customer', '').view).toEqual({ kind: 'object', id: 'customer' });
    expect(parseLocation('/', '?view=graph')).toEqual({ view: null, mode: 'graph' });
    expect(parseLocation('/', '?team=team-crm').view).toEqual({ kind: 'team', id: 'team-crm' });
  });
  it('only follows same-site paths after sign-in', () => {
    expect(safeNext('/flows/x')).toBe('/flows/x');
    expect(safeNext('//evil.example')).toBe('/');
    expect(safeNext('https://evil.example')).toBe('/');
    expect(safeNext(null)).toBe('/');
  });
});

describe('lens layouts', () => {
  const m = memberModel();
  it('places every partner of an application once, upstream or downstream', () => {
    const l = appLayout(m, 'billing', 1300, false);
    const partners = new Set(
      m.flows
        .filter((f) => f.fromId === 'billing' || f.toId === 'billing')
        .map((f) => (f.fromId === 'billing' ? f.toId : f.fromId)),
    );
    expect(
      l.nodes
        .filter((n) => n.role === 'node')
        .map((n) => n.id)
        .sort(),
    ).toEqual([...partners].sort());
    expect(l.pills).toHaveLength(m.flows.filter((f) => f.fromId === 'billing' || f.toId === 'billing').length);
    expect(appLayout(m, 'billing', 1300, true).nodes.some((n) => n.role === 'ghost')).toBe(true);
  });
  it('lays out a flow and a lineage', () => {
    const f = m.flows.find((x) => !x.restricted)!;
    expect(flowLayout(m, f.id, 1200, 220)?.card?.f.id).toBe(f.id);
    expect(lineageLayout(m, 'customer')?.nodes.find((n) => n.role === 'src')?.id).toBe('crm');
  });
});
