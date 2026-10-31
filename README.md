# Noosphere

[Français](README.fr.md)

Noosphere is a self-hosted web application that documents a company's whole application landscape: the
**applications**, the **flows** between them, the **data** they carry and the **teams** that own them. It answers
questions such as "what does Billing receive, from whom, and when?", "where does customer data go?" or "who do I call
before changing this API?".

- An explorer with cards, a connections view and a graph, a lens on one application and data lineage.
- Records for every application and flow, with completeness hints.
- Everyone reads, anyone proposes, the owning team validates: change proposals with a field diff, validated by the
  teams that own the records.
- A revision history on every record and page, with compare and restore, and a trash for deleted records.
- Markdown documentation pages in a tree, attached to records, with wiki links (`[[application:billing]]`),
  backlinks and broken link detection, an editor with live preview, link suggestions and three page templates.
- Restricted applications and pages with access lists of teams and people, records shared with guests, protected
  notes for operational details, and an access log for administrators. A **Who can see and edit?** window on every
  record, and an **Access** page for administrators.
- Secrets pasted by mistake (tokens, keys, passwords) are refused: Noosphere documents where secrets live.
- A computed sensitivity for every application, from the data it holds and the flows touching it, with a landscape
  filter on it.
- A documented exchange format (JSON or YAML) to import and export the landscape: what you can see, or, for
  administrators, a full export recorded in the access log.
- **AI agents** connect through MCP (`/mcp`) for one person, after a consent page: they search, read and draft
  changes with that person's rights, capped by an administrator's ceiling and an hourly quota, and never publish.
  Every call is in their activity; **My agents** and an agents console revoke them, one by one or all at once.
  `/llms.txt`, `/llms-full.txt` and a Markdown view of every record.
- **Links with other applications**, gestion-mep first: paired applications read the catalogue and send their
  deployments, shown in a "Deployments and changes" block on application and flow records. Administrators pair,
  confirm, restrict and cut links on the Links page. Requests are signed (RFC 9421) and bodies encrypted (JWE), with
  fingerprints confirmed on both sides.
- English and French interface. Runs as one container next to PostgreSQL.

## Quick start

With Docker and Docker Compose:

```sh
git clone https://github.com/SolaDevSolide/noosphere.git
cd noosphere
cp .env.example .env
# Try it with the demo data:
echo "NOOSPHERE_DEMO=true" >> .env
docker compose up -d --build
```

Then open <http://localhost:8080>.

### Demo accounts

With `NOOSPHERE_DEMO=true`, Noosphere loads "Alizé", a fictional insurer (20 applications, 36 flows, 6 domains,
5 data objects, 7 documentation pages, a history on Billing, a few proposals, a restricted Fraud scoring application, protected notes on Billing and a customer portal shared with `claire`) and four accounts, all with the
password `demo`:

| Username | Role   | Teams                       | Can                                                        |
| -------- | ------ | --------------------------- | ---------------------------------------------------------- |
| `alice`  | admin  |                             | everything, including domains, teams, users and settings   |
| `bob`    | member | Team Finance, Team Payments | publish changes to their teams' records, validate proposals |
| `claire` | guest  |                             | read names, and the customer portal shared with her        |
| `david`  | member | Team CRM                    | publish for Team CRM, propose changes elsewhere            |

Sign in as `bob` to validate the two proposals that `david` sent to Team Finance. The
[demo toolbox](docs/en/demo.md) switches persona in one click and makes a colleague, another team, a validator, an AI
agent or the guest act, to try the collaboration features alone: open it with the **Demo** button at the bottom of
every page.

Never turn demo mode on for a shared server.

Without demo mode, the first start creates an administrator `admin` with the password from
`NOOSPHERE_ADMIN_PASSWORD`, or prints a random password once in the logs (`docker compose logs noosphere`).

## Configuration

All settings are environment variables (see [.env.example](.env.example) and
[docs/en/configuration.md](docs/en/configuration.md)):

| Variable                     | Default             | Purpose                                                        |
| ---------------------------- | ------------------- | -------------------------------------------------------------- |
| `NOOSPHERE_DATABASE`         | (required)          | PostgreSQL connection string                                   |
| `NOOSPHERE_DEMO`             | `false`             | Load the demo landscape and accounts                           |
| `NOOSPHERE_ADMIN_USERNAME`   | `admin`             | First administrator, created when there is no account          |
| `NOOSPHERE_ADMIN_PASSWORD`   | (random, logged)    | Password of that administrator (at least 8 characters)         |
| `NOOSPHERE_LOGIN_RATE_LIMIT` | `10`                | Sign-in attempts per minute and per client address             |
| `NOOSPHERE_KEYS_DIR`         | `/data/keys`        | Session key storage (Docker volume)                            |
| `NOOSPHERE_BACKUPS_DIR`      | `/data/backups`     | Backups taken before database migrations                       |
| `NOOSPHERE_INSTANCE_NAME`    | `Noosphere`         | Instance name until an administrator sets it in the settings   |
| `NOOSPHERE_PUBLIC_URL`       | (request address)   | Public address written in exports                              |
| `NOOSPHERE_TRUSTED_PROXIES`  | (none)              | Reverse proxy networks allowed to send `X-Forwarded-*` headers |
| `NOOSPHERE_ACCESS_LOG_DAYS`  | `365`               | Days the access log is kept                                    |
| `NOOSPHERE_AGENT_ACTIVITY_DAYS` | `90`             | Days the AI agents' activity is kept                           |
| `NOOSPHERE_REGISTRATION_RATE_LIMIT` | `20`         | OAuth client registrations per minute and per client address   |
| `NOOSPHERE_LINK_BOOTSTRAP_CODE` | (none)           | Preset pairing code for gestion-mep started together           |

## Command line

The image ships a `noosphere` command ([docs/en/cli.md](docs/en/cli.md)):

```sh
docker compose exec noosphere noosphere users create dana --role member
docker compose exec noosphere noosphere users reset-password dana
docker compose exec noosphere noosphere export --format yaml > landscape.yaml
docker compose exec -T noosphere noosphere import - --dry-run < landscape.yaml
docker compose exec noosphere noosphere demo reset --yes
docker compose exec noosphere noosphere link code --expires 30m
```

## Documentation

- [Installation](docs/en/installation.md)
- [Configuration](docs/en/configuration.md)
- [User guide](docs/en/user-guide.md)
- [Proposals and validation](docs/en/proposals.md)
- [History](docs/en/history.md)
- [Pages](docs/en/pages.md)
- [Access, protected notes and the access log](docs/en/access.md)
- [AI agents (MCP)](docs/en/agents.md), with llms.txt and the Markdown views
- [Demo toolbox](docs/en/demo.md)
- [Links with other applications (gestion-mep first)](docs/en/links.md)
- [API](docs/en/api.md) (OpenAPI: [docs/openapi.json](docs/openapi.json), served at `/openapi/v1.json`)
- [Exchange format](docs/en/exchange-format.md) (JSON Schema: [docs/format/noosphere-v1.schema.json](docs/format/noosphere-v1.schema.json))
- [Command line](docs/en/cli.md)
- [Contributing](CONTRIBUTING.md) and [security policy](SECURITY.md)

## License

[MIT](LICENSE)
