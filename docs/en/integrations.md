# Integrations: API keys and webhooks

[Français](../fr/integrations.md)

Many tools cannot sign requests or encrypt bodies as the [link protocol](links.md) asks: CI/CD pipelines, ITSM
tools, scripts. They get a simpler connection:

- an **API key**, sent as `Authorization: Bearer`, to read the catalogue and send deployments;
- an **outgoing webhook**, an HTTPS URL that receives change notices and deployments, signed with HMAC-SHA256.

Only administrators create keys and webhooks (`/api/integrations/...`). What a key reads is what the link protocol
shares: records visible to everyone, never restricted records, protected notes or people. Its calls, refusals
included, are in the exchange log shared with links (`GET /api/links/exchanges`), kept 90 days.

## API keys

Create a key with a name, one or more scopes and an expiry of 90, 180 or 365 days (a key always expires):

```sh
curl -sS -X POST https://noosphere.example.com/api/integrations/keys \
  -b cookies.txt -H 'X-Requested-With: fetch' -H 'Content-Type: application/json' \
  -d '{"name": "Release pipeline", "scopes": ["deployments:write"], "expiresInDays": 365}'
```

| Scope | Allows |
| ----- | ------ |
| `catalog:read` | `GET /api/integrations/catalog` |
| `deployments:write` | `POST /api/integrations/deployments` |

The answer holds `key` (`nsk_` + 8 characters + `_` + 40 characters) **only this once**: store it in the tool's
secrets. Noosphere keeps its SHA-256 only; lists show the prefix (`nsk_` + 8 characters), the scopes, the expiry and
the last use. `POST /api/integrations/keys/{id}/revoke` revokes it at once; deployments sent with it stay, shown as
from a revoked key.

To replace a key without a gap, rotate it: `POST /api/integrations/keys/{id}/rotate` answers a new key (same name,
scopes and lifetime) **once**, and the old key keeps working for 24 hours, the time to update the tool's secret. The
old key then shows `replacedById`; the deployments it sent follow the new key. The Links page reminds administrators
of keys that expire within 30 days.

A key works only on the two endpoints below: it never opens a session, and the rest of the API answers 401. These
endpoints need no `X-Requested-With` header and are limited to 120 requests per minute per key.

| Problem type (`https://noosphere.dev/problems/...`) | Status | Meaning |
| --------------------------------------------------- | ------ | ------- |
| `invalid-api-key` | 401 | No key, or an unknown one. |
| `api-key-expired` / `api-key-revoked` | 401 | Ask an administrator for a new key. |
| `scope-missing` | 403 | The key lacks the scope of this endpoint. |
| `bad-body` | 400 | Not JSON, or invalid events (field errors in `errors`, e.g. `events[0].phase`). |
| `idempotency-key-reused` | 422 | The same `Idempotency-Key` with another body within 24 hours. |
| `idempotency-key-in-use` | 409 | The same `Idempotency-Key` is being processed: retry. |
| (429) | 429 | More than 120 requests per minute with this key. |

### Read the catalogue

```sh
curl -sS -H "Authorization: Bearer $NOOSPHERE_API_KEY" \
  "https://noosphere.example.com/api/integrations/catalog?limit=100"
```

The answer is `{generatedAt, instanceName, baseUrl, domains, teams, applications, flows, nextCursor}`, with the same
fields as the link catalogue. Applications come by id, `limit` per page (100 by default, at most 500), with the flows
that start from them, so each flow is on exactly one page; domains and teams are on every page. Pass `nextCursor` as
`cursor` for the next page, until it is null.

### Send deployments

The body is the link protocol's: `{events: [...]}`, at most 500 events. Events are upserted by `deploymentId` for
this key; an event whose `updatedAt` is older than the stored one changes nothing. Unknown or restricted
application ids are ignored and listed in the answer.

```sh
curl -sS -X POST https://noosphere.example.com/api/integrations/deployments \
  -H "Authorization: Bearer $NOOSPHERE_API_KEY" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $CI_PIPELINE_ID-deploy" \
  -d '{"events": [{"deploymentId": "run-4812", "title": "Billing 4.2", "phase": "execution",
       "outcome": null, "postponed": false, "plannedStart": "2026-11-12T20:00:00Z", "plannedEnd": null,
       "url": "https://ci.example.com/runs/4812", "applicationIds": ["billing"],
       "environments": [{"name": "Production", "production": true}], "updatedAt": "2026-11-12T20:01:00Z"}]}'
```

The answer is `{accepted, ignoredApplicationIds}`. `phase` is `preparation`, `execution`, `rollback`, `acceptance`,
`closed` or `cancelled`; `outcome` is `success`, `success-with-incidents`, `rolled-back`, `failed` or null. With an
`Idempotency-Key` (at most 200 characters), the same request sent again within 24 hours gets the first answer back
(with `Idempotent-Replayed: true`) and changes nothing.

The deployments appear on application and flow records like those of links; their source is the key's name, with
`source.kind: "apiKey"` in the API.

#### GitHub Actions

```yaml
- name: Tell Noosphere
  if: always()
  env:
    NOOSPHERE_API_KEY: ${{ secrets.NOOSPHERE_API_KEY }}
    PHASE: ${{ job.status == 'success' && 'closed' || 'rollback' }}
    OUTCOME: ${{ job.status == 'success' && '"success"' || '"failed"' }}
  run: |
    now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    curl -fsS -X POST https://noosphere.example.com/api/integrations/deployments \
      -H "Authorization: Bearer $NOOSPHERE_API_KEY" -H 'Content-Type: application/json' \
      -H "Idempotency-Key: gh-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT" \
      -d "{\"events\":[{\"deploymentId\":\"gh-$GITHUB_RUN_ID\",\"title\":\"$GITHUB_REPOSITORY $GITHUB_REF_NAME\",
           \"phase\":\"$PHASE\",\"outcome\":$OUTCOME,\"url\":\"$GITHUB_SERVER_URL/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID\",
           \"applicationIds\":[\"billing\"],\"environments\":[{\"name\":\"Production\",\"production\":true}],\"updatedAt\":\"$now\"}]}"
```

#### GitLab CI

```yaml
notify-noosphere:
  stage: .post
  when: always
  script:
    - now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    - if [ "$CI_JOB_STATUS" = "success" ]; then phase=closed; outcome='"success"'; else phase=rollback; outcome='"failed"'; fi
    - >
      curl -fsS -X POST https://noosphere.example.com/api/integrations/deployments
      -H "Authorization: Bearer $NOOSPHERE_API_KEY" -H 'Content-Type: application/json'
      -H "Idempotency-Key: gl-$CI_PIPELINE_ID"
      -d "{\"events\":[{\"deploymentId\":\"gl-$CI_PIPELINE_ID\",\"title\":\"$CI_PROJECT_PATH $CI_COMMIT_REF_NAME\",
      \"phase\":\"$phase\",\"outcome\":$outcome,\"url\":\"$CI_PIPELINE_URL\",\"applicationIds\":[\"billing\"],
      \"environments\":[{\"name\":\"Production\",\"production\":true}],\"updatedAt\":\"$now\"}]}"
```

Store `NOOSPHERE_API_KEY` as a masked, protected CI/CD variable (GitLab) or a repository secret (GitHub).

## Outgoing webhooks

```sh
curl -sS -X POST https://noosphere.example.com/api/integrations/webhooks \
  -b cookies.txt -H 'X-Requested-With: fetch' -H 'Content-Type: application/json' \
  -d '{"name": "ITSM", "url": "https://itsm.example.com/hooks/noosphere", "events": ["catalog.changed", "deployment.updated"]}'
```

The answer holds `secret` (`whsec_` + 40 characters) **only this once**: the receiver needs it to check signatures.
Noosphere stores it encrypted with ASP.NET Core Data Protection.

| Event type | Sent when | `data` |
| ---------- | --------- | ------ |
| `catalog.changed` | A shared application, flow, domain or team changes (as link change notices) | `{changes: [{kind, id, operation, at}]}` |
| `deployment.updated` | Deployments are received, from a link or an API key | `{deployments: [{deploymentId, title, phase, outcome, postponed, plannedStart, plannedEnd, url, applicationIds, environments, updatedAt, source: {kind, id, name}}]}` |
| `ping` | The test button (`POST /api/integrations/webhooks/{id}/test`) | `{message, webhookId}` |

Each delivery is a `POST` with a JSON body `{id, type, createdAt, data}` and these headers:

| Header | Value |
| ------ | ----- |
| `X-Noosphere-Event` | The event type. |
| `X-Noosphere-Delivery` | The event id, the same on every retry: drop a delivery you have already handled. |
| `X-Noosphere-Signature` | `t=<unix seconds>,v1=<hex HMAC-SHA256(secret, "<t>.<raw body>")>` |

A 2xx answer within 10 seconds counts as delivered. Otherwise the delivery is retried after 1 min, 5 min, 30 min,
2 h and 6 h (6 attempts over about 9 hours), then marked failed. Redirects are not followed.
`GET /api/integrations/webhooks/{id}/deliveries` lists each delivery with its state, attempts, last status and error;
every attempt is also in the exchange log. `POST .../deliveries/{deliveryId}/retry` makes one more attempt at once.
The test button sends a `ping` at once and answers its delivery; a disabled webhook (`enabled: false`) receives
nothing new.

When no delivery has succeeded for 24 hours (`failingSince` in the webhook), the webhook is **paused** (`pausedAt`):
its waiting deliveries fail, nothing more is queued, the pause is written to the exchange log (reason
`webhook-paused`) and to the server log, and the Links page shows it to administrators. Once the receiver is fixed,
switch the webhook on again (`PUT` with `enabled: true`, or **Resume** on the tool's page).

`POST /api/integrations/webhooks/{id}/secret` regenerates the signing secret and answers it **once**; the old secret
stops signing at once.

### Check the signature

Refuse a request whose `t` is more than 5 minutes from your clock (replay), and compare in constant time. Compute the
HMAC over the raw body bytes, before any JSON parsing.

Node.js (Express):

```js
import crypto from 'node:crypto';
import express from 'express';

const secret = process.env.NOOSPHERE_WEBHOOK_SECRET;
const app = express();

app.post('/hooks/noosphere', express.raw({ type: 'application/json' }), (req, res) => {
  const header = req.get('X-Noosphere-Signature') ?? '';
  const parts = Object.fromEntries(header.split(',').map((p) => p.split('=', 2)));
  const t = Number(parts.t);
  if (!Number.isInteger(t) || Math.abs(Date.now() / 1000 - t) > 300) return res.sendStatus(400);
  const expected = crypto.createHmac('sha256', secret).update(`${t}.`).update(req.body).digest();
  const given = Buffer.from(parts.v1 ?? '', 'hex');
  if (given.length !== expected.length || !crypto.timingSafeEqual(given, expected)) return res.sendStatus(401);
  const event = JSON.parse(req.body.toString('utf8'));
  // Handle event.type and event.data, once per event.id.
  res.sendStatus(204);
});
```

Python:

```python
import hashlib, hmac, os, time

SECRET = os.environ["NOOSPHERE_WEBHOOK_SECRET"].encode()

def verify(header: str, body: bytes, tolerance: int = 300) -> bool:
    parts = dict(p.split("=", 1) for p in header.split(",") if "=" in p)
    try:
        t = int(parts["t"])
    except (KeyError, ValueError):
        return False
    if abs(time.time() - t) > tolerance:
        return False
    expected = hmac.new(SECRET, f"{t}.".encode() + body, hashlib.sha256).hexdigest()
    return hmac.compare_digest(expected, parts.get("v1", ""))
```

### Addresses a webhook may reach

To protect the network Noosphere runs in (server-side request forgery), a webhook URL must be HTTPS, without
credentials, and its host must resolve to public addresses only. Loopback (`127.0.0.0/8`, `::1`, `localhost`),
private (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `fc00::/7`), link-local (`169.254.0.0/16`, cloud metadata
included, `fe80::/10`), carrier-grade NAT, multicast and reserved addresses are refused when the webhook is saved,
and again right before each connection (so a DNS answer that changed in between is refused too).

For a receiver on a trusted internal network, an administrator allows private addresses (and plain HTTP):

```sh
curl -sS -X PUT https://noosphere.example.com/api/integrations/settings \
  -b cookies.txt -H 'X-Requested-With: fetch' -H 'Content-Type: application/json' \
  -d '{"allowPrivateAddresses": true}'
```

## Endpoints

| Method and path | Who | Body, answer |
| --------------- | --- | ------------ |
| `GET /api/integrations/catalog?cursor=&limit=` | key with `catalog:read` | One page of the catalogue |
| `POST /api/integrations/deployments` | key with `deployments:write` | `{events}`, then `{accepted, ignoredApplicationIds}` |
| `GET /api/integrations/keys` | admin | `{id, name, prefix, scopes, state, createdAt, createdBy, expiresAt, lastUsedAt, revokedAt, revokedBy, replacedById}`; `state` is `active`, `expired` or `revoked` |
| `POST /api/integrations/keys` | admin | `{name, scopes, expiresInDays}` (90, 180 or 365), then 201 `{apiKey, key}`: `key` is shown only this once |
| `POST /api/integrations/keys/{id}/rotate` | admin | 201 `{apiKey, key}`: a new key shown once; the old one works 24 more hours (409 if not active or already rotated) |
| `POST /api/integrations/keys/{id}/revoke` | admin | The key, revoked |
| `GET /api/integrations/keys/{id}/exchanges?cursor=&limit=` | admin | The key's exchange lines |
| `GET /api/integrations/exchanges?apiKeyId=&webhookId=&cursor=&limit=` | admin | The exchange lines of several keys and webhooks (1 to 20 ids), newest first |
| `GET /api/integrations/webhooks`, `GET .../{id}` | admin | `{id, name, url, events, enabled, createdAt, createdBy, lastDeliveryAt, pending, failingSince, pausedAt, version}` |
| `POST /api/integrations/webhooks` | admin | `{name, url, events, enabled?}`, then 201 `{webhook, secret}`: `secret` is shown only this once |
| `PUT /api/integrations/webhooks/{id}` | admin | `{name, url, events, enabled?, version?}` (409 when `version` is stale); `enabled: true` resumes a paused webhook |
| `DELETE /api/integrations/webhooks/{id}` | admin | 204; its deliveries go with it |
| `POST /api/integrations/webhooks/{id}/secret` | admin | `{webhook, secret}`: a new secret, shown only this once |
| `POST /api/integrations/webhooks/{id}/test` | admin | Sends a `ping` now; answers the delivery |
| `POST /api/integrations/webhooks/{id}/deliveries/{deliveryId}/retry` | admin | One more attempt now; answers the delivery (409 if delivered, or the webhook is off or paused) |
| `GET /api/integrations/webhooks/{id}/deliveries?cursor=&limit=` | admin | `{items, nextCursor}`: `{id, eventId, eventType, state, attempts, createdAt, lastAttemptAt, nextAttemptAt, responseStatus, error, test}` |
| `GET /api/integrations/settings`, `PUT` | admin | `{allowPrivateAddresses}` |
