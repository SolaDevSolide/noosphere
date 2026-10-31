// Development-only mock of the milestone 2 API: team memberships, ownership, proposals, revisions, trash and
// settings. server.ts calls `handleGovernance` before its own routes; it answers null for paths it does not own.
import type {
  Application,
  RecordKind,
  DataObject,
  FieldDiff,
  Flow,
  HistoryEntry,
  OwnerApproval,
  Page,
  Permissions,
  PersonRef,
  Proposal,
  ProposalChange,
  ProposalInput,
  Revision,
  Settings,
  Team,
  TrashItem,
  TrashKind,
  UserM2,
} from '../api/types';

type Rec = Record<string, unknown>;
type Respond = (status: number, body?: unknown) => Response;
type Problem = (status: number, title: string, detail?: string, errors?: Record<string, string[]>) => Response;

export interface GovDb {
  domains: { id: string; name: string }[];
  teams: Team[];
  applications: Application[];
  dataObjects: DataObject[];
  flows: Flow[];
  pages: Page[];
  users: UserM2[];
}

export interface GovCtx {
  db: GovDb;
  json: Respond;
  problem: Problem;
  now: () => string;
  /** Recomputes the server-side fields of a record after a change. */
  finish: (kind: RecordKind, rec: Rec) => Rec;
}

/* ---------- field catalogue used by the diff ---------- */

const FIELDS: Record<RecordKind, [string, string, FieldDiff['kind']][]> = {
  application: [
    ['name', 'Name', 'text'],
    ['domainId', 'Domain', 'ref'],
    ['teamId', 'Team', 'ref'],
    ['kind', 'Kind', 'text'],
    ['tech', 'Technology', 'text'],
    ['lifecycle', 'Lifecycle', 'text'],
    ['visibility', 'Visibility', 'text'],
    ['description', 'Description', 'markdown'],
  ],
  flow: [
    ['name', 'Name', 'text'],
    ['fromId', 'From', 'ref'],
    ['toId', 'To', 'ref'],
    ['type', 'Type', 'text'],
    ['schedule', 'Schedule', 'text'],
    ['dataObjectIds', 'Data objects', 'list'],
    ['ownerTeamId', 'Owner team', 'ref'],
    ['status', 'Status', 'text'],
    ['volume', 'Volume', 'text'],
    ['plannedChange', 'Planned change', 'text'],
    ['description', 'Description', 'markdown'],
  ],
  dataObject: [
    ['name', 'Name', 'text'],
    ['sensitivity', 'Sensitivity', 'text'],
    ['sourceApplicationId', 'Source application', 'ref'],
    ['description', 'Description', 'markdown'],
  ],
  page: [
    ['title', 'Title', 'text'],
    ['parentId', 'Parent page', 'ref'],
    ['order', 'Order', 'text'],
    ['attachedTo', 'Attached to', 'text'],
    ['ownerTeamId', 'Owner team', 'ref'],
    ['tags', 'Tags', 'list'],
    ['visibility', 'Visibility', 'text'],
    ['body', 'Body', 'markdown'],
  ],
  team: [
    ['name', 'Name', 'text'],
    ['contact', 'Contact', 'text'],
    ['description', 'Description', 'markdown'],
  ],
  domain: [
    ['name', 'Name', 'text'],
    ['order', 'Order', 'text'],
    ['description', 'Description', 'markdown'],
  ],
};

const norm = (v: unknown): unknown => {
  if (v === undefined || v === '') return null;
  if (v && typeof v === 'object' && !Array.isArray(v)) {
    const o = v as { summary?: string; date?: string | null };
    if ('summary' in o) return o.date ? `${o.summary} (${o.date})` : (o.summary ?? null);
    const r = v as { kind?: string; id?: string };
    if (r.kind && r.id) return `${r.kind}:${r.id}`;
  }
  return v;
};
const same = (a: unknown, b: unknown) => JSON.stringify(norm(a)) === JSON.stringify(norm(b));

export function fieldDiff(kind: RecordKind, before: Rec | null, after: Rec | null): FieldDiff[] {
  const out: FieldDiff[] = [];
  for (const [field, label, k] of FIELDS[kind]) {
    const b = before ? norm(before[field]) : null;
    const a = after ? norm(after[field]) : null;
    if (!same(b, a)) out.push({ field, label, before: b, after: a, kind: k });
  }
  return out;
}

const pick = (kind: RecordKind, r: Rec): Rec => {
  const o: Rec = { id: r.id, version: r.version };
  for (const [f] of FIELDS[kind]) o[f] = r[f] ?? null;
  if (kind === 'application') o.sourceOf = r.sourceOf ?? [];
  return o;
};

/* ---------- state ---------- */

interface StoredChange {
  kind: RecordKind;
  targetId: string | null;
  operation: 'create' | 'update' | 'delete';
  baseVersion: number | null;
  before: Rec | null;
  after: Rec | null;
  conflicts: string[];
}
interface StoredProposal extends Omit<
  Proposal,
  'author' | 'decidedBy' | 'changes' | 'outdated' | 'ownerTeams' | 'requiresAdmin' | 'canDecide' | 'canEdit'
> {
  author: string;
  decidedBy: string | null;
  changes: StoredChange[];
}
interface StoredRevision extends Omit<Revision, 'author' | 'kind' | 'entityId'> {
  author: string | null;
}
interface Deleted {
  kind: TrashKind;
  entityId: string;
  title: string;
  deletedAt: string;
  deletedBy: string | null;
  version: number;
  snapshot: Rec;
}

const revisions = new Map<string, StoredRevision[]>();
const proposals: StoredProposal[] = [];
const trash: Deleted[] = [];
let settings: Settings = {
  instanceName: 'Noosphere',
  organisationName: 'Alizé',
  defaultLanguage: 'en',
  version: 1,
  updatedAt: null,
};
let seq = 100;

const key = (kind: RecordKind, id: string) => kind + ':' + id;

function listOf(db: GovDb, kind: RecordKind): Rec[] {
  if (kind === 'application') return db.applications as unknown as Rec[];
  if (kind === 'flow') return db.flows as unknown as Rec[];
  if (kind === 'dataObject') return db.dataObjects as unknown as Rec[];
  if (kind === 'team') return db.teams as unknown as Rec[];
  if (kind === 'domain') return db.domains as unknown as Rec[];
  if (kind === 'page') return db.pages as unknown as Rec[];
  return [];
}
const find = (db: GovDb, kind: RecordKind, id: string | null) =>
  id ? (listOf(db, kind).find((x) => x.id === id) ?? null) : null;
const titleOf = (r: Rec | null) => (r ? String(r.name ?? r.title ?? r.id) : '');

/** The team that owns a record, or null when only administrators do. */
export function ownerOf(db: GovDb, kind: RecordKind, rec: Rec | null): string | null {
  if (!rec) return null;
  const appTeam = (id: unknown) => db.applications.find((a) => a.id === id)?.teamId ?? null;
  if (kind === 'application') return (rec.teamId as string | null) ?? null;
  if (kind === 'flow') return (rec.ownerTeamId as string | null) || appTeam(rec.fromId);
  if (kind === 'dataObject') return appTeam(rec.sourceApplicationId);
  if (kind === 'team' || kind === 'domain') return null;
  if (rec.ownerTeamId) return rec.ownerTeamId as string;
  const at = rec.attachedTo as { kind: RecordKind; id: string } | null | undefined;
  return at && at.kind !== 'page' ? ownerOf(db, at.kind, find(db, at.kind, at.id)) : null;
}

const LINK = /\[\[(application|flow|dataObject|page|domain|team):([a-z0-9-]+)(?:\|[^\]\n]*)?\]\]/g;
const linksIn = (body: string) =>
  [
    ...body
      .replace(/```[\s\S]*?(```|$)/g, '')
      .replace(/`[^`\n]*`/g, '')
      .matchAll(LINK),
  ].map((m) => m[1] + ':' + m[2]);

/** The pages attached to a record, and the pages that link to it. */
export function pagesOf(db: GovDb, kind: RecordKind, id: string) {
  const ref = (p: Page) => ({ id: p.id, title: p.title });
  return {
    pages: db.pages.filter((p) => p.attachedTo?.kind === kind && p.attachedTo.id === id).map(ref),
    mentionedIn: db.pages.filter((p) => linksIn(p.body).includes(kind + ':' + id)).map(ref),
  };
}

export function permissions(db: GovDb, u: UserM2, kind: RecordKind, rec: Rec | null): Permissions {
  if (u.role === 'guest') return { publish: false, propose: false };
  if (u.role === 'admin') return { publish: true, propose: true };
  const owner = ownerOf(db, kind, rec);
  return { publish: !!owner && (u.teamIds ?? []).includes(owner), propose: true };
}

/** Members of a team owning a record, or anyone when it has no owner (the admins). */
function canPublish(db: GovDb, u: UserM2, kind: RecordKind, rec: Rec | null) {
  return permissions(db, u, kind, rec).publish;
}

function person(db: GovDb, username: string | null): PersonRef | null {
  if (!username) return null;
  const u = db.users.find((x) => x.username === username);
  return { username, displayName: u?.displayName ?? username };
}

/** Records a published version. */
function addRevision(
  ctx: GovCtx,
  kind: RecordKind,
  rec: Rec,
  author: string | null,
  summary: string | null,
  operation: HistoryEntry['operation'],
  proposalId: string | null = null,
) {
  const list = history(ctx, kind, String(rec.id));
  list.push({
    version: Number(rec.version),
    operation,
    author,
    authorKind: 'user',
    createdAt: ctx.now(),
    summary,
    proposalId,
    snapshot: pick(kind, rec),
  });
}

/** The revisions of a record, oldest first; an entity without any gets its current state as the first one. */
export function history(ctx: GovCtx, kind: RecordKind, id: string): StoredRevision[] {
  const k = key(kind, id);
  let list = revisions.get(k);
  if (!list) {
    list = [];
    revisions.set(k, list);
    const rec = find(ctx.db, kind, id);
    if (rec)
      list.push({
        version: Number(rec.version),
        operation: 'create',
        author: null,
        authorKind: 'user',
        createdAt: typeof rec.createdAt === 'string' ? rec.createdAt : '2026-09-01T08:00:00Z',
        summary: 'Initial import',
        proposalId: null,
        snapshot: pick(kind, rec),
      });
  }
  return list;
}

/* ---------- seed ---------- */

export function seedGovernance(ctx: GovCtx) {
  const { db } = ctx;
  const billing = db.applications.find((a) => a.id === 'billing')!;
  const v3 = pick('application', billing as unknown as Rec);
  const v2 = { ...v3, version: 2, tech: '.NET, SQL Server' };
  const v1 = {
    ...v2,
    version: 1,
    description: 'Issues invoices for every policy and keeps the customer account balance.',
  };
  billing.version = 3;
  billing.updatedAt = '2026-10-21T09:12:00Z';
  revisions.set(key('application', 'billing'), [
    {
      version: 1,
      operation: 'create',
      author: null,
      authorKind: 'user',
      createdAt: '2026-09-01T08:00:00Z',
      summary: 'Initial import',
      proposalId: null,
      snapshot: v1,
    },
    {
      version: 2,
      operation: 'update',
      author: 'david',
      authorKind: 'user',
      createdAt: '2026-09-20T14:30:00Z',
      summary: 'Billing modes and mandates listed',
      proposalId: 'p-4',
      snapshot: v2,
    },
    {
      version: 3,
      operation: 'update',
      author: 'bob',
      authorKind: 'user',
      createdAt: '2026-10-21T09:12:00Z',
      summary: 'Moved to PostgreSQL',
      proposalId: null,
      snapshot: { ...v3, version: 3 },
    },
  ]);

  // A flow owned by Team Finance, changed by Bob after David's proposal was written.
  const paid = db.flows.find((f) => f.id === 'bus-billing')!;
  const paidV1 = pick('flow', paid as unknown as Rec);
  paid.version = 2;
  paid.schedule = 'Within 5 minutes';
  paid.updatedAt = '2026-10-29T09:12:00Z';
  revisions.set(key('flow', 'bus-billing'), [
    {
      version: 1,
      operation: 'create',
      author: null,
      authorKind: 'user',
      createdAt: '2026-09-01T08:00:00Z',
      summary: 'Initial import',
      proposalId: null,
      snapshot: paidV1,
    },
    {
      version: 2,
      operation: 'update',
      author: 'bob',
      authorKind: 'user',
      createdAt: '2026-10-29T09:12:00Z',
      summary: 'Schedule measured',
      proposalId: null,
      snapshot: pick('flow', paid as unknown as Rec),
    },
  ]);

  const billingAfter = {
    ...v3,
    version: 3,
    tech: '.NET 10, PostgreSQL 16',
    description:
      'Issues invoices for every policy and keeps the customer account balance.\n\n- Monthly, quarterly and yearly billing\n- Direct debit mandates and payment plans\n- Sends **invoice emails** and print files\n\nPayment plans split a yearly premium into up to 12 instalments.',
  };
  const base = {
    authorKind: 'user' as const,
    onBehalfOf: null,
    decidedBy: null,
    decidedAt: null,
    refusalReason: null,
    version: 1,
  };
  proposals.push(
    {
      ...base,
      id: 'p-1',
      title: 'Billing · payment plans documented',
      comment:
        'Billing 4.2 added payment plans and the quarterly mode. The servers moved to .NET 10 and PostgreSQL 16 at the same time.',
      status: 'submitted',
      author: 'david',
      createdAt: '2026-10-30T07:40:00Z',
      submittedAt: '2026-10-30T07:42:00Z',
      changes: [
        {
          kind: 'application',
          targetId: 'billing',
          operation: 'update',
          baseVersion: 3,
          before: { ...v3, version: 3 },
          after: billingAfter,
          conflicts: [],
        },
      ],
    },
    {
      ...base,
      id: 'p-2',
      title: 'PaymentReceived · volume and schedule',
      comment: 'Measured on the bus dashboard last week.',
      status: 'submitted',
      author: 'david',
      createdAt: '2026-10-28T16:05:00Z',
      submittedAt: '2026-10-28T16:05:00Z',
      changes: [
        {
          kind: 'flow',
          targetId: 'bus-billing',
          operation: 'update',
          baseVersion: 1,
          before: paidV1,
          after: { ...paidV1, schedule: 'Real time', volume: 'About 2,000 events a day' },
          conflicts: [],
        },
      ],
    },
    {
      ...base,
      id: 'p-3',
      title: 'Accounting · technology',
      comment: null,
      status: 'refused',
      author: 'david',
      createdAt: '2026-10-12T10:00:00Z',
      submittedAt: '2026-10-12T10:00:00Z',
      decidedBy: 'bob',
      decidedAt: '2026-10-13T08:20:00Z',
      refusalReason: 'Accounting still runs on SAP; the migration is planned for next year.',
      changes: [
        {
          kind: 'application',
          targetId: 'acct',
          operation: 'update',
          baseVersion: 1,
          before: pick('application', db.applications.find((a) => a.id === 'acct') as unknown as Rec),
          after: {
            ...pick('application', db.applications.find((a) => a.id === 'acct') as unknown as Rec),
            tech: 'Oracle Fusion',
          },
          conflicts: [],
        },
      ],
    },
    {
      ...base,
      id: 'p-4',
      title: 'Billing modes and mandates listed',
      comment: 'From the Billing 4.1 release notes.',
      status: 'accepted',
      author: 'david',
      createdAt: '2026-09-19T11:00:00Z',
      submittedAt: '2026-09-19T11:00:00Z',
      decidedBy: 'bob',
      decidedAt: '2026-09-20T14:30:00Z',
      changes: [
        {
          kind: 'application',
          targetId: 'billing',
          operation: 'update',
          baseVersion: 1,
          before: v1,
          after: { ...v2, version: 1 },
          conflicts: [],
        },
      ],
    },
    {
      ...base,
      id: 'p-5',
      title: 'CRM · description',
      comment: 'Bob asked for the customer master to be explained.',
      status: 'submitted',
      author: 'bob',
      createdAt: '2026-10-29T15:00:00Z',
      submittedAt: '2026-10-29T15:00:00Z',
      changes: [
        {
          kind: 'application',
          targetId: 'crm',
          operation: 'update',
          baseVersion: 1,
          before: pick('application', db.applications.find((a) => a.id === 'crm') as unknown as Rec),
          after: {
            ...pick('application', db.applications.find((a) => a.id === 'crm') as unknown as Rec),
            description:
              'Customer master: every person and company insured by Alizé, with their contacts and consents.',
          },
          conflicts: [],
        },
      ],
    },
  );
}

/* ---------- proposals ---------- */

function ownerTeams(ctx: GovCtx, p: StoredProposal): OwnerApproval[] {
  const ids = new Set<string>();
  for (const c of p.changes) {
    const o = ownerOf(ctx.db, c.kind, find(ctx.db, c.kind, c.targetId) ?? c.after);
    if (o) ids.add(o);
  }
  return [...ids].map((id) => ({
    id,
    name: ctx.db.teams.find((t) => t.id === id)?.name ?? id,
    approvedBy: p.status === 'accepted' ? person(ctx.db, p.decidedBy) : null,
    approvedAt: p.status === 'accepted' ? p.decidedAt : null,
  }));
}

function canDecide(ctx: GovCtx, u: UserM2, p: StoredProposal) {
  if (p.status !== 'submitted' || p.author === u.username) return false;
  if (u.role === 'admin') return true;
  if (u.role !== 'member') return false;
  const owners = ownerTeams(ctx, p);
  return owners.length > 0 && owners.every((o) => (u.teamIds ?? []).includes(o.id));
}

function view(ctx: GovCtx, u: UserM2, p: StoredProposal): Proposal {
  const changes: ProposalChange[] = p.changes.map((c) => {
    const cur = find(ctx.db, c.kind, c.targetId);
    const currentVersion = cur ? Number(cur.version) : null;
    const outdated = c.operation !== 'create' && currentVersion !== c.baseVersion;
    return {
      kind: c.kind,
      targetId: c.targetId,
      targetTitle: titleOf(cur ?? c.after ?? c.before),
      operation: c.operation,
      baseVersion: c.baseVersion,
      currentVersion,
      outdated: p.status === 'submitted' || p.status === 'draft' ? outdated : false,
      before: c.before,
      after: c.after,
      conflicts: c.conflicts,
      diff: fieldDiff(c.kind, c.operation === 'create' ? null : c.before, c.operation === 'delete' ? null : c.after),
    };
  });
  const owners = ownerTeams(ctx, p);
  return {
    ...p,
    author: person(ctx.db, p.author) ?? { username: p.author, displayName: p.author },
    decidedBy: person(ctx.db, p.decidedBy),
    outdated: changes.some((c) => c.outdated),
    ownerTeams: owners,
    requiresAdmin: owners.length === 0,
    canDecide: canDecide(ctx, u, p),
    canEdit: p.author === u.username && (p.status === 'draft' || p.status === 'submitted'),
    changes,
  };
}

function buildChanges(ctx: GovCtx, body: ProposalInput): StoredChange[] | string {
  const out: StoredChange[] = [];
  for (const c of body.changes ?? []) {
    const kind = c.kind as RecordKind;
    const operation = c.operation ?? 'update';
    const cur = find(ctx.db, kind, c.targetId);
    if (operation !== 'create' && !cur) return `Unknown ${kind} ${String(c.targetId)}.`;
    out.push({
      kind,
      targetId: c.targetId,
      operation,
      baseVersion: cur ? Number(cur.version) : null,
      before: cur ? pick(kind, cur) : null,
      after: operation === 'delete' ? null : { ...(cur ? pick(kind, cur) : null), ...c.after },
      conflicts: [],
    });
  }
  return out.length ? out : 'A proposal holds at least one change.';
}

function moveToTrash(ctx: GovCtx, kind: RecordKind, id: string, by: string) {
  const list = listOf(ctx.db, kind);
  const i = list.findIndex((x) => x.id === id);
  if (i < 0) return;
  const rec = list[i];
  trash.push({
    kind,
    entityId: id,
    title: titleOf(rec),
    deletedAt: ctx.now(),
    deletedBy: by,
    version: Number(rec.version),
    snapshot: { ...rec },
  });
  list.splice(i, 1);
  if (kind === 'application')
    ctx.db.flows = ctx.db.flows.filter((f) => f.fromId !== id && f.toId !== id) as GovDb['flows'];
}

function applyChange(ctx: GovCtx, c: StoredChange, by: string, summary: string, proposalId: string | null) {
  const list = listOf(ctx.db, c.kind);
  if (c.operation === 'delete') return moveToTrash(ctx, c.kind, c.targetId!, by);
  if (c.operation === 'create') {
    const rec = ctx.finish(c.kind, {
      ...c.after,
      restricted: false,
      version: 1,
      createdAt: ctx.now(),
      updatedAt: ctx.now(),
    });
    list.push(rec);
    return addRevision(ctx, c.kind, rec, by, summary, 'create', proposalId);
  }
  const i = list.findIndex((x) => x.id === c.targetId);
  const cur = list[i];
  const next = ctx.finish(c.kind, {
    ...cur,
    ...c.after,
    id: cur.id,
    version: Number(cur.version) + 1,
    updatedAt: ctx.now(),
  });
  list[i] = next;
  addRevision(ctx, c.kind, next, by, summary, 'update', proposalId);
}

/** Keeps the author's changed fields on top of the latest version and flags the fields changed on both sides. */
function rebase(ctx: GovCtx, c: StoredChange): StoredChange {
  if (c.operation !== 'update' || !c.before || !c.after) return c;
  const cur = find(ctx.db, c.kind, c.targetId);
  if (!cur) return c;
  const latest = pick(c.kind, cur);
  const after: Rec = { ...latest };
  const conflicts: string[] = [];
  for (const [f] of FIELDS[c.kind]) {
    if (same(c.after[f], c.before[f])) continue;
    after[f] = c.after[f];
    if (!same(latest[f], c.before[f]) && !same(latest[f], c.after[f])) conflicts.push(f);
  }
  return { ...c, baseVersion: Number(cur.version), before: latest, after, conflicts };
}

/* ---------- requests ---------- */

const KINDS: Record<string, RecordKind> = {
  applications: 'application',
  flows: 'flow',
  'data-objects': 'dataObject',
  pages: 'page',
  teams: 'team',
  domains: 'domain',
};

/** The routes reachable without signing in. */
export function handlePublic(ctx: GovCtx, method: string, path: string): Response | null {
  if (path === '/settings' && method === 'GET') return ctx.json(200, settings);
  return null;
}

export function recordExtras(ctx: GovCtx, u: UserM2, kind: RecordKind, rec: Rec) {
  return {
    owningTeamId: ownerOf(ctx.db, kind, rec),
    permissions: permissions(ctx.db, u, kind, rec),
    ...pagesOf(ctx.db, kind, String(rec.id)),
  };
}

/** Called by the record PUT and DELETE routes: refuses non-owners, as the real API does. */
export function guardWrite(ctx: GovCtx, u: UserM2, kind: RecordKind, rec: Rec): Response | null {
  return canPublish(ctx.db, u, kind, rec)
    ? null
    : ctx.problem(403, 'Forbidden', 'Only the owning team publishes this record; send a proposal instead.');
}
export function afterSave(ctx: GovCtx, u: UserM2, kind: RecordKind, rec: Rec) {
  history(ctx, kind, String(rec.id));
  addRevision(ctx, kind, rec, u.username, null, 'update');
}
export function deleteRecord(ctx: GovCtx, u: UserM2, kind: RecordKind, id: string) {
  history(ctx, kind, id);
  moveToTrash(ctx, kind, id, u.username);
}

export function handleGovernance(
  ctx: GovCtx,
  u: UserM2,
  method: string,
  url: URL,
  path: string,
  body: () => Rec,
): Response | null {
  const { db, json, problem } = ctx;
  const isAdmin = u.role === 'admin';

  if (path === '/settings' && method === 'PUT') {
    if (!isAdmin) return problem(403, 'Administrators only.');
    const b = body() as unknown as Settings;
    if (b.version != null && b.version !== settings.version) return problem(409, 'Conflict');
    const errors: Record<string, string[]> = {};
    if (!b.instanceName?.trim()) errors.instanceName = ['The instance name is required.'];
    else if (b.instanceName.length > 60) errors.instanceName = ['Use at most 60 characters.'];
    if ((b.organisationName ?? '').length > 80) errors.organisationName = ['Use at most 80 characters.'];
    if (Object.keys(errors).length) return problem(400, 'Validation failed.', undefined, errors);
    settings = {
      instanceName: b.instanceName.trim(),
      organisationName: (b.organisationName ?? '').trim(),
      defaultLanguage: b.defaultLanguage === 'fr' ? 'fr' : 'en',
      version: settings.version + 1,
      updatedAt: ctx.now(),
    };
    return json(200, settings);
  }

  if (path === '/me/inbox') {
    const toValidate = proposals.filter((p) => canDecide(ctx, u, p)).length;
    const mineOpen = proposals.filter(
      (p) => p.author === u.username && (p.status === 'submitted' || p.status === 'draft'),
    ).length;
    return json(200, { toValidate, mineOpen });
  }

  /* teams */
  let m = /^\/teams\/([^/]+)(\/members)?$/.exec(path);
  if (m) {
    const id = decodeURIComponent(m[1]);
    const team = db.teams.find((x) => x.id === id);
    if (!team) return problem(404, 'Team not found.');
    if (!m[2] && method === 'GET')
      return json(200, {
        ...team,
        owningTeamId: null,
        permissions: { publish: isAdmin, propose: false },
        ...pagesOf(db, 'team', id),
      });
    if (m[2] && method === 'GET')
      return json(
        200,
        db.users
          .filter((x) => (x.teamIds ?? []).includes(id))
          .map((x) => ({ username: x.username, displayName: x.displayName, role: x.role })),
      );
    if (m[2] && method === 'PUT') {
      if (!isAdmin) return problem(403, 'Administrators only.');
      const names = (body().usernames as string[] | undefined) ?? [];
      const unknown = names.filter((n) => !db.users.some((x) => x.username === n));
      if (unknown.length) return problem(400, 'Validation failed.', undefined, { usernames: ['Unknown users.'] });
      for (const x of db.users) {
        const has = (x.teamIds ?? []).includes(id);
        const want = names.includes(x.username);
        if (has && !want) x.teamIds = (x.teamIds ?? []).filter((t) => t !== id);
        if (!has && want) x.teamIds = [...(x.teamIds ?? []), id];
      }
      return json(
        200,
        db.users
          .filter((x) => names.includes(x.username))
          .map((x) => ({ username: x.username, displayName: x.displayName, role: x.role })),
      );
    }
    return null;
  }

  /* proposals */
  if (path === '/proposals' && method === 'GET') {
    const sp = url.searchParams;
    let list = proposals.filter((p) => p.status !== 'draft' || p.author === u.username);
    if (sp.get('status')) {
      const st = sp.get('status')!.split(',');
      list = list.filter((p) => st.includes(p.status));
    }
    if (sp.get('mine') === 'true') list = list.filter((p) => p.author === u.username);
    if (sp.get('toValidate') === 'true') list = list.filter((p) => canDecide(ctx, u, p));
    list = [...list].sort((a, b) => (b.submittedAt ?? b.createdAt).localeCompare(a.submittedAt ?? a.createdAt));
    return json(
      200,
      list.map((p) => view(ctx, u, p)),
    );
  }
  if (path === '/proposals' && method === 'POST') {
    if (u.role === 'guest') return problem(403, 'Guests cannot propose changes.');
    const b = body() as unknown as ProposalInput;
    if (!b.title?.trim()) return problem(400, 'Validation failed.', undefined, { title: ['The title is required.'] });
    const changes = buildChanges(ctx, b);
    if (typeof changes === 'string') return problem(400, changes);
    const p: StoredProposal = {
      id: 'p-' + ++seq,
      title: b.title.trim(),
      comment: b.comment?.trim() || null,
      status: b.submit ? 'submitted' : 'draft',
      authorKind: 'user',
      author: u.username,
      onBehalfOf: null,
      createdAt: ctx.now(),
      submittedAt: b.submit ? ctx.now() : null,
      decidedBy: null,
      decidedAt: null,
      refusalReason: null,
      version: 1,
      changes,
    };
    proposals.push(p);
    return json(201, view(ctx, u, p));
  }
  m = /^\/proposals\/([^/]+)(?:\/(submit|withdraw|accept|refuse|rebase))?$/.exec(path);
  if (m) {
    const p = proposals.find((x) => x.id === decodeURIComponent(m![1]));
    if (!p || (p.status === 'draft' && p.author !== u.username)) return problem(404, 'Proposal not found.');
    const action = m[2];
    const mine = p.author === u.username;
    const open = p.status === 'draft' || p.status === 'submitted';
    if (!action && method === 'GET') return json(200, view(ctx, u, p));
    if (!action && method === 'PUT') {
      if (!mine || !open) return problem(403, 'Only its author changes an open proposal.');
      const b = body() as unknown as ProposalInput;
      if (b.version != null && b.version !== p.version) return problem(409, 'Conflict');
      const changes = buildChanges(ctx, b);
      if (typeof changes === 'string') return problem(400, changes);
      Object.assign(p, {
        title: b.title?.trim() || p.title,
        comment: b.comment?.trim() || null,
        changes,
        version: p.version + 1,
      });
      return json(200, view(ctx, u, p));
    }
    if (method !== 'POST') return null;
    if (action === 'submit') {
      if (!mine || p.status !== 'draft') return problem(400, 'Only a draft can be submitted.');
      Object.assign(p, { status: 'submitted', submittedAt: ctx.now(), version: p.version + 1 });
    } else if (action === 'withdraw') {
      if (!mine || !open) return problem(400, 'Only an open proposal can be withdrawn.');
      Object.assign(p, { status: 'withdrawn', version: p.version + 1 });
    } else if (action === 'rebase') {
      if (!mine || !open) return problem(400, 'Only an open proposal can be updated.');
      Object.assign(p, { changes: p.changes.map((c) => rebase(ctx, c)), version: p.version + 1 });
    } else if (action === 'refuse') {
      if (!canDecide(ctx, u, p)) return problem(403, 'You cannot decide on this proposal.');
      const raw = body().reason;
      const reason = typeof raw === 'string' ? raw.trim() : '';
      if (reason.length < 3 || reason.length > 500)
        return problem(400, 'Validation failed.', undefined, { reason: ['Give a reason of 3 to 500 characters.'] });
      Object.assign(p, {
        status: 'refused',
        refusalReason: reason,
        decidedBy: u.username,
        decidedAt: ctx.now(),
        version: p.version + 1,
      });
    } else if (action === 'accept') {
      if (!canDecide(ctx, u, p)) return problem(403, 'You cannot decide on this proposal.');
      const stale = p.changes.some(
        (c) => c.operation !== 'create' && Number(find(db, c.kind, c.targetId)?.version) !== c.baseVersion,
      );
      if (stale)
        return json(409, {
          type: 'about:blank',
          title: 'Conflict',
          status: 409,
          detail: 'A record changed since this proposal was written.',
          outdated: true,
        });
      for (const c of p.changes) applyChange(ctx, c, p.author, p.title, p.id);
      Object.assign(p, { status: 'accepted', decidedBy: u.username, decidedAt: ctx.now(), version: p.version + 1 });
    }
    return json(200, view(ctx, u, p));
  }

  /* history */
  m =
    /^\/(applications|flows|data-objects|pages|teams|domains)\/([^/]+)\/history(?:\/(compare|\d+)(\/restore)?)?$/.exec(
      path,
    );
  if (m) {
    const kind = KINDS[m[1]];
    const id = decodeURIComponent(m[2]);
    const list = history(ctx, kind, id);
    if (!list.length) return problem(404, 'Not found.');
    if (!m[3])
      return json(
        200,
        [...list].reverse().map(({ snapshot: _s, ...h }) => ({ ...h, author: person(db, h.author) })),
      );
    if (m[3] === 'compare') {
      const from = list.find((r) => r.version === Number(url.searchParams.get('from')));
      const to = list.find((r) => r.version === Number(url.searchParams.get('to')));
      if (!from || !to) return problem(404, 'Version not found.');
      return json(200, { from: from.version, to: to.version, diff: fieldDiff(kind, from.snapshot, to.snapshot) });
    }
    const rev = list.find((r) => r.version === Number(m![3]));
    if (!rev) return problem(404, 'Version not found.');
    if (!m[4]) return json(200, { ...rev, kind, entityId: id, author: person(db, rev.author) });
    if (method !== 'POST') return null;
    if (u.role === 'guest') return problem(403, 'Read-only account.');
    const cur = find(db, kind, id);
    if (!cur) return problem(404, 'Deleted records are restored from the trash.');
    const { id: _i, version: _v, ...fields } = rev.snapshot;
    if (canPublish(db, u, kind, cur)) {
      applyChange(
        ctx,
        {
          kind,
          targetId: id,
          operation: 'update',
          baseVersion: Number(cur.version),
          before: null,
          after: fields,
          conflicts: [],
        },
        u.username,
        `Restored v${rev.version}`,
        null,
      );
      const last = list[list.length - 1];
      last.operation = 'restore';
      return json(200, { outcome: 'published', version: last.version, proposalId: null });
    }
    const p: StoredProposal = {
      id: 'p-' + ++seq,
      title: `${titleOf(cur)} · restore v${rev.version}`,
      comment: null,
      status: 'submitted',
      authorKind: 'user',
      author: u.username,
      onBehalfOf: null,
      createdAt: ctx.now(),
      submittedAt: ctx.now(),
      decidedBy: null,
      decidedAt: null,
      refusalReason: null,
      version: 1,
      changes: [
        {
          kind,
          targetId: id,
          operation: 'update',
          baseVersion: Number(cur.version),
          before: pick(kind, cur),
          after: { ...pick(kind, cur), ...fields },
          conflicts: [],
        },
      ],
    };
    proposals.push(p);
    return json(200, { outcome: 'proposed', version: null, proposalId: p.id });
  }

  /* trash */
  if (path === '/trash') {
    if (!isAdmin) return problem(403, 'Administrators only.');
    return json(
      200,
      [...trash]
        .reverse()
        .map(({ snapshot: _s, deletedBy, ...x }): TrashItem => ({ ...x, deletedBy: person(db, deletedBy) })),
    );
  }
  m = /^\/trash\/([^/]+)\/([^/]+)\/restore$/.exec(path);
  if (m && method === 'POST') {
    if (!isAdmin) return problem(403, 'Administrators only.');
    const i = trash.findIndex((x) => x.kind === m![1] && x.entityId === decodeURIComponent(m![2]));
    if (i < 0) return problem(404, 'Not in the trash.');
    const d = trash[i];
    const kind = d.kind as RecordKind;
    const rec = { ...d.snapshot, version: d.version + 1, updatedAt: ctx.now() };
    listOf(db, kind).push(rec);
    trash.splice(i, 1);
    addRevision(ctx, kind, rec, u.username, 'Restored from the trash', 'restore');
    return json(204);
  }
  return null;
}

/* ---------- used by the agents and demo mocks ---------- */

export type MockProposal = StoredProposal;

export const demoGov = {
  proposals,
  view,
  find: (ctx: GovCtx, kind: RecordKind, id: string) => find(ctx.db, kind, id),
  title: titleOf,
  nextId: () => 'p-' + ++seq,
  /** Proposal changes for an update of one record, based on its current version. */
  updateChange: (ctx: GovCtx, kind: RecordKind, id: string, patch: Rec): StoredChange | null => {
    const cur = find(ctx.db, kind, id);
    if (!cur) return null;
    const before = pick(kind, cur);
    return {
      kind,
      targetId: id,
      operation: 'update',
      baseVersion: Number(cur.version),
      before,
      after: { ...before, ...patch },
      conflicts: [],
    };
  },
  /** Publishes a change as `by`, creating a revision: what an owner's save does. */
  publish: (ctx: GovCtx, kind: RecordKind, id: string, patch: Rec, by: string, summary: string) => {
    const c = demoGov.updateChange(ctx, kind, id, patch);
    if (c) applyChange(ctx, c, by, summary, null);
  },
  ownerOf: (ctx: GovCtx, kind: RecordKind, id: string) => ownerOf(ctx.db, kind, find(ctx.db, kind, id)),
  decide: (ctx: GovCtx, p: StoredProposal, by: string, accept: boolean, reason: string) => {
    if (accept) for (const c of p.changes) applyChange(ctx, c, p.author, p.title, p.id);
    Object.assign(p, {
      status: accept ? 'accepted' : 'refused',
      refusalReason: accept ? null : reason,
      decidedBy: by,
      decidedAt: ctx.now(),
      version: p.version + 1,
    });
  },
};
