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

/* ---- Milestone 2: teams, proposals, history, settings ---- */

export type TeamRef = S['TeamRefDto'];
/** A person as the API names them in proposals and history. */
export type PersonRef = S['UserRefDto'];
/** What the current user may do on one record: publish directly, or send a proposal. */
export type Permissions = S['PermissionsDto'];
/** Extra members of a record's detail answer (owner, permissions, attached pages). */
export type RecordExtras = Pick<S['ApplicationDetailDto'], 'owningTeamId' | 'permissions' | 'pages' | 'mentionedIn'>;
export type MeM2 = Me;
export type UserM2 = User;
export type TeamMember = S['TeamMemberDto'];

export type RecordKind = S['RecordKind'];
/** The kinds a proposal can change. */
export type ChangeKind = Extract<RecordKind, 'application' | 'flow' | 'dataObject' | 'page'>;
export type ChangeOperation = S['ChangeOperation'];
export type ProposalStatus = S['ProposalStatus'];
export type AuthorKind = S['AuthorKind'];
export type DiffKind = S['DiffKind'];

/** One changed field, as computed by the server; references carry the record names too. */
export type FieldDiff = Omit<S['DiffEntryDto'], 'beforeTitle' | 'afterTitle' | 'before' | 'after'> & {
  before: unknown;
  after: unknown;
  beforeTitle?: string | null;
  afterTitle?: string | null;
};
export type ProposalChange = Omit<S['ProposalChangeDto'], 'diff' | 'before' | 'after'> & {
  diff: FieldDiff[];
  before: Record<string, unknown> | null;
  after: Record<string, unknown> | null;
};
export type OwnerApproval = S['ProposalOwnerDto'];
export type Proposal = Omit<S['ProposalDto'], 'changes'> & { changes: ProposalChange[] };
export type ProposalChangeInput = Omit<S['ProposalChangeInput'], 'after'> & {
  after: Record<string, unknown> | null;
};
export interface ProposalInput {
  title: string;
  comment?: string | null;
  changes: ProposalChangeInput[];
  submit?: boolean;
  version?: number;
}
export type Inbox = S['InboxDto'];

export type HistoryOperation = S['RevisionOperation'];
export type HistoryEntry = S['HistoryEntryDto'];
export type Revision = Omit<S['RevisionDto'], 'snapshot'> & { snapshot: Record<string, unknown> };
export type Compare = Omit<S['CompareDto'], 'diff'> & { diff: FieldDiff[] };
/** A restore publishes for the owners, and creates a proposal for anyone else. */
export type RestoreResult = Omit<S['RestoreResultDto'], 'outcome'> & { outcome: 'published' | 'proposed' };
export type TrashKind = RecordKind;
export type TrashItem = S['TrashItemDto'];
export type Settings = Omit<S['SettingsDto'], 'defaultLanguage' | 'updatedAt'> & {
  defaultLanguage: Language;
  updatedAt?: string | null;
};
