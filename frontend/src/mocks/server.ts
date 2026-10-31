// Development-only mock of the Noosphere API. Loaded by main.tsx when VITE_MOCK=1 in `vite dev`; never part of a
// production build (vite.config.ts refuses VITE_MOCK at build time and main.tsx only imports it under import.meta.env.DEV).
import type {
  Application,
  ApplicationInput,
  DataObject,
  ExchangeEnvelope,
  Flow,
  FlowInput,
  ImportResult,
  LandscapeApplication,
  LandscapeFlow,
  Me,
  Role,
  SearchHit,
  Sensitivity,
  UserCreateInput,
  UserM2,
  UserUpdateInput,
} from '../api/types';
import * as acc from './access';
import * as seed from './alize';
import {
  afterSave,
  deleteRecord,
  guardWrite,
  handleGovernance,
  handlePublic,
  recordExtras,
  seedGovernance,
  type GovCtx,
} from './governance';
import { handlePages, seedPages } from './pages';
import { handleAgents, seedAgents } from './agents';
import { deploymentsFor, handleLinks } from './links';
import { slugify } from '../docpages/model';

const RANK: Record<Sensitivity, number> = { public: 0, internal: 1, personal: 2, sensitive: 3 };
const clone = <T>(v: T): T => JSON.parse(JSON.stringify(v)) as T;

const db = {
  domains: clone(seed.domains),
  teams: clone(seed.teams),
  applications: clone(seed.applications),
  dataObjects: clone(seed.dataObjects),
  flows: clone(seed.flows),
  pages: seedPages(),
  users: [
    { username: 'alice', displayName: 'Alice Martin', role: 'admin', language: 'en', teamIds: [] },
    {
      username: 'bob',
      displayName: 'Bob Durand',
      role: 'member',
      language: 'en',
      teamIds: ['team-finance', 'team-payments'],
    },
    { username: 'claire', displayName: 'Claire Petit', role: 'guest', language: 'fr', teamIds: [] },
    { username: 'david', displayName: 'David Leroy', role: 'member', language: 'en', teamIds: ['team-crm'] },
  ].map((x): UserM2 => ({
    ...(x as Pick<UserM2, 'username' | 'displayName' | 'role' | 'language' | 'teamIds'>),
    version: 1,
    createdAt: '2026-09-01T08:00:00Z',
    updatedAt: '2026-09-01T08:00:00Z',
  })),
};

const SESSION_KEY = 'noos-mock-user';
function currentUser(): UserM2 | null {
  let name: string | null = null;
  try {
    name = sessionStorage.getItem(SESSION_KEY);
  } catch {
    /* storage blocked */
  }
  return db.users.find((u) => u.username === name) ?? null;
}

const delay = () => {
  let ms = 320;
  try {
    const v = sessionStorage.getItem('noos-mock-delay');
    if (v) ms = Number(v);
  } catch {
    /* default */
  }
  return new Promise((r) => setTimeout(r, ms));
};

const json = (status: number, body?: unknown) =>
  new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
  });
const problem = (status: number, title: string, detail?: string, errors?: Record<string, string[]>) =>
  json(status, { type: 'about:blank', title, status, detail, errors });

// Claire, the demo guest, sees the customer portal in full.
for (const a of db.applications) if (a.id === 'portal') a.access = { teamIds: [], usernames: ['claire'] };
acc.seedNotes();
const logAccess = acc.makeLogger(db, () => new Date().toISOString());

const viewApp = (a: Application, u: UserM2) => acc.viewApp(db, u, a) as LandscapeApplication | null;
const viewFlow = (f: Flow, u: UserM2) => acc.viewFlow(db, u, f) as LandscapeFlow | null;
const KINDS: Record<string, string> = {
  applications: 'application',
  flows: 'flow',
  pages: 'page',
  'data-objects': 'dataObject',
  teams: 'team',
  domains: 'domain',
};

function flowSensitivity(ids: string[]): Sensitivity {
  let s: Sensitivity = 'public';
  for (const id of ids) {
    const o = db.dataObjects.find((x) => x.id === id);
    if (o && RANK[o.sensitivity] > RANK[s]) s = o.sensitivity;
  }
  return s;
}
function appCompleteness(a: Application) {
  const hasFlow = db.flows.some((f) => f.fromId === a.id || f.toId === a.id);
  const parts = [!!a.teamId, !!a.tech, !!a.description, hasFlow, !!a.lifecycle];
  return parts.filter(Boolean).length / parts.length;
}
function flowCompleteness(f: Flow) {
  const parts = [!!f.schedule, f.dataObjectIds.length > 0, !!f.ownerTeamId, !!f.description];
  return parts.filter(Boolean).length / parts.length;
}

const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
function validateApp(b: ApplicationInput) {
  const e: Record<string, string[]> = {};
  if (!b.name?.trim()) e.name = ['Name is required.'];
  if (!db.domains.some((d) => d.id === b.domainId)) e.domainId = ['Unknown domain.'];
  if (b.teamId && !db.teams.some((t) => t.id === b.teamId)) e.teamId = ['Unknown team.'];
  return e;
}
function validateFlow(b: FlowInput) {
  const e: Record<string, string[]> = {};
  if (!b.name?.trim()) e.name = ['Name is required.'];
  if (!db.applications.some((a) => a.id === b.fromId)) e.fromId = ['Unknown application.'];
  if (!db.applications.some((a) => a.id === b.toId)) e.toId = ['Unknown application.'];
  if (b.fromId === b.toId) e.toId = ['A flow links two different applications.'];
  return e;
}

const now = () => new Date().toISOString();

type Rec = Record<string, unknown>;
const gov: GovCtx = {
  db,
  json: (status, body) => json(status, body),
  problem: (status, title, detail, errors) => problem(status, title, detail, errors),
  now,
  finish: (kind, rec) => {
    if (kind === 'application') {
      const a = rec as unknown as Application;
      a.sourceOf = db.dataObjects.filter((o) => o.sourceApplicationId === a.id).map((o) => o.id);
      a.completeness = appCompleteness(a);
    }
    if (kind === 'flow') {
      const f = rec as unknown as Flow;
      f.sensitivity = flowSensitivity(f.dataObjectIds ?? []);
      f.completeness = flowCompleteness(f);
    }
    if (kind === 'page') {
      // A page proposed for creation carries the editor's input: give it an id and the defaults.
      if (!rec.id)
        rec.id = slugify(typeof rec.title === 'string' ? rec.title : '').slice(0, 64) || 'page-' + Date.now();
      rec.parentId ??= null;
      rec.order ??= 0;
      rec.attachedTo ??= null;
      rec.ownerTeamId ??= null;
      rec.tags ??= [];
      rec.visibility ??= 'everyone';
      rec.body ??= '';
    }
    return rec;
  },
};

function search(u: UserM2, text: string): SearchHit[] {
  const fold = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  const t = fold(text.trim());
  if (!t) return [];
  const hits: SearchHit[] = [];
  for (const a of db.applications.map((x) => viewApp(x, u)))
    if (a && !a.restricted && fold(a.name).includes(t))
      hits.push({
        kind: 'application',
        id: a.id,
        title: a.name,
        subtitle: a.domainId,
        sensitivity: 'sensitivity' in a ? a.sensitivity : null,
      });
  for (const f of db.flows.map((x) => viewFlow(x, u)))
    if (f && !f.restricted && 'shared' in f === false && fold(f.name).includes(t))
      hits.push({ kind: 'flow', id: f.id, title: f.name, subtitle: `${f.fromId} → ${f.toId}` });
  for (const o of db.dataObjects)
    if (fold(o.name).includes(t)) hits.push({ kind: 'dataObject', id: o.id, title: o.name, subtitle: o.sensitivity });
  for (const tm of db.teams)
    if (fold(tm.name).includes(t)) hits.push({ kind: 'team', id: tm.id, title: tm.name, subtitle: '' });
  for (const d of db.domains)
    if (fold(d.name).includes(t)) hits.push({ kind: 'domain', id: d.id, title: d.name, subtitle: '' });
  for (const p of db.pages)
    if (fold(p.title).includes(t) || fold(p.body).includes(t) || p.tags.some((x) => fold(x).includes(t))) {
      const parent = db.pages.find((x) => x.id === p.parentId);
      hits.push({ kind: 'page', id: p.id, title: p.title, subtitle: parent?.title ?? '' });
    }
  return hits.slice(0, 20);
}

function exportEnvelope(): ExchangeEnvelope {
  const strip = <T extends { id: string; name: string; description?: string | null }>(x: T) => {
    const { id, name, description, ...rest } = x as T & { createdAt?: string; updatedAt?: string; version?: number };
    delete (rest as Record<string, unknown>).createdAt;
    delete (rest as Record<string, unknown>).updatedAt;
    delete (rest as Record<string, unknown>).version;
    return { id, metadata: { name, description: description ?? null }, spec: rest as Record<string, never> };
  };
  return {
    formatVersion: 'noosphere/v1',
    source: { name: 'Alizé (mock)', url: location.origin, exportedAt: now() },
    mode: 'full',
    scope: 'visible',
    items: [
      ...db.domains.map((d) => ({ kind: 'Domain' as const, ...strip(d) })),
      ...db.teams.map((d) => ({ kind: 'Team' as const, ...strip(d) })),
      ...db.applications.map((d) => ({ kind: 'Application' as const, ...strip(d) })),
      ...db.dataObjects.map((d) => ({ kind: 'DataObject' as const, ...strip(d) })),
      ...db.flows.map((d) => ({ kind: 'Flow' as const, ...strip(d) })),
    ],
  };
}

function toYaml(v: unknown, indent = ''): string {
  if (Array.isArray(v))
    return v.length ? v.map((x) => `\n${indent}- ${toYaml(x, indent + '  ').replace(/^\n\s*/, '')}`).join('') : ' []';
  if (v && typeof v === 'object') {
    const e = Object.entries(v).filter(([, x]) => x !== undefined);
    return e
      .map(
        ([k, x]) =>
          `\n${indent}${k}:${typeof x === 'object' && x !== null ? toYaml(x, indent + '  ') : ' ' + JSON.stringify(x)}`,
      )
      .join('');
  }
  return JSON.stringify(v);
}

async function handle(method: string, url: URL, body: string | null): Promise<Response> {
  await delay();
  const path = url.pathname.replace(/^\/api/, '');
  const parse = <T>() => (body ? (JSON.parse(body) as T) : ({} as T));

  if (path === '/auth/login' && method === 'POST') {
    const { username, password } = parse<{ username: string; password: string }>();
    const u = db.users.find((x) => x.username === username);
    if (!u || password !== 'demo') return problem(401, 'Invalid username or password.');
    sessionStorage.setItem(SESSION_KEY, u.username);
    return json(200, {
      ...u,
      teams: db.teams.filter((x) => u.teamIds.includes(x.id)).map((x) => ({ id: x.id, name: x.name })),
    } satisfies Me);
  }
  if (path === '/auth/logout') {
    sessionStorage.removeItem(SESSION_KEY);
    return json(204);
  }
  const pub = handlePublic(gov, method, path);
  if (pub) return pub;
  const u = currentUser();
  if (!u) return problem(401, 'Not signed in.');
  const g = handleGovernance(gov, u, method, url, path, () => parse<Rec>());
  if (g) return g;
  const pg = handlePages(gov, u, method, path, () => parse<Rec>());
  if (pg) return pg;
  const ag = handleAgents(
    gov,
    u,
    method,
    url,
    path,
    () => parse<Rec>(),
    (name) => {
      const n = db.users.find((x) => x.username === name);
      if (!n) return null;
      sessionStorage.setItem(SESSION_KEY, n.username);
      return {
        username: n.username,
        displayName: n.displayName,
        role: n.role,
        language: n.language,
        teams: db.teams.filter((x) => n.teamIds.includes(x.id)).map((x) => ({ id: x.id, name: x.name })),
      };
    },
  );
  if (ag) return ag;
  const lk = handleLinks(
    gov,
    u,
    method,
    url,
    path,
    () => parse<Rec>(),
    (flowId) => {
      const f = db.flows.find((x) => x.id === flowId);
      return f && viewFlow(f, u) ? [f.fromId, f.toId] : null;
    },
  );
  if (lk) return lk;
  const can = (r: Role[]) => r.includes(u.role);

  if (path === '/auth/me')
    return json(200, {
      username: u.username,
      displayName: u.displayName,
      role: u.role,
      language: u.language,
      teams: db.teams.filter((x) => u.teamIds.includes(x.id)).map((x) => ({ id: x.id, name: x.name })),
    });
  if (path === '/auth/me/language' && method === 'PUT') {
    u.language = parse<{ language: 'en' | 'fr' }>().language;
    return json(204);
  }
  if (path === '/landscape') {
    const apps = db.applications.map((a) => viewApp(a, u)).filter((a) => !!a);
    const flows = db.flows.map((f) => viewFlow(f, u)).filter((f) => !!f);
    return json(200, {
      domains: db.domains,
      teams: db.teams,
      applications: apps,
      dataObjects: db.dataObjects,
      flows,
      stats: { applications: apps.length, flows: flows.length, domains: db.domains.length },
      generatedAt: now(),
    });
  }
  if (path === '/search') return json(200, search(u, url.searchParams.get('q') ?? ''));
  if (path === '/domains') return json(200, db.domains);
  const dm = /^\/domains\/([^/]+)$/.exec(path);
  if (dm && method === 'GET') {
    const d = db.domains.find((x) => x.id === decodeURIComponent(dm![1]));
    if (!d) return problem(404, 'Domain not found.');
    return json(200, { ...d, ...recordExtras(gov, u, 'domain', d as unknown as Rec) });
  }
  const am = /^\/(applications|flows|pages|data-objects|teams|domains)\/([^/]+)\/access$/.exec(path);
  if (am && method === 'GET') {
    const kind = KINDS[am[1]];
    const id = decodeURIComponent(am[2]);
    const list: Rec[] =
      kind === 'application'
        ? (db.applications as unknown as Rec[])
        : kind === 'flow'
          ? (db.flows as unknown as Rec[])
          : kind === 'page'
            ? (db.pages as unknown as Rec[])
            : kind === 'team'
              ? (db.teams as unknown as Rec[])
              : kind === 'domain'
                ? (db.domains as unknown as Rec[])
                : (db.dataObjects as unknown as Rec[]);
    const rec = list.find((x) => x.id === id);
    if (!rec) return problem(404, 'Not found.');
    return json(200, acc.recordAccess(db, u, kind, rec));
  }
  if (path === '/people') {
    if (u.role === 'guest') return problem(403, 'Guests do not list people.');
    return json(
      200,
      db.users.map((x) => ({
        username: x.username,
        displayName: x.displayName,
        role: x.role,
        teamIds: x.teamIds ?? [],
      })),
    );
  }
  if (path === '/access/overview') {
    if (!can(['admin'])) return problem(403, 'Administrators only.');
    return json(200, acc.overview(db, db.pages));
  }
  if (path === '/access-log') {
    if (!can(['admin'])) return problem(403, 'Administrators only.');
    return json(200, acc.logPage(url.searchParams));
  }
  if (path === '/teams') return json(200, db.teams);
  if (path === '/data-objects') return json(200, db.dataObjects);

  let m = /^\/applications\/([^/]+)$/.exec(path);
  if (m) {
    const id = decodeURIComponent(m[1]);
    const i = db.applications.findIndex((a) => a.id === id);
    if (i < 0) return problem(404, 'Application not found.');
    const cur = db.applications[i];
    if (method === 'GET') {
      const v = viewApp(cur, u);
      if (!v) return problem(404, 'Application not found.');
      if (v.restricted || 'shared' in v) return json(200, v);
      if (cur.visibility === 'restricted' && !acc.isMember(u, cur.teamId))
        logAccess(u, 'record', 'application', id, u.role === 'admin' ? 'admin' : 'listedUser');
      return json(200, {
        ...v,
        ...recordExtras(gov, u, 'application', cur as unknown as Rec),
        ...acc.notesFor(db, u, 'application', cur, logAccess),
        deployments: deploymentsFor(u, [cur.id], 3),
      });
    }
    if (method === 'PUT') {
      if (!can(['admin', 'member'])) return problem(403, 'Read-only account.');
      const denied = guardWrite(gov, u, 'application', cur as unknown as Rec);
      if (denied) return denied;
      const b = parse<ApplicationInput>();
      if (b.version !== cur.version) return problem(409, 'Conflict', 'The record was changed by someone else.');
      const errors = validateApp(b);
      if (Object.keys(errors).length) return problem(400, 'Validation failed.', undefined, errors);
      const secrets = acc.secretErrors(b as unknown as Rec);
      if (Object.keys(secrets).length)
        return problem(422, 'Unprocessable content', 'Some fields look like secrets.', secrets);
      acc.saveNotes('application', id, b);
      const { protectedNotes: _n, ...rest } = b as ApplicationInput & { protectedNotes?: string | null };
      const next: Application = {
        ...cur,
        ...(rest as Partial<Application>),
        access: b.access ? { teamIds: b.access.teamIds ?? [], usernames: b.access.usernames ?? [] } : cur.access,
        id,
        restricted: false,
        sourceOf: cur.sourceOf,
        version: cur.version + 1,
        updatedAt: now(),
      };
      next.completeness = appCompleteness(next);
      db.applications[i] = next;
      afterSave(gov, u, 'application', next as unknown as Rec);
      return json(200, {
        ...next,
        ...recordExtras(gov, u, 'application', next as unknown as Rec),
        ...acc.notesFor(db, u, 'application', next, logAccess),
      });
    }
    if (method === 'DELETE') {
      if (!can(['admin'])) return problem(403, 'Only administrators can delete.');
      deleteRecord(gov, u, 'application', id);
      return json(204);
    }
  }
  m = /^\/data-objects\/([^/]+)$/.exec(path);
  if (m && method === 'GET') {
    const o = db.dataObjects.find((x) => x.id === decodeURIComponent(m![1]));
    if (!o) return problem(404, 'Data object not found.');
    return json(200, { ...o, ...recordExtras(gov, u, 'dataObject', o as unknown as Rec) });
  }
  m = /^\/flows\/([^/]+)$/.exec(path);
  if (m) {
    const id = decodeURIComponent(m[1]);
    const i = db.flows.findIndex((a) => a.id === id);
    if (i < 0) return problem(404, 'Flow not found.');
    const cur = db.flows[i];
    if (method === 'GET') {
      const v = viewFlow(cur, u);
      if (!v) return problem(404, 'Flow not found.');
      if (v.restricted || 'shared' in v) return json(200, v);
      return json(200, {
        ...v,
        completeness: flowCompleteness(cur),
        ...recordExtras(gov, u, 'flow', cur as unknown as Rec),
        ...acc.notesFor(db, u, 'flow', cur, logAccess),
        deployments: deploymentsFor(u, [cur.fromId, cur.toId], 3),
      });
    }
    if (method === 'PUT') {
      if (!can(['admin', 'member'])) return problem(403, 'Read-only account.');
      const denied = guardWrite(gov, u, 'flow', cur as unknown as Rec);
      if (denied) return denied;
      const b = parse<FlowInput>();
      if (b.version !== cur.version) return problem(409, 'Conflict', 'The record was changed by someone else.');
      const errors = validateFlow(b);
      if (Object.keys(errors).length) return problem(400, 'Validation failed.', undefined, errors);
      const secrets = acc.secretErrors(b as unknown as Rec);
      if (Object.keys(secrets).length)
        return problem(422, 'Unprocessable content', 'Some fields look like secrets.', secrets);
      acc.saveNotes('flow', id, b);
      const { protectedNotes: _fn, ...frest } = b as FlowInput & { protectedNotes?: string | null };
      const next: Flow = {
        ...cur,
        ...(frest as Partial<Flow>),
        id,
        restricted: false,
        version: cur.version + 1,
        updatedAt: now(),
        sensitivity: flowSensitivity(b.dataObjectIds ?? []),
      };
      db.flows[i] = next;
      afterSave(gov, u, 'flow', next as unknown as Rec);
      return json(200, {
        ...next,
        completeness: flowCompleteness(next),
        ...recordExtras(gov, u, 'flow', next as unknown as Rec),
        ...acc.notesFor(db, u, 'flow', next, logAccess),
      });
    }
    if (method === 'DELETE') {
      if (!can(['admin'])) return problem(403, 'Only administrators can delete.');
      deleteRecord(gov, u, 'flow', id);
      return json(204);
    }
  }
  if (path === '/users') {
    if (!can(['admin'])) return problem(403, 'Administrators only.');
    if (method === 'GET') return json(200, db.users);
    if (method === 'POST') {
      const b = parse<UserCreateInput>();
      if (!SLUG.test(b.username ?? ''))
        return problem(400, 'Validation failed.', undefined, {
          username: ['Use lower-case letters, digits and dashes.'],
        });
      if (db.users.some((x) => x.username === b.username))
        return problem(409, 'Conflict', 'This username already exists.');
      const nu: UserM2 = {
        teamIds: (b as { teamIds?: string[] }).teamIds ?? [],
        username: b.username ?? '',
        displayName: b.displayName ?? '',
        role: b.role ?? 'guest',
        language: b.language === 'fr' ? 'fr' : 'en',
        version: 1,
        createdAt: now(),
        updatedAt: now(),
      };
      db.users.push(nu);
      return json(201, nu);
    }
  }
  m = /^\/users\/([^/]+)$/.exec(path);
  if (m) {
    if (!can(['admin'])) return problem(403, 'Administrators only.');
    const i = db.users.findIndex((x) => x.username === decodeURIComponent(m![1]));
    if (i < 0) return problem(404, 'User not found.');
    if (method === 'PUT') {
      const b = parse<UserUpdateInput>();
      const cur = db.users[i];
      if (b.version != null && b.version !== cur.version) return problem(409, 'Conflict');
      db.users[i] = {
        ...cur,
        displayName: b.displayName ?? cur.displayName,
        role: b.role ?? cur.role,
        language: b.language === 'fr' || b.language === 'en' ? b.language : cur.language,
        teamIds: (b as { teamIds?: string[] | null }).teamIds ?? cur.teamIds,
        version: cur.version + 1,
        updatedAt: now(),
      };
      return json(200, db.users[i]);
    }
    if (method === 'DELETE') {
      if (db.users[i].username === u.username) return problem(400, 'You cannot delete your own account.');
      db.users.splice(i, 1);
      return json(204);
    }
  }
  if (path === '/export') {
    const full = url.searchParams.get('scope') === 'full';
    if (full && !can(['admin'])) return problem(403, 'Only administrators export the full landscape.');
    const reason = url.searchParams.get('reason')?.trim();
    if (reason && reason.length > 200)
      return problem(400, 'Validation failed.', undefined, { reason: ['At most 200 characters.'] });
    if (full) logAccess(u, 'export', null, null, 'admin', reason || 'scope full, json');
    const env = exportEnvelope();
    if (url.searchParams.get('format') === 'yaml')
      return new Response(toYaml(env).trimStart() + '\n', {
        status: 200,
        headers: { 'Content-Type': 'application/yaml' },
      });
    return json(200, env);
  }
  if (path === '/import' && method === 'POST') {
    if (!can(['admin'])) return problem(403, 'Administrators only.');
    let env: ExchangeEnvelope;
    try {
      env = JSON.parse(body ?? '') as ExchangeEnvelope;
    } catch {
      return problem(400, 'The file is not valid JSON.');
    }
    if (env.formatVersion !== 'noosphere/v1')
      return problem(400, 'Unsupported format version.', `Expected noosphere/v1, got ${String(env.formatVersion)}.`);
    const dryRun = url.searchParams.get('dryRun') === 'true';
    const res: ImportResult = { created: 0, updated: 0, unchanged: 0, errors: [], dryRun, applied: false };
    const err = (index: number, it: { id?: string; kind?: string }, path: string, message: string) =>
      res.errors.push({ index, kind: it.kind ?? null, id: it.id ?? null, path, message });
    const lists: Record<string, { id: string }[]> = {
      Domain: db.domains,
      Team: db.teams,
      Application: db.applications,
      DataObject: db.dataObjects as DataObject[],
      Flow: db.flows,
    };
    for (const [index, it] of (env.items ?? []).entries()) {
      const list = lists[it.kind];
      if (!list) {
        err(index, it, `items[${index}].kind`, `Unknown kind ${it.kind}.`);
        continue;
      }
      if (!it.id || !SLUG.test(it.id)) {
        err(index, it, `items[${index}].id`, 'The id must be a kebab-case slug.');
        continue;
      }
      const ex = list.find((x) => x.id === it.id);
      if (!ex) res.created++;
      else if (JSON.stringify({ ...ex, ...it.spec, name: it.metadata.name }) === JSON.stringify(ex)) res.unchanged++;
      else res.updated++;
    }
    if (res.errors.length) return json(422, res);
    res.applied = !dryRun;
    return json(200, res);
  }
  return problem(404, 'Not found.');
}

seedGovernance(gov);
seedAgents(gov);

export function installMockApi() {
  const real = window.fetch.bind(window);
  window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(
      typeof input === 'string' ? input : input instanceof URL ? input.href : input.url,
      location.origin,
    );
    if (!url.pathname.startsWith('/api/')) return real(input, init);
    const method = (init?.method ?? 'GET').toUpperCase();
    const body = typeof init?.body === 'string' ? init.body : null;
    const headers = new Headers(init?.headers);
    if (method !== 'GET' && headers.get('X-Requested-With') !== 'fetch')
      return new Response(JSON.stringify({ title: 'Forbidden', status: 403 }), {
        status: 403,
        headers: { 'Content-Type': 'application/problem+json' },
      });
    return handle(method, url, body);
  };
  console.warn('Noosphere: mock API enabled (VITE_MOCK=1).');
}
