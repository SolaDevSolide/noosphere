# Configuration

[Français](../fr/configuration.md)

Every setting is an environment variable starting with `NOOSPHERE_`. With Docker Compose, put them in `.env`
(see [.env.example](../../.env.example)).

| Variable                     | Default                                   | Description |
| ---------------------------- | ----------------------------------------- | ----------- |
| `NOOSPHERE_DATABASE`         | (required)                                | PostgreSQL connection string, e.g. `Host=db;Port=5432;Database=noosphere;Username=noosphere;Password=...`. |
| `NOOSPHERE_DEMO`             | `false`                                   | `true` loads the "Alizé" demo landscape when the database is empty and creates the accounts `alice` (admin), `bob` (member) and `claire` (guest) with the password `demo`. |
| `NOOSPHERE_ADMIN_USERNAME`   | `admin`                                   | Username of the first administrator, created at startup when there is no account at all (outside demo mode). |
| `NOOSPHERE_ADMIN_PASSWORD`   | (empty)                                   | Its password, at least 8 characters. Empty: a random password is written once to the log. Change it after the first sign-in. |
| `NOOSPHERE_LOGIN_RATE_LIMIT` | `10`                                      | Sign-in attempts allowed per minute and per client address; beyond, 429. |
| `NOOSPHERE_KEYS_DIR`         | `/data/keys` in the image                 | Where the keys that protect session cookies are stored. Without it, sessions end at each restart of a container. |
| `NOOSPHERE_BACKUPS_DIR`      | `/data/backups` in the image              | Before migrating a database that holds data, every table is copied there as JSON. Empty: no backup (a warning is logged). |
| `NOOSPHERE_INSTANCE_NAME`    | `Noosphere`                               | Written as `source.name` in exports. |
| `NOOSPHERE_PUBLIC_URL`       | (address of the request)                  | Written as `source.url` in exports, e.g. `https://noosphere.example.com`. |
| `NOOSPHERE_TRUSTED_PROXIES`  | (none; `172.16.0.0/12` in the compose file) | Networks (CIDR) or addresses of the reverse proxy allowed to send `X-Forwarded-For` and `X-Forwarded-Proto`. |

Standard ASP.NET Core variables also apply, for example `ASPNETCORE_URLS` (the image listens on
`http://+:8080`) and `Logging__LogLevel__Default`.

## Roles

| Role     | Read | Create and edit | Delete, import, manage users | See restricted applications |
| -------- | ---- | --------------- | ---------------------------- | --------------------------- |
| `admin`  | yes  | yes             | yes                          | yes                         |
| `member` | yes  | yes             | no                           | no                          |
| `guest`  | yes  | no              | no                           | no                          |

Readers who may not see a restricted application get only its id and domain, and the flows touching it only with
their id, ends and type.
