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
  User,
  UserCreateInput,
  UserUpdateInput,
} from '../api/types';
import * as seed from './alize';

const RANK: Record<Sensitivity, number> = { public: 0, internal: 1, personal: 2, sensitive: 3 };
const clone = <T>(v: T): T => JSON.parse(JSON.stringify(v)) as T;

const db = {
  domains: clone(seed.domains),
  teams: clone(seed.teams),
  applications: clone(seed.applications),
  dataObjects: clone(seed.dataObjects),
  flows: clone(seed.flows),
  users: [
    { username: 'alice', displayName: 'Alice Martin', role: 'admin', language: 'en' },
    { username: 'bob', displayName: 'Bob Durand', role: 'member', language: 'en' },
    { username: 'claire', displayName: 'Claire Petit', role: 'guest', language: 'fr' },
  ].map((x): User => ({
    ...(x as Pick<User, 'username' | 'displayName' | 'role' | 'language'>),
    version: 1,
    createdAt: '2026-09-01T08:00:00Z',
    updatedAt: '2026-09-01T08:00:00Z',
  })),
};

const SESSION_KEY = 'noos-mock-user';
function currentUser(): User | null {
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

// The mock never authorises anyone for restricted applications, so the restricted shapes are always visible.
const canSeeRestricted = (_u: User) => false;

function viewApp(a: Application, u: User): LandscapeApplication {
  return a.visibility === 'restricted' && !canSeeRestricted(u)
    ? { id: a.id, domainId: a.domainId, restricted: true }
    : a;
}
function viewFlow(f: Flow, u: User): LandscapeFlow {
  const hidden = (id: string) => {
    const a = db.applications.find((x) => x.id === id);
    return !!a && a.visibility === 'restricted' && !canSeeRestricted(u);
  };
  return hidden(f.fromId) || hidden(f.toId)
    ? { id: f.id, fromId: f.fromId, toId: f.toId, type: f.type, restricted: true }
    : f;
}

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

function search(u: User, text: string): SearchHit[] {
  const fold = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  const t = fold(text.trim());
  if (!t) return [];
  const hits: SearchHit[] = [];
  for (const a of db.applications.map((x) => viewApp(x, u)))
    if (!a.restricted && fold(a.name).includes(t))
      hits.push({ kind: 'application', id: a.id, title: a.name, subtitle: a.domainId });
  for (const f of db.flows.map((x) => viewFlow(x, u)))
    if (!f.restricted && fold(f.name).includes(t))
      hits.push({ kind: 'flow', id: f.id, title: f.name, subtitle: `${f.fromId} → ${f.toId}` });
  for (const o of db.dataObjects)
    if (fold(o.name).includes(t)) hits.push({ kind: 'dataObject', id: o.id, title: o.name, subtitle: o.sensitivity });
  for (const tm of db.teams)
    if (fold(tm.name).includes(t)) hits.push({ kind: 'team', id: tm.id, title: tm.name, subtitle: '' });
  for (const d of db.domains)
    if (fold(d.name).includes(t)) hits.push({ kind: 'domain', id: d.id, title: d.name, subtitle: '' });
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
    return json(200, u satisfies Me);
  }
  if (path === '/auth/logout') {
    sessionStorage.removeItem(SESSION_KEY);
    return json(204);
  }
  const u = currentUser();
  if (!u) return problem(401, 'Not signed in.');
  const can = (r: Role[]) => r.includes(u.role);

  if (path === '/auth/me')
    return json(200, { username: u.username, displayName: u.displayName, role: u.role, language: u.language });
  if (path === '/auth/me/language' && method === 'PUT') {
    u.language = parse<{ language: 'en' | 'fr' }>().language;
    return json(204);
  }
  if (path === '/landscape') {
    const apps = db.applications.map((a) => viewApp(a, u));
    return json(200, {
      domains: db.domains,
      teams: db.teams,
      applications: apps,
      dataObjects: db.dataObjects,
      flows: db.flows.map((f) => viewFlow(f, u)),
      stats: { applications: apps.length, flows: db.flows.length, domains: db.domains.length },
      generatedAt: now(),
    });
  }
  if (path === '/search') return json(200, search(u, url.searchParams.get('q') ?? ''));
  if (path === '/domains') return json(200, db.domains);
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
      return v.restricted ? problem(403, 'Restricted application.') : json(200, v);
    }
    if (method === 'PUT') {
      if (!can(['admin', 'member'])) return problem(403, 'Read-only account.');
      const b = parse<ApplicationInput>();
      if (b.version !== cur.version) return problem(409, 'Conflict', 'The record was changed by someone else.');
      const errors = validateApp(b);
      if (Object.keys(errors).length) return problem(400, 'Validation failed.', undefined, errors);
      const next: Application = {
        ...cur,
        ...(b as Partial<Application>),
        id,
        restricted: false,
        sourceOf: cur.sourceOf,
        version: cur.version + 1,
        updatedAt: now(),
      };
      next.completeness = appCompleteness(next);
      db.applications[i] = next;
      return json(200, next);
    }
    if (method === 'DELETE') {
      if (!can(['admin'])) return problem(403, 'Only administrators can delete.');
      db.applications.splice(i, 1);
      db.flows = db.flows.filter((f) => f.fromId !== id && f.toId !== id);
      return json(204);
    }
  }
  m = /^\/flows\/([^/]+)$/.exec(path);
  if (m) {
    const id = decodeURIComponent(m[1]);
    const i = db.flows.findIndex((a) => a.id === id);
    if (i < 0) return problem(404, 'Flow not found.');
    const cur = db.flows[i];
    if (method === 'GET') {
      const v = viewFlow(cur, u);
      return v.restricted ? problem(403, 'Restricted flow.') : json(200, { ...v, completeness: flowCompleteness(cur) });
    }
    if (method === 'PUT') {
      if (!can(['admin', 'member'])) return problem(403, 'Read-only account.');
      const b = parse<FlowInput>();
      if (b.version !== cur.version) return problem(409, 'Conflict', 'The record was changed by someone else.');
      const errors = validateFlow(b);
      if (Object.keys(errors).length) return problem(400, 'Validation failed.', undefined, errors);
      const next: Flow = {
        ...cur,
        ...(b as Partial<Flow>),
        id,
        restricted: false,
        version: cur.version + 1,
        updatedAt: now(),
        sensitivity: flowSensitivity(b.dataObjectIds ?? []),
      };
      db.flows[i] = next;
      return json(200, { ...next, completeness: flowCompleteness(next) });
    }
    if (method === 'DELETE') {
      if (!can(['admin'])) return problem(403, 'Only administrators can delete.');
      db.flows.splice(i, 1);
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
      const nu: User = {
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
