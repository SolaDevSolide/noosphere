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
/** What a guest receives for an application not shared with them: its name and place only. */
export type GuestApplication = S['GuestApplicationDto'];
/** A record the reader does not see in full: a restricted stub, or a guest outline. */
export type HiddenApplication = RestrictedApplication | GuestApplication;
export type LandscapeApplication = Application | HiddenApplication;
export type DataObject = S['DataObjectDto'];
export type PlannedChange = S['PlannedChangeDto'];
export type Flow = S['FlowDto'];
/** A flow touching a restricted application, as seen by a user without access. */
export type RestrictedFlow = S['RestrictedFlowDto'];
export type GuestFlow = S['GuestFlowDto'];
export type HiddenFlow = RestrictedFlow | GuestFlow;
export type LandscapeFlow = Flow | HiddenFlow;
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

/** True for a record the reader does not see in full (restricted, or a guest outline). */
export const isRestrictedApp = (a: LandscapeApplication): a is HiddenApplication => a.restricted || 'shared' in a;
export const isRestrictedFlow = (f: LandscapeFlow): f is HiddenFlow => f.restricted || 'shared' in f;

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

/* ---- Documentation pages ---- */

export type AttachedTo = S['AttachedToDto'];
/** The kinds of record a page can be attached to. */
export type AttachKind = Exclude<RecordKind, 'page'>;
export type PageRef = S['PageRefDto'];
export type PageLink = S['PageLinkDto'];
export type Page = S['PageDto'];
export type PageInput = S['PageInput'];
export type PageSummary = S['PageSummaryDto'];
export type RestrictedPage = S['RestrictedPageDto'];
export type PageListItem = PageSummary | RestrictedPage;
export type PageTreeNode = S['PageTreeNodeDto'];
/** A page with its resolved links, backlinks, place in the tree, owner and the reader's permissions. */
export type PageDetail = Page &
  Pick<
    S['PageDetailDto'],
    'links' | 'backlinks' | 'ancestors' | 'children' | 'attachedTitle' | 'owningTeamId' | 'permissions'
  >;
export type DomainDetail = S['DomainDetailDto'];
export type DataObjectDetail = S['DataObjectDetailDto'];

export const isRestrictedPage = (p: PageListItem | PageDetail | RestrictedPage): p is RestrictedPage =>
  p.restricted === true;

/* ---- Milestone 3: access lists, protected notes, sensitivity, access log ---- */

export type AccessList = S['AccessListDto'];
export type SensitivityReason = S['SensitivityReasonDto'];
/** "Who can see and edit?" for one record, as explained by the server. */
export type RecordAccess = S['RecordAccessDto'];
export type AccessTeam = S['AccessTeamDto'];
export type AccessLogEntry = S['AccessLogEntryDto'];
export type AccessLogPage = S['AccessLogPageDto'];
export type AccessLogWhat = S['AccessLogWhat'];
export type AccessLogWhy = NonNullable<S['AccessLogWhy']>;
/** Someone who can be named on an access list. */
export type Person = S['PersonDto'];
export type AccessOverview = S['AccessOverviewDto'];
export type AccessRecord = S['AccessRecordDto'];
export type GuestPage = S['GuestPageDto'];

/** Extra members of an application or flow detail: its protected notes, or the fact that they are hidden. */
export interface NotesExtras {
  protectedNotes?: string | null;
  protectedNotesHidden?: boolean;
}

/** What `protectedNotes` holds in history, compares and proposals for readers who may not read them. */
export const PROTECTED_MASK = '••• protected';

/** True for a guest's outline of a record not shared with them. */
export const isGuestOutline = (x: object): boolean => 'shared' in x && (x as { shared?: boolean }).shared === false;
