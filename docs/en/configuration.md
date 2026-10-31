# Configuration

[Français](../fr/configuration.md)

Every setting is an environment variable starting with `NOOSPHERE_`. With Docker Compose, put them in `.env`
(see [.env.example](../../.env.example)).

| Variable                     | Default                                   | Description |
| ---------------------------- | ----------------------------------------- | ----------- |
| `NOOSPHERE_DATABASE`         | (required)                                | PostgreSQL connection string, e.g. `Host=db;Port=5432;Database=noosphere;Username=noosphere;Password=...`. |
| `NOOSPHERE_DEMO`             | `false`                                   | `true` loads the "Alizé" demo landscape when the database is empty (with pages, history, proposals, a restricted application, protected notes and a record shared with `claire`) and creates the accounts `alice` (admin), `bob` (member of Team Finance and Team Payments), `claire` (guest) and `david` (member of Team CRM) with the password `demo`. |
| `NOOSPHERE_ADMIN_USERNAME`   | `admin`                                   | Username of the first administrator, created at startup when there is no account at all (outside demo mode). |
| `NOOSPHERE_ADMIN_PASSWORD`   | (empty)                                   | Its password, at least 8 characters. Empty: a random password is written once to the log. Change it after the first sign-in. |
| `NOOSPHERE_LOGIN_RATE_LIMIT` | `10`                                      | Sign-in attempts allowed per minute and per client address; beyond, 429. |
| `NOOSPHERE_KEYS_DIR`         | `/data/keys` in the image                 | Where the keys that protect session cookies are stored. Without it, sessions end at each restart of a container. |
| `NOOSPHERE_BACKUPS_DIR`      | `/data/backups` in the image              | Before migrating a database that holds data, every table is copied there as JSON. Empty: no backup (a warning is logged). |
| `NOOSPHERE_INSTANCE_NAME`    | `Noosphere`                               | Instance name until an administrator sets one in the settings (`PUT /api/settings`); shown in the header and written as `source.name` in exports. |
| `NOOSPHERE_PUBLIC_URL`       | (address of the request)                  | Written as `source.url` in exports, e.g. `https://noosphere.example.com`. Also the issuer and resource of the [MCP server](agents.md#oauth-reference) and the address given to gestion-mep for [links](links.md), with any path prefix: set it when Noosphere runs behind a proxy. |
| `NOOSPHERE_TRUSTED_PROXIES`  | (none; `172.16.0.0/12` in the compose file) | Networks (CIDR) or addresses of the reverse proxy allowed to send `X-Forwarded-For` and `X-Forwarded-Proto`. |
| `NOOSPHERE_ACCESS_LOG_DAYS`  | `365`                                     | Days the [access log](access.md#access-log) is kept; older lines are deleted at startup and every day. |
| `NOOSPHERE_AGENT_ACTIVITY_DAYS` | `90`                                   | Days the [AI agents' activity](agents.md#agent-settings) is kept; expired tokens and authorization requests are deleted with it. |
| `NOOSPHERE_REGISTRATION_RATE_LIMIT` | `20`                               | OAuth client registrations (`POST /oauth/register`) allowed per minute and per client address; beyond, 429. |
| `NOOSPHERE_LINK_BOOTSTRAP_CODE` | (none)                                 | A pairing code preset for a [gestion-mep link](links.md#docker-compose-with-gestion-mep) started together: at least 16 letters or digits (dashes and spaces ignored), single use, valid 24 hours after start. Also give it to gestion-mep as `NOOSPHERE_CODE_LIAISON`. |

Standard ASP.NET Core variables also apply, for example `ASPNETCORE_URLS` (the image listens on
`http://+:8080`) and `Logging__LogLevel__Default`.

## Roles

| Role     | Read | Publish changes | Propose changes | Domains, teams, import, users, settings, trash | See restricted records |
| -------- | ---- | --------------- | --------------- | ---------------------------------------------- | ---------------------- |
| `admin`  | yes  | yes             | yes             | yes                                            | yes                    |
| `member` | yes  | records owned by their teams | yes | no                                       | when their team or they are on the access list |
| `guest`  | an outline, and what is shared with them | no | no | no                                   | when named on the access list |

Members publish (create, edit, delete, restore) the records owned by one of their teams and send proposals for the
others: see [proposals and validation](proposals.md). Administrators manage team memberships.

Readers who may not see a restricted application get only its id and domain, and the flows touching it only with
their id, ends and type. Restricted pages are shown as `{id, restricted: true, parentId, order}`. Guests see names
only, except for the records shared with them. See [access, protected notes and the access log](access.md).

## Instance settings

Administrators set the instance name (default "Noosphere"), the organisation name and the default language of new
accounts in the settings (`GET/PUT /api/settings`). The header shows the instance name with the organisation name
under it, and the browser tab uses the instance name. The technical name `noosphere` does not change.
