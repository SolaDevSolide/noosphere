import type {
  Application,
  ApplicationInput,
  DataObject,
  Domain,
  ExchangeEnvelope,
  ChangeKind,
  FieldDiff,
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

const q = (params: Record<string, string | undefined>) => {
  const s = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v) s.set(k, v);
  const str = s.toString();
  return str ? '?' + str : '';
};
const enc = encodeURIComponent;

/** The URL segment of each kind of record. */
export const KIND_PATH: Record<ChangeKind, string> = {
  application: 'applications',
  flow: 'flows',
  dataObject: 'data-objects',
  page: 'pages',
};
const hist = (kind: ChangeKind, id: string) => `/${KIND_PATH[kind]}/${enc(id)}/history`;

export interface ProposalQuery {
  status?: string;
  mine?: boolean;
  toValidate?: boolean;
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

  history: (kind: ChangeKind, id: string) => request<HistoryEntry[]>('GET', hist(kind, id)),
  revision: (kind: ChangeKind, id: string, version: number) => request<Revision>('GET', `${hist(kind, id)}/${version}`),
  compare: (kind: ChangeKind, id: string, from: number, to: number) =>
    request<FieldDiff[]>('GET', `${hist(kind, id)}/compare` + q({ from: String(from), to: String(to) })),
  restore: (kind: ChangeKind, id: string, version: number) =>
    request<RestoreResult>('POST', `${hist(kind, id)}/${version}/restore`, {}),
  trash: () => request<TrashItem[]>('GET', '/trash'),
  restoreTrash: (kind: string, id: string) => request<void>('POST', `/trash/${enc(kind)}/${enc(id)}/restore`, {}),

  settings: () => request<Settings>('GET', '/settings'),
  updateSettings: (body: Settings) => request<Settings>('PUT', '/settings', body),

  exportUrl: (format: 'json' | 'yaml') => `/api/export?format=${format}`,
  importEnvelope: (envelope: ExchangeEnvelope | string, dryRun: boolean, contentType = 'application/json') =>
    request<ImportResult>('POST', '/import' + q({ dryRun: String(dryRun) }), undefined, {
      headers: { 'Content-Type': contentType },
      body: typeof envelope === 'string' ? envelope : JSON.stringify(envelope),
    }),
};
