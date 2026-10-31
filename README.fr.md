# Noosphere

[English](README.md)

Noosphere est une application web auto-hébergée qui documente tout le système d'information d'une entreprise : les
**applications**, les **flux** qui les relient, les **données** qu'ils transportent et les **équipes** qui en sont
responsables. Elle répond à des questions comme « que reçoit la facturation, de qui et quand ? », « où vont les
données clients ? » ou « qui prévenir avant de changer cette API ? ».

- Un explorateur avec des cartes, une vue des connexions et un graphe, une loupe sur une application et le lignage des données.
- Une fiche pour chaque application et chaque flux, avec un indicateur de complétude.
- Tout le monde lit, chacun propose, l'équipe responsable valide : des propositions de modification avec une
  comparaison par champ, validées par les équipes responsables des enregistrements.
- Un historique des révisions sur chaque fiche et chaque page, avec comparaison et restauration, et une corbeille.
- Des pages de documentation en Markdown, en arborescence, rattachées aux fiches, avec des liens wiki
  (`[[application:billing]]`), des rétroliens et la détection des liens cassés, un éditeur avec aperçu en direct,
  suggestions de liens et trois modèles de page.
- Des applications et des pages restreintes avec des listes d'accès (équipes et personnes), des fiches partagées
  avec des invités, des notes protégées pour les détails d'exploitation et un journal des accès pour les
  administrateurs. Une fenêtre **Qui peut voir et modifier ?** sur chaque fiche, et une page **Accès** pour les
  administrateurs.
- Les secrets collés par erreur (jetons, clés, mots de passe) sont refusés : Noosphere documente où ils se trouvent.
- Une sensibilité calculée pour chaque application, d'après ses données et les flux qui la touchent, avec un filtre
  du paysage sur ce niveau.
- Un format d'échange documenté (JSON ou YAML) pour importer et exporter le paysage : ce que vous voyez ou, pour
  les administrateurs, un export complet inscrit au journal des accès.
- Des **agents IA** se connectent par MCP (`/mcp`) pour une personne, après une page de consentement : ils
  cherchent, lisent et rédigent des brouillons avec les droits de cette personne, sous un plafond fixé par un
  administrateur et un quota horaire, et ne publient jamais. Chaque appel figure dans leur activité ; **Mes agents**
  et une console des agents les révoquent, un par un ou tous d'un coup. `/llms.txt`, `/llms-full.txt` et une vue
  Markdown de chaque fiche.
- **Liaisons avec d'autres applications**, gestion-mep d'abord : les applications appairées lisent le catalogue et
  envoient leurs déploiements, affichés dans un bloc « Déploiements et changements » sur les fiches d'application et
  de flux. Les administrateurs appairent, confirment, restreignent et coupent les liaisons sur la page Liaisons. Les
  requêtes sont signées (RFC 9421) et les corps chiffrés (JWE), avec des empreintes confirmées des deux côtés.
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
5 objets de données, 7 pages de documentation, un historique sur Billing, quelques propositions, une application Fraud scoring restreinte, des notes protégées sur Billing et un portail client partagé avec `claire`) et quatre
comptes, tous avec le mot de passe `demo` :

| Identifiant | Rôle   | Équipes                     | Peut                                                                |
| ----------- | ------ | --------------------------- | ------------------------------------------------------------------- |
| `alice`     | admin  |                             | tout, y compris les domaines, les équipes, les comptes et les réglages |
| `bob`       | member | Team Finance, Team Payments | publier sur les enregistrements de ses équipes, valider les propositions |
| `claire`    | guest  |                             | lire les noms, et le portail client partagé avec elle               |
| `david`     | member | Team CRM                    | publier pour Team CRM, proposer ailleurs                            |

Connectez-vous en `bob` pour valider les deux propositions que `david` a envoyées à Team Finance. La
[boîte à outils de démo](docs/fr/demo.md) change de personnage d'un clic et fait agir un collègue, une autre équipe, un
validateur, un agent IA ou l'invitée, pour essayer seul les fonctions collaboratives : ouvrez-la avec le bouton
**Démo** en bas de chaque page.

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
| `NOOSPHERE_INSTANCE_NAME`    | `Noosphere`             | Nom de l'instance tant qu'un administrateur ne l'a pas réglé          |
| `NOOSPHERE_PUBLIC_URL`       | (adresse de la requête) | Adresse publique écrite dans les exports                              |
| `NOOSPHERE_TRUSTED_PROXIES`  | (aucun)                 | Réseaux du reverse proxy autorisés à envoyer les en-têtes `X-Forwarded-*` |
| `NOOSPHERE_ACCESS_LOG_DAYS`  | `365`                   | Durée de conservation du journal des accès, en jours                  |
| `NOOSPHERE_AGENT_ACTIVITY_DAYS` | `90`                 | Durée de conservation de l'activité des agents IA, en jours           |
| `NOOSPHERE_REGISTRATION_RATE_LIMIT` | `20`             | Enregistrements de clients OAuth par minute et par adresse cliente    |
| `NOOSPHERE_LINK_BOOTSTRAP_CODE` | (aucun)          | Code d'appairage prédéfini pour gestion-mep démarré en même temps     |

## Ligne de commande

L'image fournit la commande `noosphere` ([docs/fr/cli.md](docs/fr/cli.md)) :

```sh
docker compose exec noosphere noosphere users create dana --role member
docker compose exec noosphere noosphere users reset-password dana
docker compose exec noosphere noosphere export --format yaml > paysage.yaml
docker compose exec -T noosphere noosphere import - --dry-run < paysage.yaml
docker compose exec noosphere noosphere demo reset --yes
docker compose exec noosphere noosphere link code --expires 30m
```

## Documentation

- [Installation](docs/fr/installation.md)
- [Configuration](docs/fr/configuration.md)
- [Guide utilisateur](docs/fr/guide-utilisateur.md)
- [Propositions et validation](docs/fr/proposals.md)
- [Historique](docs/fr/history.md)
- [Pages](docs/fr/pages.md)
- [Accès, notes protégées et journal des accès](docs/fr/acces.md)
- [Agents IA (MCP)](docs/fr/agents.md), avec llms.txt et les vues Markdown
- [Boîte à outils de démo](docs/fr/demo.md)
- [Liaisons avec d'autres applications (gestion-mep d'abord)](docs/fr/liaisons.md)
- [API](docs/fr/api.md) (OpenAPI : [docs/openapi.json](docs/openapi.json), servi sur `/openapi/v1.json`)
- [Format d'échange](docs/fr/exchange-format.md) (schéma JSON : [docs/format/noosphere-v1.schema.json](docs/format/noosphere-v1.schema.json))
- [Ligne de commande](docs/fr/cli.md)
- [Contribuer](CONTRIBUTING.md) et [politique de sécurité](SECURITY.md) (en anglais)

## Licence

[MIT](LICENSE)
