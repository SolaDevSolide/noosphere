// Development-only mock of the milestone 5 API: links with other applications (administrators) and the deployments
// they send to records. No peer is called: a new pairing code is "used" by a fictional application a few seconds
// after it is created, so that the fingerprint step can be tried.
import type {
  Deployment,
  DeploymentEnvironment,
  Deployments,
  Link,
  LinkCode,
  LinkExchange,
  UserM2,
} from '../api/types';
import { LINK_CAPABILITIES } from '../api/types';
import type { GovCtx } from './governance';

type Rec = Record<string, unknown>;
const ago = (min: number) => new Date(Date.now() - min * 60000).toISOString();
const inDays = (days: number, hour = 20) => {
  const d = new Date();
  d.setDate(d.getDate() + days);
  d.setHours(hour, 0, 0, 0);
  return d.toISOString();
};
const plusHours = (iso: string, h: number) => new Date(new Date(iso).getTime() + h * 3600000).toISOString();

/** Seconds before the fictional application uses a new code; sessionStorage `noos-mock-pair-delay` overrides it. */
function pairDelay(): number {
  try {
    const v = sessionStorage.getItem('noos-mock-pair-delay');
    if (v) return Number(v);
  } catch {
    /* default */
  }
  return 8000;
}

const WORDS = ['orbit', 'cedar', 'lumen', 'maple', 'quartz', 'heron', 'tundra', 'violet', 'kayak', 'amber'];
const words = (n: number) =>
  `${WORDS[n % 10]} ${WORDS[(n + 3) % 10]} ${String((n * 731) % 10000).padStart(4, '0')} ${WORDS[(n + 7) % 10]}`;
const uuid = (n: number) => `0000000${n}`.slice(-8) + '-5000-4000-8000-000000000000';
const keys = (n: number) => ({
  signing: 'own-sig-' + n,
  encryption: 'own-enc-' + n,
  peerSigning: 'peer-sig-' + n,
  peerEncryption: 'peer-enc-' + n,
});

interface StoredDeployment extends Omit<Deployment, 'source'> {
  linkId: string;
}
interface StoredCode extends LinkCode {
  code: string;
  burnt?: boolean;
}

let links: Link[] = [];
let codes: StoredCode[] = [];
let exchanges: LinkExchange[] = [];
let deployments: StoredDeployment[] = [];
let seq = 0;
let exSeq = 0;

function link(n: number, l: Partial<Link> & Pick<Link, 'product' | 'instanceName' | 'url'>): Link {
  return {
    id: uuid(n),
    peerVersion: null,
    state: 'active',
    confirmedHere: true,
    confirmedAt: ago(60 * 24 * 9),
    confirmedBy: 'alice',
    peerConfirmed: true,
    peerCheckedAt: ago(30),
    capabilities: [...LINK_CAPABILITIES],
    fingerprint: words(n),
    pairedAt: ago(60 * 24 * 9),
    pairedBy: 'alice',
    keysRenewedAt: null,
    lastExchangeAt: null,
    cutAt: null,
    cutBy: null,
    demo: false,
    keys: keys(n),
    version: 2,
    ...l,
  };
}

function log(linkId: string | null, at: string, x: Omit<LinkExchange, 'id' | 'at' | 'linkId'>) {
  exchanges.push({ id: ++exSeq, at, linkId, ...x });
}

const env = (name: string, production: boolean): DeploymentEnvironment => ({ name, production });

function dep(
  linkId: string,
  d: Partial<StoredDeployment> & Pick<StoredDeployment, 'title' | 'phase' | 'applicationIds' | 'plannedStart'>,
): StoredDeployment {
  const id = 'dep-' + ++seq;
  return {
    linkId,
    deploymentId: id,
    outcome: null,
    postponed: false,
    plannedEnd: d.plannedStart ? plusHours(d.plannedStart, 3) : null,
    url: `${links.find((l) => l.id === linkId)?.url ?? 'https://example.org'}/meps/${id}`,
    environments: [env('Production', true)],
    updatedAt: ago(60 * 24 * 2),
    receivedAt: ago(60 * 24 * 2),
    ...d,
  };
}

function seed() {
  seq = 0;
  exSeq = 0;
  const mep = link(1, {
    product: 'gestion-mep',
    instanceName: 'MEP Alizé',
    url: 'https://mep.alize.example',
    peerVersion: '1.11.0',
    pairedAt: ago(60 * 24 * 9),
    keysRenewedAt: ago(60 * 24 * 3),
    lastExchangeAt: ago(2),
    demo: true,
  });
  const ci = link(2, {
    product: 'Release pipeline',
    instanceName: 'Alizé CI',
    url: 'https://ci.alize.example',
    peerVersion: '2.3.0',
    pairedAt: ago(60 * 24 * 4),
    capabilities: ['deployments:receive'],
    lastExchangeAt: ago(60),
  });
  const test = link(3, {
    product: 'gestion-mep',
    instanceName: 'MEP Alizé · test',
    url: 'https://mep-test.alize.example',
    peerVersion: '1.11.0',
    pairedAt: ago(90),
    confirmedAt: ago(80),
    capabilities: ['catalog:read', 'notices:send'],
    peerConfirmed: false,
  });
  const old = link(4, {
    product: 'gestion-mep',
    instanceName: 'MEP Alizé (old server)',
    url: 'https://mep-old.alize.example',
    peerVersion: '1.9.2',
    pairedAt: ago(60 * 24 * 59),
    state: 'cut',
    peerConfirmed: null,
    cutAt: ago(60 * 24 * 28),
    cutBy: 'alice',
    lastExchangeAt: ago(60 * 24 * 28),
    keys: { signing: '', encryption: '', peerSigning: '', peerEncryption: '' },
  });
  links = [mep, ci, test, old];
  // sessionStorage `noos-mock-links` = "0" starts without any link, to see the empty page.
  try {
    if (sessionStorage.getItem('noos-mock-links') === '0') links = [];
  } catch {
    /* default */
  }
  codes = [];
  exchanges = [];
  log(mep.id, mep.pairedAt, {
    direction: 'in',
    operation: 'pair',
    result: 'ok',
    status: 200,
    reason: null,
    detail: 'Paired with MEP Alizé (gestion-mep); waiting for confirmation on both sides.',
  });
  log(mep.id, ago(60 * 24 * 3), {
    direction: 'in',
    operation: 'rekey',
    result: 'ok',
    status: 200,
    reason: null,
    detail: 'Old key accepted 5 more minutes.',
  });
  log(mep.id, ago(60 * 26), {
    direction: 'in',
    operation: 'catalog',
    result: 'refused',
    status: 401,
    reason: 'clock-skew',
    detail: 'Signed 7 min ahead of this server.',
  });
  log(mep.id, ago(160), {
    direction: 'out',
    operation: 'notices',
    result: 'failed',
    status: null,
    reason: null,
    detail: 'Timeout after 3 tries.',
  });
  log(mep.id, ago(75), {
    direction: 'in',
    operation: 'catalog',
    result: 'ok',
    status: 304,
    reason: null,
    detail: 'Not modified.',
  });
  log(mep.id, ago(46), {
    direction: 'in',
    operation: 'deployments',
    result: 'refused',
    status: 401,
    reason: 'replay',
    detail: 'This nonce was already used in the last 10 minutes.',
  });
  log(mep.id, ago(32), {
    direction: 'in',
    operation: 'deployments',
    result: 'refused',
    status: 401,
    reason: 'bad-signature',
    detail: 'From 10.20.4.18.',
  });
  log(mep.id, ago(4), {
    direction: 'in',
    operation: 'deployments',
    result: 'ok',
    status: 200,
    reason: null,
    detail: 'Accepted 2, ignored 0.',
  });
  log(mep.id, ago(3), {
    direction: 'out',
    operation: 'notices',
    result: 'ok',
    status: 202,
    reason: null,
    detail: 'After 30 s grouping.',
  });
  log(mep.id, ago(2), {
    direction: 'in',
    operation: 'catalog',
    result: 'ok',
    status: 200,
    reason: null,
    detail: 'Signed, encrypted.',
  });
  log(ci.id, ago(60), {
    direction: 'in',
    operation: 'deployments',
    result: 'ok',
    status: 200,
    reason: null,
    detail: 'Accepted 1, ignored 0.',
  });
  log(test.id, ago(90), { direction: 'in', operation: 'pair', result: 'ok', status: 200, reason: null, detail: null });
  log(test.id, ago(80), {
    direction: 'out',
    operation: 'status',
    result: 'refused',
    status: 403,
    reason: 'link-not-confirmed',
    detail: 'Not confirmed on the other side yet.',
  });
  log(old.id, old.cutAt!, {
    direction: 'out',
    operation: 'unpair',
    result: 'ok',
    status: 204,
    reason: null,
    detail: null,
  });

  deployments = [
    dep(mep.id, {
      title: 'Billing 4.3',
      phase: 'preparation',
      plannedStart: inDays(4),
      applicationIds: ['billing', 'crm'],
    }),
    dep(mep.id, {
      title: 'Billing 4.4',
      phase: 'preparation',
      plannedStart: inDays(18, 21),
      plannedEnd: null,
      applicationIds: ['billing'],
      environments: [env('Test', false)],
    }),
    dep(mep.id, {
      title: 'CRM 7.0',
      phase: 'acceptance',
      plannedStart: inDays(12, 19),
      applicationIds: ['crm'],
      postponed: true,
    }),
    dep(ci.id, {
      title: 'billing-api 4.2.3',
      phase: 'closed',
      outcome: 'success',
      plannedStart: inDays(-2, 14),
      plannedEnd: plusHours(inDays(-2, 14), 0.1),
      applicationIds: ['billing'],
    }),
    dep(ci.id, {
      title: 'crm-export 3.8.1',
      phase: 'closed',
      outcome: 'success-with-incidents',
      plannedStart: inDays(-1, 10),
      applicationIds: ['crm'],
    }),
    dep(mep.id, {
      title: 'Billing 4.2.1',
      phase: 'closed',
      outcome: 'success',
      plannedStart: inDays(-4),
      applicationIds: ['billing'],
    }),
    dep(mep.id, {
      title: 'Billing 4.2 hotfix',
      phase: 'cancelled',
      plannedStart: inDays(-9),
      applicationIds: ['billing'],
    }),
    dep(old.id, {
      title: 'Billing 4.0',
      phase: 'closed',
      outcome: 'rolled-back',
      plannedStart: inDays(-35),
      applicationIds: ['billing'],
    }),
  ];
  if (links.length === 0) deployments = [];
}

seed();

function codeState(c: Pick<StoredCode, 'burnt' | 'usedAt' | 'expiresAt'>): LinkCode['state'] {
  if (c.burnt) return 'burnt';
  if (c.usedAt) return 'used';
  return new Date(c.expiresAt).getTime() <= Date.now() ? 'expired' : 'active';
}
const codeDto = (x: StoredCode): LinkCode => {
  const { code: _c, burnt: _b, ...c } = x;
  return { ...c, state: codeState(x) };
};

/** The fictional application uses a new code: a pending link appears, waiting for the fingerprint. */
function usedByPeer(c: StoredCode) {
  if (codeState(c) !== 'active') return;
  const n = 10 + links.length;
  const l = link(n, {
    product: 'gestion-mep',
    instanceName: 'MEP Alizé · recette',
    url: 'https://mep-recette.alize.example',
    peerVersion: '1.11.0',
    state: 'pending',
    confirmedHere: false,
    confirmedAt: null,
    confirmedBy: null,
    peerConfirmed: false,
    pairedAt: new Date().toISOString(),
    pairedBy: c.createdBy,
    capabilities: [...c.capabilities],
    version: 1,
  });
  links.unshift(l);
  c.usedAt = l.pairedAt;
  c.linkId = l.id;
  log(l.id, l.pairedAt, {
    direction: 'in',
    operation: 'pair',
    result: 'ok',
    status: 200,
    reason: null,
    detail: 'Paired with MEP Alizé · recette (gestion-mep); waiting for confirmation on both sides.',
  });
}

const sourceOf = (linkId: string) => {
  const l = links.find((x) => x.id === linkId);
  return {
    linkId,
    product: l?.product ?? '',
    instanceName: l?.instanceName ?? '',
    linkActive: !!l && l.state !== 'cut',
  };
};
const ended = (d: StoredDeployment) => d.phase === 'closed' || d.phase === 'cancelled';
const toDto = ({ linkId, ...d }: StoredDeployment): Deployment => ({ ...d, source: sourceOf(linkId) });

/** Deployments touching any of `apps`, as the API splits them; guests get none. */
export function deploymentsFor(u: UserM2, apps: string[], pastLimit?: number): Deployments {
  if (u.role === 'guest') return { upcoming: [], past: [] };
  const rows = deployments.filter((d) => d.applicationIds.some((a) => apps.includes(a)));
  const key = (d: StoredDeployment) => d.plannedStart ?? '9999';
  const upcoming = rows.filter((d) => !ended(d)).sort((a, b) => key(a).localeCompare(key(b)));
  const past = rows
    .filter(ended)
    .sort((a, b) =>
      (b.plannedEnd ?? b.plannedStart ?? b.updatedAt).localeCompare(a.plannedEnd ?? a.plannedStart ?? a.updatedAt),
    )
    .slice(0, pastLimit ?? Infinity);
  return { upcoming: upcoming.map(toDto), past: past.map(toDto) };
}

export function seedLinks() {
  seed();
}

/** The links routes and the deployments of a record; null when the path is not one of them. */
export function handleLinks(
  ctx: GovCtx,
  u: UserM2,
  method: string,
  url: URL,
  path: string,
  body: () => Rec,
  ends: (flowId: string) => [string, string] | null,
): Response | null {
  const { json, problem } = ctx;
  const dm = /^\/(applications|flows)\/([^/]+)\/deployments$/.exec(path);
  if (dm && method === 'GET') {
    const id = decodeURIComponent(dm[2]);
    if (dm[1] === 'applications') return json(200, deploymentsFor(u, [id]));
    const e = ends(id);
    if (!e) return problem(404, 'Flow not found.');
    return json(200, deploymentsFor(u, e));
  }
  if (!path.startsWith('/links')) return null;
  if (u.role !== 'admin') return problem(403, 'Administrators only.');

  if (path === '/links/codes') {
    if (method === 'GET')
      return json(200, [...codes].sort((a, b) => b.createdAt.localeCompare(a.createdAt)).map(codeDto));
    if (method === 'POST') {
      const b = body();
      const caps = Array.isArray(b.capabilities) ? (b.capabilities as string[]) : [...LINK_CAPABILITIES];
      const unknown = caps.filter((c) => !(LINK_CAPABILITIES as string[]).includes(c));
      if (unknown.length)
        return problem(400, 'Validation failed.', undefined, {
          capabilities: [`Unknown capability '${unknown[0]}'.`],
        });
      const minutes = typeof b.expiresInMinutes === 'number' ? b.expiresInMinutes : 15;
      const alphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
      const raw = Array.from({ length: 12 }, () => alphabet[Math.floor(Math.random() * alphabet.length)]).join('');
      const code = `${raw.slice(0, 4)}-${raw.slice(4, 8)}-${raw.slice(8)}`;
      const c: StoredCode = {
        id: uuid(900 + codes.length),
        code,
        codeId: raw.slice(0, 4),
        state: 'active',
        capabilities: LINK_CAPABILITIES.filter((x) => caps.includes(x)),
        createdAt: new Date().toISOString(),
        expiresAt: new Date(Date.now() + minutes * 60000).toISOString(),
        createdBy: u.username,
        usedAt: null,
        linkId: null,
        failedAttempts: 0,
        bootstrap: false,
      };
      codes.push(c);
      const delay = pairDelay();
      if (delay >= 0) setTimeout(() => usedByPeer(c), delay);
      return json(201, {
        id: c.id,
        code,
        codeId: c.codeId,
        expiresAt: c.expiresAt,
        capabilities: c.capabilities,
        pairUrl: location.origin,
      });
    }
  }
  if (path === '/links' && method === 'GET') {
    const order = (l: Link) => (l.state === 'cut' ? 1 : 0);
    return json(
      200,
      [...links].sort((a, b) => order(a) - order(b) || b.pairedAt.localeCompare(a.pairedAt)),
    );
  }
  const m = /^\/links\/([^/]+)(?:\/(confirm|capabilities|renew-keys|cut|exchanges))?$/.exec(path);
  if (!m) return problem(404, 'Not found.');
  const l = links.find((x) => x.id === decodeURIComponent(m[1]));
  if (!l) return problem(404, 'Not found', `No link with the id '${m[1]}'.`);
  const op = m[2];
  const now = new Date().toISOString();
  const notCut = () =>
    l.state === 'cut' ? problem(409, 'Conflict', 'This link is cut: pair again with a new code.') : null;
  if (!op && method === 'GET') return json(200, l);
  if (op === 'exchanges' && method === 'GET') {
    const cursor = Number(url.searchParams.get('cursor') ?? 0);
    const limit = Number(url.searchParams.get('limit') ?? 50);
    const rows = exchanges.filter((x) => x.linkId === l.id && (!cursor || x.id < cursor)).sort((a, b) => b.id - a.id);
    const items = rows.slice(0, limit);
    return json(200, { items, nextCursor: rows.length > limit ? String(items[items.length - 1].id) : null });
  }
  if (op === 'confirm' && method === 'POST') {
    const blocked = notCut();
    if (blocked) return blocked;
    const norm = (s: unknown) => (typeof s === 'string' ? s : '').trim().toLowerCase().replace(/\s+/g, ' ');
    if (norm(body().fingerprint) !== norm(l.fingerprint))
      return problem(422, 'Unprocessable content', 'The fingerprint does not match: do not confirm.');
    Object.assign(l, {
      confirmedHere: true,
      confirmedAt: now,
      confirmedBy: u.username,
      state: 'active',
      version: l.version + 1,
    });
    return json(200, l);
  }
  if (op === 'capabilities' && method === 'PUT') {
    const blocked = notCut();
    if (blocked) return blocked;
    const caps = body().capabilities;
    if (!Array.isArray(caps))
      return problem(400, 'Validation failed.', undefined, { capabilities: ['This field is required.'] });
    l.capabilities = LINK_CAPABILITIES.filter((c) => (caps as string[]).includes(c));
    l.version++;
    return json(200, l);
  }
  if (op === 'renew-keys' && method === 'POST') {
    const blocked = notCut();
    if (blocked) return blocked;
    l.keysRenewedAt = now;
    l.fingerprint = words(l.version + 5);
    l.version++;
    log(l.id, now, {
      direction: 'out',
      operation: 'rekey',
      result: l.demo ? 'demo' : 'ok',
      status: l.demo ? null : 200,
      reason: null,
      detail: l.demo ? 'Demo link: nothing was sent.' : 'Old key accepted 5 more minutes.',
    });
    return json(200, l);
  }
  if (op === 'cut' && method === 'POST') {
    if (body().confirm !== true)
      return problem(400, 'Validation failed.', undefined, { confirm: ['Cutting a link cannot be undone.'] });
    if (l.state !== 'cut') {
      log(l.id, now, {
        direction: 'out',
        operation: 'unpair',
        result: l.demo ? 'demo' : 'ok',
        status: l.demo ? null : 204,
        reason: null,
        detail: l.demo ? 'Demo link: nothing was sent.' : null,
      });
      Object.assign(l, {
        state: 'cut',
        cutAt: now,
        cutBy: u.username,
        keys: { signing: '', encryption: '', peerSigning: '', peerEncryption: '' },
        version: l.version + 1,
      });
    }
    return json(200, l);
  }
  return problem(405, 'Method not allowed.');
}
