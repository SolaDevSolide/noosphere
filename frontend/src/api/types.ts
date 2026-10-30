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
