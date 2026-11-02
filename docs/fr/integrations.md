# Intégrations : clés d'API et webhooks

[English](../en/integrations.md)

Beaucoup d'outils ne savent pas signer les requêtes ni chiffrer les corps comme le demande le
[protocole de liaison](liaisons.md) : chaînes CI/CD, outils ITSM, scripts. Ils disposent d'une connexion plus
simple :

- une **clé d'API**, envoyée en `Authorization: Bearer`, pour lire le catalogue et envoyer des déploiements ;
- un **webhook sortant**, une URL HTTPS qui reçoit les avis de changement et les déploiements, signés en
  HMAC-SHA256.

Seuls les administrateurs créent les clés et les webhooks (`/api/integrations/...`). Une clé lit ce que partage le
protocole de liaison : les fiches visibles par tous, jamais les fiches restreintes, les notes protégées ni les
personnes. Ses appels, refus compris, sont inscrits au journal des échanges partagé avec les liaisons
(`GET /api/links/exchanges`), conservé 90 jours.

## Clés d'API

Créez une clé avec un nom, une ou plusieurs portées et une expiration de 90, 180 ou 365 jours (une clé expire toujours) :

```sh
curl -sS -X POST https://noosphere.example.com/api/integrations/keys \
  -b cookies.txt -H 'X-Requested-With: fetch' -H 'Content-Type: application/json' \
  -d '{"name": "Chaîne de livraison", "scopes": ["deployments:write"], "expiresInDays": 365}'
```

| Portée | Permet |
| ------ | ------ |
| `catalog:read` | `GET /api/integrations/catalog` |
| `deployments:write` | `POST /api/integrations/deployments` |

La réponse contient `key` (`nsk_` + 8 caractères + `_` + 40 caractères) **cette fois seulement** : rangez-la dans
les secrets de l'outil. Noosphere n'en garde que le SHA-256 ; les listes montrent le préfixe (`nsk_` + 8 caractères),
les portées, l'expiration et la dernière utilisation. `POST /api/integrations/keys/{id}/revoke` la révoque aussitôt ;
les déploiements envoyés avec elle restent, indiqués comme venant d'une clé révoquée.

Pour remplacer une clé sans coupure, renouvelez-la : `POST /api/integrations/keys/{id}/rotate` répond une nouvelle clé
(même nom, mêmes portées, même durée) **une seule fois**, et l'ancienne fonctionne encore 24 heures, le temps de mettre
à jour le secret de l'outil. L'ancienne clé indique alors `replacedById` ; les déploiements qu'elle a envoyés suivent
la nouvelle clé. La page Liaisons rappelle aux administrateurs les clés qui expirent dans les 30 jours.

Une clé ne sert qu'aux deux points d'accès ci-dessous : elle n'ouvre jamais de session, et le reste de l'API répond
401. Ces points d'accès n'exigent pas l'en-tête `X-Requested-With` et sont limités à 120 requêtes par minute et par
clé.

| Type de problème (`https://noosphere.dev/problems/...`) | Statut | Signification |
| ------------------------------------------------------- | ------ | ------------- |
| `invalid-api-key` | 401 | Pas de clé, ou une clé inconnue. |
| `api-key-expired` / `api-key-revoked` | 401 | Demandez une nouvelle clé à un administrateur. |
| `scope-missing` | 403 | La clé n'a pas la portée de ce point d'accès. |
| `bad-body` | 400 | Pas du JSON, ou des événements invalides (erreurs par champ dans `errors`, par exemple `events[0].phase`). |
| `idempotency-key-reused` | 422 | La même `Idempotency-Key` avec un autre corps dans les 24 heures. |
| `idempotency-key-in-use` | 409 | La même `Idempotency-Key` est en cours de traitement : réessayez. |
| (429) | 429 | Plus de 120 requêtes par minute avec cette clé. |

### Lire le catalogue

```sh
curl -sS -H "Authorization: Bearer $NOOSPHERE_API_KEY" \
  "https://noosphere.example.com/api/integrations/catalog?limit=100"
```

La réponse est `{generatedAt, instanceName, baseUrl, domains, teams, applications, flows, nextCursor}`, avec les mêmes
champs que le catalogue des liaisons. Les applications viennent par identifiant, `limit` par page (100 par défaut,
500 au plus), avec les flux qui en partent : chaque flux est sur une seule page ; les domaines et les équipes sont
sur chaque page. Passez `nextCursor` en `cursor` pour la page suivante, jusqu'à ce qu'il soit null.

### Envoyer des déploiements

Le corps est celui du protocole de liaison : `{events: [...]}`, 500 événements au plus. Les événements sont mis à
jour par `deploymentId` pour cette clé ; un événement dont `updatedAt` est plus ancien que celui enregistré ne change
rien. Les identifiants d'application inconnus ou restreints sont ignorés et listés dans la réponse.

```sh
curl -sS -X POST https://noosphere.example.com/api/integrations/deployments \
  -H "Authorization: Bearer $NOOSPHERE_API_KEY" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $CI_PIPELINE_ID-deploy" \
  -d '{"events": [{"deploymentId": "run-4812", "title": "Billing 4.2", "phase": "execution",
       "outcome": null, "postponed": false, "plannedStart": "2026-11-12T20:00:00Z", "plannedEnd": null,
       "url": "https://ci.example.com/runs/4812", "applicationIds": ["billing"],
       "environments": [{"name": "Production", "production": true}], "updatedAt": "2026-11-12T20:01:00Z"}]}'
```

La réponse est `{accepted, ignoredApplicationIds}`. `phase` vaut `preparation`, `execution`, `rollback`,
`acceptance`, `closed` ou `cancelled` ; `outcome` vaut `success`, `success-with-incidents`, `rolled-back`, `failed`
ou null. Avec une `Idempotency-Key` (200 caractères au plus), la même requête renvoyée dans les 24 heures reçoit la
première réponse (avec `Idempotent-Replayed: true`) et ne change rien.

Les déploiements apparaissent sur les fiches d'application et de flux comme ceux des liaisons ; leur source est le
nom de la clé, avec `source.kind: "apiKey"` dans l'API.

#### GitHub Actions

```yaml
- name: Prévenir Noosphere
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

Rangez `NOOSPHERE_API_KEY` dans une variable CI/CD masquée et protégée (GitLab) ou un secret du dépôt (GitHub).

## Webhooks sortants

```sh
curl -sS -X POST https://noosphere.example.com/api/integrations/webhooks \
  -b cookies.txt -H 'X-Requested-With: fetch' -H 'Content-Type: application/json' \
  -d '{"name": "ITSM", "url": "https://itsm.example.com/hooks/noosphere", "events": ["catalog.changed", "deployment.updated"]}'
```

La réponse contient `secret` (`whsec_` + 40 caractères) **cette fois seulement** : le destinataire en a besoin pour
vérifier les signatures. Noosphere le stocke chiffré par la Data Protection d'ASP.NET Core.

| Type d'événement | Envoyé quand | `data` |
| ---------------- | ------------ | ------ |
| `catalog.changed` | Une application, un flux, un domaine ou une équipe partagés changent (comme les avis de changement des liaisons) | `{changes: [{kind, id, operation, at}]}` |
| `deployment.updated` | Des déploiements sont reçus, d'une liaison ou d'une clé d'API | `{deployments: [{deploymentId, title, phase, outcome, postponed, plannedStart, plannedEnd, url, applicationIds, environments, updatedAt, source: {kind, id, name}}]}` |
| `ping` | Le bouton de test (`POST /api/integrations/webhooks/{id}/test`) | `{message, webhookId}` |

Chaque livraison est un `POST` avec un corps JSON `{id, type, createdAt, data}` et ces en-têtes :

| En-tête | Valeur |
| ------- | ------ |
| `X-Noosphere-Event` | Le type d'événement. |
| `X-Noosphere-Delivery` | L'identifiant de l'événement, le même à chaque nouvel essai : ignorez une livraison déjà traitée. |
| `X-Noosphere-Signature` | `t=<secondes unix>,v1=<hex de HMAC-SHA256(secret, "<t>.<corps brut>")>` |

Une réponse 2xx en moins de 10 secondes vaut livraison. Sinon, la livraison est réessayée après 1 min, 5 min, 30 min,
2 h et 6 h (6 essais sur environ 9 heures), puis marquée en échec. Les redirections ne sont pas suivies.
`GET /api/integrations/webhooks/{id}/deliveries` liste chaque livraison avec son état, ses essais, le dernier statut
et l'erreur ; chaque essai est aussi inscrit au journal des échanges. `POST .../deliveries/{deliveryId}/retry` fait
un essai de plus tout de suite. Le bouton de test envoie un `ping` aussitôt et répond par sa livraison ; un webhook
désactivé (`enabled: false`) ne reçoit plus rien de nouveau.

Quand aucune livraison n'a réussi depuis 24 heures (`failingSince` du webhook), le webhook est **suspendu**
(`pausedAt`) : ses livraisons en attente échouent, plus rien n'est mis en file, la suspension est inscrite au journal
des échanges (motif `webhook-paused`) et au journal du serveur, et la page Liaisons la signale aux administrateurs.
Une fois le destinataire réparé, réactivez le webhook (`PUT` avec `enabled: true`, ou **Reprendre** sur la fiche de
l'outil).

`POST /api/integrations/webhooks/{id}/secret` régénère le secret de signature et le répond **une seule fois** ;
l'ancien secret cesse aussitôt de signer.

### Vérifier la signature

Refusez une requête dont `t` est à plus de 5 minutes de votre horloge (rejeu), et comparez en temps constant.
Calculez le HMAC sur les octets bruts du corps, avant toute lecture du JSON.

Node.js (Express) :

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
  // Traiter event.type et event.data, une fois par event.id.
  res.sendStatus(204);
});
```

Python :

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

### Adresses qu'un webhook peut joindre

Pour protéger le réseau où tourne Noosphere (falsification de requêtes côté serveur, SSRF), l'URL d'un webhook doit
être en HTTPS, sans identifiants, et son hôte ne doit résoudre que vers des adresses publiques. Les adresses de
bouclage (`127.0.0.0/8`, `::1`, `localhost`), privées (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`,
`fc00::/7`), lien-local (`169.254.0.0/16`, métadonnées cloud comprises, `fe80::/10`), NAT d'opérateur, multicast et
réservées sont refusées à l'enregistrement du webhook, et de nouveau juste avant chaque connexion (une réponse DNS
qui a changé entre-temps est donc refusée aussi).

Pour un destinataire sur un réseau interne de confiance, un administrateur autorise les adresses privées (et le
HTTP simple) :

```sh
curl -sS -X PUT https://noosphere.example.com/api/integrations/settings \
  -b cookies.txt -H 'X-Requested-With: fetch' -H 'Content-Type: application/json' \
  -d '{"allowPrivateAddresses": true}'
```

## Points d'accès

| Méthode et chemin | Qui | Corps, réponse |
| ----------------- | --- | -------------- |
| `GET /api/integrations/catalog?cursor=&limit=` | clé avec `catalog:read` | Une page du catalogue |
| `POST /api/integrations/deployments` | clé avec `deployments:write` | `{events}`, puis `{accepted, ignoredApplicationIds}` |
| `GET /api/integrations/keys` | admin | `{id, name, prefix, scopes, state, createdAt, createdBy, expiresAt, lastUsedAt, revokedAt, revokedBy, replacedById}` ; `state` vaut `active`, `expired` ou `revoked` |
| `POST /api/integrations/keys` | admin | `{name, scopes, expiresInDays}` (90, 180 ou 365), puis 201 `{apiKey, key}` : `key` n'est montrée que cette fois |
| `POST /api/integrations/keys/{id}/rotate` | admin | 201 `{apiKey, key}` : une nouvelle clé montrée une fois ; l'ancienne marche encore 24 heures (409 si elle n'est pas active ou déjà renouvelée) |
| `POST /api/integrations/keys/{id}/revoke` | admin | La clé, révoquée |
| `GET /api/integrations/keys/{id}/exchanges?cursor=&limit=` | admin | Les lignes d'échange de la clé |
| `GET /api/integrations/exchanges?apiKeyId=&webhookId=&cursor=&limit=` | admin | Les lignes d'échange de plusieurs clés et webhooks (1 à 20 identifiants), les plus récentes d'abord |
| `GET /api/integrations/webhooks`, `GET .../{id}` | admin | `{id, name, url, events, enabled, createdAt, createdBy, lastDeliveryAt, pending, failingSince, pausedAt, version}` |
| `POST /api/integrations/webhooks` | admin | `{name, url, events, enabled?}`, puis 201 `{webhook, secret}` : `secret` n'est montré que cette fois |
| `PUT /api/integrations/webhooks/{id}` | admin | `{name, url, events, enabled?, version?}` (409 si `version` est dépassée) ; `enabled: true` reprend un webhook suspendu |
| `DELETE /api/integrations/webhooks/{id}` | admin | 204 ; ses livraisons partent avec lui |
| `POST /api/integrations/webhooks/{id}/secret` | admin | `{webhook, secret}` : un nouveau secret, montré cette fois seulement |
| `POST /api/integrations/webhooks/{id}/test` | admin | Envoie un `ping` tout de suite ; répond par la livraison |
| `POST /api/integrations/webhooks/{id}/deliveries/{deliveryId}/retry` | admin | Un essai de plus, tout de suite ; répond par la livraison (409 si elle est livrée, ou si le webhook est désactivé ou suspendu) |
| `GET /api/integrations/webhooks/{id}/deliveries?cursor=&limit=` | admin | `{items, nextCursor}` : `{id, eventId, eventType, state, attempts, createdAt, lastAttemptAt, nextAttemptAt, responseStatus, error, test}` |
| `GET /api/integrations/settings`, `PUT` | admin | `{allowPrivateAddresses}` |
