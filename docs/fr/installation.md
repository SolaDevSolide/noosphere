# Installation

[English](../en/installation.md)

Noosphere tourne dans un seul conteneur (l'application web, son API et la ligne de commande `noosphere`), à côté
d'une base PostgreSQL 16.

## Avec Docker Compose

```sh
git clone https://github.com/SolaDevSolide/noosphere.git
cd noosphere
cp .env.example .env      # puis le modifier : au moins POSTGRES_PASSWORD
docker compose up -d --build
docker compose logs noosphere   # mot de passe du premier administrateur, si NOOSPHERE_ADMIN_PASSWORD est vide
```

Par défaut, Noosphere écoute sur `127.0.0.1:8080` (`NOOSPHERE_BIND`, `NOOSPHERE_PORT`). Placez devant un reverse
proxy HTTPS (nginx, Caddy, Traefik, IIS) et indiquez son réseau dans `NOOSPHERE_TRUSTED_PROXIES` : les cookies de
session sont alors marqués Secure et la limite de connexions voit la vraie adresse du client.

Pour essayer Noosphere avec le paysage de démonstration « Alizé », mettez `NOOSPHERE_DEMO=true` dans `.env`.
Comptes : `alice` (admin), `bob` (member), `claire` (guest), mot de passe `demo`.

## Volumes

| Volume    | Chemin dans le conteneur | Contenu                                                  |
| --------- | ------------------------ | -------------------------------------------------------- |
| `db-data` | (PostgreSQL)             | La base de données                                       |
| `keys`    | `/data/keys`             | Clés des cookies de session (les sessions survivent aux redémarrages) |
| `backups` | `/data/backups`          | Copie de toutes les tables faite avant une migration     |

## Mises à jour

Récupérez la nouvelle version puis lancez `docker compose up -d --build`. Les migrations de la base s'appliquent au
démarrage. Si la base contient déjà des données, toutes les tables sont d'abord copiées dans
`/data/backups/before-migration-<date>.json`. `noosphere migrate` les applique à la main.

## Sondes

- `GET /health/live` : le processus répond.
- `GET /health/ready` : la base est joignable et à jour (503 sinon). Le `HEALTHCHECK` de l'image l'interroge par
  `noosphere health`.

## Sans Docker

Construisez le front (`cd frontend && npm ci && npm run build`), publiez le back
(`dotnet publish backend/src/Noosphere.Api -c Release -o out`), copiez `frontend/dist` dans `out/wwwroot`, définissez
les variables de la [configuration](configuration.md) puis lancez `dotnet out/Noosphere.dll`.
