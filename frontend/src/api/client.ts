import type {
  Application,
  ApplicationInput,
  DataObject,
  Domain,
  ExchangeEnvelope,
  Flow,
  FlowInput,
  ImportResult,
  Landscape,
  Language,
  Me,
  SearchHit,
  Team,
  User,
  UserInput,
} from './types';

/** An error answered by the API (problem+json), or a network failure (status 0). */
export class ApiError extends Error {
  status: number;
  detail?: string;
  /** Validation messages by field, from the problem's `errors` member. */
  fieldErrors: Record<string, string[]>;
  constructor(status: number, message: string, detail?: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.detail = detail;
    this.fieldErrors = fieldErrors;
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
  errors?: Record<string, string[]>;
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
    throw new ApiError(res.status, p.title ?? res.statusText, p.detail, p.errors ?? {});
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
  createUser: (body: UserInput) => request<User>('POST', '/users', body),
  updateUser: (username: string, body: UserInput) => request<User>('PUT', `/users/${enc(username)}`, body),
  deleteUser: (username: string) => request<void>('DELETE', `/users/${enc(username)}`),

  exportUrl: (format: 'json' | 'yaml') => `/api/export?format=${format}`,
  importEnvelope: (envelope: ExchangeEnvelope | string, dryRun: boolean, contentType = 'application/json') =>
    request<ImportResult>('POST', '/import' + q({ dryRun: dryRun ? 'true' : undefined }), undefined, {
      headers: { 'Content-Type': contentType },
      body: typeof envelope === 'string' ? envelope : JSON.stringify(envelope),
    }),
};
