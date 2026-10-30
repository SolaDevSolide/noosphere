// Development-only mock of the milestone 4 API: agent settings, connections, activity, the consent page and the demo
// toolbox. Answers like the real API for the screens; it does not run an MCP server.
import type {
  AgentActivity,
  AgentActivityKind,
  AgentConnection,
  AgentSettings,
  Capability,
  ConsentRequest,
  DemoNotice,
  DemoPersona,
  Me,
  RecordKind,
  UserM2,
} from '../api/types';
import { demoGov, type GovCtx, type MockProposal } from './governance';

type Rec = Record<string, unknown>;
const ago = (min: number) => new Date(Date.now() - min * 60000).toISOString();
const ref = (u: UserM2) => ({ username: u.username, displayName: u.displayName });
const nextHour = () => {
  const d = new Date();
  d.setMinutes(60, 0, 0);
  return d.toISOString();
};

/** Demo mode in the mock: on unless sessionStorage `noos-mock-demo` is "0". */
export function mockDemoOn(): boolean {
  try {
    return sessionStorage.getItem('noos-mock-demo') !== '0';
  } catch {
    return true;
  }
}

interface StoredConnection extends Omit<
  AgentConnection,
  'person' | 'state' | 'callsLastHour' | 'openDrafts' | 'lastActivity'
> {
  username: string;
  pending: boolean;
  calls: number;
}
interface StoredActivity extends Omit<AgentActivity, 'agentName' | 'person'> {
  username: string;
}

let settings: AgentSettings = {
  enabled: true,
  ceiling: 'public',
  quotaPerHour: 600,
  whoConnects: 'anyMember',
  dynamicRegistration: true,
  anonymousLlms: false,
  mcpUrl: (typeof location === 'undefined' ? 'http://localhost:5173' : location.origin) + '/mcp',
  version: 1,
  updatedAt: null,
};
let connections: StoredConnection[] = [];
let activity: StoredActivity[] = [];
let actSeq = 0;
let connSeq = 0;
const consents = new Map<string, { clientName: string; decided: boolean }>();

const conn = (c: Partial<StoredConnection> & Pick<StoredConnection, 'clientName' | 'username'>): StoredConnection => ({
  id: 'c' + ++connSeq + '-0000-4000-8000-000000000000',
  clientId: c.clientUri ?? 'nsclient_' + connSeq,
  clientUri: null,
  scopes: ['docs:read', 'docs:draft'],
  createdAt: ago(60 * 24 * 7),
  lastUsedAt: null,
  revokedAt: null,
  pausedUntil: null,
  approvedAt: ago(60 * 24 * 7),
  demo: false,
  pending: false,
  calls: 0,
  ...c,
});

function log(
  c: StoredConnection,
  tool: string,
  kind: AgentActivityKind,
  summary: string,
  rec?: [RecordKind, string, string],
) {
  activity.unshift({
    id: ++actSeq,
    at: new Date().toISOString(),
    connectionId: c.id,
    username: c.username,
    tool,
    kind,
    summary,
    recordKind: rec?.[0] ?? null,
    recordId: rec?.[1] ?? null,
    recordTitle: rec?.[2] ?? null,
  });
  c.lastUsedAt = new Date().toISOString();
  c.calls++;
}

function seed(ctx: GovCtx) {
  connections = [];
  activity = [];
  const claude = conn({
    clientName: 'Claude Code',
    clientUri: 'https://claude.ai',
    username: 'bob',
    createdAt: ago(60 * 24 * 9),
    approvedAt: ago(60 * 24 * 9),
  });
  const chat = conn({ clientName: 'Claude', clientUri: 'https://claude.ai/', username: 'bob', scopes: ['docs:read'] });
  const copilot = conn({ clientName: 'GitHub Copilot', clientUri: 'https://github.com', username: 'david' });
  const docs = conn({
    clientName: 'Docs assistant',
    clientUri: 'https://docs.alize.example',
    username: 'alice',
    scopes: ['docs:read'],
    pausedUntil: nextHour(),
    calls: 600,
  });
  const cursor = conn({ clientName: 'Cursor', username: 'david', pending: true, approvedAt: null, createdAt: ago(14) });
  connections.push(cursor, claude, chat, copilot, docs);
  const at = (
    c: StoredConnection,
    min: number,
    tool: string,
    kind: AgentActivityKind,
    summary: string,
    rec?: [RecordKind, string, string],
  ) => {
    log(c, tool, kind, summary, rec);
    activity[0].at = ago(min);
    c.lastUsedAt = activity[0].at;
  };
  at(
    copilot,
    60 * 26,
    'draft_change',
    'draft',
    'Drafted a change on Policies; the description was refused as a secret.',
    ['application', 'policy', 'Policies'],
  );
  at(
    claude,
    52,
    'get_record',
    'refused',
    'Refused (ceiling): the application is restricted and above the agent ceiling.',
    ['application', 'fraud', 'Fraud scoring'],
  );
  at(docs, 48, 'search', 'quota', 'Quota reached: 600 calls this hour. Paused until the next hour.');
  at(copilot, 30, 'get_record', 'read', 'Read CRM (application) and its 4 flows.', ['application', 'crm', 'CRM']);
  at(claude, 12, 'search', 'read', 'Searched "billing nightly import": 6 result(s).');
  at(claude, 10, 'draft_change', 'draft', 'Changed its draft on Billing: Technology, Description.', [
    'application',
    'billing',
    'Billing',
  ]);
  at(claude, 8, 'submit_draft', 'proposal', 'Submitted "Customers · description and volume".', [
    'flow',
    'crm-to-billing-customers',
    'Customers',
  ]);
  at(claude, 2, 'get_record', 'refused', 'Refused (protected notes): protected notes are above the agent ceiling.', [
    'application',
    'billing',
    'Billing',
  ]);
  claude.calls = 312;
  copilot.calls = 41;

  const bob = ctx.db.users.find((u) => u.username === 'bob')!;
  const draft = demoGov.updateChange(ctx, 'application', 'billing', {
    tech: '.NET 10, PostgreSQL 16',
    description:
      'Issues invoices for every policy and keeps the customer account balance. Runs in three environments: dev, staging and production.',
  });
  const flow = demoGov.updateChange(ctx, 'flow', 'crm-to-billing-customers', {
    volume: '18,500 lines',
    description:
      'Nightly export of new and changed customers from CRM. Billing uses it to address invoices and direct-debit mandates.',
  });
  const base = {
    authorKind: 'agent' as const,
    author: 'bob',
    onBehalfOf: ref(bob),
    decidedBy: null,
    decidedAt: null,
    refusalReason: null,
    version: 1,
  };
  if (draft)
    demoGov.proposals.push({
      ...base,
      id: demoGov.nextId(),
      title: 'Billing · environments',
      comment: 'The description did not say where Billing runs.',
      status: 'draft',
      createdAt: ago(10),
      submittedAt: null,
      changes: [draft],
      agentName: 'Claude Code',
      agentConnectionId: claude.id,
    } as MockProposal);
  if (flow)
    demoGov.proposals.push({
      ...base,
      id: demoGov.nextId(),
      title: 'Customers · description and volume',
      comment: 'The nightly file grew after the October campaign: 18,240 lines on 29 Oct.',
      status: 'submitted',
      createdAt: ago(9),
      submittedAt: ago(8),
      changes: [flow],
      agentName: 'Claude Code',
      agentConnectionId: claude.id,
    } as MockProposal);
}

function state(c: StoredConnection): AgentConnection['state'] {
  if (c.revokedAt) return 'revoked';
  if (c.pending) return 'pending';
  if (c.pausedUntil && c.pausedUntil > new Date().toISOString()) return 'paused';
  return 'active';
}

function dto(ctx: GovCtx, c: StoredConnection): AgentConnection {
  const u = ctx.db.users.find((x) => x.username === c.username)!;
  const { username: _u, pending: _p, calls, ...rest } = c;
  const last = activity.find((a) => a.connectionId === c.id);
  return {
    ...rest,
    person: ref(u),
    state: state(c),
    callsLastHour: calls,
    openDrafts: demoGov.proposals.filter((p) => p.agentConnectionId === c.id && p.status === 'draft').length,
    lastActivity: last ? activityDto(ctx, last) : null,
  };
}

function activityDto(ctx: GovCtx, a: StoredActivity): AgentActivity {
  const c = connections.find((x) => x.id === a.connectionId);
  const u = ctx.db.users.find((x) => x.username === a.username)!;
  const { username: _u, ...rest } = a;
  return { ...rest, agentName: c?.clientName ?? 'Agent', person: ref(u) };
}

function capabilities(u: UserM2, scopes: string[]): Capability[] {
  const t = (en: string, fr: string) => ({ en, fr });
  const draft = scopes.includes('docs:draft') && u.role !== 'guest';
  const notes = settings.ceiling !== 'public';
  const restricted = settings.ceiling === 'asUser' && scopes.includes('docs:read:restricted');
  return [
    {
      key: 'read',
      allowed: true,
      text:
        u.role === 'guest'
          ? t('Reads the records shared with the guest.', 'Lit les fiches partagées avec l’invité.')
          : t('Reads every record visible to everyone.', 'Lit toutes les fiches visibles par tous.'),
    },
    {
      key: 'draft',
      allowed: draft,
      text: draft
        ? t(
            'Writes drafts in its own workspace and submits them as proposals.',
            'Écrit des brouillons dans son espace et les soumet comme propositions.',
          )
        : t(
            `Cannot write drafts: ${u.displayName} allowed reading only.`,
            `N’écrit pas de brouillon : ${u.displayName} n’a autorisé que la lecture.`,
          ),
    },
    {
      key: 'publish',
      allowed: false,
      text: t(
        'Cannot publish anything: the owning teams validate its proposals.',
        'Ne publie rien : les équipes propriétaires valident ses propositions.',
      ),
    },
    {
      key: 'restricted',
      allowed: restricted,
      text: restricted
        ? t(
            `Opens the restricted records ${u.displayName} can see.`,
            `Ouvre les fiches restreintes que ${u.displayName} voit.`,
          )
        : t(
            `Cannot open the 1 restricted record(s) ${u.displayName} can see.`,
            `N’ouvre pas la ou les 1 fiche(s) restreinte(s) que ${u.displayName} voit.`,
          ),
    },
    {
      key: 'protectedNotes',
      allowed: notes,
      text: notes
        ? t('Reads the protected notes the person can read.', 'Lit les notes protégées que la personne peut lire.')
        : t('Cannot read protected notes.', 'Ne lit pas les notes protégées.'),
    },
    {
      key: 'quota',
      allowed: true,
      text: t(
        `At most ${settings.quotaPerHour} calls an hour; beyond, it is paused until the next hour.`,
        `Au plus ${settings.quotaPerHour} appels par heure ; au-delà, il est mis en pause jusqu’à l’heure suivante.`,
      ),
    },
  ];
}

const WHAT_TO_TRY: Record<string, [string, string]> = {
  alice: [
    'Administrator: open the agents console, change the agent ceiling, try the emergency stop, and read the access log.',
    'Administratrice : ouvrez la console des agents, changez le plafond, essayez l’arrêt d’urgence et lisez le journal des accès.',
  ],
  bob: [
    'Team Finance and Team Payments: validate David’s proposals on Billing, edit Billing while a colleague changes it, and connect an AI agent.',
    'Équipes Finance et Paiements : validez les propositions de David sur Facturation, modifiez Facturation pendant qu’un collègue la change, et connectez un agent IA.',
  ],
  claire: [
    'Guest: you see the customer portal in full because it is shared with you; everything else shows its outline only.',
    'Invitée : vous voyez le portail client en entier car il vous est partagé ; le reste n’apparaît qu’en aperçu.',
  ],
  david: [
    'Team CRM: propose a change to a Finance record, follow its validation, and let an agent draft a change for you.',
    'Équipe CRM : proposez une modification d’une fiche de Finance, suivez sa validation, et laissez un agent rédiger un brouillon pour vous.',
  ],
};

const KIND_PLURAL: Record<string, string> = {
  application: 'applications',
  flow: 'flows',
  dataObject: 'data-objects',
  page: 'pages',
};

export function seedAgents(ctx: GovCtx) {
  seed(ctx);
}

/** The agents and demo routes; null when the path is not one of them. */
export function handleAgents(
  ctx: GovCtx,
  u: UserM2,
  method: string,
  url: URL,
  path: string,
  body: () => Rec,
  signIn: (username: string) => Me | null,
): Response | null {
  const { json, problem, db } = ctx;
  const admin = u.role === 'admin';
  const fr = u.language === 'fr';
  const notice = (en: [string, string], frx: [string, string], link: string | null): DemoNotice => ({
    title: fr ? frx[0] : en[0],
    detail: fr ? frx[1] : en[1],
    link,
  });

  if (path === '/settings/agents') {
    if (method === 'GET') return json(200, settings);
    if (!admin) return problem(403, 'Administrators only.');
    const b = body() as Partial<AgentSettings>;
    if (b.version !== settings.version) return problem(409, 'Conflict', 'Someone changed the settings meanwhile.');
    if (b.quotaPerHour != null && (b.quotaPerHour < 1 || b.quotaPerHour > 100000))
      return problem(400, 'Validation failed.', undefined, { quotaPerHour: ['Between 1 and 100000.'] });
    settings = {
      ...settings,
      ...Object.fromEntries(Object.entries(b).filter(([, v]) => v !== null && v !== undefined)),
      version: settings.version + 1,
      updatedAt: ctx.now(),
    };
    return json(200, settings);
  }
  if (path === '/me/agents') {
    const mine = demoGov.proposals.filter((p) => p.authorKind === 'agent' && p.author === u.username);
    return json(200, {
      connections: connections.filter((c) => c.username === u.username).map((c) => dto(ctx, c)),
      drafts: mine.filter((p) => p.status === 'draft').map((p) => demoGov.view(ctx, u, p)),
      proposals: mine.filter((p) => p.status !== 'draft').map((p) => demoGov.view(ctx, u, p)),
      settings: {
        enabled: settings.enabled,
        ceiling: settings.ceiling,
        whoConnects: settings.whoConnects,
        quotaPerHour: settings.quotaPerHour,
        mcpUrl: settings.mcpUrl,
      },
    });
  }
  if (path === '/agents' && method === 'GET') {
    if (!admin) return problem(403, 'Administrators only.');
    return json(
      200,
      connections.map((c) => dto(ctx, c)),
    );
  }
  if (path === '/agents/activity') {
    const sp = url.searchParams;
    const mine = new Set(connections.filter((c) => c.username === u.username).map((c) => c.id));
    let list = activity.filter((a) => admin || mine.has(a.connectionId));
    if (sp.get('connectionId')) list = list.filter((a) => a.connectionId === sp.get('connectionId'));
    if (sp.get('kind')) list = list.filter((a) => a.kind === sp.get('kind'));
    const cursor = Number(sp.get('cursor') ?? 0);
    if (cursor) list = list.filter((a) => a.id < cursor);
    const limit = Number(sp.get('limit') ?? 50);
    const page = list.slice(0, limit);
    return json(200, {
      items: page.map((a) => activityDto(ctx, a)),
      nextCursor: list.length > limit ? String(page[page.length - 1].id) : null,
    });
  }
  if (path === '/agents/stop-all' && method === 'POST') {
    if (!admin) return problem(403, 'Administrators only.');
    const live = connections.filter((c) => !c.revokedAt);
    for (const c of live) {
      c.revokedAt = ctx.now();
      log(c, 'stop-all', 'revoked', `Emergency stop by ${u.username}: access revoked.`);
    }
    return json(200, { revokedConnections: live.length, revokedTokens: live.length * 2, at: ctx.now() });
  }
  let m = /^\/agents\/([^/]+)\/(revoke|approve|capabilities)$/.exec(path);
  if (m) {
    const c = connections.find((x) => x.id === m![1]);
    if (!c || (!admin && c.username !== u.username)) return problem(404, 'Connection not found.');
    if (m[2] === 'capabilities') {
      const p = db.users.find((x) => x.username === c.username)!;
      return json(200, {
        connectionId: c.id,
        agentName: c.clientName,
        person: ref(p),
        ceiling: settings.ceiling,
        scopes: c.scopes,
        items: capabilities(p, c.scopes),
      });
    }
    if (method !== 'POST') return null;
    if (m[2] === 'approve') {
      if (!admin) return problem(403, 'Administrators only.');
      if (!c.pending) return problem(409, 'This connection is already approved.');
      c.pending = false;
      c.approvedAt = ctx.now();
    } else if (!c.revokedAt) {
      c.revokedAt = ctx.now();
      log(c, 'revoke', 'revoked', `Revoked by ${u.username}.`);
    }
    return json(200, dto(ctx, c));
  }

  /* consent page: any request id works, "expired" gives 404 */
  m = /^\/oauth\/requests\/([^/]+)(?:\/(approve|deny))?$/.exec(path);
  if (m) {
    const id = m[1];
    if (id === 'expired') return problem(404, 'This authorization request is unknown or expired.');
    const entry = consents.get(id) ?? { clientName: 'Claude Code', decided: false };
    consents.set(id, entry);
    if (entry.decided) return problem(409, 'This authorization request was already decided.');
    const grantable = (s: string) => s !== 'docs:read:restricted' || settings.ceiling === 'asUser';
    const asked = ['docs:read', 'docs:draft'];
    if (!m[2]) {
      const req: ConsentRequest = {
        id,
        clientId: 'https://claude.ai/oauth/claude-code-client-metadata',
        clientKind: 'metadataDocument',
        clientName: entry.clientName,
        clientUri: 'https://claude.ai',
        redirectHost: '127.0.0.1:53682',
        person: ref(u),
        scopes: asked.map((s) => ({
          scope: s,
          grantable: grantable(s) && !(s === 'docs:draft' && u.role === 'guest'),
          text: { en: s, fr: s },
        })),
        capabilities: capabilities(u, u.role === 'guest' ? ['docs:read'] : asked),
        ceiling: settings.ceiling,
        approvalRequired: settings.whoConnects === 'approvalRequired' && !admin,
        alreadyConnected: connections.some(
          (c) => c.username === u.username && c.clientName === entry.clientName && !c.revokedAt,
        ),
        expiresAt: new Date(Date.now() + 600000).toISOString(),
      };
      return json(200, req);
    }
    if (method !== 'POST') return null;
    entry.decided = true;
    const back = location.origin + '/my-agents';
    if (m[2] === 'deny') return json(200, { redirectTo: back + '?error=access_denied', pendingApproval: false });
    const chosen = (body().scopes as string[] | null) ?? asked;
    const pending = settings.whoConnects === 'approvalRequired' && !admin;
    connections.unshift(
      conn({
        clientName: entry.clientName,
        clientUri: 'https://claude.ai',
        username: u.username,
        scopes: chosen.filter((s) => asked.includes(s)),
        createdAt: ctx.now(),
        approvedAt: pending ? null : ctx.now(),
        pending,
      }),
    );
    return json(200, { redirectTo: back + '?code=mock', pendingApproval: pending });
  }

  /* demo toolbox */
  if (!path.startsWith('/demo/')) return null;
  if (!mockDemoOn()) return problem(404, 'Not found.');
  if (path === '/demo/state') {
    const personas: DemoPersona[] = db.users
      .filter((x) => WHAT_TO_TRY[x.username])
      .map((x) => ({
        username: x.username,
        displayName: x.displayName,
        role: x.role,
        teams: db.teams.filter((t) => x.teamIds?.includes(t.id)).map((t) => ({ id: t.id, name: t.name })),
        whatToTry: { en: WHAT_TO_TRY[x.username][0], fr: WHAT_TO_TRY[x.username][1] },
        current: x.username === u.username,
      }));
    return json(200, {
      personas,
      scenarios: [],
      current: u.username,
      notice: { title: 'Demo mode', detail: 'Switch between the demo accounts.', link: null },
    });
  }
  if (method !== 'POST') return null;
  const b = body();
  if (path === '/demo/switch') {
    const me = signIn(String(b.username));
    if (!me) return problem(404, `No demo account named '${String(b.username)}'.`);
    const n = db.users.find((x) => x.username === me.username)!;
    return json(200, {
      me,
      notice: notice(
        [`You are now ${n.displayName}`, WHAT_TO_TRY[n.username][0]],
        [`Vous êtes maintenant ${n.displayName}`, WHAT_TO_TRY[n.username][1]],
        null,
      ),
    });
  }
  if (path === '/demo/simulate/edit') {
    const kind = String(b.kind) as RecordKind;
    const id = String(b.id);
    const rec = demoGov.find(ctx, kind, id);
    if (!rec) return problem(404, 'Record not found.');
    const team = demoGov.ownerOf(ctx, kind, id);
    const colleague =
      db.users.find((x) => x.username !== u.username && x.role !== 'guest' && team && x.teamIds?.includes(team)) ??
      db.users.find((x) => x.username !== u.username && x.role === 'admin')!;
    const field = kind === 'page' ? 'body' : 'description';
    const raw = rec[field];
    const text = (typeof raw === 'string' ? raw : '').split('\n\n_Checked by ')[0];
    demoGov.publish(
      ctx,
      kind,
      id,
      { [field]: `${text}\n\n_Checked by ${colleague.displayName}._` },
      colleague.username,
      'Checked during the demo',
    );
    const v = Number(demoGov.find(ctx, kind, id)?.version);
    const title = demoGov.title(rec);
    const outdated = demoGov.proposals.filter(
      (p) =>
        (p.status === 'submitted' || p.status === 'draft') &&
        p.changes.some((c) => c.kind === kind && c.targetId === id),
    );
    return json(200, {
      kind,
      id,
      version: v,
      author: ref(colleague),
      outdatedProposals: outdated.map((p) => p.id),
      notice: notice(
        [
          `${colleague.displayName} changed ${title}`,
          `Version ${v} is published. Saving the version you opened now gets a conflict${outdated.length ? `, and ${outdated.length} open proposal(s) became outdated.` : '.'}`,
        ],
        [
          `${colleague.displayName} a modifié ${title}`,
          `La version ${v} est publiée. Enregistrer la version que vous avez ouverte provoque maintenant un conflit${outdated.length ? `, et ${outdated.length} proposition(s) ouverte(s) sont devenues obsolètes.` : '.'}`,
        ],
        `/${KIND_PLURAL[kind]}/${id}`,
      ),
    });
  }
  if (path === '/demo/simulate/proposal') {
    if (u.role === 'guest') return problem(409, 'Guests do not validate proposals: switch to bob or david first.');
    const teams = u.teamIds?.length ? u.teamIds : ['team-finance'];
    const app = db.applications.find((a) => a.teamId && teams.includes(a.teamId) && a.visibility === 'everyone');
    if (!app) return problem(409, 'None of your teams owns an application to propose a change to.');
    const author = db.users.find(
      (x) => x.role === 'member' && x.username !== u.username && !x.teamIds?.includes(app.teamId!),
    )!;
    const change = demoGov.updateChange(ctx, 'application', app.id, {
      description: `${app.description ?? ''}\n\nAsked by ${author.displayName}: who should we call when this is down at night?`,
    })!;
    const p = {
      id: demoGov.nextId(),
      title: `Add the night contact to ${app.name}`,
      comment: 'Our team gets calls about it at night and does not know whom to call.',
      status: 'submitted',
      authorKind: 'user',
      author: author.username,
      onBehalfOf: null,
      createdAt: ctx.now(),
      submittedAt: ctx.now(),
      decidedBy: null,
      decidedAt: null,
      refusalReason: null,
      version: 1,
      changes: [change],
    } as MockProposal;
    demoGov.proposals.push(p);
    const team = db.teams.find((x) => x.id === app.teamId)?.name ?? 'an administrator';
    return json(200, {
      proposalId: p.id,
      kind: 'application',
      id: app.id,
      author: ref(author),
      notice: notice(
        [`${author.displayName} sent a proposal`, `"${p.title}" waits for ${team}.`],
        [`${author.displayName} a envoyé une proposition`, `« ${p.title} » attend ${team}.`],
        `/review/${p.id}`,
      ),
    });
  }
  if (path === '/demo/simulate/decision') {
    const p = demoGov.proposals.find((x) => x.id === b.proposalId && x.author === u.username);
    if (!p) return problem(404, 'No proposal of yours with this id.');
    if (p.status !== 'submitted') return problem(409, 'Only a proposal waiting for validation can be decided.');
    const accept = b.decision === 'accept';
    const team = p.changes.map((c) => demoGov.ownerOf(ctx, c.kind, c.targetId ?? '')).find(Boolean);
    const decider =
      db.users.find((x) => x.username !== u.username && x.role === 'member' && team && x.teamIds?.includes(team)) ??
      db.users.find((x) => x.role === 'admin' && x.username !== u.username)!;
    const reason = 'Thanks, but this is already documented on the team page: link to it instead of copying it here.';
    demoGov.decide(ctx, p, decider.username, accept, reason);
    return json(200, {
      proposalId: p.id,
      status: p.status,
      decidedBy: ref(decider),
      refusalReason: p.refusalReason,
      notice: accept
        ? notice(
            [`${decider.displayName} accepted your proposal`, `"${p.title}" is published.`],
            [`${decider.displayName} a accepté votre proposition`, `« ${p.title} » est publiée.`],
            `/review/${p.id}`,
          )
        : notice(
            [`${decider.displayName} refused your proposal`, `Reason: ${reason}`],
            [`${decider.displayName} a refusé votre proposition`, `Motif : ${reason}`],
            `/review/${p.id}`,
          ),
    });
  }
  if (path === '/demo/simulate/agent') {
    if (!settings.enabled) return problem(409, 'AI agents are turned off in the agent settings.');
    const scenario = String(b.scenario);
    let c = connections.find((x) => x.username === u.username && x.demo && !x.revokedAt);
    if (!c) {
      c = conn({
        clientName: 'Demo assistant',
        clientId: 'noosphere-demo-assistant',
        username: u.username,
        demo: true,
        createdAt: ctx.now(),
        approvedAt: ctx.now(),
      });
      connections.unshift(c);
    }
    if (scenario !== 'quota') c.pausedUntil = null;
    const start = actSeq;
    let proposalId: string | null = null;
    let n: DemoNotice;
    if (scenario === 'draft' || scenario === 'submit') {
      const app = db.applications.find((a) => a.id === 'billing') ?? db.applications[0];
      log(c, 'search', 'read', `Searched "${app.name}": 3 result(s).`);
      log(c, 'get_record', 'read', `Read ${app.name} (application).`, ['application', app.id, app.name]);
      if (u.role === 'guest') {
        log(c, 'draft_change', 'refused', 'Refused (role): guests cannot write drafts.', [
          'application',
          app.id,
          app.name,
        ]);
        n = notice(
          ['The agent could not write a draft', 'Your role does not allow proposals: see its activity for the reason.'],
          [
            'L’agent n’a pas pu écrire de brouillon',
            'Votre rôle ne permet pas de proposer : voyez son activité pour la raison.',
          ],
          '/my-agents',
        );
      } else {
        const change = demoGov.updateChange(ctx, 'application', app.id, {
          description: `${app.description ?? ''}\n\nIts nightly jobs are listed on the operations page: [[page:operations]].`,
        })!;
        const p = {
          id: demoGov.nextId(),
          title: `${app.name}: link the operations page`,
          comment: 'The description does not say where the nightly jobs are documented.',
          status: 'draft',
          authorKind: 'agent',
          author: u.username,
          onBehalfOf: ref(u),
          createdAt: ctx.now(),
          submittedAt: null,
          decidedBy: null,
          decidedAt: null,
          refusalReason: null,
          version: 1,
          changes: [change],
          agentName: c.clientName,
          agentConnectionId: c.id,
        } as MockProposal;
        demoGov.proposals.push(p);
        proposalId = p.id;
        log(c, 'draft_change', 'draft', `Drafted a change on ${app.name}: Description.`, [
          'application',
          app.id,
          app.name,
        ]);
        const team = db.teams.find((x) => x.id === app.teamId)?.name ?? 'an administrator';
        if (scenario === 'submit') {
          Object.assign(p, { status: 'submitted', submittedAt: ctx.now(), version: 2 });
          log(c, 'submit_draft', 'proposal', `Submitted "${p.title}".`, ['application', app.id, app.name]);
          n = notice(
            [`${c.clientName} submitted a proposal`, `The proposal on ${app.name} now waits for ${team}.`],
            [`${c.clientName} a soumis une proposition`, `La proposition sur ${app.name} attend maintenant ${team}.`],
            `/review/${p.id}`,
          );
        } else
          n = notice(
            [
              `${c.clientName} wrote a draft`,
              `A draft on ${app.name} waits for you in My agents. Nobody else sees it until it is submitted.`,
            ],
            [
              `${c.clientName} a écrit un brouillon`,
              `Un brouillon sur ${app.name} vous attend dans Mes agents. Personne d’autre ne le voit avant sa soumission.`,
            ],
            '/my-agents',
          );
      }
    } else if (scenario === 'refused') {
      log(c, 'get_record', 'refused', 'Refused (ceiling): the application is restricted and above the agent ceiling.', [
        'application',
        'fraud',
        'Fraud scoring',
      ]);
      log(c, 'draft_change', 'refused', 'Refused a draft: description looks like a secret.', [
        'application',
        'acct',
        'Accounting',
      ]);
      n = notice(
        [
          `${c.clientName} was refused twice`,
          'It tried to open a restricted record (above the ceiling) and to write a token (a secret). Both refusals are in its activity.',
        ],
        [
          `${c.clientName} a été refusé deux fois`,
          'Il a tenté d’ouvrir une fiche restreinte (au-dessus du plafond) et d’écrire un jeton (un secret). Les deux refus sont dans son activité.',
        ],
        '/my-agents',
      );
    } else {
      log(c, 'search', 'read', 'Searched "billing": 18 result(s).');
      log(c, 'search', 'read', 'Searched "billing": 18 result(s).');
      log(c, 'search', 'quota', 'Quota reached: 3 calls this hour. Paused until the next hour.');
      c.pausedUntil = nextHour();
      n = notice(
        [
          `${c.clientName} reached its quota`,
          'It is paused until the next hour; a quota line is in its activity. (The demo uses a quota of 3 calls.)',
        ],
        [
          `${c.clientName} a atteint son quota`,
          'Il est en pause jusqu’à l’heure suivante ; une ligne de quota est dans son activité. (La démo utilise un quota de 3 appels.)',
        ],
        '/my-agents',
      );
    }
    return json(200, {
      scenario,
      connectionId: c.id,
      activity: activity
        .filter((a) => a.id > start)
        .map((a) => activityDto(ctx, a))
        .reverse(),
      proposalId,
      notice: n,
    });
  }
  if (path === '/demo/simulate/guest-read') {
    const rec = demoGov.find(ctx, String(b.kind) as RecordKind, String(b.id));
    if (!rec) return problem(404, 'Record not found.');
    const title = demoGov.title(rec);
    const shared = ((rec.access as { usernames?: string[] } | undefined)?.usernames ?? []).includes('claire');
    return json(200, {
      kind: b.kind,
      id: b.id,
      read: shared,
      accessLogLines: shared ? 1 : 0,
      notice: shared
        ? notice(
            [`Claire read ${title}`, '1 line(s) added to the access log.'],
            [`Claire a lu ${title}`, '1 ligne(s) ajoutée(s) au journal des accès.'],
            '/admin/access',
          )
        : notice(
            [
              `Claire cannot read ${title}`,
              'It is not shared with her: she sees its outline only, and nothing is logged.',
            ],
            [
              `Claire ne peut pas lire ${title}`,
              'La fiche ne lui est pas partagée : elle n’en voit que l’aperçu, rien n’est journalisé.',
            ],
            `/${KIND_PLURAL[String(b.kind)]}/${String(b.id)}`,
          ),
    });
  }
  if (path === '/demo/reset') {
    if (b.confirm !== true)
      return problem(400, 'Validation failed.', undefined, { confirm: ['Confirm the reset with confirm: true.'] });
    for (let i = demoGov.proposals.length - 1; i >= 0; i--)
      if (demoGov.proposals[i].authorKind === 'agent') demoGov.proposals.splice(i, 1);
    seed(ctx);
    return json(200, {
      notice: notice(
        ['Demo reset', 'The demo agents were reloaded. (The dev mock keeps the landscape.)'],
        [
          'Démo réinitialisée',
          'Les agents de démonstration ont été rechargés. (Le mock de développement garde le paysage.)',
        ],
        '/',
      ),
    });
  }
  return null;
}
