import { paths } from './paths';

export type LensKind = 'app' | 'flow' | 'object';
export type View = { kind: LensKind | 'team'; id: string } | null;
export type OverviewMode = 'cards' | 'connections' | 'graph';

/** What the explorer shows for a location. */
export function parseLocation(pathname: string, search: string): { view: View; mode: OverviewMode } {
  const q = new URLSearchParams(search);
  const v = q.get('view');
  const mode: OverviewMode = v === 'connections' || v === 'graph' ? v : 'cards';
  const dec = (s: string) => {
    try {
      return decodeURIComponent(s);
    } catch {
      return s;
    }
  };
  let m = /^\/applications\/([^/]+)\/lens\/?$/.exec(pathname);
  if (m) return { view: { kind: 'app', id: dec(m[1]) }, mode };
  m = /^\/flows\/([^/]+)\/lens\/?$/.exec(pathname);
  if (m) return { view: { kind: 'flow', id: dec(m[1]) }, mode };
  m = /^\/data-objects\/([^/]+)\/?$/.exec(pathname);
  if (m) return { view: { kind: 'object', id: dec(m[1]) }, mode };
  const team = q.get('team');
  if (team) return { view: { kind: 'team', id: team }, mode: 'cards' };
  return { view: null, mode };
}

export function viewPath(v: NonNullable<View>) {
  if (v.kind === 'app') return paths.appLens(v.id);
  if (v.kind === 'flow') return paths.flowLens(v.id);
  if (v.kind === 'object') return paths.lineage(v.id);
  return paths.team(v.id);
}

export const sameView = (a: View, b: View) => !!a && !!b && a.kind === b.kind && a.id === b.id;

/** Card rectangles captured when leaving the overview, so the lens can animate from them. */
export const flip: { rects: Record<string, DOMRect> | null } = { rects: null };
