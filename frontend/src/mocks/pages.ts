// Development-only mock of the documentation pages API (/api/pages). server.ts calls `handlePages` after the
// governance routes; it answers null for paths it does not own.
import type { Page, PageInput, PageLink, PageTreeNode, RecordKind, UserM2 } from '../api/types';
import { SLUG, slugify, wikiLinks } from '../docpages/model';
import { afterSave, deleteRecord, history, ownerOf, pagesOf, permissions, type GovCtx, type GovDb } from './governance';
import { SEED_PAGES } from './pagesSeed';

type Rec = Record<string, unknown>;

export function seedPages(): Page[] {
  const base = Date.now();
  return SEED_PAGES.map(({ hoursAgo, ...p }) => {
    const at = new Date(base - hoursAgo * 3600_000).toISOString();
    return {
      parentId: null,
      attachedTo: null,
      ownerTeamId: null,
      tags: [],
      visibility: 'everyone',
      ...p,
      restricted: false,
      version: 1,
      createdAt: at,
      updatedAt: at,
    } as Page;
  });
}

function exists(db: GovDb, kind: RecordKind, id: string): string | null {
  const name = (l: { id: string; name?: string; title?: string }[]) => {
    const r = l.find((x) => x.id === id);
    return r ? (r.name ?? r.title ?? id) : null;
  };
  if (kind === 'application') return name(db.applications as never);
  if (kind === 'flow') return name(db.flows as never);
  if (kind === 'dataObject') return name(db.dataObjects as never);
  if (kind === 'team') return name(db.teams);
  if (kind === 'domain') return name(db.domains);
  return name(db.pages as never);
}

function links(db: GovDb, body: string): PageLink[] {
  return wikiLinks(body).map((l) => {
    const title = exists(db, l.kind, l.id);
    return { kind: l.kind, id: l.id, label: l.label, title, exists: title !== null, restricted: false };
  });
}

const broken = (db: GovDb, p: Page) => links(db, p.body).filter((l) => !l.exists).length;

const summary = (db: GovDb, p: Page) => {
  const { body: _b, createdAt: _c, ...rest } = p;
  return { ...rest, brokenLinks: broken(db, p) };
};

const byOrder = (a: Page, b: Page) => a.order - b.order || a.title.localeCompare(b.title);

function tree(db: GovDb, parentId: string | null): PageTreeNode[] {
  return db.pages
    .filter((p) => p.parentId === parentId)
    .sort(byOrder)
    .map((p) => ({
      id: p.id,
      restricted: false,
      title: p.title,
      order: p.order,
      attachedTo: p.attachedTo,
      children: tree(db, p.id),
    }));
}

function detail(ctx: GovCtx, u: UserM2, p: Page) {
  const { db } = ctx;
  const ancestors: { id: string; title: string }[] = [];
  for (let cur = p.parentId; cur;) {
    const a = db.pages.find((x) => x.id === cur);
    if (!a || ancestors.some((x) => x.id === a.id)) break;
    ancestors.unshift({ id: a.id, title: a.title });
    cur = a.parentId;
  }
  return {
    ...p,
    links: links(db, p.body),
    backlinks: pagesOf(db, 'page', p.id).mentionedIn.filter((x) => x.id !== p.id),
    ancestors,
    children: db.pages
      .filter((x) => x.parentId === p.id)
      .sort(byOrder)
      .map((x) => ({ id: x.id, title: x.title })),
    attachedTitle: p.attachedTo ? exists(db, p.attachedTo.kind, p.attachedTo.id) : null,
    owningTeamId: ownerOf(db, 'page', p as unknown as Rec),
    permissions: permissions(db, u, 'page', p as unknown as Rec),
  };
}

function validate(db: GovDb, b: PageInput, cur: Page | null) {
  const e: Record<string, string[]> = {};
  if (!b.title?.trim()) e.title = ['The title is required.'];
  else if (b.title.length > 200) e.title = ['Use at most 200 characters.'];
  if (!cur && b.id && (!SLUG.test(b.id) || b.id.length > 64)) e.id = ['The id must be a kebab-case slug.'];
  if ((b.body ?? '').length > 200_000) e.body = ['The page is too long.'];
  if ((b.tags ?? []).length > 20 || (b.tags ?? []).some((t) => t.length > 50)) e.tags = ['At most 20 short tags.'];
  if (b.ownerTeamId && !db.teams.some((t) => t.id === b.ownerTeamId)) e.ownerTeamId = ['Unknown team.'];
  if (b.parentId) {
    if (!db.pages.some((p) => p.id === b.parentId)) e.parentId = ['Unknown parent page.'];
    else if (cur && cycles(db, cur.id, b.parentId)) e.parentId = ['A page cannot be moved under itself.'];
  }
  if (b.attachedTo && !exists(db, b.attachedTo.kind, b.attachedTo.id)) e.attachedTo = ['Unknown record.'];
  return e;
}

function cycles(db: GovDb, id: string, parentId: string | null) {
  for (let cur = parentId; cur; cur = db.pages.find((p) => p.id === cur)?.parentId ?? null) if (cur === id) return true;
  return false;
}

export function handlePages(ctx: GovCtx, u: UserM2, method: string, path: string, body: () => Rec): Response | null {
  const { db, json, problem } = ctx;
  const isAdmin = u.role === 'admin';

  if (path === '/pages' && method === 'GET')
    return json(
      200,
      db.pages.sort(byOrder).map((p) => summary(db, p)),
    );
  if (path === '/pages/tree' && method === 'GET') return json(200, tree(db, null));
  if (path === '/pages' && method === 'POST') {
    if (u.role === 'guest') return problem(403, 'Read-only account.');
    const b = body() as unknown as PageInput;
    const errors = validate(db, b, null);
    const id = b.id || slugify(b.title ?? '').slice(0, 64);
    if (!errors.title && !id) errors.title = ['Use letters or digits in the title.'];
    if (Object.keys(errors).length) return problem(400, 'Validation failed.', undefined, errors);
    if (db.pages.some((p) => p.id === id)) return problem(409, 'Conflict', 'A page with this id already exists.');
    if (b.visibility === 'restricted' && !isAdmin) return problem(403, 'Only administrators restrict pages.');
    const at = ctx.now();
    const page: Page = {
      id,
      restricted: false,
      title: b.title!.trim(),
      body: b.body ?? '',
      parentId: b.parentId ?? null,
      order: b.order ?? 0,
      attachedTo: b.attachedTo ?? null,
      ownerTeamId: b.ownerTeamId ?? null,
      tags: b.tags ?? [],
      visibility: b.visibility ?? 'everyone',
      version: 1,
      createdAt: at,
      updatedAt: at,
    };
    if (!permissions(db, u, 'page', page as unknown as Rec).publish)
      return problem(403, 'Forbidden', 'Only the owning team publishes this page; send a proposal instead.');
    db.pages.push(page);
    history(ctx, 'page', id);
    return json(201, detail(ctx, u, page));
  }

  const m = /^\/pages\/([^/]+)(\/raw|\/move)?$/.exec(path);
  if (!m || m[1] === 'tree') return null;
  const id = decodeURIComponent(m[1]);
  const i = db.pages.findIndex((p) => p.id === id);
  if (i < 0) return problem(404, 'Page not found.');
  const cur = db.pages[i];

  if (m[2] === '/raw' && method === 'GET')
    return new Response(`# ${cur.title}\n\n${cur.body}`, {
      status: 200,
      headers: { 'Content-Type': 'text/markdown; charset=utf-8' },
    });
  if (!m[2] && method === 'GET') return json(200, detail(ctx, u, cur));

  if (u.role === 'guest') return problem(403, 'Read-only account.');
  if (!permissions(db, u, 'page', cur as unknown as Rec).publish)
    return problem(403, 'Forbidden', 'Only the owning team publishes this page; send a proposal instead.');

  const save = (next: Page) => {
    history(ctx, 'page', id);
    db.pages[i] = next;
    afterSave(ctx, u, 'page', next as unknown as Rec);
    return json(200, detail(ctx, u, next));
  };

  if (m[2] === '/move' && method === 'POST') {
    const b = body() as { parentId?: string | null; order?: number | null; version?: number | null };
    if (b.version != null && b.version !== cur.version) return problem(409, 'Conflict');
    const parentId = b.parentId ?? null;
    if (parentId && !db.pages.some((p) => p.id === parentId)) return problem(400, 'Unknown parent page.');
    if (cycles(db, id, parentId)) return problem(400, 'A page cannot be moved under itself.');
    return save({
      ...cur,
      parentId,
      order: b.order ?? cur.order,
      version: cur.version + 1,
      updatedAt: ctx.now(),
    });
  }
  if (!m[2] && method === 'PUT') {
    const b = body() as unknown as PageInput;
    if (b.version !== cur.version) return problem(409, 'Conflict', 'The page was changed by someone else.');
    const errors = validate(db, b, cur);
    if (Object.keys(errors).length) return problem(400, 'Validation failed.', undefined, errors);
    if (b.visibility === 'restricted' && cur.visibility !== 'restricted' && !isAdmin)
      return problem(403, 'Only administrators restrict pages.');
    return save({
      ...cur,
      title: b.title!.trim(),
      body: b.body ?? '',
      parentId: b.parentId ?? null,
      order: b.order ?? cur.order,
      attachedTo: b.attachedTo ?? null,
      ownerTeamId: b.ownerTeamId ?? null,
      tags: b.tags ?? [],
      visibility: b.visibility ?? cur.visibility,
      version: cur.version + 1,
      updatedAt: ctx.now(),
    });
  }
  if (!m[2] && method === 'DELETE') {
    if (db.pages.some((p) => p.parentId === id)) return problem(409, 'Conflict', 'Move or delete the sub-pages first.');
    deleteRecord(ctx, u, 'page', id);
    return json(204);
  }
  return null;
}
