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
  optional field is cleared) and must carry the `version` that was read. A stale version returns **409**: "Someone
  changed this record. Reload it and try again."

| Status | Meaning |
| ------ | ------- |
| 400 | Invalid input (`errors` per field) |
| 401 | Not signed in, or wrong credentials |
| 403 | Role not allowed, restricted record, missing `X-Requested-With` |
| 404 | No such record |
| 409 | Stale version, id already used, record still in use (delete) |
| 422 | Import file with errors (body: the import result) |
| 429 | Too many sign-in attempts |

## Authentication and accounts

| Method and path | Who | Body, answer |
| --------------- | --- | ------------ |
| `POST /api/auth/login` | anyone | `{username, password}`, then `{username, displayName, role, language}` |
| `POST /api/auth/logout` | anyone | 204 |
| `GET /api/auth/me` | signed in | `{username, displayName, role, language}` |
| `PUT /api/auth/me/language` | signed in | `{language: "en" or "fr"}` |
| `PUT /api/auth/me/password` | signed in | `{currentPassword, newPassword}`, 204; closes the account's other sessions |
| `GET /api/users`, `GET /api/users/{username}` | admin | `UserDto` |
| `POST /api/users` | admin | `{username, displayName, role, language?, password}`, 201 |
| `PUT /api/users/{username}` | admin | `{displayName, role, language, password?, version}` |
| `DELETE /api/users/{username}` | admin | 204; refused for your own account and for the last admin |

Roles: `admin`, `member` and `guest` (see [configuration](configuration.md#roles)).

## Landscape records

`GET/POST /api/domains`, `GET/PUT/DELETE /api/domains/{id}`, and the same for `teams`, `applications`,
`data-objects` and `flows`. Everyone signed in reads, members and admins create and edit, admins delete. A record
that others still refer to cannot be deleted (409).

| Kind | Fields (input) | Computed (output) |
| ---- | -------------- | ----------------- |
| Domain | `name`, `description?`, `order` | |
| Team | `name`, `description?`, `contact?` | |
| Application | `name`, `domainId`, `teamId?`, `kind` (`app`, `bus`, `saas`, `data`, `file`, `external`), `tech?`, `description?` (markdown), `lifecycle` (`planned`, `live`, `retiring`, `retired`), `visibility` (`everyone`, `restricted`) | `sourceOf` (data objects whose source it is), `completeness` (0 to 1), `restricted: false` |
| Data object | `name`, `sensitivity` (`public`, `internal`, `personal`, `sensitive`), `sourceApplicationId?`, `description?` | |
| Flow | `name`, `fromId`, `toId`, `type` (`api`, `event`, `batch`), `schedule?`, `dataObjectIds[]`, `ownerTeamId?`, `status` (`planned`, `live`, `deprecated`), `volume?`, `description?`, `plannedChange?` (`{summary, date?}`) | `sensitivity` (most sensitive carried object, `internal` when none), `completeness`, `restricted: false` |

Completeness: the share of recommended fields that are filled. For an application: team, tech, description, at
least one flow, lifecycle. For a flow: schedule, data objects, owner, description.

List filters, all optional: `?domainId=&teamId=&type=&status=&dataObjectId=&q=`. `q` is accent- and
case-insensitive.

### Restricted applications

Only admins see a restricted application in full. Others get `{id, restricted: true, domainId}`, and the flows from
or to it as `{id, restricted: true, fromId, toId, type}`. Restricted records cannot be changed by non-admins (403),
and filters and search never match their hidden fields. In the OpenAPI document, lists hold
`ApplicationDto | RestrictedApplicationDto` (and `FlowDto | RestrictedFlowDto`), told apart by `restricted`.

## Explorer

- `GET /api/landscape` returns everything the explorer needs in one response, with the same filters:
  `{domains[], teams[], applications[], dataObjects[], flows[], stats: {applications, flows, domains}, generatedAt}`.
  Application filters (`domainId`, `teamId`, `q`) keep the matching applications and the flows touching them. Flow
  filters (`type`, `status`, `dataObjectId`) keep the matching flows and only the applications at their ends. A flow
  always comes with both of its ends.
- `GET /api/search?q=` returns at most 20 `{kind, id, title, subtitle}` items, where `kind` is `application`, `flow`,
  `dataObject`, `team` or `domain`. Names starting with the text come first.

## Exchange

- `GET /api/export?format=json|yaml`, for anyone signed in, downloads the landscape in the
  [exchange format](exchange-format.md). Restricted applications are left out for non-admins.
- `POST /api/import?dryRun=true|false`, for admins, takes the file as the request body (JSON or YAML, told apart by
  `Content-Type` or `?format=`) and returns `{created, updated, unchanged, errors[], dryRun, applied}`.

## Health

`GET /health/live` and `GET /health/ready` need no session. See [installation](installation.md#health-checks).
