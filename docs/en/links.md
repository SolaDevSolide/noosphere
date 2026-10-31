# Links with gestion-mep

[Français](../fr/liaisons.md)

A link joins this Noosphere instance to a [gestion-mep](https://github.com/SolaDevSolide/gestion-mep) instance,
which plans deployments (MEP). Once linked:

- gestion-mep reads Noosphere's **catalogue** (applications, flows, domains, teams) and follows the applications its
  administrator chooses;
- Noosphere tells gestion-mep when a shared record changes (**change notices**);
- gestion-mep sends the **deployments** touching the followed applications. Each application and flow record shows
  the upcoming and past deployments, and AI agents see them in `get_record` and the Markdown views.

Only administrators manage links. Restricted applications (any access list) are never sent, nor the flows touching
them, nor protected notes, pages or history.

## Pairing

1. In Noosphere, create a pairing code: `noosphere link code` or `POST /api/links/codes`. A code is 12 characters
   shown as `XXXX-XXXX-XXXX` (Crockford base32: no I, L, O, U), valid 15 minutes by default (up to 24 h), single use.
   It is shown once.
2. In gestion-mep, enter the URL of this Noosphere (its `NOOSPHERE_PUBLIC_URL`) and the code. gestion-mep calls
   `POST /link/v1/pair`; both sides create their keys and a **pending** link.
3. Both sides show the same **fingerprint**, four words and a number (e.g. `cedar anchor 8451 arbor`). Compare them
   (on the phone, in person), then confirm on each side: `noosphere link confirm <id> --fingerprint "..."` here,
   `gestion-mep liaison confirmer <id> --empreinte "..."` there.
4. The link is **active**. Until gestion-mep confirms too, Noosphere shows "waiting for confirmation on gestion-mep"
   and data requests answer `403 link-not-confirmed`.

A wrong code counts as a failure: after 5 wrong MACs the code is burnt (`code-burnt`). Codes are stored hashed.

Capabilities are chosen with the code (all three by default) and changed later
(`PUT /api/links/{id}/capabilities`):

| Capability | Allows |
| ---------- | ------ |
| `catalog:read` | gestion-mep reads the catalogue (`GET /link/v1/catalog`) |
| `notices:send` | Noosphere posts change notices to gestion-mep |
| `deployments:receive` | gestion-mep posts deployments (`POST /link/v1/deployments`) |

**Renew keys** (`noosphere link renew <id>`) creates new keys and tells the peer, signed with the old key: the
fingerprint changes and no new confirmation is needed. Requests signed with the old key are still accepted for
5 minutes. **Cut** (`noosphere link cut <id> --yes`) tells the peer (best effort) and erases the keys; it cannot be
undone. Received deployments stay as history, without their link to gestion-mep.

## Protocol summary

The shared contract is the reference; in short:

- Endpoints under `/link/v1` (outside `/api`: no cookie, no CSRF), limited to 120 requests per minute per link.
  Noosphere serves `pair`, `status`, `catalog`, `deployments`, `rekey`, `unpair`; gestion-mep serves `notices`,
  `status`, `rekey`, `unpair`.
- Each side has, per link, a P-256 signing key and a P-256 encryption key. Private keys are encrypted at rest with
  ASP.NET Core Data Protection (the `NOOSPHERE_KEYS_DIR` key ring) and never leave the server. Key ids are RFC 7638
  thumbprints.
- Every request but `pair`, and every response, is signed with **HTTP Message Signatures** (RFC 9421,
  `ecdsa-p256-sha256`, `tag="noosphere-link"`), over `@method`, `@path`, `@query` and `content-digest` (RFC 9530)
  for requests, `@status` and `content-digest` for responses. `created` must be within 5 minutes; each `nonce` is
  accepted once.
- Bodies are **JWE** compact (`ECDH-ES` + `A256GCM`) to the peer's encryption key, sent as
  `application/jose`.
- Refusals are `application/problem+json` with `type: https://noosphere.dev/problems/<reason>`.

`@path` is the full path of the URL the sender calls. Behind a reverse proxy that adds a prefix, set
`NOOSPHERE_PUBLIC_URL` with that prefix (e.g. `https://tools.example.com/noosphere`): Noosphere accepts a signature
over the path with the prefix of `NOOSPHERE_PUBLIC_URL`, with the request's `PathBase`, or without prefix.

## Command line

| Command | Does |
| ------- | ---- |
| `noosphere link code [--expires 15m\|2h] [--capabilities catalog:read,notices:send,deployments:receive]` | Creates a pairing code, shown once. |
| `noosphere link list [--json]` | Lists links: state, peer confirmation, instance, fingerprint. |
| `noosphere link confirm <id> --fingerprint "<four words>"` | Confirms a pending link after comparing fingerprints. |
| `noosphere link renew <id>` | Renews this side's keys and tells the peer. |
| `noosphere link cut <id> --yes` | Cuts the link and erases its keys (exit code 4 without `--yes`). |

`<id>` is the link id or its first characters (at least 4). An unknown link exits with 5.

## Docker Compose with gestion-mep

Started together, gestion-mep pairs at its first start with a preset code (at least 16 letters or digits, single
use, valid 24 hours after Noosphere's start). The administrators still compare and confirm the fingerprint on both
sides.

```yaml
services:
  noosphere:
    image: noosphere:local
    environment:
      NOOSPHERE_DATABASE: Host=db;Port=5432;Database=noosphere;Username=noosphere;Password=${POSTGRES_PASSWORD}
      NOOSPHERE_PUBLIC_URL: http://noosphere:8080
      NOOSPHERE_LINK_BOOTSTRAP_CODE: ${LINK_CODE}
    volumes:
      - keys:/data/keys
  gestion-mep:
    image: gestion-mep:local
    depends_on: [noosphere]
    environment:
      NOOSPHERE_URL: http://noosphere:8080
      NOOSPHERE_CODE_LIAISON: ${LINK_CODE}
  # db, gestion-mep's database and the volumes as in each project's compose file
```

Put a long random `LINK_CODE` in `.env` (for example `openssl rand -hex 12`). Outside a Docker network, use HTTPS.
Keep the `keys` volume: without it the link keys cannot be read after a restart.

## Troubleshooting

Refused requests are written to the exchange log (`GET /api/links/{id}/exchanges`, or `GET /api/links/exchanges`
for those of unknown keys), kept 90 days.

| Reason | Status | What to check |
| ------ | ------ | ------------- |
| `code-unknown` | 404 | No active code starts with these 4 characters: typo, expired code (15 min by default; create another, or `--expires 2h`; the bootstrap code lasts 24 h after start), or a code from another instance. |
| `bad-mac` | 401 | The code is wrong after its first 4 characters. 5 failures burn it. |
| `code-used` / `code-burnt` | 409 / 410 | Create a new code. |
| `unknown-key` | 401 | The peer uses a key this side does not know: link cut or never paired here, or keys renewed while the peer was unreachable. Pair again. |
| `link-cut` | 401 | The link was cut on this side. Pair again. |
| `link-not-confirmed` | 403 | Confirm the fingerprint on both sides. |
| `clock-skew` | 401 | The clocks differ by more than 5 minutes: check NTP on both hosts. |
| `bad-digest` | 401 | The body changed on the way (a proxy rewriting bodies?). |
| `bad-signature` | 401 | Usually a proxy changing the path or query: set `NOOSPHERE_PUBLIC_URL` with the public prefix, and do not rewrite the query. |
| `replay` | 401 | A nonce was sent twice: a retrying proxy, or a replayed request. |
| `capability-missing` | 403 | Grant the capability (`PUT /api/links/{id}/capabilities`). |
| `bad-body` | 400 | The decrypted body does not match the contract (field errors in `detail`). |
| (429) | 429 | More than 120 requests per minute for this link (or address, unsigned). |

## Demo

The demo landscape includes a link to a fictional gestion-mep instance, "MEP Alizé", active, with deployments on
Billing, CRM, Payments, Portal and Claims, and a few exchanges. No real peer is called: outbound calls to it are
skipped and logged as `demo`.
