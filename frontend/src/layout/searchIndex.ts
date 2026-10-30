import type { FlowStatus, FlowType, Sensitivity } from '../api/types';
import { isRestrictedApp, isRestrictedFlow } from '../api/types';
import type { Dict } from '../i18n/en';
import { fold, type Model } from '../domain/model';
import { nameOf, objName, teamName } from '../explorer/bits';

export type SearchKind = 'app' | 'flow' | 'object' | 'team' | 'filter';
export type FilterKey = 'type' | 'obj' | 'sens' | 'status' | 'preset';

export interface SearchItem {
  kind: SearchKind;
  id: string;
  label: string;
  sub: string;
  hay: string;
  flowType?: FlowType;
  sens?: Sensitivity;
  fk?: FilterKey;
  fv?: string;
}

export function buildIndex(m: Model, t: Dict): SearchItem[] {
  const items: SearchItem[] = [];
  for (const id of m.appIds) {
    const a = m.apps.get(id)!;
    if (isRestrictedApp(a)) continue;
    const dom = m.domain.get(a.domainId)?.name ?? '';
    const team = teamName(m, a.teamId);
    items.push({
      kind: 'app',
      id,
      label: a.name,
      sub: [dom, team].filter(Boolean).join(' · '),
      hay: fold(`${a.name} ${team} ${a.tech ?? ''} ${t.kind[a.kind]}`),
    });
  }
  for (const f of m.flows) {
    if (isRestrictedFlow(f)) continue;
    const from = nameOf(t, m, f.fromId);
    const to = nameOf(t, m, f.toId);
    items.push({
      kind: 'flow',
      id: f.id,
      label: f.name,
      sub: `${from} → ${to}${f.schedule ? ' · ' + f.schedule : ''}`,
      hay: fold(`${f.name} ${from} ${to} ${f.dataObjectIds.map((o) => objName(m, o)).join(' ')} ${t.type[f.type]}`),
      flowType: f.type,
    });
  }
  for (const o of m.objects.values()) {
    const n = m.flows.filter((f) => !isRestrictedFlow(f) && f.dataObjectIds.includes(o.id)).length;
    const src = o.sourceApplicationId ? nameOf(t, m, o.sourceApplicationId) : '—';
    items.push({
      kind: 'object',
      id: o.id,
      label: o.name,
      sub: t.search.objectSub(src, t.sens[o.sensitivity], n),
      hay: fold(o.name + ' data object donnée'),
      sens: o.sensitivity,
    });
  }
  for (const tm of m.teams.values()) {
    const apps = m.appIds.filter((id) => {
      const a = m.apps.get(id)!;
      return !isRestrictedApp(a) && a.teamId === tm.id;
    }).length;
    const owned = m.flows.filter((f) => !isRestrictedFlow(f) && f.ownerTeamId === tm.id).length;
    if (!apps && !owned) continue;
    items.push({ kind: 'team', id: tm.id, label: tm.name, sub: t.search.teamSub(apps, owned), hay: fold(tm.name) });
  }
  const fi = t.search.filterItems;
  const filters: [FilterKey, string, string, string][] = [
    ['sens', 'personal', ...fi.personal],
    ['sens', 'sensitive', ...fi.sensitive],
    ['sens', 'internal', ...fi.internal],
    ['type', 'api', ...fi.api],
    ['type', 'event', ...fi.event],
    ['type', 'batch', ...fi.batch],
    ['status', 'planned', ...fi.planned],
    ['status', 'deprecated', ...fi.deprecated],
    ['status', 'live', ...fi.live],
  ];
  const out: SearchItem[] = filters.map(([k, v, label, what]) => ({
    kind: 'filter',
    id: k + ':' + v,
    label,
    sub: t.search.filterPrefix + t.search.only(what),
    hay: fold(label + ' filter filtre ' + what),
    fk: k,
    fv: v,
    sens: k === 'sens' ? (v as Sensitivity) : undefined,
    flowType: k === 'type' ? (v as FlowType) : undefined,
  }));
  out.push({
    kind: 'filter',
    id: 'preset:changes',
    label: t.filters.presetChanges,
    sub: t.search.filterPrefix + t.filters.presetChangesDesc,
    hay: fold(t.filters.presetChanges + ' filter filtre'),
    fk: 'preset',
    fv: 'changes',
  });
  for (const o of m.objects.values())
    out.push({
      kind: 'filter',
      id: 'obj:' + o.id,
      label: t.search.carries(o.name),
      sub: t.search.filterPrefix + t.search.onlyCarrying(o.name),
      hay: fold(t.search.carries(o.name) + ' filter filtre'),
      fk: 'obj',
      fv: o.id,
    });
  return [...items, ...out];
}

export function score(it: SearchItem, q: string): number {
  const l = fold(it.label);
  if (l === q) return 10;
  if (l.startsWith(q)) return 6;
  if (l.split(/[\s()-]+/).some((w) => w.startsWith(q))) return 4;
  if (l.includes(q)) return 3;
  if (it.hay.includes(q)) return 1;
  return 0;
}

export const GROUPS: SearchKind[] = ['app', 'flow', 'object', 'team', 'filter'];
const LIMIT: Record<SearchKind, number> = { app: 5, flow: 6, object: 5, team: 5, filter: 3 };

export interface SearchGroup {
  kind: SearchKind;
  items: SearchItem[];
}

/** Grouped results; with no query, a starter list (busiest apps, data objects, teams, quick filters). */
export function runSearch(index: SearchItem[], m: Model, raw: string): SearchGroup[] {
  const q = fold(raw.trim());
  let items: SearchItem[];
  if (!q) {
    const deg = (id: string) => (m.flowsIn.get(id)?.length ?? 0) + (m.flowsOut.get(id)?.length ?? 0);
    const busy = new Set(
      index
        .filter((i) => i.kind === 'app')
        .sort((a, b) => deg(b.id) - deg(a.id))
        .slice(0, 4)
        .map((i) => i.id),
    );
    items = index.filter(
      (it) =>
        it.kind === 'object' ||
        it.kind === 'team' ||
        (it.kind === 'filter' && /^(preset|sens:sensitive|sens:personal)/.test(it.id)) ||
        (it.kind === 'app' && busy.has(it.id)),
    );
  } else {
    items = index
      .map((it) => ({ it, s: score(it, q) }))
      .filter((x) => x.s > 0)
      .sort((a, b) => b.s - a.s || a.it.label.localeCompare(b.it.label))
      .map((x) => x.it);
  }
  const best = (k: SearchKind) =>
    Math.max(0, ...items.filter((it) => it.kind === k).map((it) => (q ? score(it, q) : 0)));
  const groups = q ? [...GROUPS].sort((a, b) => best(b) - best(a)) : GROUPS;
  return groups
    .map((kind) => ({ kind, items: items.filter((it) => it.kind === kind).slice(0, LIMIT[kind]) }))
    .filter((g) => g.items.length);
}

export type { FlowStatus };
