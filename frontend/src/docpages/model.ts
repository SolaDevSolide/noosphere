import type {
  AttachedTo,
  FlowType,
  PageListItem,
  PageSummary,
  PageTreeNode,
  RecordKind,
  Role,
  Sensitivity,
} from '../api/types';
import { isRestrictedApp, isRestrictedFlow, isRestrictedPage } from '../api/types';
import type { Model } from '../domain/model';

/** Where a record of each kind is shown in the app. */
export function recordHref(kind: RecordKind, id: string): string {
  const e = encodeURIComponent(id);
  switch (kind) {
    case 'application':
      return `/applications/${e}`;
    case 'flow':
      return `/flows/${e}`;
    case 'dataObject':
      return `/data-objects/${e}`;
    case 'team':
      return `/teams/${e}`;
    case 'domain':
      return `/domains/${e}`;
    default:
      return `/pages/${e}`;
  }
}

export const pagePaths = {
  home: '/pages',
  page: (id: string) => `/pages/${encodeURIComponent(id)}`,
  edit: (id: string) => `/pages/${encodeURIComponent(id)}/edit`,
  create: (o: { template?: TemplateId; attachedTo?: AttachedTo | null; parentId?: string | null } = {}) => {
    const s = new URLSearchParams();
    if (o.template && o.template !== 'blank') s.set('template', o.template);
    if (o.attachedTo) {
      s.set('attachedKind', o.attachedTo.kind);
      s.set('attachedId', o.attachedTo.id);
    }
    if (o.parentId) s.set('parentId', o.parentId);
    const q = s.toString();
    return '/pages/new' + (q ? '?' + q : '');
  },
};

/** The kinds a wiki link may point to, as written in `[[kind:id]]`. */
export const LINK_KINDS: RecordKind[] = ['application', 'flow', 'dataObject', 'page', 'domain', 'team'];
export const ATTACH_KINDS: RecordKind[] = ['application', 'flow', 'dataObject', 'domain', 'team'];

/** Same pattern as the server: `[[application:billing]]` or `[[flow:crm-to-billing|the nightly file]]`. */
export const WIKI_LINK =
  /\[\[(application|flow|dataObject|page|domain|team):([a-z0-9]+(?:-[a-z0-9]+)*)(?:\|([^\]\r\n]*))?\]\]/g;

export interface WikiLink {
  kind: RecordKind;
  id: string;
  label: string | null;
}

/** Wiki links of a body, in order, without duplicates (by target). Links inside code are ignored. */
export function wikiLinks(body: string): WikiLink[] {
  const out: WikiLink[] = [];
  const seen = new Set<string>();
  const text = body.replace(/```[\s\S]*?(```|$)/g, '').replace(/`[^`\n]*`/g, '');
  for (const m of text.matchAll(WIKI_LINK)) {
    const key = m[1] + ':' + m[2];
    if (seen.has(key)) continue;
    seen.add(key);
    out.push({ kind: m[1] as RecordKind, id: m[2], label: m[3]?.trim() || null });
  }
  return out;
}

/** "Réseau Auto+" becomes "reseau-auto", like the server does for ids. */
export function slugify(s: string): string {
  return s
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80)
    .replace(/-+$/, '');
}
export const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

/** How a wiki link target looks in a page: its name, where it leads and an icon hint. */
export interface Resolved {
  exists: boolean;
  restricted: boolean;
  title: string;
  href: string | null;
  flowType?: FlowType;
  sens?: Sensitivity;
}

export type PageIndex = Map<string, PageListItem>;

export function indexPages(list: PageListItem[] | null): PageIndex {
  return new Map((list ?? []).map((p) => [p.id, p]));
}

/** A flow named as in the mockups: "Customers · CRM → Billing". */
export function flowLabel(m: Model, id: string): string | null {
  const f = m.flow.get(id);
  if (!f || isRestrictedFlow(f)) return null;
  const name = (x: string) => {
    const a = m.apps.get(x);
    return a && !isRestrictedApp(a) ? a.name : x;
  };
  return `${f.name} · ${name(f.fromId)} → ${name(f.toId)}`;
}

/** Resolves a link target against the landscape and the page list (as the reader may see them). */
export function resolveLink(m: Model | null, pages: PageIndex, kind: RecordKind, id: string): Resolved {
  const href = recordHref(kind, id);
  const none: Resolved = { exists: false, restricted: false, title: id, href: null };
  if (kind === 'page') {
    const p = pages.get(id);
    if (!p) return none;
    if (isRestrictedPage(p)) return { exists: true, restricted: true, title: id, href };
    return { exists: true, restricted: false, title: p.title, href };
  }
  if (!m) return { exists: true, restricted: false, title: id, href };
  if (kind === 'application') {
    const a = m.apps.get(id);
    if (!a) return none;
    return isRestrictedApp(a)
      ? { exists: true, restricted: true, title: id, href: null }
      : { exists: true, restricted: false, title: a.name, href };
  }
  if (kind === 'flow') {
    const f = m.flow.get(id);
    if (!f) return none;
    return isRestrictedFlow(f)
      ? { exists: true, restricted: true, title: id, href: null, flowType: f.type }
      : { exists: true, restricted: false, title: flowLabel(m, id) ?? f.name, href, flowType: f.type };
  }
  if (kind === 'dataObject') {
    const o = m.objects.get(id);
    return o ? { exists: true, restricted: false, title: o.name, href, sens: o.sensitivity } : none;
  }
  if (kind === 'team') {
    const tm = m.teams.get(id);
    return tm ? { exists: true, restricted: false, title: tm.name, href } : none;
  }
  const d = m.domain.get(id);
  return d ? { exists: true, restricted: false, title: d.name, href } : none;
}

/** The team owning a record, as the server computes it (null: administrators). */
export function recordOwner(m: Model | null, kind: RecordKind, id: string): string | null {
  if (!m) return null;
  if (kind === 'application') {
    const a = m.apps.get(id);
    return a && !isRestrictedApp(a) ? (a.teamId ?? null) : null;
  }
  if (kind === 'flow') {
    const f = m.flow.get(id);
    if (!f || isRestrictedFlow(f)) return null;
    return f.ownerTeamId || recordOwner(m, 'application', f.fromId);
  }
  if (kind === 'dataObject') {
    const o = m.objects.get(id);
    return o?.sourceApplicationId ? recordOwner(m, 'application', o.sourceApplicationId) : null;
  }
  return null;
}

/** A page's owner: its own team, or else the owner of the record it is attached to. */
export function pageOwner(
  m: Model | null,
  p: { ownerTeamId?: string | null; attachedTo?: AttachedTo | null },
): string | null {
  if (p.ownerTeamId) return p.ownerTeamId;
  return p.attachedTo ? recordOwner(m, p.attachedTo.kind, p.attachedTo.id) : null;
}

export function canPublishPage(role: Role | undefined, teamIds: string[], owner: string | null): boolean {
  if (role === 'admin') return true;
  return role === 'member' && !!owner && teamIds.includes(owner);
}

/** Titles from the top-level page down to the parent of `id`. */
export function ancestorsOf(pages: PageIndex, id: string): PageSummary[] {
  const out: PageSummary[] = [];
  const seen = new Set([id]);
  let at = pages.get(id)?.parentId ?? null;
  while (at && !seen.has(at)) {
    seen.add(at);
    const p = pages.get(at);
    if (!p) break;
    if (!isRestrictedPage(p)) out.unshift(p);
    at = p.parentId;
  }
  return out;
}

export const pathOf = (pages: PageIndex, id: string) =>
  ancestorsOf(pages, id)
    .map((p) => p.title)
    .join(' › ');

/** Ids of a page and every page under it (a page cannot move under itself). */
export function descendants(pages: PageListItem[], id: string): Set<string> {
  const out = new Set([id]);
  let grew = true;
  while (grew) {
    grew = false;
    for (const p of pages)
      if (p.parentId && out.has(p.parentId) && !out.has(p.id)) {
        out.add(p.id);
        grew = true;
      }
  }
  return out;
}

export function childCount(pages: PageListItem[], id: string) {
  return pages.filter((p) => p.parentId === id).length;
}

/** Tree nodes whose title or one descendant's title matches the filter, the rest pruned. */
export function filterTree(nodes: PageTreeNode[], q: string, fold: (s: string) => string): PageTreeNode[] {
  if (!q) return nodes;
  const out: PageTreeNode[] = [];
  for (const n of nodes) {
    const kids = filterTree(n.children, q, fold);
    if (kids.length || fold(n.title ?? '').includes(q)) out.push({ ...n, children: kids });
  }
  return out;
}

export function flattenTree(nodes: PageTreeNode[], depth = 0, parent: string | null = null) {
  const out: { node: PageTreeNode; depth: number; parentId: string | null }[] = [];
  for (const n of nodes) {
    out.push({ node: n, depth, parentId: parent });
    out.push(...flattenTree(n.children, depth + 1, n.id));
  }
  return out;
}

export type TemplateId = 'blank' | 'runbook' | 'adr' | 'overview';
export const TEMPLATE_IDS: TemplateId[] = ['blank', 'runbook', 'adr', 'overview'];
/** The tag a template adds; it also names the kind of page shown above its title. */
export const TEMPLATE_TAG: Record<Exclude<TemplateId, 'blank'>, string> = {
  runbook: 'runbook',
  adr: 'adr',
  overview: 'overview',
};

export function pageKind(tags: string[]): Exclude<TemplateId, 'blank'> | null {
  for (const k of ['runbook', 'adr', 'overview'] as const) if (tags.includes(TEMPLATE_TAG[k])) return k;
  return null;
}

/**
 * Where a dragged page lands: before or after a row (same parent), or inside it (last child). Returns the new parent
 * and the order to send, plus the siblings whose order must change to make room (rare: only when orders are equal).
 */
export function dropTarget(
  pages: PageListItem[],
  dragged: string,
  target: string,
  where: 'before' | 'after' | 'inside',
): { parentId: string | null; order: number; renumber: { id: string; order: number }[] } | null {
  if (dragged === target || descendants(pages, dragged).has(target)) return null;
  const byId = indexPages(pages);
  const t = byId.get(target);
  if (!t) return null;
  const parentId = where === 'inside' ? target : t.parentId;
  const siblings = pages
    .filter((p) => p.parentId === parentId && p.id !== dragged)
    .sort(
      (a, b) =>
        a.order - b.order || (isRestrictedPage(a) ? a.id : a.title).localeCompare(isRestrictedPage(b) ? b.id : b.title),
    );
  let index = where === 'inside' ? siblings.length : siblings.findIndex((p) => p.id === target);
  if (where === 'after') index += 1;
  const prev = siblings[index - 1];
  const next = siblings[index];
  if (!prev && !next) return { parentId, order: 0, renumber: [] };
  if (!next) return { parentId, order: prev.order + 10, renumber: [] };
  if (!prev) return { parentId, order: next.order - 10, renumber: [] };
  if (next.order - prev.order >= 2) return { parentId, order: Math.floor((prev.order + next.order) / 2), renumber: [] };
  // No room between the neighbours: number the siblings again by tens, the dragged page in its place.
  const renumber: { id: string; order: number }[] = [];
  let order = 0;
  siblings.forEach((p, i) => {
    if (i === index) order += 10;
    order += 10;
    if (p.order !== order) renumber.push({ id: p.id, order });
  });
  return { parentId, order: (index + 1) * 10, renumber };
}

/** "2 hours ago", "Yesterday", "3 weeks ago": how long ago a page changed. */
export function relativeTime(iso: string, lang: string, now = Date.now()): string {
  const d = new Date(iso).getTime();
  if (Number.isNaN(d)) return '';
  const rtf = new Intl.RelativeTimeFormat(lang === 'fr' ? 'fr' : 'en', { numeric: 'auto' });
  const s = Math.round((d - now) / 1000);
  const abs = Math.abs(s);
  const out =
    abs < 60
      ? rtf.format(0, 'second')
      : abs < 3600
        ? rtf.format(Math.round(s / 60), 'minute')
        : abs < 86400
          ? rtf.format(Math.round(s / 3600), 'hour')
          : abs < 86400 * 14
            ? rtf.format(Math.round(s / 86400), 'day')
            : abs < 86400 * 60
              ? rtf.format(Math.round(s / (86400 * 7)), 'week')
              : abs < 86400 * 365
                ? rtf.format(Math.round(s / (86400 * 30)), 'month')
                : rtf.format(Math.round(s / (86400 * 365)), 'year');
  return out.charAt(0).toUpperCase() + out.slice(1);
}
