# Noosphere

[Français](README.fr.md)

Noosphere is a self-hosted web application that documents a company's whole application landscape: the
**applications**, the **flows** between them, the **data** they carry and the **teams** that own them. It answers
questions such as "what does Billing receive, from whom, and when?", "where does customer data go?" or "who do I call
before changing this API?".

- An explorer with cards, a connections view and a graph, a lens on one application and data lineage.
- Records for every application and flow, editable by members, with completeness hints.
- Restricted applications, shown in full to administrators only.
- A documented exchange format (JSON or YAML) to import and export the whole landscape.
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
5 data objects) and three accounts, all with the password `demo`:

| Username | Role   | Can                                         |
| -------- | ------ | ------------------------------------------- |
| `alice`  | admin  | everything, including deleting and users    |
| `bob`    | member | read, create and edit                       |
| `claire` | guest  | read only                                   |

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
| `NOOSPHERE_INSTANCE_NAME`    | `Noosphere`         | Name written in exports                                        |
| `NOOSPHERE_PUBLIC_URL`       | (request address)   | Public address written in exports                              |
| `NOOSPHERE_TRUSTED_PROXIES`  | (none)              | Reverse proxy networks allowed to send `X-Forwarded-*` headers |

## Command line

The image ships a `noosphere` command ([docs/en/cli.md](docs/en/cli.md)):

```sh
docker compose exec noosphere noosphere users create dana --role member
docker compose exec noosphere noosphere users reset-password dana
docker compose exec noosphere noosphere export --format yaml > landscape.yaml
docker compose exec -T noosphere noosphere import - --dry-run < landscape.yaml
docker compose exec noosphere noosphere demo reset --yes
```

## Documentation

- [Installation](docs/en/installation.md)
- [Configuration](docs/en/configuration.md)
- [API](docs/en/api.md) (OpenAPI: [docs/openapi.json](docs/openapi.json), served at `/openapi/v1.json`)
- [Exchange format](docs/en/exchange-format.md) (JSON Schema: [docs/format/noosphere-v1.schema.json](docs/format/noosphere-v1.schema.json))
- [Command line](docs/en/cli.md)
- [Contributing](CONTRIBUTING.md) and [security policy](SECURITY.md)

## License

[MIT](LICENSE)
