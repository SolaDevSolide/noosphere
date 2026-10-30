// Names used across the app for the Noosphere v1 API types. They alias the schema generated from
// docs/openapi.json (`npm run api:types` writes src/api/schema.d.ts); only a few are narrowed here.
import type { components } from './schema';

type S = components['schemas'];

export type Role = S['Role'];
/** The API sends a free string; the application only knows these two. */
export type Language = 'en' | 'fr';
export type AppKind = S['ApplicationKind'];
export type Lifecycle = S['Lifecycle'];
export type Visibility = S['Visibility'];
export type Sensitivity = S['Sensitivity'];
export type FlowType = S['FlowType'];
export type FlowStatus = S['FlowStatus'];

export const APP_KINDS: AppKind[] = ['app', 'bus', 'saas', 'data', 'file', 'external'];
export const LIFECYCLES: Lifecycle[] = ['planned', 'live', 'retiring', 'retired'];
export const VISIBILITIES: Visibility[] = ['everyone', 'restricted'];
export const SENSITIVITIES: Sensitivity[] = ['public', 'internal', 'personal', 'sensitive'];
export const FLOW_TYPES: FlowType[] = ['api', 'event', 'batch'];
export const FLOW_STATUSES: FlowStatus[] = ['live', 'planned', 'deprecated'];
export const ROLES: Role[] = ['admin', 'member', 'guest'];

export type Domain = S['DomainDto'];
export type Team = S['TeamDto'];
export type Application = S['ApplicationDto'];
/** What a user without access receives for a restricted application. */
export type RestrictedApplication = S['RestrictedApplicationDto'];
export type LandscapeApplication = Application | RestrictedApplication;
export type DataObject = S['DataObjectDto'];
export type PlannedChange = S['PlannedChangeDto'];
export type Flow = S['FlowDto'];
/** A flow touching a restricted application, as seen by a user without access. */
export type RestrictedFlow = S['RestrictedFlowDto'];
export type LandscapeFlow = Flow | RestrictedFlow;
export type Landscape = S['LandscapeDto'];

export type Me = Omit<S['MeDto'], 'language'> & { language: Language };
export type User = Omit<S['UserDto'], 'language'> & { language: Language };
export type UserCreateInput = S['UserCreateInput'];
export type UserUpdateInput = S['UserUpdateInput'];

export type SearchKind = S['SearchKind'];
export type SearchHit = S['SearchResultDto'];
export type ExchangeEnvelope = S['ExchangeEnvelopeDto'];
export type ImportError = S['ImportErrorDto'];
export type ImportResult = S['ImportResultDto'];
export type Problem = S['ProblemDto'];

/** Bodies sent by the edit forms (server-computed fields left out). */
export type ApplicationInput = S['ApplicationInput'];
export type FlowInput = S['FlowInput'];

export const isRestrictedApp = (a: LandscapeApplication): a is RestrictedApplication => a.restricted;
export const isRestrictedFlow = (f: LandscapeFlow): f is RestrictedFlow => f.restricted;

/* ---- Milestone 2: teams, proposals, history, settings ----
 * Written against the shared contract while the generated schema catches up; the fields an older server may leave out
 * are optional so the screens keep working. */

export interface TeamRef {
  id: string;
  name: string;
}
/** A person as the API names them in proposals and history. */
export interface PersonRef {
  username: string;
  displayName: string;
}
/** What the current user may do on one record: publish directly, or send a proposal. */
export interface Permissions {
  publish: boolean;
  propose: boolean;
}
/** Extra members of a record's detail answer. */
export interface RecordExtras {
  /** The resolved owner team; null means administrators only. */
  owningTeamId?: string | null;
  permissions?: Permissions;
  /** Documentation pages attached to the record (shown in a later milestone). */
  pages?: { id: string; title: string }[];
  mentionedIn?: { id: string; title: string }[];
}
export type MeM2 = Me & { teams?: TeamRef[] };
export type UserM2 = User & { teamIds?: string[] };
export interface TeamMember extends PersonRef {
  role?: Role;
}

export type ChangeKind = 'application' | 'flow' | 'dataObject' | 'page';
export type ChangeOperation = 'create' | 'update' | 'delete';
export type ProposalStatus = 'draft' | 'submitted' | 'accepted' | 'refused' | 'withdrawn';
export type AuthorKind = 'user' | 'agent';
export type DiffKind = 'text' | 'markdown' | 'list' | 'ref';

/** One changed field, as computed by the server. */
export interface FieldDiff {
  field: string;
  label: string;
  before: unknown;
  after: unknown;
  kind: DiffKind;
}
export interface ProposalChange {
  kind: ChangeKind;
  targetId: string | null;
  targetTitle?: string | null;
  operation: ChangeOperation;
  baseVersion: number | null;
  /** The target's current version; differs from baseVersion when the change is outdated. */
  currentVersion?: number | null;
  outdated?: boolean;
  before?: Record<string, unknown> | null;
  after?: Record<string, unknown> | null;
  /** Fields changed both here and in the latest version, after an update from the latest version. */
  conflicts?: string[];
  diff?: FieldDiff[];
}
export interface OwnerApproval {
  id: string;
  name: string;
  approvedBy?: PersonRef | null;
  approvedAt?: string | null;
}
export interface Proposal {
  id: string;
  title: string;
  comment?: string | null;
  status: ProposalStatus;
  authorKind: AuthorKind;
  author: PersonRef | null;
  onBehalfOf?: string | null;
  createdAt: string;
  submittedAt?: string | null;
  decidedBy?: PersonRef | null;
  decidedAt?: string | null;
  refusalReason?: string | null;
  version: number;
  outdated?: boolean;
  ownerTeams?: OwnerApproval[];
  requiresAdmin?: boolean;
  /** The current user may accept or refuse it. */
  canDecide?: boolean;
  /** The current user (its author) may change, withdraw or update it. */
  canEdit?: boolean;
  changes: ProposalChange[];
}
export interface ProposalChangeInput {
  kind: ChangeKind;
  targetId: string | null;
  operation: ChangeOperation;
  baseVersion: number | null;
  after: Record<string, unknown> | null;
}
export interface ProposalInput {
  title: string;
  comment?: string | null;
  changes: ProposalChangeInput[];
  submit?: boolean;
  version?: number;
}
export interface Inbox {
  toValidate: number;
  mineOpen: number;
}

export type HistoryOperation = 'create' | 'update' | 'delete' | 'restore';
export interface HistoryEntry {
  version: number;
  operation?: HistoryOperation;
  author: PersonRef | null;
  authorKind?: AuthorKind;
  createdAt: string;
  summary?: string | null;
  proposalId?: string | null;
}
export interface Revision extends HistoryEntry {
  snapshot: Record<string, unknown>;
}
/** A restore publishes for the owners, and creates a proposal for anyone else. */
export interface RestoreResult {
  outcome: 'published' | 'proposed';
  version?: number | null;
  proposalId?: string | null;
}
export type TrashKind = ChangeKind | 'domain' | 'team';
export interface TrashItem {
  kind: TrashKind;
  entityId: string;
  title: string;
  deletedAt: string;
  deletedBy?: PersonRef | null;
  version: number;
}
export interface Settings {
  instanceName: string;
  organisationName: string;
  defaultLanguage: Language;
  version?: number;
}
