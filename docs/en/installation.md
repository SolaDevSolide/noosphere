# Installation

[Français](../fr/installation.md)

Noosphere runs as one container (the web application, its API and the `noosphere` command line) next to a
PostgreSQL 16 database.

## With Docker Compose

```sh
git clone https://github.com/SolaDevSolide/noosphere.git
cd noosphere
cp .env.example .env      # then edit it: at least POSTGRES_PASSWORD
docker compose up -d --build
docker compose logs noosphere   # the first administrator's password, if NOOSPHERE_ADMIN_PASSWORD is empty
```

Noosphere listens on `127.0.0.1:8080` by default (`NOOSPHERE_BIND`, `NOOSPHERE_PORT`). Put an HTTPS reverse proxy
(nginx, Caddy, Traefik, IIS) in front of it and list its network in `NOOSPHERE_TRUSTED_PROXIES`, so that session
cookies are marked Secure and the rate limit sees the real client address.

To try Noosphere with the "Alizé" demo landscape, set `NOOSPHERE_DEMO=true` in `.env`. Demo accounts: `alice`
(admin), `bob` (member), `claire` (guest), password `demo`.

## Volumes

| Volume    | Path in the container | Content                                            |
| --------- | --------------------- | -------------------------------------------------- |
| `db-data` | (PostgreSQL)          | The database                                       |
| `keys`    | `/data/keys`          | Session cookie keys (sessions survive restarts)    |
| `backups` | `/data/backups`       | Copies of every table taken before a migration     |

## Upgrades

Pull the new version and run `docker compose up -d --build`. Database migrations are applied at startup. When the
database already holds data, every table is first copied to `/data/backups/before-migration-<date>.json`.
`noosphere migrate` applies them by hand.

## Health checks

- `GET /health/live`: the process answers.
- `GET /health/ready`: the database is reachable and up to date (503 otherwise). The image's `HEALTHCHECK` uses it
  through `noosphere health`.

## Without Docker

Build the frontend (`cd frontend && npm ci && npm run build`), publish the backend
(`dotnet publish backend/src/Noosphere.Api -c Release -o out`), copy `frontend/dist` into `out/wwwroot`, set the
environment variables of the [configuration](configuration.md) and run `dotnet out/Noosphere.dll`.
