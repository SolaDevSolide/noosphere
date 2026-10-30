# Configuration

[English](../en/configuration.md)

Chaque réglage est une variable d'environnement qui commence par `NOOSPHERE_`. Avec Docker Compose, placez-les dans
`.env` (voir [.env.example](../../.env.example)).

| Variable                     | Par défaut                                 | Description |
| ---------------------------- | ------------------------------------------ | ----------- |
| `NOOSPHERE_DATABASE`         | (obligatoire)                              | Chaîne de connexion PostgreSQL, par exemple `Host=db;Port=5432;Database=noosphere;Username=noosphere;Password=...`. |
| `NOOSPHERE_DEMO`             | `false`                                    | `true` charge le paysage de démonstration « Alizé » quand la base est vide et crée les comptes `alice` (admin), `bob` (member) et `claire` (guest) avec le mot de passe `demo`. |
| `NOOSPHERE_ADMIN_USERNAME`   | `admin`                                    | Identifiant du premier administrateur, créé au démarrage quand aucun compte n'existe (hors démonstration). |
| `NOOSPHERE_ADMIN_PASSWORD`   | (vide)                                     | Son mot de passe, 8 caractères au moins. Vide : un mot de passe aléatoire est écrit une seule fois dans le journal. Changez-le après la première connexion. |
| `NOOSPHERE_LOGIN_RATE_LIMIT` | `10`                                       | Tentatives de connexion permises par minute et par adresse ; au-delà, 429. |
| `NOOSPHERE_KEYS_DIR`         | `/data/keys` dans l'image                  | Dossier des clés qui protègent les cookies de session. Sans lui, les sessions se ferment à chaque recréation du conteneur. |
| `NOOSPHERE_BACKUPS_DIR`      | `/data/backups` dans l'image               | Avant de migrer une base qui contient des données, toutes les tables y sont copiées en JSON. Vide : pas de sauvegarde (un avertissement est journalisé). |
| `NOOSPHERE_INSTANCE_NAME`    | `Noosphere`                                | Écrit dans `source.name` des exports. |
| `NOOSPHERE_PUBLIC_URL`       | (adresse de la requête)                    | Écrit dans `source.url` des exports, par exemple `https://noosphere.exemple.fr`. |
| `NOOSPHERE_TRUSTED_PROXIES`  | (aucun ; `172.16.0.0/12` dans le compose)  | Réseaux (CIDR) ou adresses du reverse proxy autorisés à envoyer `X-Forwarded-For` et `X-Forwarded-Proto`. |

Les variables standard d'ASP.NET Core s'appliquent aussi, par exemple `ASPNETCORE_URLS` (l'image écoute sur
`http://+:8080`) et `Logging__LogLevel__Default`.

## Rôles

| Rôle     | Lire | Créer et modifier | Supprimer, importer, gérer les comptes | Voir les applications restreintes |
| -------- | ---- | ----------------- | -------------------------------------- | --------------------------------- |
| `admin`  | oui  | oui               | oui                                    | oui                               |
| `member` | oui  | oui               | non                                    | non                               |
| `guest`  | oui  | non               | non                                    | non                               |

Qui ne peut pas voir une application restreinte n'en reçoit que l'identifiant et le domaine, et les flux qui la
touchent seulement avec leur identifiant, leurs extrémités et leur type.
