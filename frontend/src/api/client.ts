import type {
  AgentActivityKind,
  AgentActivityPage,
  AgentCapabilities,
  AgentConnection,
  AgentSettings,
  AgentSettingsInput,
  ConsentRequest,
  ConsentResult,
  DemoAgentResult,
  DemoAgentScenario,
  DemoDecisionResult,
  DemoEditResult,
  DemoGuestReadResult,
  DemoProposalResult,
  DemoResetResult,
  DemoState,
  DemoSwitchResult,
  MyAgents,
  StopAllResult,
  AccessLogPage,
  AccessOverview,
  Person,
  RecordAccess,
  Application,
  ApplicationInput,
  DataObject,
  DataObjectDetail,
  DomainDetail,
  Page,
  PageDetail,
  PageInput,
  PageListItem,
  PageTreeNode,
  RestrictedPage,
  Domain,
  ExchangeEnvelope,
  Compare,
  RecordKind,
  Flow,
  FlowInput,
  HistoryEntry,
  Inbox,
  Proposal,
  ProposalInput,
  RestoreResult,
  Revision,
  Settings,
  TeamMember,
  TrashItem,
  ImportResult,
  Landscape,
  Language,
  Me,
  SearchHit,
  Team,
  User,
  UserCreateInput,
  UserUpdateInput,
} from './types';

/** An error answered by the API (problem+json), or a network failure (status 0). */
export class ApiError extends Error {
  status: number;
  detail?: string;
  /** Validation messages by field, from the problem's `errors` member. */
  fieldErrors: Record<string, string[]>;
  /** The parsed response body, for errors that carry a result (an import refused with 422). */
  body: unknown;
  constructor(
    status: number,
    message: string,
    detail?: string,
    fieldErrors: Record<string, string[]> = {},
    body?: unknown,
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.detail = detail;
    this.fieldErrors = fieldErrors;
    this.body = body;
  }
  get isConflict() {
    return this.status === 409;
  }
}

let onUnauthorized: () => void = () => {};
/** Called on a 401 outside the login call (the session ended). */
export function setUnauthorizedHandler(fn: () => void) {
  onUnauthorized = fn;
}

interface Problem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]> | null | unknown[];
}

async function readProblem(res: Response): Promise<Problem> {
  try {
    const body: unknown = await res.json();
    return body && typeof body === 'object' ? (body as Problem) : {};
  } catch {
    return {};
  }
}

export async function request<T>(method: string, path: string, body?: unknown, init?: RequestInit): Promise<T> {
  let res: Response;
  try {
    res = await fetch('/api' + path, {
      method,
      credentials: 'same-origin',
      body: body === undefined ? undefined : JSON.stringify(body),
      ...init,
      // The API refuses state-changing requests without this header (CSRF guard).
      headers: {
        Accept: 'application/json',
        'X-Requested-With': 'fetch',
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(init?.headers as Record<string, string> | undefined),
      },
    });
  } catch {
    throw new ApiError(0, 'network');
  }
  if (!res.ok) {
    const p = await readProblem(res);
    if (res.status === 401 && path !== '/auth/login' && path !== '/auth/me') onUnauthorized();
    const fieldErrors = p.errors && !Array.isArray(p.errors) ? p.errors : {};
    throw new ApiError(res.status, p.title ?? res.statusText, p.detail, fieldErrors, p);
  }
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** A GET answered as plain text (the raw Markdown of a page). */
async function requestText(path: string): Promise<string> {
  let res: Response;
  try {
    res = await fetch('/api' + path, {
      credentials: 'same-origin',
      headers: { Accept: 'text/markdown, text/plain', 'X-Requested-With': 'fetch' },
    });
  } catch {
    throw new ApiError(0, 'network');
  }
  if (!res.ok) {
    const p = await readProblem(res);
    if (res.status === 401) onUnauthorized();
    throw new ApiError(res.status, p.title ?? res.statusText, p.detail, {}, p);
  }
  return res.text();
}

const q = (params: Record<string, string | undefined>) => {
  const s = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v) s.set(k, v);
  const str = s.toString();
  return str ? '?' + str : '';
};
const enc = encodeURIComponent;

/** The URL segment of each kind of record. */
export const KIND_PATH: Record<RecordKind, string> = {
  application: 'applications',
  flow: 'flows',
  dataObject: 'data-objects',
  page: 'pages',
  domain: 'domains',
  team: 'teams',
};
const hist = (kind: RecordKind, id: string) => `/${KIND_PATH[kind]}/${enc(id)}/history`;

export interface AccessLogQuery {
  username?: string;
  kind?: string;
  entityId?: string;
  what?: string;
  from?: string;
  to?: string;
  cursor?: string;
  limit?: number;
}

export interface ProposalQuery {
  status?: string;
  mine?: boolean;
  toValidate?: boolean;
}

export interface AgentActivityQuery {
  connectionId?: string;
  kind?: AgentActivityKind;
  cursor?: string;
  limit?: number;
}

export const api = {
  login: (username: string, password: string) => request<Me>('POST', '/auth/login', { username, password }),
  logout: () => request<void>('POST', '/auth/logout'),
  me: () => request<Me>('GET', '/auth/me'),
  setLanguage: (language: Language) => request<void>('PUT', '/auth/me/language', { language }),

  landscape: () => request<Landscape>('GET', '/landscape'),
  search: (text: string) => request<SearchHit[]>('GET', '/search' + q({ q: text })),

  domains: () => request<Domain[]>('GET', '/domains'),
  teams: () => request<Team[]>('GET', '/teams'),
  dataObjects: () => request<DataObject[]>('GET', '/data-objects'),

  application: (id: string) => request<Application>('GET', `/applications/${enc(id)}`),
  createApplication: (body: ApplicationInput) => request<Application>('POST', '/applications', body),
  updateApplication: (id: string, body: ApplicationInput) =>
    request<Application>('PUT', `/applications/${enc(id)}`, body),
  deleteApplication: (id: string) => request<void>('DELETE', `/applications/${enc(id)}`),

  flow: (id: string) => request<Flow>('GET', `/flows/${enc(id)}`),
  createFlow: (body: FlowInput) => request<Flow>('POST', '/flows', body),
  updateFlow: (id: string, body: FlowInput) => request<Flow>('PUT', `/flows/${enc(id)}`, body),
  deleteFlow: (id: string) => request<void>('DELETE', `/flows/${enc(id)}`),

  domain: (id: string) => request<DomainDetail>('GET', `/domains/${enc(id)}`),
  dataObject: (id: string) => request<DataObjectDetail>('GET', `/data-objects/${enc(id)}`),

  pages: () => request<PageListItem[]>('GET', '/pages'),
  pageTree: () => request<PageTreeNode[]>('GET', '/pages/tree'),
  page: (id: string) => request<PageDetail | RestrictedPage>('GET', `/pages/${enc(id)}`),
  pageRaw: (id: string) => requestText(`/pages/${enc(id)}/raw`),
  createPage: (body: PageInput) => request<Page>('POST', '/pages', body),
  updatePage: (id: string, body: PageInput) => request<Page>('PUT', `/pages/${enc(id)}`, body),
  deletePage: (id: string) => request<void>('DELETE', `/pages/${enc(id)}`),
  movePage: (id: string, parentId: string | null, order: number, version?: number) =>
    request<Page>('POST', `/pages/${enc(id)}/move`, { parentId, order, version: version ?? null }),

  users: () => request<User[]>('GET', '/users'),
  createUser: (body: UserCreateInput) => request<User>('POST', '/users', body),
  updateUser: (username: string, body: UserUpdateInput) => request<User>('PUT', `/users/${enc(username)}`, body),
  deleteUser: (username: string) => request<void>('DELETE', `/users/${enc(username)}`),

  team: (id: string) => request<Team>('GET', `/teams/${enc(id)}`),
  teamMembers: (id: string) => request<TeamMember[]>('GET', `/teams/${enc(id)}/members`),
  setTeamMembers: (id: string, usernames: string[]) =>
    request<TeamMember[]>('PUT', `/teams/${enc(id)}/members`, { usernames }),

  proposals: (f: ProposalQuery = {}) =>
    request<Proposal[]>(
      'GET',
      '/proposals' +
        q({ status: f.status, mine: f.mine ? 'true' : undefined, toValidate: f.toValidate ? 'true' : undefined }),
    ),
  proposal: (id: string) => request<Proposal>('GET', `/proposals/${enc(id)}`),
  createProposal: (body: ProposalInput) => request<Proposal>('POST', '/proposals', body),
  updateProposal: (id: string, body: ProposalInput) => request<Proposal>('PUT', `/proposals/${enc(id)}`, body),
  submitProposal: (id: string) => request<Proposal>('POST', `/proposals/${enc(id)}/submit`, {}),
  withdrawProposal: (id: string) => request<Proposal>('POST', `/proposals/${enc(id)}/withdraw`, {}),
  acceptProposal: (id: string) => request<Proposal>('POST', `/proposals/${enc(id)}/accept`, {}),
  refuseProposal: (id: string, reason: string) => request<Proposal>('POST', `/proposals/${enc(id)}/refuse`, { reason }),
  rebaseProposal: (id: string) => request<Proposal>('POST', `/proposals/${enc(id)}/rebase`, {}),
  inbox: () => request<Inbox>('GET', '/me/inbox'),

  history: (kind: RecordKind, id: string) => request<HistoryEntry[]>('GET', hist(kind, id)),
  revision: (kind: RecordKind, id: string, version: number) => request<Revision>('GET', `${hist(kind, id)}/${version}`),
  compare: (kind: RecordKind, id: string, from: number, to: number) =>
    request<Compare>('GET', `${hist(kind, id)}/compare` + q({ from: String(from), to: String(to) })),
  restore: (kind: RecordKind, id: string, version: number) =>
    request<RestoreResult>('POST', `${hist(kind, id)}/${version}/restore`, {}),
  trash: () => request<TrashItem[]>('GET', '/trash'),
  restoreTrash: (kind: RecordKind, id: string) =>
    request<unknown>('POST', `/trash/${enc(kind)}/${enc(id)}/restore`, {}),

  settings: () => request<Settings>('GET', '/settings'),
  updateSettings: (body: Settings) => request<Settings>('PUT', '/settings', body),

  exportUrl: (format: 'json' | 'yaml', scope: 'visible' | 'full' = 'visible', reason?: string) =>
    '/api/export' + q({ format, scope: scope === 'full' ? 'full' : undefined, reason: reason?.trim() || undefined }),
  /** Downloads an export as a file; errors (403, 422 on the reason) come back as an ApiError. */
  exportFile: async (format: 'json' | 'yaml', scope: 'visible' | 'full', reason?: string) => {
    let res: Response;
    try {
      res = await fetch(api.exportUrl(format, scope, reason), {
        credentials: 'same-origin',
        headers: { 'X-Requested-With': 'fetch' },
      });
    } catch {
      throw new ApiError(0, 'network');
    }
    if (!res.ok) {
      const p = await readProblem(res);
      if (res.status === 401) onUnauthorized();
      const fieldErrors = p.errors && !Array.isArray(p.errors) ? p.errors : {};
      throw new ApiError(res.status, p.title ?? res.statusText, p.detail, fieldErrors, p);
    }
    const name = /filename="?([^";]+)"?/.exec(res.headers.get('Content-Disposition') ?? '')?.[1];
    return { blob: await res.blob(), name: name ?? `noosphere-landscape.${format}` };
  },

  recordAccess: (kind: RecordKind, id: string) => request<RecordAccess>('GET', `/${KIND_PATH[kind]}/${enc(id)}/access`),
  people: () => request<Person[]>('GET', '/people'),
  accessOverview: () => request<AccessOverview>('GET', '/access/overview'),
  accessLog: (f: AccessLogQuery = {}) =>
    request<AccessLogPage>(
      'GET',
      '/access-log' +
        q({
          username: f.username,
          kind: f.kind,
          entityId: f.entityId,
          what: f.what,
          from: f.from,
          to: f.to,
          cursor: f.cursor,
          limit: f.limit ? String(f.limit) : undefined,
        }),
    ),
  agentSettings: () => request<AgentSettings>('GET', '/settings/agents'),
  updateAgentSettings: (body: AgentSettingsInput) => request<AgentSettings>('PUT', '/settings/agents', body),
  myAgents: () => request<MyAgents>('GET', '/me/agents'),
  agents: () => request<AgentConnection[]>('GET', '/agents'),
  revokeAgent: (id: string) => request<AgentConnection>('POST', `/agents/${enc(id)}/revoke`, {}),
  approveAgent: (id: string) => request<AgentConnection>('POST', `/agents/${enc(id)}/approve`, {}),
  agentCapabilities: (id: string) => request<AgentCapabilities>('GET', `/agents/${enc(id)}/capabilities`),
  agentActivity: (f: AgentActivityQuery = {}) =>
    request<AgentActivityPage>(
      'GET',
      '/agents/activity' +
        q({
          connectionId: f.connectionId,
          kind: f.kind,
          cursor: f.cursor,
          limit: f.limit ? String(f.limit) : undefined,
        }),
    ),
  stopAllAgents: () => request<StopAllResult>('POST', '/agents/stop-all', { confirm: true }),

  consentRequest: (id: string) => request<ConsentRequest>('GET', `/oauth/requests/${enc(id)}`),
  approveConsent: (id: string, scopes?: string[]) =>
    request<ConsentResult>('POST', `/oauth/requests/${enc(id)}/approve`, { scopes: scopes ?? null }),
  denyConsent: (id: string) => request<ConsentResult>('POST', `/oauth/requests/${enc(id)}/deny`, {}),

  demoState: () => request<DemoState>('GET', '/demo/state'),
  demoSwitch: (username: string) => request<DemoSwitchResult>('POST', '/demo/switch', { username }),
  demoEdit: (kind: string, id: string) => request<DemoEditResult>('POST', '/demo/simulate/edit', { kind, id }),
  demoProposal: (kind?: string, id?: string) =>
    request<DemoProposalResult>('POST', '/demo/simulate/proposal', { kind: kind ?? null, id: id ?? null }),
  demoDecision: (proposalId: string, decision: 'accept' | 'refuse') =>
    request<DemoDecisionResult>('POST', '/demo/simulate/decision', { proposalId, decision }),
  demoAgent: (scenario: DemoAgentScenario) => request<DemoAgentResult>('POST', '/demo/simulate/agent', { scenario }),
  demoGuestRead: (kind: string, id: string) =>
    request<DemoGuestReadResult>('POST', '/demo/simulate/guest-read', { kind, id }),
  demoReset: () => request<DemoResetResult>('POST', '/demo/reset', { confirm: true }),

  importEnvelope: (envelope: ExchangeEnvelope | string, dryRun: boolean, contentType = 'application/json') =>
    request<ImportResult>('POST', '/import' + q({ dryRun: String(dryRun) }), undefined, {
      headers: { 'Content-Type': contentType },
      body: typeof envelope === 'string' ? envelope : JSON.stringify(envelope),
    }),
};
