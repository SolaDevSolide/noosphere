# Noosphere

[English](README.md)

Noosphere est une application web auto-hébergée qui documente tout le système d'information d'une entreprise : les
**applications**, les **flux** qui les relient, les **données** qu'ils transportent et les **équipes** qui en sont
responsables. Elle répond à des questions comme « que reçoit la facturation, de qui et quand ? », « où vont les
données clients ? » ou « qui prévenir avant de changer cette API ? ».

- Un explorateur avec des cartes, une vue des connexions et un graphe, une loupe sur une application et le lignage des données.
- Une fiche pour chaque application et chaque flux, modifiable par les membres, avec un indicateur de complétude.
- Des applications restreintes, visibles en entier par les seuls administrateurs.
- Un format d'échange documenté (JSON ou YAML) pour importer et exporter tout le paysage.
- Une interface en anglais et en français. Un seul conteneur, à côté de PostgreSQL.

## Démarrage rapide

Avec Docker et Docker Compose :

```sh
git clone https://github.com/SolaDevSolide/noosphere.git
cd noosphere
cp .env.example .env
# Pour essayer avec les données de démonstration :
echo "NOOSPHERE_DEMO=true" >> .env
docker compose up -d --build
```

Puis ouvrir <http://localhost:8080>.

### Comptes de démonstration

Avec `NOOSPHERE_DEMO=true`, Noosphere charge « Alizé », un assureur fictif (20 applications, 36 flux, 6 domaines,
5 objets de données) et trois comptes, tous avec le mot de passe `demo` :

| Identifiant | Rôle   | Peut                                                   |
| ----------- | ------ | ------------------------------------------------------ |
| `alice`     | admin  | tout, y compris supprimer et gérer les comptes         |
| `bob`       | member | lire, créer et modifier                                |
| `claire`    | guest  | lire seulement                                         |

N'activez jamais le mode démonstration sur un serveur partagé.

Hors démonstration, le premier démarrage crée un administrateur `admin` avec le mot de passe de
`NOOSPHERE_ADMIN_PASSWORD`, ou affiche une seule fois un mot de passe aléatoire dans le journal
(`docker compose logs noosphere`).

## Configuration

Tous les réglages sont des variables d'environnement (voir [.env.example](.env.example) et
[docs/fr/configuration.md](docs/fr/configuration.md)) :

| Variable                     | Par défaut              | Rôle                                                                  |
| ---------------------------- | ----------------------- | --------------------------------------------------------------------- |
| `NOOSPHERE_DATABASE`         | (obligatoire)           | Chaîne de connexion PostgreSQL                                        |
| `NOOSPHERE_DEMO`             | `false`                 | Charger le paysage et les comptes de démonstration                    |
| `NOOSPHERE_ADMIN_USERNAME`   | `admin`                 | Premier administrateur, créé quand aucun compte n'existe              |
| `NOOSPHERE_ADMIN_PASSWORD`   | (aléatoire, journalisé) | Mot de passe de cet administrateur (8 caractères au moins)            |
| `NOOSPHERE_LOGIN_RATE_LIMIT` | `10`                    | Tentatives de connexion par minute et par adresse                     |
| `NOOSPHERE_KEYS_DIR`         | `/data/keys`            | Clés des sessions (volume Docker)                                     |
| `NOOSPHERE_BACKUPS_DIR`      | `/data/backups`         | Sauvegardes faites avant les migrations de la base                    |
| `NOOSPHERE_INSTANCE_NAME`    | `Noosphere`             | Nom écrit dans les exports                                            |
| `NOOSPHERE_PUBLIC_URL`       | (adresse de la requête) | Adresse publique écrite dans les exports                              |
| `NOOSPHERE_TRUSTED_PROXIES`  | (aucun)                 | Réseaux du reverse proxy autorisés à envoyer les en-têtes `X-Forwarded-*` |

## Ligne de commande

L'image fournit la commande `noosphere` ([docs/fr/cli.md](docs/fr/cli.md)) :

```sh
docker compose exec noosphere noosphere users create dana --role member
docker compose exec noosphere noosphere users reset-password dana
docker compose exec noosphere noosphere export --format yaml > paysage.yaml
docker compose exec -T noosphere noosphere import - --dry-run < paysage.yaml
docker compose exec noosphere noosphere demo reset --yes
```

## Documentation

- [Installation](docs/fr/installation.md)
- [Configuration](docs/fr/configuration.md)
- [API](docs/fr/api.md) (OpenAPI : [docs/openapi.json](docs/openapi.json), servi sur `/openapi/v1.json`)
- [Format d'échange](docs/fr/exchange-format.md) (schéma JSON : [docs/format/noosphere-v1.schema.json](docs/format/noosphere-v1.schema.json))
- [Ligne de commande](docs/fr/cli.md)
- [Contribuer](CONTRIBUTING.md) et [politique de sécurité](SECURITY.md) (en anglais)

## Licence

[MIT](LICENSE)
