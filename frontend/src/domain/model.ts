import type {
  DataObject,
  Domain,
  FlowStatus,
  FlowType,
  Landscape,
  LandscapeApplication,
  LandscapeFlow,
  Flow,
  Application,
  Sensitivity,
  Team,
} from '../api/types';
import { isRestrictedApp, isRestrictedFlow } from '../api/types';

/** Indexed, read-only view of the landscape used by every explorer view. */
export interface Model {
  domains: Domain[];
  domainIndex: Map<string, number>;
  domain: Map<string, Domain>;
  teams: Map<string, Team>;
  apps: Map<string, LandscapeApplication>;
  /** Applications ordered by domain order, then name. */
  appIds: string[];
  flows: LandscapeFlow[];
  flow: Map<string, LandscapeFlow>;
  objects: Map<string, DataObject>;
  objectIds: string[];
  flowsIn: Map<string, LandscapeFlow[]>;
  flowsOut: Map<string, LandscapeFlow[]>;
}

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true });

export function buildModel(l: Landscape): Model {
  const domains = [...l.domains].sort((a, b) => a.order - b.order || collator.compare(a.name, b.name));
  const domainIndex = new Map(domains.map((d, i) => [d.id, i]));
  const apps = new Map(l.applications.map((a) => [a.id, a]));
  const appName = (a: LandscapeApplication) => (isRestrictedApp(a) ? '￿' : a.name);
  const appIds = [...l.applications]
    .sort(
      (a, b) =>
        (domainIndex.get(a.domainId) ?? 999) - (domainIndex.get(b.domainId) ?? 999) ||
        collator.compare(appName(a), appName(b)),
    )
    .map((a) => a.id);
  const flowsIn = new Map<string, LandscapeFlow[]>();
  const flowsOut = new Map<string, LandscapeFlow[]>();
  for (const id of appIds) {
    flowsIn.set(id, []);
    flowsOut.set(id, []);
  }
  for (const f of l.flows) {
    flowsOut.get(f.fromId)?.push(f);
    flowsIn.get(f.toId)?.push(f);
  }
  const objects = new Map(l.dataObjects.map((o) => [o.id, o]));
  return {
    domains,
    domainIndex,
    domain: new Map(domains.map((d) => [d.id, d])),
    teams: new Map(l.teams.map((t) => [t.id, t])),
    apps,
    appIds,
    flows: l.flows,
    flow: new Map(l.flows.map((f) => [f.id, f])),
    objects,
    objectIds: l.dataObjects.map((o) => o.id),
    flowsIn,
    flowsOut,
  };
}

export const flowsOf = (m: Model, id: string) => [...(m.flowsIn.get(id) ?? []), ...(m.flowsOut.get(id) ?? [])];

/** Order applications by domain, then by name (restricted ones last in their domain). */
export function appOrder(m: Model) {
  const pos = new Map(m.appIds.map((id, i) => [id, i]));
  return (a: string, b: string) => (pos.get(a) ?? 0) - (pos.get(b) ?? 0);
}

export function isExternal(_m: Model, a: LandscapeApplication | undefined) {
  if (!a) return false;
  if (!isRestrictedApp(a) && a.kind === 'external') return true;
  return a.domainId === 'external';
}

export const fullApp = (m: Model, id: string): Application | null => {
  const a = m.apps.get(id);
  return a && !isRestrictedApp(a) ? a : null;
};
export const fullFlow = (f: LandscapeFlow | undefined): Flow | null => (f && !isRestrictedFlow(f) ? f : null);

/** Teams running applications of a domain, in name order. */
export function domainTeams(m: Model, domainId: string): Team[] {
  const ids = new Set<string>();
  for (const id of m.appIds) {
    const a = fullApp(m, id);
    if (a && a.domainId === domainId && a.teamId) ids.add(a.teamId);
  }
  return [...ids]
    .map((t) => m.teams.get(t))
    .filter((t): t is Team => !!t)
    .sort((a, b) => collator.compare(a.name, b.name));
}

/* ---------------- filters ---------------- */

export interface Filters {
  type: FlowType[];
  obj: string[];
  sens: Sensitivity[];
  status: FlowStatus[];
  /** "Changes coming": planned flows and flows with a planned change. */
  changeOnly: boolean;
  /** Domains hidden in the graph view. */
  hidden: string[];
  /** Graph view: hide applications without any visible link. */
  isolated: boolean;
}

export const EMPTY_FILTERS: Filters = {
  type: [],
  obj: [],
  sens: [],
  status: [],
  changeOnly: false,
  hidden: [],
  isolated: false,
};

/** Filters that act on flows (all views). */
export const anyFlowFilter = (f: Filters) =>
  f.changeOnly || f.type.length > 0 || f.obj.length > 0 || f.sens.length > 0 || f.status.length > 0;
export const filterCount = (f: Filters) =>
  (f.changeOnly ? 1 : 0) +
  f.type.length +
  f.obj.length +
  f.sens.length +
  f.status.length +
  f.hidden.length +
  (f.isolated ? 1 : 0);

export function matches(f: LandscapeFlow, fl: Filters): boolean {
  if (isRestrictedFlow(f)) {
    // Only its type is known: any filter other than the type hides it.
    if (fl.changeOnly || fl.obj.length || fl.sens.length || fl.status.length) return false;
    return !fl.type.length || fl.type.includes(f.type);
  }
  if (fl.changeOnly && !(f.plannedChange?.summary || f.status === 'planned')) return false;
  if (fl.type.length && !fl.type.includes(f.type)) return false;
  if (fl.obj.length && !f.dataObjectIds.some((o) => fl.obj.includes(o))) return false;
  if (fl.sens.length && !fl.sens.includes(f.sensitivity)) return false;
  if (fl.status.length && !fl.status.includes(f.status)) return false;
  return true;
}

export interface AppStats {
  ins: number;
  outs: number;
  total: number;
  change: number;
  dep: number;
  sens: number;
  pers: number;
}

/** Flow filters plus the hidden domains: a flow touching a hidden domain is not shown. */
export function makeMatcher(m: Model, fl: Filters) {
  if (!fl.hidden.length) return (f: LandscapeFlow) => matches(f, fl);
  const hidden = new Set(fl.hidden);
  const dom = (id: string) => m.apps.get(id)?.domainId ?? '';
  return (f: LandscapeFlow) => !hidden.has(dom(f.fromId)) && !hidden.has(dom(f.toId)) && matches(f, fl);
}

/** Applications shown by the overview views: not in a hidden domain, and with a matching flow when asked. */
export function visibleApps(m: Model, fl: Filters): string[] {
  const hidden = new Set(fl.hidden);
  const ok = makeMatcher(m, fl);
  return m.appIds.filter((id) => {
    if (hidden.has(m.apps.get(id)!.domainId)) return false;
    return !fl.isolated || flowsOf(m, id).some(ok);
  });
}

export const anyFilter = (f: Filters) => anyFlowFilter(f) || f.hidden.length > 0 || f.isolated;

export function appStats(m: Model, id: string, fl: Filters): AppStats {
  const ok = makeMatcher(m, fl);
  const fs = flowsOf(m, id).filter(ok);
  const full = fs.map(fullFlow).filter((f): f is Flow => !!f);
  return {
    ins: fs.filter((f) => f.toId === id).length,
    outs: fs.filter((f) => f.fromId === id).length,
    total: fs.length,
    change: full.filter((f) => f.plannedChange?.summary || f.status === 'planned').length,
    dep: full.filter((f) => f.status === 'deprecated').length,
    sens: full.filter((f) => f.sensitivity === 'sensitive').length,
    pers: full.filter((f) => f.sensitivity === 'personal').length,
  };
}

/* ---------------- pairs and paths ---------------- */

export interface Pair {
  key: string;
  a: string;
  b: string;
  flows: LandscapeFlow[];
  /** Number of flows matching the filters. */
  w: number;
}

export function pairs(m: Model, fl: Filters): Pair[] {
  const ok = makeMatcher(m, fl);
  const map = new Map<string, Pair>();
  for (const f of m.flows) {
    if (!m.apps.has(f.fromId) || !m.apps.has(f.toId) || f.fromId === f.toId) continue;
    const [a, b] = f.fromId < f.toId ? [f.fromId, f.toId] : [f.toId, f.fromId];
    const key = a + '|' + b;
    let p = map.get(key);
    if (!p) map.set(key, (p = { key, a, b, flows: [], w: 0 }));
    p.flows.push(f);
    if (ok(f)) p.w++;
  }
  return [...map.values()];
}

export type Adjacency = Map<string, Map<string, number>>;

export function adjacency(m: Model, ps: Pair[]): Adjacency {
  const adj: Adjacency = new Map(m.appIds.map((id) => [id, new Map<string, number>()]));
  for (const p of ps) {
    if (!p.w) continue;
    adj.get(p.a)?.set(p.b, p.w);
    adj.get(p.b)?.set(p.a, p.w);
  }
  return adj;
}

/** Breadth-first shortest path between two applications, or null. */
export function shortestPath(adj: Map<string, Iterable<string> | Map<string, unknown>>, a: string, b: string) {
  const prev = new Map<string, string | null>([[a, null]]);
  const queue = [a];
  while (queue.length) {
    const n = queue.shift()!;
    if (n === b) break;
    const next = adj.get(n);
    if (!next) continue;
    for (const k of next instanceof Map ? next.keys() : next) {
      if (!prev.has(k)) {
        prev.set(k, n);
        queue.push(k);
      }
    }
  }
  if (!prev.has(b)) return null;
  const path: string[] = [];
  for (let n: string | null = b; n != null; n = prev.get(n) ?? null) path.unshift(n);
  return path;
}

/* ---------------- lineage ---------------- */

export interface LineageLink {
  parent: string;
  flows: Flow[];
  dir: 'down' | 'up';
}
export interface Lineage {
  src: string;
  depth: Map<string, number>;
  link: Map<string, LineageLink>;
  kids: Map<string, string[]>;
  ukids: Map<string, string[]>;
  extra: Flow[];
  off: Flow[];
  carry: Flow[];
}

/** Source of a data object: its declared source, else the first sender that never receives it. */
export function lineageSource(m: Model, objectId: string): string | null {
  const o = m.objects.get(objectId);
  if (o?.sourceApplicationId && m.apps.has(o.sourceApplicationId)) return o.sourceApplicationId;
  const carry = m.flows.map(fullFlow).filter((f): f is Flow => !!f && f.dataObjectIds.includes(objectId));
  const receivers = new Set(carry.map((f) => f.toId));
  return carry.find((f) => !receivers.has(f.fromId))?.fromId ?? carry[0]?.fromId ?? null;
}

export function lineage(m: Model, objectId: string): Lineage | null {
  const src = lineageSource(m, objectId);
  if (!src) return null;
  const order = appOrder(m);
  const carry = m.flows.map(fullFlow).filter((f): f is Flow => !!f && f.dataObjectIds.includes(objectId));
  const depth = new Map<string, number>([[src, 0]]);
  const link = new Map<string, LineageLink>();
  const kids = new Map<string, string[]>([[src, []]]);
  const ukids = new Map<string, string[]>([[src, []]]);
  const used = new Set<string>();
  const q = [src];
  while (q.length) {
    const n = q.shift()!;
    if (!kids.has(n)) kids.set(n, []);
    for (const f of carry.filter((x) => x.fromId === n).sort((a, b) => order(a.toId, b.toId))) {
      if (!depth.has(f.toId)) {
        depth.set(f.toId, depth.get(n)! + 1);
        link.set(f.toId, { parent: n, flows: [f], dir: 'down' });
        kids.get(n)!.push(f.toId);
        q.push(f.toId);
        used.add(f.id);
      } else {
        const l = link.get(f.toId);
        if (l && l.parent === n && l.dir === 'down') {
          l.flows.push(f);
          used.add(f.id);
        }
      }
    }
  }
  const q2 = [src];
  while (q2.length) {
    const n = q2.shift()!;
    if (!ukids.has(n)) ukids.set(n, []);
    for (const f of carry.filter((x) => x.toId === n).sort((a, b) => order(a.fromId, b.fromId))) {
      if (!depth.has(f.fromId)) {
        depth.set(f.fromId, depth.get(n)! - 1);
        link.set(f.fromId, { parent: n, flows: [f], dir: 'up' });
        ukids.get(n)!.push(f.fromId);
        q2.push(f.fromId);
        used.add(f.id);
      } else {
        const l = link.get(f.fromId);
        if (l && l.parent === n && l.dir === 'up') {
          l.flows.push(f);
          used.add(f.id);
        }
      }
    }
  }
  const rest = carry.filter((f) => !used.has(f.id));
  const extra = rest.filter((f) => depth.has(f.fromId) && depth.has(f.toId));
  const off = rest.filter((f) => !(depth.has(f.fromId) && depth.has(f.toId)));
  return { src, depth, link, kids, ukids, extra, off, carry };
}

/* ---------------- text ---------------- */

/** Lower case without accents, for accent-insensitive matching. */
export const fold = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
