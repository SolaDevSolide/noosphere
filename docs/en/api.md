# API

[Français](../fr/api.md)

JSON over HTTP, under `/api`, with camelCase keys and enums written as lower-camelCase strings (`saas`,
`dataObject`). The OpenAPI 3.1 description is served at `/openapi/v1.json` and committed as
[docs/openapi.json](../openapi.json).

## Conventions

- **Session**: `POST /api/auth/login` sets an HttpOnly cookie, `noosphere.auth`. Every other `/api` route needs it
  (401 otherwise).
- **Anti-CSRF header**: every request other than GET, HEAD and OPTIONS must send `X-Requested-With` (any value,
  for example `fetch`), or it gets 403.
- **Errors**: `application/problem+json`, as `{type, title, status, detail, errors?}`. On 400, `errors` maps each
  invalid field to its messages: `{"errors": {"name": ["This field is required."]}}`.
- **Ids**: stable kebab-case slugs, unique per kind (`crm-to-billing-customers`). On create, a missing `id` is derived
  from the name and made unique (`customer-portal`, `customer-portal-2`). Ids never change.
- **Versions**: every record has `version`, `createdAt` and `updatedAt`. `PUT` replaces the whole record (a missing
  optional field is cleared, except `access` and `protectedNotes`, which are kept when left out) and must carry the
  `version` that was read. A stale version returns **409**: "Someone
  changed this record. Reload it and try again."

| Status | Meaning |
| ------ | ------- |
| 400 | Invalid input (`errors` per field) |
| 401 | Not signed in, or wrong credentials |
| 403 | Role not allowed, not in the owning team (send a proposal), restricted record or record not shared with a guest, missing `X-Requested-With` |
| 404 | No such record |
| 409 | Stale version, id already used, record still in use (delete), outdated proposal (`outdated: true`) |
| 422 | A field looks like a secret (see [access](access.md#secrets-are-refused)), or an import file with errors (body: the import result) |
| 429 | Too many sign-in attempts, or client registrations |

## Authentication and accounts

| Method and path | Who | Body, answer |
| --------------- | --- | ------------ |
| `POST /api/auth/login` | anyone | `{username, password}`, then `{username, displayName, role, language, teams}` |
| `POST /api/auth/logout` | anyone | 204 |
| `GET /api/auth/me` | signed in | `{username, displayName, role, language, teams: [{id, name}]}` |
| `PUT /api/auth/me/language` | signed in | `{language: "en" or "fr"}` |
| `PUT /api/auth/me/password` | signed in | `{currentPassword, newPassword}`, 204; closes the account's other sessions |
| `GET /api/users`, `GET /api/users/{username}` | admin | `UserDto`, with `teamIds` |
| `POST /api/users` | admin | `{username, displayName, role, language?, password, teamIds?}`, 201 |
| `PUT /api/users/{username}` | admin | `{displayName, role, language, password?, teamIds?, version}`; `teamIds` left out keeps the teams |
| `GET /api/teams/{id}/members` | signed in | `[{username, displayName, role}]` |
| `PUT /api/teams/{id}/members` | admin | `{usernames: []}` replaces the members |
| `DELETE /api/users/{username}` | admin | 204; refused for your own account and for the last admin |

Roles: `admin`, `member` and `guest` (see [configuration](configuration.md#roles)).

## Landscape records

`GET/POST /api/domains`, `GET/PUT/DELETE /api/domains/{id}`, and the same for `teams`, `applications`,
`data-objects` and `flows`. Everyone signed in reads. Administrators and members of the owning team create, edit and
delete; other members send a [proposal](proposals.md) instead (a direct write returns 403). Domains and teams are
changed by administrators only. Every change creates a [revision](history.md). A record that others still refer to
cannot be deleted (409).

`GET /api/{kind}/{id}` also returns, for the reader, `owningTeamId` (null: administrators only), `permissions:
{publish, propose}`, `pages` (attached pages) and `mentionedIn` (pages linking to the record). Applications and flows
also return their [deployments](#deployments). A team also returns its
`members`.

| Kind | Fields (input) | Computed (output) |
| ---- | -------------- | ----------------- |
| Domain | `name`, `description?`, `order` | |
| Team | `name`, `description?`, `contact?` | |
| Application | `name`, `domainId`, `teamId?`, `kind` (`app`, `bus`, `saas`, `data`, `file`, `external`), `tech?`, `description?` (markdown), `lifecycle` (`planned`, `live`, `retiring`, `retired`), `visibility` (`everyone`, `restricted`), `access?` (`{teamIds, usernames}`), `protectedNotes?` | `sourceOf` (data objects whose source it is), `completeness` (0 to 1), `sensitivity` and `sensitivityReasons`, `restricted: false` |
| Data object | `name`, `sensitivity` (`public`, `internal`, `personal`, `sensitive`), `sourceApplicationId?`, `description?` | |
| Flow | `name`, `fromId`, `toId`, `type` (`api`, `event`, `batch`), `schedule?`, `dataObjectIds[]`, `ownerTeamId?`, `status` (`planned`, `live`, `deprecated`), `volume?`, `description?`, `plannedChange?` (`{summary, date?}`), `protectedNotes?` | `sensitivity` (most sensitive carried object, `internal` when none), `completeness`, `restricted: false` |

Completeness: the share of recommended fields that are filled. For an application: team, tech, description, at
least one flow, lifecycle. For a flow: schedule, data objects, owner, description.

List filters, all optional: `?domainId=&teamId=&type=&status=&dataObjectId=&sensitivity=&q=`. `q` is accent- and
case-insensitive.

### Restricted records, guests and protected notes

See [access](access.md). A reader who may not see a restricted application gets `{id, restricted: true, domainId}`,
and the flows they may not see as `{id, restricted: true, fromId, toId, type}`. A guest gets an outline of what is
not shared with them (`restricted: false, shared: false`, the name and place only). Filters and search never match
hidden fields. In the OpenAPI document, lists hold `ApplicationDto | RestrictedApplicationDto | GuestApplicationDto`
(and the same for flows and pages), told apart by `restricted` and `shared`.

Application and flow details carry `protectedNotes` (null when the reader may not read them) and
`protectedNotesHidden`.

| Method and path | Who | Answer |
| --------------- | --- | ------ |
| `GET /api/{kind}/{id}/access` | who sees the record in full | `{visibility, owningTeam, readers, publishers, proposers, protectedNotes, canManage}` |
| `GET /api/access-log?username=&kind=&entityId=&what=&from=&to=&cursor=&limit=` | admin | `{items, nextCursor}`, newest first |
| `GET /api/people` | member, admin | `[{username, displayName, role, teamIds}]`: who can be named on an access list |
| `GET /api/access/overview` | admin | the Access page: `{roles, restricted, protectedNotes, guests, teams}` |

## Explorer

- `GET /api/landscape` returns everything the explorer needs in one response, with the same filters:
  `{domains[], teams[], applications[], dataObjects[], flows[], stats: {applications, flows, domains}, generatedAt}`.
  Application filters (`domainId`, `teamId`, `q`) keep the matching applications and the flows touching them. Flow
  filters (`type`, `status`, `dataObjectId`) keep the matching flows and only the applications at their ends. A flow
  always comes with both of its ends.
- `GET /api/search?q=` returns at most 20 `{kind, id, title, subtitle}` items, where `kind` is `application`, `flow`,
  `dataObject`, `team`, `domain` or `page`, plus `sensitivity` for applications. Names starting with the text come first. For a page, a match in the body
  counts too, and the subtitle is an excerpt around it.

## Proposals

See [proposals and validation](proposals.md).

| Method and path | Who | Description |
| --------------- | --- | ----------- |
| `GET /api/proposals?status=&mine=true&toValidate=true` | signed in | Proposals the reader may see (drafts: their author only), newest first |
| `GET /api/proposals/{id}` | signed in | One proposal, with each change's snapshots and field `diff` |
| `POST /api/proposals` | member, admin | `{title, comment?, changes: [{kind, targetId?, operation, baseVersion?, after?}], submit?}`, 201 |
| `PUT /api/proposals/{id}` | author | Same body plus `version`, while draft or submitted |
| `POST /api/proposals/{id}/submit`, `/withdraw` | author | |
| `POST /api/proposals/{id}/accept` | validator | 409 with `outdated: true` when a target changed |
| `POST /api/proposals/{id}/refuse` | validator | `{reason}`, 3 to 500 characters |
| `POST /api/proposals/{id}/rebase` | author | Moves the changes onto the latest versions |
| `GET /api/me/inbox` | signed in | `{toValidate, mineOpen}` |

## History and trash

See [history](history.md): `GET /api/{kind}/{id}/history`, `.../history/{version}`, `.../history/compare?from=&to=`,
`POST .../history/{version}/restore`, and for administrators `GET /api/trash` and
`POST /api/trash/{kind}/{id}/restore`.

## Pages

See [pages](pages.md): `GET/POST /api/pages`, `GET /api/pages/tree`, `GET/PUT/DELETE /api/pages/{id}`,
`GET /api/pages/{id}/raw` and `POST /api/pages/{id}/move`.

## Settings

| Method and path | Who | Body, answer |
| --------------- | --- | ------------ |
| `GET /api/settings` | anyone, even signed out | `{instanceName, organisationName, defaultLanguage, version, updatedAt}` |
| `PUT /api/settings` | admin | `{instanceName, organisationName?, defaultLanguage?, version}` (`version` 0 before the first save) |

The header shows `instanceName`, with `organisationName` under it, and the browser tab uses `instanceName`. New
accounts get `defaultLanguage`. The technical name `noosphere` (command, image, cookie) never changes.

## AI agents

See [AI agents (MCP)](agents.md) for the MCP server at `/mcp`, the OAuth endpoints (`/.well-known/*`, `/oauth/*`),
`/llms.txt`, `/llms-full.txt` and the Markdown views, which live outside `/api`.

| Method and path | Who | Body, answer |
| --------------- | --- | ------------ |
| `GET /api/settings/agents` | signed in | `{enabled, ceiling, quotaPerHour, whoConnects, dynamicRegistration, anonymousLlms, mcpUrl, version, updatedAt}` |
| `PUT /api/settings/agents` | admin | The same fields (each optional) plus `version` |
| `GET /api/me/agents` | signed in | `{connections, drafts, proposals, settings}`: my connections, my agents' drafts and other proposals |
| `GET /api/agents` | admin | Every connection: `{id, clientId, clientName, clientUri, person, scopes, state, createdAt, lastUsedAt, revokedAt, pausedUntil, approvedAt, callsLastHour, openDrafts, lastActivity, demo}` |
| `POST /api/agents/{id}/revoke` | admin, or the person | Revokes the connection and its tokens at once |
| `POST /api/agents/{id}/approve` | admin | A connection waiting for approval (409 otherwise) |
| `GET /api/agents/{id}/capabilities` | admin, or the person | `{connectionId, clientName, person, ceiling, scopes, items: [{key, allowed, text: {en, fr}}]}` for "What this agent can do" |
| `GET /api/agents/activity?connectionId=&kind=&cursor=&limit=` | signed in | `{items, nextCursor}`, newest first; mine only, every connection for an admin. `kind`: `read`, `draft`, `proposal`, `refused`, `quota`, `revoked` |
| `POST /api/agents/stop-all` | admin | `{confirm: true}`, then `{revokedConnections, revokedTokens, at}` |
| `GET /api/oauth/requests/{id}`, `POST .../approve`, `POST .../deny` | signed in | The consent page: see [OAuth reference](agents.md#oauth-reference) |

An activity line is `{id, at, connectionId, agentName, person, tool, kind, summary, recordKind, recordId,
recordTitle}`. Proposals written by an agent have `authorKind: "agent"`, `onBehalfOf` (the person, `{username,
displayName}`), `agentName` and `agentConnectionId`.

## Links with gestion-mep

Administrators only. See [links with gestion-mep](links.md) for the protocol under `/link/v1`, which lives outside
`/api` and is not in the OpenAPI document.

| Method and path | Body, answer |
| --------------- | ------------ |
| `GET /api/links/codes` | Pairing codes: `{id, codeId, state, capabilities, createdAt, expiresAt, createdBy, usedAt, linkId, failedAttempts, bootstrap}`; `state` is `active`, `used`, `expired` or `burnt` |
| `POST /api/links/codes` | `{expiresInMinutes?, capabilities?}` (15 minutes and all capabilities by default), then 201 `{id, code, codeId, expiresAt, capabilities, pairUrl}`: `code` is shown only this once |
| `GET /api/links` | Every link: `{id, product, instanceName, url, peerVersion, state, confirmedHere, confirmedAt, confirmedBy, peerConfirmed, peerCheckedAt, capabilities, fingerprint, pairedAt, pairedBy, keysRenewedAt, lastExchangeAt, cutAt, cutBy, demo, keys, version}`; `state` is `pending`, `active` or `cut` |
| `GET /api/links/{id}?refresh=true` | One link; `refresh` asks the peer for its status first when the last check is older than a minute |
| `POST /api/links/{id}/confirm` | `{fingerprint}`, the four words read on gestion-mep; 422 when they differ |
| `PUT /api/links/{id}/capabilities` | `{capabilities}`, a subset of `catalog:read`, `notices:send`, `deployments:receive` |
| `POST /api/links/{id}/renew-keys` | New keys, announced to the peer (502 when it refuses) |
| `POST /api/links/{id}/cut` | `{confirm: true}`: tells the peer (best effort) and erases the keys |
| `GET /api/links/{id}/exchanges?cursor=&limit=` | `{items, nextCursor}`, newest first: `{id, at, linkId, direction, operation, result, status, reason, detail}` |
| `GET /api/links/exchanges?cursor=&limit=` | Every exchange line, refused requests of unknown keys included |

## Deployments

`GET /api/applications/{id}/deployments` and `GET /api/flows/{id}/deployments`, for anyone who sees the record in
full (403 otherwise; guests get empty lists), return `{upcoming, past}`: upcoming soonest first, past latest first. A flow gets the
deployments of both its ends. A deployment is `{deploymentId, title, phase, outcome, postponed, plannedStart,
plannedEnd, url, applicationIds, environments: [{name, production}], updatedAt, receivedAt, source: {linkId,
product, instanceName, linkActive}}`; `url` stays once the link is cut (`linkActive` is then false), and
`applicationIds` lists only the
applications the reader may see. `GET /api/applications/{id}` and `GET /api/flows/{id}` also return `deployments`,
with the 3 latest past ones.

## Demo toolbox

With `NOOSPHERE_DEMO=true` only (404 otherwise): `GET /api/demo/state`, `POST /api/demo/switch`,
`POST /api/demo/simulate/{edit|proposal|decision|agent|guest-read}` and `POST /api/demo/reset`. See
[demo toolbox](demo.md).

## Exchange

- `GET /api/export?format=json|yaml&scope=visible|full&reason=`, for anyone signed in, downloads the landscape in
  the [exchange format](exchange-format.md): what the reader sees in full (`visible`, the default), or everything
  (`full`, administrators only, written to the [access log](access.md#access-log) with the optional `reason`).
- `POST /api/import?dryRun=true|false`, for admins, takes the file as the request body (JSON or YAML, told apart by
  `Content-Type` or `?format=`) and returns `{created, updated, unchanged, errors[], dryRun, applied}`.

## Health

`GET /health/live` and `GET /health/ready` need no session. See [installation](installation.md#health-checks).
