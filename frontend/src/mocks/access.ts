// Development-only: access lists, guests, protected notes, sensitivity, the secret check and the access log of the
// mock API, close to what the real API does (docs/en/access.md).
import type {
  AccessLogEntry,
  Application,
  DataObject,
  Flow,
  RecordAccess,
  Sensitivity,
  SensitivityReason,
  UserM2,
} from '../api/types';

const RANK: Record<Sensitivity, number> = { public: 0, internal: 1, personal: 2, sensitive: 3 };
export const MASK = '••• protected';

export interface AccessDb {
  teams: { id: string; name: string }[];
  applications: Application[];
  flows: Flow[];
  dataObjects: DataObject[];
  users: UserM2[];
}

type Notes = { protectedNotes?: string | null };
const notes = new Map<string, string>();
const key = (kind: string, id: string) => kind + ':' + id;

export const seedNotes = () => {
  notes.set(
    key('application', 'billing'),
    'Production database on `pg-fin-02`. On-call: Team Finance rota. Credentials live in Vault under `kv/finance/billing`.',
  );
  notes.set(key('flow', 'billing-mail'), 'Mail relay account is managed by Team Platform; ask them for the SMTP host.');
};

export const isMember = (u: UserM2, team: string | null | undefined) => !!team && (u.teamIds ?? []).includes(team);
const members = (db: AccessDb, team: string) => db.users.filter((x) => (x.teamIds ?? []).includes(team)).length;

/** True when the reader sees the application in full. */
export function canSee(_db: AccessDb, u: UserM2, a: Application): boolean {
  if (u.role === 'admin') return true;
  const listed = a.access?.usernames.includes(u.username) ?? false;
  if (u.role === 'guest') return listed;
  if (a.visibility !== 'restricted') return true;
  return isMember(u, a.teamId) || listed || (a.access?.teamIds ?? []).some((t) => isMember(u, t));
}

const app = (db: AccessDb, id: string) => db.applications.find((x) => x.id === id);

export function canSeeFlow(db: AccessDb, u: UserM2, f: Flow): boolean {
  const ends = [app(db, f.fromId), app(db, f.toId)].filter((a): a is Application => !!a);
  if (u.role === 'guest') return ends.some((a) => canSee(db, u, a)) && ends.every((a) => a.visibility !== 'restricted');
  return ends.every((a) => canSee(db, u, a));
}

/** Computed sensitivity of an application: the highest of its flows and the data it is the source of. */
export function appSensitivity(db: AccessDb, a: Application): { level: Sensitivity; reasons: SensitivityReason[] } {
  const reasons: SensitivityReason[] = [];
  for (const f of db.flows)
    if (f.fromId === a.id || f.toId === a.id)
      reasons.push({ kind: 'flow', id: f.id, name: f.name, sensitivity: f.sensitivity });
  for (const o of db.dataObjects)
    if (o.sourceApplicationId === a.id)
      reasons.push({ kind: 'dataObject', id: o.id, name: o.name, sensitivity: o.sensitivity });
  const level = reasons.reduce<Sensitivity>((s, r) => (RANK[r.sensitivity] > RANK[s] ? r.sensitivity : s), 'public');
  const top = reasons.filter((r) => r.sensitivity === level).slice(0, 5);
  return { level, reasons: top };
}

/** What the reader receives for an application in the landscape; null when it is left out entirely. */
export function viewApp(db: AccessDb, u: UserM2, a: Application) {
  const s = appSensitivity(db, a);
  if (canSee(db, u, a)) return { ...a, sensitivity: s.level, sensitivityReasons: s.reasons };
  if (u.role === 'guest')
    return a.visibility === 'restricted'
      ? null
      : {
          id: a.id,
          restricted: false as const,
          shared: false as const,
          name: a.name,
          domainId: a.domainId,
          kind: a.kind,
        };
  return { id: a.id, domainId: a.domainId, restricted: true as const };
}

export function viewFlow(db: AccessDb, u: UserM2, f: Flow) {
  if (canSeeFlow(db, u, f)) return f;
  if (u.role === 'guest') {
    const ends = [app(db, f.fromId), app(db, f.toId)];
    if (ends.some((a) => a?.visibility === 'restricted')) return null;
    return {
      id: f.id,
      restricted: false as const,
      shared: false as const,
      name: f.name,
      fromId: f.fromId,
      toId: f.toId,
      type: f.type,
    };
  }
  return { id: f.id, fromId: f.fromId, toId: f.toId, type: f.type, restricted: true as const };
}

const ownerOfFlow = (db: AccessDb, f: Flow) => f.ownerTeamId || app(db, f.fromId)?.teamId || null;

/** Protected notes of a record for its reader: the text, or the fact that they are hidden. */
export function notesFor(db: AccessDb, u: UserM2, kind: 'application' | 'flow', rec: Application | Flow, log: Logger) {
  const text = notes.get(key(kind, rec.id)) ?? null;
  if (!text) return { protectedNotes: null, protectedNotesHidden: false };
  const owner = kind === 'application' ? (rec as Application).teamId : ownerOfFlow(db, rec as Flow);
  const a = kind === 'application' ? (rec as Application) : null;
  const listed =
    !!a &&
    a.visibility === 'restricted' &&
    (a.access?.usernames.includes(u.username) || (a.access?.teamIds ?? []).some((t) => isMember(u, t)));
  const readable = u.role === 'admin' || (u.role !== 'guest' && (isMember(u, owner) || listed));
  if (!readable) return { protectedNotes: null, protectedNotesHidden: true };
  if (u.role === 'admin' && !isMember(u, owner)) log(u, 'protectedNotes', kind, rec.id, 'admin');
  else if (!isMember(u, owner)) log(u, 'protectedNotes', kind, rec.id, 'listedUser');
  return { protectedNotes: text, protectedNotesHidden: false };
}

/** Saves protected notes from a PUT body: left out, they are kept; empty, they are removed. */
export function saveNotes(kind: string, id: string, body: Notes) {
  if (body.protectedNotes === undefined) return;
  if (body.protectedNotes) notes.set(key(kind, id), body.protectedNotes);
  else notes.delete(key(kind, id));
}
export const hasNotes = (kind: string, id: string) => notes.has(key(kind, id));

/* ---- the secret check ---- */

const PATTERNS: [RegExp, string][] = [
  [/-{5}BEGIN [A-Z ]*PRIVATE KEY-{5}/, 'private key'],
  [/\bAKIA[0-9A-Z]{16}\b/, 'AWS access key'],
  [/\bgh[pousr]_[A-Za-z0-9]{36}\b/, 'GitHub token'],
  [/\bxox[abprs]-[A-Za-z0-9-]{10,}/, 'Slack token'],
  [/\bsk_live_[A-Za-z0-9]{16,}/, 'Stripe secret key'],
  [/\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}/, 'JSON Web Token'],
  [/[a-z][a-z0-9+.-]*:\/\/[^\s:@/]+:[^\s@/]{3,}@/i, 'password in a URL'],
  [/\b(password|passwd|pwd)\s*[:=]\s*(?!<|\$\{|\*{3}|x{3})\S{6,}/i, 'password'],
];

/** Field errors for every text that looks like a secret. */
export function secretErrors(body: Record<string, unknown>, prefix = ''): Record<string, string[]> {
  const e: Record<string, string[]> = {};
  for (const [k, v] of Object.entries(body)) {
    if (typeof v !== 'string') continue;
    const hit = PATTERNS.find(([re]) => re.test(v));
    if (hit)
      e[prefix + k] = [
        `This looks like a secret (${hit[1]}). Noosphere is documentation, not a vault: store it in your secret manager and write where it lives instead.`,
      ];
  }
  return e;
}

/* ---- "Who can see and edit?" ---- */

export function recordAccess(db: AccessDb, u: UserM2, kind: string, rec: Record<string, unknown>): RecordAccess {
  const team = (id: string) => ({
    id,
    name: db.teams.find((x) => x.id === id)?.name ?? id,
    memberCount: members(db, id),
  });
  let owner: string | null = null;
  let visibility: 'everyone' | 'restricted' = 'everyone';
  let access = { teamIds: [] as string[], usernames: [] as string[] };
  let notesKey: string | null = null;
  if (kind === 'application') {
    const a = rec as unknown as Application;
    owner = a.teamId;
    visibility = a.visibility;
    access = a.access ?? access;
    notesKey = key('application', a.id);
  } else if (kind === 'flow') {
    const f = rec as unknown as Flow;
    owner = ownerOfFlow(db, f);
    const ends = [app(db, f.fromId), app(db, f.toId)].filter((a): a is Application => !!a);
    const r = ends.filter((a) => a.visibility === 'restricted');
    if (r.length) {
      visibility = 'restricted';
      access = {
        teamIds: [...new Set(r.flatMap((a) => [a.teamId ?? '', ...(a.access?.teamIds ?? [])]))].filter(Boolean),
        usernames: [],
      };
    }
    notesKey = key('flow', f.id);
  } else if (kind === 'page') {
    owner = (rec.ownerTeamId as string | null) ?? null;
    visibility = (rec.visibility as 'everyone' | 'restricted') ?? 'everyone';
    access = (rec.access as typeof access) ?? access;
  }
  const readers =
    visibility === 'restricted'
      ? [...new Set([owner, ...access.teamIds].filter((x): x is string => !!x))].map(team)
      : [];
  const users = access.usernames
    .map((n) => db.users.find((x) => x.username === n))
    .filter((x): x is UserM2 => !!x)
    .map((x) => ({ username: x.username, displayName: x.displayName }));
  const exists = !!notesKey && notes.has(notesKey);
  const canManage =
    (kind === 'application' || kind === 'page') && (u.role === 'admin' || (u.role === 'member' && isMember(u, owner)));
  return {
    visibility,
    owningTeam: owner ? team(owner) : null,
    readers: { everyoneExceptGuests: visibility !== 'restricted', teams: readers, users },
    publishers: { teams: owner ? [team(owner)] : [], admins: true },
    proposers: 'members',
    protectedNotes: {
      exists,
      readable: exists && (u.role === 'admin' || (u.role === 'member' && isMember(u, owner))),
    },
    canManage,
  };
}

/* ---- the access log ---- */

export type Logger = (
  u: UserM2,
  what: AccessLogEntry['what'],
  kind: AccessLogEntry['kind'],
  id: string | null,
  why: AccessLogEntry['why'],
  detail?: string | null,
) => void;

export const accessLog: AccessLogEntry[] = [];

export function makeLogger(db: AccessDb, now: () => string): Logger {
  return (u, what, kind, id, why, detail = null) => {
    const a = kind === 'application' && id ? app(db, id) : undefined;
    const f = kind === 'flow' && id ? db.flows.find((x) => x.id === id) : undefined;
    const owner = a ? a.teamId : f ? ownerOfFlow(db, f) : null;
    const ownerTeam = owner ? { id: owner, name: db.teams.find((x) => x.id === owner)?.name ?? owner } : null;
    accessLog.unshift({
      id: accessLog.length + 1,
      at: now(),
      user: { username: u.username, displayName: u.displayName },
      kind,
      entityId: id,
      title: a?.name ?? f?.name ?? null,
      what,
      detail,
      admin: u.role === 'admin',
      why,
      whyTeam: null,
      owningTeam: ownerTeam,
    });
  };
}

/** One page of the access log, filtered as the real API does. */
export function logPage(q: URLSearchParams) {
  const username = q.get('username')?.trim().toLowerCase();
  const kind = q.get('kind');
  const entityId = q.get('entityId');
  const what = q.get('what');
  const from = q.get('from');
  const to = q.get('to');
  const limit = Number(q.get('limit') ?? 50);
  const start = Number(q.get('cursor') ?? 0);
  const rows = accessLog.filter(
    (l) =>
      (!username || l.user.username === username) &&
      (!kind || l.kind === kind) &&
      (!entityId || l.entityId === entityId) &&
      (!what || l.what === what) &&
      (!from || l.at.slice(0, 10) >= from) &&
      (!to || l.at.slice(0, 10) <= to),
  );
  const items = rows.slice(start, start + limit);
  return { items, nextCursor: start + limit < rows.length ? String(start + limit) : null };
}

/** The administrators' overview of the Access page. */
export function overview(
  db: AccessDb,
  pages: {
    id: string;
    title: string;
    visibility: string;
    ownerTeamId: string | null;
    access?: { usernames: string[] };
  }[],
) {
  const rec = (kind: 'application' | 'flow' | 'page', id: string, title: string, owningTeamId: string | null) => ({
    kind,
    id,
    title,
    owningTeamId,
  });
  const restricted = [
    ...db.applications
      .filter((a) => a.visibility === 'restricted')
      .map((a) => rec('application', a.id, a.name, a.teamId)),
    ...pages.filter((p) => p.visibility === 'restricted').map((p) => rec('page', p.id, p.title, p.ownerTeamId)),
  ];
  const protectedNotes = [
    ...db.applications
      .filter((a) => notes.has(key('application', a.id)))
      .map((a) => rec('application', a.id, a.name, a.teamId)),
    ...db.flows.filter((f) => notes.has(key('flow', f.id))).map((f) => rec('flow', f.id, f.name, ownerOfFlow(db, f))),
  ];
  const guests = db.users
    .filter((u) => u.role === 'guest')
    .map((u) => ({
      user: { username: u.username, displayName: u.displayName },
      records: [
        ...db.applications
          .filter((a) => a.access?.usernames.includes(u.username))
          .map((a) => rec('application', a.id, a.name, a.teamId)),
        ...pages
          .filter((p) => p.access?.usernames.includes(u.username))
          .map((p) => rec('page', p.id, p.title, p.ownerTeamId)),
      ],
    }));
  return {
    roles: {
      admins: db.users.filter((u) => u.role === 'admin').length,
      members: db.users.filter((u) => u.role === 'member').length,
      guests: db.users.filter((u) => u.role === 'guest').length,
    },
    restricted,
    protectedNotes,
    guests,
    teams: db.teams.map((t) => ({
      id: t.id,
      name: t.name,
      members: members(db, t.id),
      applications: db.applications.filter((a) => a.teamId === t.id).length,
      flows: db.flows.filter((f) => ownerOfFlow(db, f) === t.id).length,
    })),
  };
}
