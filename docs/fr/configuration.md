# Configuration

[English](../en/configuration.md)

Chaque réglage est une variable d'environnement qui commence par `NOOSPHERE_`. Avec Docker Compose, placez-les dans
`.env` (voir [.env.example](../../.env.example)).

| Variable                     | Par défaut                                 | Description |
| ---------------------------- | ------------------------------------------ | ----------- |
| `NOOSPHERE_DATABASE`         | (obligatoire)                              | Chaîne de connexion PostgreSQL, par exemple `Host=db;Port=5432;Database=noosphere;Username=noosphere;Password=...`. |
| `NOOSPHERE_DEMO`             | `false`                                    | `true` charge le paysage de démonstration « Alizé » quand la base est vide (avec des pages, un historique et des propositions) et crée les comptes `alice` (admin), `bob` (member, dans Team Finance et Team Payments), `claire` (guest) et `david` (member, dans Team CRM) avec le mot de passe `demo`. |
| `NOOSPHERE_ADMIN_USERNAME`   | `admin`                                    | Identifiant du premier administrateur, créé au démarrage quand aucun compte n'existe (hors démonstration). |
| `NOOSPHERE_ADMIN_PASSWORD`   | (vide)                                     | Son mot de passe, 8 caractères au moins. Vide : un mot de passe aléatoire est écrit une seule fois dans le journal. Changez-le après la première connexion. |
| `NOOSPHERE_LOGIN_RATE_LIMIT` | `10`                                       | Tentatives de connexion permises par minute et par adresse ; au-delà, 429. |
| `NOOSPHERE_KEYS_DIR`         | `/data/keys` dans l'image                  | Dossier des clés qui protègent les cookies de session. Sans lui, les sessions se ferment à chaque recréation du conteneur. |
| `NOOSPHERE_BACKUPS_DIR`      | `/data/backups` dans l'image               | Avant de migrer une base qui contient des données, toutes les tables y sont copiées en JSON. Vide : pas de sauvegarde (un avertissement est journalisé). |
| `NOOSPHERE_INSTANCE_NAME`    | `Noosphere`                                | Nom de l'instance tant qu'un administrateur ne l'a pas réglé (`PUT /api/settings`) ; affiché dans l'en-tête et écrit dans `source.name` des exports. |
| `NOOSPHERE_PUBLIC_URL`       | (adresse de la requête)                    | Écrit dans `source.url` des exports, par exemple `https://noosphere.exemple.fr`. |
| `NOOSPHERE_TRUSTED_PROXIES`  | (aucun ; `172.16.0.0/12` dans le compose)  | Réseaux (CIDR) ou adresses du reverse proxy autorisés à envoyer `X-Forwarded-For` et `X-Forwarded-Proto`. |

Les variables standard d'ASP.NET Core s'appliquent aussi, par exemple `ASPNETCORE_URLS` (l'image écoute sur
`http://+:8080`) et `Logging__LogLevel__Default`.

## Rôles

| Rôle     | Lire | Publier des modifications | Proposer des modifications | Domaines, équipes, import, comptes, réglages, corbeille | Voir les enregistrements restreints |
| -------- | ---- | ------------------------- | -------------------------- | ------------------------------------------------------- | ----------------------------------- |
| `admin`  | oui  | oui                       | oui                        | oui                                                     | oui                                 |
| `member` | oui  | enregistrements de ses équipes | oui                   | non                                                     | non                                 |
| `guest`  | oui  | non                       | non                        | non                                                     | non                                 |

Les membres publient (créent, modifient, suppriment, restaurent) les enregistrements dont l'une de leurs équipes est
responsable, et envoient des propositions pour les autres : voir [propositions et validation](proposals.md). Les
administrateurs gèrent les appartenances aux équipes.

Qui ne peut pas voir une application restreinte n'en reçoit que l'identifiant et le domaine, et les flux qui la
touchent seulement avec leur identifiant, leurs extrémités et leur type. Les pages restreintes apparaissent sous la
forme `{id, restricted: true, parentId, order}`.

## Réglages de l'instance

Les administrateurs règlent le nom de l'instance (« Noosphere » par défaut), le nom de l'organisation et la langue par
défaut des nouveaux comptes (`GET/PUT /api/settings`). L'en-tête affiche le nom de l'instance avec celui de
l'organisation en dessous, et l'onglet du navigateur utilise le nom de l'instance. Le nom technique `noosphere` ne
change pas.
