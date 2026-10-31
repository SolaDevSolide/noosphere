# Liaisons avec gestion-mep

[English](../en/links.md)

Une liaison relie cette instance de Noosphere à une instance de
[gestion-mep](https://github.com/SolaDevSolide/gestion-mep), qui planifie les mises en production (MEP). Une fois
liées :

- gestion-mep lit le **catalogue** de Noosphere (applications, flux, domaines, équipes) et suit les applications
  choisies par son administrateur ;
- Noosphere prévient gestion-mep quand une fiche partagée change (**avis de changement**) ;
- gestion-mep envoie les **déploiements** touchant les applications suivies. Chaque fiche d'application et de flux
  montre les déploiements à venir et passés, et les agents IA les voient dans `get_record` et les vues Markdown.

Seuls les administrateurs gèrent les liaisons. Les applications restreintes (avec une liste d'accès) ne sont jamais
envoyées, ni les flux qui les touchent, ni les notes protégées, les pages ou l'historique.

## Appairage

1. Dans Noosphere, créez un code d'appairage : `noosphere link code` ou `POST /api/links/codes`. Un code fait
   12 caractères affichés `XXXX-XXXX-XXXX` (base32 de Crockford : ni I, ni L, ni O, ni U), valable 15 minutes par
   défaut (jusqu'à 24 h), à usage unique. Il n'est affiché qu'une fois.
2. Dans gestion-mep, saisissez l'URL de ce Noosphere (son `NOOSPHERE_PUBLIC_URL`) et le code. gestion-mep appelle
   `POST /link/v1/pair` ; chaque côté crée ses clés et une liaison **en attente**.
3. Les deux côtés affichent la même **empreinte**, quatre mots et un nombre (par exemple `cedar anchor 8451 arbor`).
   Comparez-les (au téléphone, de vive voix), puis confirmez de chaque côté :
   `noosphere link confirm <id> --fingerprint "..."` ici, `gestion-mep liaison confirmer <id> --empreinte "..."`
   là-bas.
4. La liaison est **active**. Tant que gestion-mep n'a pas confirmé, Noosphere affiche « en attente de confirmation
   sur gestion-mep » et les requêtes de données répondent `403 link-not-confirmed`.

Un code faux compte comme un échec : après 5 MAC faux, le code est grillé (`code-burnt`). Les codes sont stockés
hachés.

Les capacités se choisissent avec le code (les trois par défaut) et se modifient ensuite
(`PUT /api/links/{id}/capabilities`) :

| Capacité | Permet |
| -------- | ------ |
| `catalog:read` | à gestion-mep de lire le catalogue (`GET /link/v1/catalog`) |
| `notices:send` | à Noosphere d'envoyer des avis de changement à gestion-mep |
| `deployments:receive` | à gestion-mep d'envoyer des déploiements (`POST /link/v1/deployments`) |

**Renouveler les clés** (`noosphere link renew <id>`) crée de nouvelles clés et prévient le pair, par une requête
signée avec l'ancienne clé : l'empreinte change, sans nouvelle confirmation. Les requêtes signées avec l'ancienne
clé restent acceptées 5 minutes. **Couper** (`noosphere link cut <id> --yes`) prévient le pair (au mieux) et efface
les clés ; c'est définitif. Les déploiements reçus restent comme historique, sans lien vers gestion-mep.

## Résumé du protocole

Le contrat partagé fait référence ; en bref :

- Points d'accès sous `/link/v1` (hors `/api` : ni cookie, ni CSRF), limités à 120 requêtes par minute et par
  liaison. Noosphere sert `pair`, `status`, `catalog`, `deployments`, `rekey`, `unpair` ; gestion-mep sert
  `notices`, `status`, `rekey`, `unpair`.
- Chaque côté a, par liaison, une clé de signature P-256 et une clé de chiffrement P-256. Les clés privées sont
  chiffrées au repos par la Data Protection d'ASP.NET Core (le trousseau de `NOOSPHERE_KEYS_DIR`) et ne quittent
  jamais le serveur. Les identifiants de clé sont des empreintes RFC 7638.
- Chaque requête sauf `pair`, et chaque réponse, est signée par **HTTP Message Signatures** (RFC 9421,
  `ecdsa-p256-sha256`, `tag="noosphere-link"`), sur `@method`, `@path`, `@query` et `content-digest` (RFC 9530)
  pour les requêtes, `@status` et `content-digest` pour les réponses. `created` doit être à moins de 5 minutes ;
  chaque `nonce` n'est accepté qu'une fois.
- Les corps sont des **JWE** compacts (`ECDH-ES` + `A256GCM`) pour la clé de chiffrement du pair, envoyés en
  `application/jose`.
- Les refus sont en `application/problem+json` avec `type: https://noosphere.dev/problems/<raison>`.

`@path` est le chemin complet de l'URL appelée par l'émetteur. Derrière un reverse proxy qui ajoute un préfixe,
indiquez ce préfixe dans `NOOSPHERE_PUBLIC_URL` (par exemple `https://outils.example.com/noosphere`) : Noosphere
accepte une signature sur le chemin avec le préfixe de `NOOSPHERE_PUBLIC_URL`, avec le `PathBase` de la requête, ou
sans préfixe.

## Ligne de commande

| Commande | Effet |
| -------- | ----- |
| `noosphere link code [--expires 15m\|2h] [--capabilities catalog:read,notices:send,deployments:receive]` | Crée un code d'appairage, affiché une fois. |
| `noosphere link list [--json]` | Liste les liaisons : état, confirmation du pair, instance, empreinte. |
| `noosphere link confirm <id> --fingerprint "<quatre mots>"` | Confirme une liaison en attente après comparaison des empreintes. |
| `noosphere link renew <id>` | Renouvelle les clés de ce côté et prévient le pair. |
| `noosphere link cut <id> --yes` | Coupe la liaison et efface ses clés (code de sortie 4 sans `--yes`). |

`<id>` est l'identifiant de la liaison ou ses premiers caractères (au moins 4). Une liaison inconnue sort avec 5.

## Docker Compose avec gestion-mep

Démarré avec Noosphere, gestion-mep s'appaire à son premier démarrage avec un code prédéfini (au moins 16 lettres ou
chiffres, à usage unique, valable 24 heures après le démarrage de Noosphere). Les administrateurs comparent et
confirment quand même l'empreinte des deux côtés.

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
  # db, la base de gestion-mep et les volumes comme dans le compose de chaque projet
```

Mettez un `LINK_CODE` long et aléatoire dans `.env` (par exemple `openssl rand -hex 12`). Hors d'un réseau Docker,
utilisez HTTPS. Gardez le volume `keys` : sans lui, les clés des liaisons sont illisibles après un redémarrage.

## Dépannage

Les requêtes refusées sont inscrites au journal des échanges (`GET /api/links/{id}/exchanges`, ou
`GET /api/links/exchanges` pour celles de clés inconnues), conservé 90 jours.

| Raison | Statut | À vérifier |
| ------ | ------ | ---------- |
| `code-unknown` | 404 | Aucun code actif ne commence par ces 4 caractères : faute de frappe, code expiré (15 min par défaut ; créez-en un autre, ou `--expires 2h` ; le code d'amorçage dure 24 h après le démarrage), ou code d'une autre instance. |
| `bad-mac` | 401 | Le code est faux après ses 4 premiers caractères. 5 échecs le grillent. |
| `code-used` / `code-burnt` | 409 / 410 | Créez un nouveau code. |
| `unknown-key` | 401 | Le pair utilise une clé inconnue ici : liaison coupée ou jamais appairée ici, ou clés renouvelées pendant que le pair était injoignable. Appairez à nouveau. |
| `link-cut` | 401 | La liaison a été coupée de ce côté. Appairez à nouveau. |
| `link-not-confirmed` | 403 | Confirmez l'empreinte des deux côtés. |
| `clock-skew` | 401 | Les horloges diffèrent de plus de 5 minutes : vérifiez NTP sur les deux hôtes. |
| `bad-digest` | 401 | Le corps a changé en route (un proxy qui réécrit les corps ?). |
| `bad-signature` | 401 | En général un proxy qui change le chemin ou la requête : indiquez le préfixe public dans `NOOSPHERE_PUBLIC_URL`, et ne réécrivez pas la query. |
| `replay` | 401 | Un nonce envoyé deux fois : un proxy qui réessaie, ou une requête rejouée. |
| `capability-missing` | 403 | Accordez la capacité (`PUT /api/links/{id}/capabilities`). |
| `bad-body` | 400 | Le corps déchiffré ne suit pas le contrat (erreurs par champ dans `detail`). |
| (429) | 429 | Plus de 120 requêtes par minute pour cette liaison (ou cette adresse, sans signature). |

## Démo

Le paysage de démo comprend une liaison avec une instance fictive de gestion-mep, « MEP Alizé », active, avec des
déploiements sur Billing, CRM, Payments, Portal et Claims, et quelques échanges. Aucun vrai pair n'est appelé : les
appels sortants vers elle sont sautés et inscrits comme `demo`.
