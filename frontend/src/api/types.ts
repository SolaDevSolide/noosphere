// Types of the Noosphere v1 API, written by hand from the shared contract.
// Once docs/openapi.json is committed, `npm run api:types` generates src/api/schema.d.ts and these aliases
// should point at the generated schema instead.

export type Role = 'admin' | 'member' | 'guest';
export type Language = 'en' | 'fr';
export type AppKind = 'app' | 'bus' | 'saas' | 'data' | 'file' | 'external';
export type Lifecycle = 'planned' | 'live' | 'retiring' | 'retired';
export type Visibility = 'everyone' | 'restricted';
export type Sensitivity = 'public' | 'internal' | 'personal' | 'sensitive';
export type FlowType = 'api' | 'event' | 'batch';
export type FlowStatus = 'planned' | 'live' | 'deprecated';

export const APP_KINDS: AppKind[] = ['app', 'bus', 'saas', 'data', 'file', 'external'];
export const LIFECYCLES: Lifecycle[] = ['planned', 'live', 'retiring', 'retired'];
export const VISIBILITIES: Visibility[] = ['everyone', 'restricted'];
export const SENSITIVITIES: Sensitivity[] = ['public', 'internal', 'personal', 'sensitive'];
export const FLOW_TYPES: FlowType[] = ['api', 'event', 'batch'];
export const FLOW_STATUSES: FlowStatus[] = ['live', 'planned', 'deprecated'];
export const ROLES: Role[] = ['admin', 'member', 'guest'];

interface Tracked {
  createdAt: string;
  updatedAt: string;
  /** Optimistic concurrency token: a PUT with a stale version returns 409. */
  version: number;
}

export interface Domain extends Tracked {
  id: string;
  name: string;
  description?: string | null;
  order: number;
}

export interface Team extends Tracked {
  id: string;
  name: string;
  description?: string | null;
  contact?: string | null;
}

export interface Application extends Tracked {
  id: string;
  name: string;
  domainId: string;
  teamId?: string | null;
  kind: AppKind;
  tech?: string | null;
  description?: string | null;
  lifecycle: Lifecycle;
  visibility: Visibility;
  sourceOf: string[];
  /** Computed by the server, 0..1. */
  completeness: number;
  restricted?: false;
}

/** What a user without access receives for a restricted application. */
export interface RestrictedApplication {
  id: string;
  domainId: string;
  restricted: true;
}

export type LandscapeApplication = Application | RestrictedApplication;

export interface DataObject extends Tracked {
  id: string;
  name: string;
  sensitivity: Sensitivity;
  sourceApplicationId?: string | null;
  description?: string | null;
}

export interface PlannedChange {
  summary: string;
  date?: string | null;
}

export interface Flow extends Tracked {
  id: string;
  name: string;
  fromId: string;
  toId: string;
  type: FlowType;
  schedule?: string | null;
  dataObjectIds: string[];
  /** Computed by the server: the highest sensitivity of the carried objects. */
  sensitivity: Sensitivity;
  ownerTeamId?: string | null;
  status: FlowStatus;
  volume?: string | null;
  description?: string | null;
  plannedChange?: PlannedChange | null;
  completeness?: number;
  restricted?: false;
}

/** A flow touching a restricted application, as seen by a user without access. */
export interface RestrictedFlow {
  id: string;
  fromId: string;
  toId: string;
  type: FlowType;
  restricted: true;
}

export type LandscapeFlow = Flow | RestrictedFlow;

export interface Landscape {
  domains: Domain[];
  teams: Team[];
  applications: LandscapeApplication[];
  dataObjects: DataObject[];
  flows: LandscapeFlow[];
  stats: { applications: number; flows: number; domains: number };
  generatedAt: string;
}

export interface Me {
  username: string;
  displayName: string;
  role: Role;
  language: Language;
}

export interface User {
  username: string;
  displayName: string;
  role: Role;
  language: Language;
  createdAt?: string;
  updatedAt?: string;
  version?: number;
}

export interface UserInput {
  username: string;
  displayName: string;
  role: Role;
  language: Language;
  password?: string;
  version?: number;
}

export type SearchKind = 'application' | 'flow' | 'dataObject' | 'team' | 'domain';
export interface SearchHit {
  kind: SearchKind;
  id: string;
  title: string;
  subtitle: string;
}

export type ExchangeKind = 'Domain' | 'Team' | 'Application' | 'DataObject' | 'Flow';
export interface ExchangeItem {
  kind: ExchangeKind;
  id: string;
  metadata: { name: string; description?: string | null; [key: string]: unknown };
  spec: Record<string, unknown>;
}
export interface ExchangeEnvelope {
  formatVersion: 'noosphere/v1';
  source: { name: string; url: string; exportedAt: string };
  mode: 'full';
  items: ExchangeItem[];
}
export interface ImportError {
  id?: string;
  kind?: string;
  path?: string;
  message: string;
}
export interface ImportResult {
  created: number;
  updated: number;
  unchanged: number;
  errors: ImportError[];
}

/** Bodies sent by the edit forms (server-computed fields left out). */
export type ApplicationInput = Pick<
  Application,
  'name' | 'domainId' | 'teamId' | 'kind' | 'tech' | 'description' | 'lifecycle' | 'visibility' | 'sourceOf'
> & { id?: string; version?: number };

export type FlowInput = Pick<
  Flow,
  | 'name'
  | 'fromId'
  | 'toId'
  | 'type'
  | 'schedule'
  | 'dataObjectIds'
  | 'ownerTeamId'
  | 'status'
  | 'volume'
  | 'description'
  | 'plannedChange'
> & { id?: string; version?: number };

export const isRestrictedApp = (a: LandscapeApplication): a is RestrictedApplication => a.restricted === true;
export const isRestrictedFlow = (f: LandscapeFlow): f is RestrictedFlow => f.restricted === true;
