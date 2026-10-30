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
  l'enregistrement (un champ facultatif absent est vidé) et doit fournir la `version` lue. Une version périmée
  renvoie **409** : « Someone changed this record. Reload it and try again. »

| Statut | Sens |
| ------ | ---- |
| 400 | Saisie invalide (`errors` par champ) |
| 401 | Pas de session, ou identifiants faux |
| 403 | Rôle insuffisant, enregistrement restreint, `X-Requested-With` absent |
| 404 | Enregistrement introuvable |
| 409 | Version périmée, identifiant déjà pris, enregistrement encore utilisé (suppression) |
| 422 | Fichier d'import en erreur (corps : le résultat de l'import) |
| 429 | Trop de tentatives de connexion |

## Authentification et comptes

| Méthode et chemin | Qui | Corps, réponse |
| ----------------- | --- | -------------- |
| `POST /api/auth/login` | tous | `{username, password}`, puis `{username, displayName, role, language}` |
| `POST /api/auth/logout` | tous | 204 |
| `GET /api/auth/me` | connecté | `{username, displayName, role, language}` |
| `PUT /api/auth/me/language` | connecté | `{language: "en" ou "fr"}` |
| `PUT /api/auth/me/password` | connecté | `{currentPassword, newPassword}`, 204 ; ferme les autres sessions du compte |
| `GET /api/users`, `GET /api/users/{username}` | admin | `UserDto` |
| `POST /api/users` | admin | `{username, displayName, role, language?, password}`, 201 |
| `PUT /api/users/{username}` | admin | `{displayName, role, language, password?, version}` |
| `DELETE /api/users/{username}` | admin | 204 ; refusé pour son propre compte et pour le dernier admin |

Rôles : `admin`, `member` et `guest` (voir la [configuration](configuration.md#rôles)).

## Enregistrements du paysage

`GET/POST /api/domains`, `GET/PUT/DELETE /api/domains/{id}`, et de même pour `teams`, `applications`,
`data-objects` et `flows`. Tout compte connecté lit, les membres et les admins créent et modifient, les admins
suppriment. Un enregistrement encore référencé ne peut pas être supprimé (409).

| Type | Champs (saisie) | Calculés (réponse) |
| ---- | --------------- | ------------------ |
| Domaine | `name`, `description?`, `order` | |
| Équipe | `name`, `description?`, `contact?` | |
| Application | `name`, `domainId`, `teamId?`, `kind` (`app`, `bus`, `saas`, `data`, `file`, `external`), `tech?`, `description?` (markdown), `lifecycle` (`planned`, `live`, `retiring`, `retired`), `visibility` (`everyone`, `restricted`) | `sourceOf` (objets de données dont elle est la source), `completeness` (0 à 1), `restricted: false` |
| Objet de données | `name`, `sensitivity` (`public`, `internal`, `personal`, `sensitive`), `sourceApplicationId?`, `description?` | |
| Flux | `name`, `fromId`, `toId`, `type` (`api`, `event`, `batch`), `schedule?`, `dataObjectIds[]`, `ownerTeamId?`, `status` (`planned`, `live`, `deprecated`), `volume?`, `description?`, `plannedChange?` (`{summary, date?}`) | `sensitivity` (l'objet transporté le plus sensible, `internal` s'il n'y en a aucun), `completeness`, `restricted: false` |

Complétude : la part des champs recommandés qui sont remplis. Pour une application : équipe, technologie,
description, au moins un flux, cycle de vie. Pour un flux : planification, objets de données, équipe responsable,
description.

Filtres des listes, tous facultatifs : `?domainId=&teamId=&type=&status=&dataObjectId=&q=`. `q` ignore les accents
et la casse.

### Applications restreintes

Seuls les admins voient une application restreinte en entier. Les autres reçoivent `{id, restricted: true, domainId}`,
et les flux qui en partent ou y arrivent sous la forme `{id, restricted: true, fromId, toId, type}`. Ils ne peuvent
pas les modifier (403), et ni les filtres ni la recherche ne portent sur leurs champs cachés. Dans le document
OpenAPI, les listes contiennent `ApplicationDto | RestrictedApplicationDto` (et `FlowDto | RestrictedFlowDto`),
distingués par `restricted`.

## Explorateur

- `GET /api/landscape` renvoie en une réponse tout ce dont l'explorateur a besoin, avec les mêmes filtres :
  `{domains[], teams[], applications[], dataObjects[], flows[], stats: {applications, flows, domains}, generatedAt}`.
  Les filtres d'application (`domainId`, `teamId`, `q`) gardent les applications qui correspondent et les flux qui
  les touchent. Les filtres de flux (`type`, `status`, `dataObjectId`) gardent les flux qui correspondent et
  seulement les applications à leurs extrémités. Un flux vient toujours avec ses deux extrémités.
- `GET /api/search?q=` renvoie au plus 20 éléments `{kind, id, title, subtitle}`, où `kind` vaut `application`, `flow`,
  `dataObject`, `team` ou `domain`. Les noms qui commencent par le texte viennent en premier.

## Échange

- `GET /api/export?format=json|yaml`, pour tout compte connecté, télécharge le paysage au
  [format d'échange](exchange-format.md). Les applications restreintes sont omises pour les non-admins.
- `POST /api/import?dryRun=true|false`, pour les admins, prend le fichier comme corps de la requête (JSON ou YAML,
  selon `Content-Type` ou `?format=`) et renvoie `{created, updated, unchanged, errors[], dryRun, applied}`.

## Sondes

`GET /health/live` et `GET /health/ready` ne demandent pas de session. Voir l'[installation](installation.md#sondes).
