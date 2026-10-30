# API

[English](../en/api.md)

JSON sur HTTP, sous `/api`. Les clés sont en camelCase et les énumérations sont des chaînes en camelCase (`saas`,
`dataObject`). Les noms de champs et de valeurs sont en anglais. La description OpenAPI 3.1 est servie sur
`/openapi/v1.json` et versionnée dans [docs/openapi.json](../openapi.json).

## Conventions

- **Session** : `POST /api/auth/login` pose un cookie HttpOnly, `noosphere.auth`. Toutes les autres routes `/api`
  l'exigent (401 sinon).
- **En-tête anti-CSRF** : toute requête autre que GET, HEAD et OPTIONS doit envoyer `X-Requested-With` (n'importe
  quelle valeur, par exemple `fetch`), sinon elle reçoit 403.
- **Erreurs** : `application/problem+json`, sous la forme `{type, title, status, detail, errors?}`. En 400, `errors`
  donne les messages de chaque champ invalide : `{"errors": {"name": ["This field is required."]}}`. Les messages
  sont en anglais ; l'interface les traduit d'après le statut et le champ.
- **Identifiants** : des slugs stables en kebab-case, uniques par type (`crm-to-billing-customers`). À la création,
  un `id` absent est déduit du nom et rendu unique (`customer-portal`, `customer-portal-2`). Un identifiant ne change
  jamais.
- **Versions** : chaque enregistrement porte `version`, `createdAt` et `updatedAt`. `PUT` remplace tout
  l'enregistrement (un champ facultatif absent est vidé, sauf `access` et `protectedNotes`, gardés quand ils sont absents) et doit fournir la `version` lue. Une version périmée
  renvoie **409** : « Someone changed this record. Reload it and try again. »

| Statut | Sens |
| ------ | ---- |
| 400 | Saisie invalide (`errors` par champ) |
| 401 | Pas de session, ou identifiants faux |
| 403 | Rôle insuffisant, hors de l'équipe responsable (envoyer une proposition), enregistrement restreint ou non partagé avec un invité, `X-Requested-With` absent |
| 404 | Enregistrement introuvable |
| 409 | Version périmée, identifiant déjà pris, enregistrement encore utilisé (suppression), proposition dépassée (`outdated: true`) |
| 422 | Fichier d'import en erreur (corps : le résultat de l'import) |
| 429 | Trop de tentatives de connexion, ou d'enregistrements de clients |

## Authentification et comptes

| Méthode et chemin | Qui | Corps, réponse |
| ----------------- | --- | -------------- |
| `POST /api/auth/login` | tous | `{username, password}`, puis `{username, displayName, role, language, teams}` |
| `POST /api/auth/logout` | tous | 204 |
| `GET /api/auth/me` | connecté | `{username, displayName, role, language, teams: [{id, name}]}` |
| `PUT /api/auth/me/language` | connecté | `{language: "en" ou "fr"}` |
| `PUT /api/auth/me/password` | connecté | `{currentPassword, newPassword}`, 204 ; ferme les autres sessions du compte |
| `GET /api/users`, `GET /api/users/{username}` | admin | `UserDto`, avec `teamIds` |
| `POST /api/users` | admin | `{username, displayName, role, language?, password, teamIds?}`, 201 |
| `PUT /api/users/{username}` | admin | `{displayName, role, language, password?, teamIds?, version}` ; sans `teamIds`, les équipes restent |
| `GET /api/teams/{id}/members` | connecté | `[{username, displayName, role}]` |
| `PUT /api/teams/{id}/members` | admin | `{usernames: []}` remplace les membres |
| `DELETE /api/users/{username}` | admin | 204 ; refusé pour son propre compte et pour le dernier admin |

Rôles : `admin`, `member` et `guest` (voir la [configuration](configuration.md#rôles)).

## Enregistrements du paysage

`GET/POST /api/domains`, `GET/PUT/DELETE /api/domains/{id}`, et de même pour `teams`, `applications`,
`data-objects` et `flows`. Tout compte connecté lit. Les administrateurs et les membres de l'équipe responsable
créent, modifient et suppriment ; les autres membres envoient une [proposition](proposals.md) (une écriture directe
renvoie 403). Les domaines et les équipes ne sont modifiés que par les administrateurs. Chaque modification crée une
[révision](history.md). Un enregistrement encore référencé ne peut pas être supprimé (409).

`GET /api/{kind}/{id}` renvoie aussi, pour le lecteur, `owningTeamId` (null : administrateurs seulement),
`permissions: {publish, propose}`, `pages` (pages rattachées) et `mentionedIn` (pages qui pointent vers
l'enregistrement). Une équipe renvoie aussi ses `members`.

| Type | Champs (saisie) | Calculés (réponse) |
| ---- | --------------- | ------------------ |
| Domaine | `name`, `description?`, `order` | |
| Équipe | `name`, `description?`, `contact?` | |
| Application | `name`, `domainId`, `teamId?`, `kind` (`app`, `bus`, `saas`, `data`, `file`, `external`), `tech?`, `description?` (markdown), `lifecycle` (`planned`, `live`, `retiring`, `retired`), `visibility` (`everyone`, `restricted`), `access?` (`{teamIds, usernames}`), `protectedNotes?` | `sourceOf` (objets de données dont elle est la source), `completeness` (0 à 1), `sensitivity` et `sensitivityReasons`, `restricted: false` |
| Objet de données | `name`, `sensitivity` (`public`, `internal`, `personal`, `sensitive`), `sourceApplicationId?`, `description?` | |
| Flux | `name`, `fromId`, `toId`, `type` (`api`, `event`, `batch`), `schedule?`, `dataObjectIds[]`, `ownerTeamId?`, `status` (`planned`, `live`, `deprecated`), `volume?`, `description?`, `plannedChange?` (`{summary, date?}`), `protectedNotes?` | `sensitivity` (l'objet transporté le plus sensible, `internal` s'il n'y en a aucun), `completeness`, `restricted: false` |

Complétude : la part des champs recommandés qui sont remplis. Pour une application : équipe, technologie,
description, au moins un flux, cycle de vie. Pour un flux : planification, objets de données, équipe responsable,
description.

Filtres des listes, tous facultatifs : `?domainId=&teamId=&type=&status=&dataObjectId=&sensitivity=&q=`. `q` ignore les accents
et la casse.

### Enregistrements restreints, invités et notes protégées

Voir [accès](acces.md). Qui ne peut pas voir une application restreinte reçoit `{id, restricted: true, domainId}`,
et les flux qu'il ne peut pas voir sous la forme `{id, restricted: true, fromId, toId, type}`. Un invité reçoit un
aperçu de ce qui n'est pas partagé avec lui (`restricted: false, shared: false`, le nom et la place seulement). Ni
les filtres ni la recherche ne portent sur les champs cachés. Dans le document OpenAPI, les listes contiennent
`ApplicationDto | RestrictedApplicationDto | GuestApplicationDto` (de même pour les flux et les pages), distingués
par `restricted` et `shared`.

Le détail d'une application ou d'un flux porte `protectedNotes` (null si le lecteur ne peut pas les lire) et
`protectedNotesHidden`.

| Méthode et chemin | Qui | Réponse |
| ----------------- | --- | ------- |
| `GET /api/{kind}/{id}/access` | qui voit l'enregistrement en entier | `{visibility, owningTeam, readers, publishers, proposers, protectedNotes, canManage}` |
| `GET /api/access-log?username=&kind=&entityId=&what=&from=&to=&cursor=&limit=` | admin | `{items, nextCursor}`, du plus récent au plus ancien |
| `GET /api/people` | membre, admin | `[{username, displayName, role, teamIds}]` : les personnes qu'on peut nommer dans une liste d'accès |
| `GET /api/access/overview` | admin | la page Accès : `{roles, restricted, protectedNotes, guests, teams}` |

## Explorateur

- `GET /api/landscape` renvoie en une réponse tout ce dont l'explorateur a besoin, avec les mêmes filtres :
  `{domains[], teams[], applications[], dataObjects[], flows[], stats: {applications, flows, domains}, generatedAt}`.
  Les filtres d'application (`domainId`, `teamId`, `q`) gardent les applications qui correspondent et les flux qui
  les touchent. Les filtres de flux (`type`, `status`, `dataObjectId`) gardent les flux qui correspondent et
  seulement les applications à leurs extrémités. Un flux vient toujours avec ses deux extrémités.
- `GET /api/search?q=` renvoie au plus 20 éléments `{kind, id, title, subtitle}`, où `kind` vaut `application`, `flow`,
  `dataObject`, `team`, `domain` ou `page`. Les noms qui commencent par le texte viennent en premier. Pour une page,
  le texte compte aussi, et le sous-titre est un extrait autour du passage trouvé.

## Propositions

Voir [propositions et validation](proposals.md).

| Méthode et chemin | Qui | Description |
| ----------------- | --- | ----------- |
| `GET /api/proposals?status=&mine=true&toValidate=true` | connecté | Propositions visibles par le lecteur (brouillons : leur auteur seulement), les plus récentes d'abord |
| `GET /api/proposals/{id}` | connecté | Une proposition, avec les instantanés et la comparaison `diff` de chaque modification |
| `POST /api/proposals` | membre, admin | `{title, comment?, changes: [{kind, targetId?, operation, baseVersion?, after?}], submit?}`, 201 |
| `PUT /api/proposals/{id}` | auteur | Même corps plus `version`, en brouillon ou à valider |
| `POST /api/proposals/{id}/submit`, `/withdraw` | auteur | |
| `POST /api/proposals/{id}/accept` | valideur | 409 avec `outdated: true` si une cible a changé |
| `POST /api/proposals/{id}/refuse` | valideur | `{reason}`, 3 à 500 caractères |
| `POST /api/proposals/{id}/rebase` | auteur | Reporte les modifications sur les dernières versions |
| `GET /api/me/inbox` | connecté | `{toValidate, mineOpen}` |

## Historique et corbeille

Voir l'[historique](history.md) : `GET /api/{kind}/{id}/history`, `.../history/{version}`,
`.../history/compare?from=&to=`, `POST .../history/{version}/restore`, et pour les administrateurs `GET /api/trash`
et `POST /api/trash/{kind}/{id}/restore`.

## Pages

Voir les [pages](pages.md) : `GET/POST /api/pages`, `GET /api/pages/tree`, `GET/PUT/DELETE /api/pages/{id}`,
`GET /api/pages/{id}/raw` et `POST /api/pages/{id}/move`.

## Réglages

| Méthode et chemin | Qui | Corps, réponse |
| ----------------- | --- | -------------- |
| `GET /api/settings` | tous, même sans session | `{instanceName, organisationName, defaultLanguage, version, updatedAt}` |
| `PUT /api/settings` | admin | `{instanceName, organisationName?, defaultLanguage?, version}` (`version` 0 avant le premier enregistrement) |

L'en-tête affiche `instanceName`, avec `organisationName` en dessous, et l'onglet du navigateur utilise
`instanceName`. Les nouveaux comptes reçoivent `defaultLanguage`. Le nom technique `noosphere` (commande, image,
cookie) ne change jamais.

## Agents IA

Voir [Agents IA (MCP)](agents.md) pour le serveur MCP à `/mcp`, les points d'accès OAuth (`/.well-known/*`,
`/oauth/*`), `/llms.txt`, `/llms-full.txt` et les vues Markdown, qui sont hors de `/api`.

| Méthode et chemin | Qui | Corps, réponse |
| ----------------- | --- | -------------- |
| `GET /api/settings/agents` | connecté | `{enabled, ceiling, quotaPerHour, whoConnects, dynamicRegistration, anonymousLlms, mcpUrl, version, updatedAt}` |
| `PUT /api/settings/agents` | admin | Les mêmes champs (chacun facultatif) plus `version` |
| `GET /api/me/agents` | connecté | `{connections, drafts, proposals, settings}` : mes connexions, les brouillons et autres propositions de mes agents |
| `GET /api/agents` | admin | Toutes les connexions : `{id, clientId, clientName, clientUri, person, scopes, state, createdAt, lastUsedAt, revokedAt, pausedUntil, approvedAt, callsLastHour, openDrafts, lastActivity, demo}` |
| `POST /api/agents/{id}/revoke` | admin, ou la personne | Révoque tout de suite la connexion et ses jetons |
| `POST /api/agents/{id}/approve` | admin | Une connexion en attente d'approbation (409 sinon) |
| `GET /api/agents/{id}/capabilities` | admin, ou la personne | `{connectionId, clientName, person, ceiling, scopes, items: [{key, allowed, text: {en, fr}}]}` pour « Ce que cet agent peut faire » |
| `GET /api/agents/activity?connectionId=&kind=&cursor=&limit=` | connecté | `{items, nextCursor}`, du plus récent au plus ancien ; les miennes seulement, toutes pour un admin. `kind` : `read`, `draft`, `proposal`, `refused`, `quota`, `revoked` |
| `POST /api/agents/stop-all` | admin | `{confirm: true}`, puis `{revokedConnections, revokedTokens, at}` |
| `GET /api/oauth/requests/{id}`, `POST .../approve`, `POST .../deny` | connecté | La page de consentement : voir la [référence OAuth](agents.md#référence-oauth) |

Une ligne d'activité vaut `{id, at, connectionId, agentName, person, tool, kind, summary, recordKind, recordId,
recordTitle}`. Les propositions écrites par un agent ont `authorKind: "agent"`, `onBehalfOf` (la personne,
`{username, displayName}`), `agentName` et `agentConnectionId`.

## Boîte à outils de démo

Avec `NOOSPHERE_DEMO=true` seulement (404 sinon) : `GET /api/demo/state`, `POST /api/demo/switch`,
`POST /api/demo/simulate/{edit|proposal|decision|agent|guest-read}` et `POST /api/demo/reset`. Voir la
[boîte à outils de démo](demo.md).

## Échange

- `GET /api/export?format=json|yaml&scope=visible|full&reason=`, pour tout compte connecté, télécharge le paysage
  au [format d'échange](exchange-format.md) : ce que le lecteur voit en entier (`visible`, par défaut), ou tout
  (`full`, administrateurs seulement, inscrit au [journal des accès](acces.md#journal-des-accès) avec le `reason`
  facultatif).
- `POST /api/import?dryRun=true|false`, pour les admins, prend le fichier comme corps de la requête (JSON ou YAML,
  selon `Content-Type` ou `?format=`) et renvoie `{created, updated, unchanged, errors[], dryRun, applied}`.

## Sondes

`GET /health/live` et `GET /health/ready` ne demandent pas de session. Voir l'[installation](installation.md#sondes).
