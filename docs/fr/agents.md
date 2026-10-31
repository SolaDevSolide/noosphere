# Agents IA (MCP)

[English](../en/agents.md)

Noosphere a un serveur [Model Context Protocol](https://modelcontextprotocol.io) à l'adresse `/mcp`. Un agent IA
(assistant de bureau, assistant de code, extension d'éditeur, script) se connecte **pour une personne**, après
l'accord de cette personne sur une page de consentement. Il lit ensuite le paysage et prépare des brouillons avec les
droits de cette personne, jamais plus, et ne publie jamais : les équipes propriétaires valident toujours chaque
modification.

- [Connecter un agent](#connecter-un-agent)
- [Ce qu'un agent peut faire](#ce-quun-agent-peut-faire) : portées, plafond, brouillons
- [Outils](#outils)
- [Réglages des agents](#réglages-des-agents), quota et arrêt d'urgence
- [Mes agents et la console](#mes-agents-et-la-console)
- [llms.txt et vues Markdown](#llmstxt-et-vues-markdown)
- [Référence OAuth](#référence-oauth) pour les développeurs de clients

## Connecter un agent

Donnez à l'agent l'adresse du serveur MCP, affichée dans **Mes agents** : `https://noosphere.example.com/mcp`. Les
clients qui gèrent l'autorisation MCP trouvent le reste seuls : au premier appel, Noosphere répond 401 avec l'adresse
de ses métadonnées OAuth, le client s'enregistre, et votre navigateur ouvre la page de consentement.

1. Ajoutez le serveur au client (exemples ci-dessous), puis démarrez-le ou demandez-lui de se connecter.
2. Le navigateur ouvre la page de consentement de Noosphere. Connectez-vous si besoin. La page montre le nom de
   l'agent, l'adresse où il revient, votre nom et, en clair, ce qu'il pourra faire sous le plafond actuel.
3. Choisissez **Autoriser** (vous pouvez décocher les brouillons) ou **Refuser**. Le navigateur revient à l'agent,
   qui est connecté.

Si un administrateur a activé **approbation requise**, la connexion attend ensuite dans la console qu'un
administrateur l'approuve ; d'ici là, l'agent reçoit une erreur explicite.

### Exemples de configuration

La plupart des clients lisent un fichier JSON qui liste les serveurs. Un serveur distant en HTTP s'écrit en général :

```json
{
  "mcpServers": {
    "noosphere": {
      "type": "http",
      "url": "https://noosphere.example.com/mcp"
    }
  }
}
```

- **Assistants de bureau** (par exemple Claude Desktop) : ajoutez un connecteur personnalisé avec l'adresse
  ci-dessus dans les réglages des connecteurs, ou l'entrée JSON dans le fichier de configuration de l'application.
- **Assistants de code en ligne de commande** (par exemple Claude Code) : `claude mcp add --transport http noosphere
  https://noosphere.example.com/mcp`, puis `/mcp` dans une session pour se connecter.
- **Éditeurs** (par exemple VS Code) : ajoutez le serveur dans la configuration MCP de l'éditeur
  (`.vscode/mcp.json` : `{"servers": {"noosphere": {"type": "http", "url": "https://noosphere.example.com/mcp"}}}`),
  puis démarrez-le depuis la liste des serveurs.

Un client qui ne gère pas l'autorisation MCP peut utiliser un jeton obtenu une fois par le parcours décrit dans la
[référence OAuth](#référence-oauth), envoyé en `Authorization: Bearer <jeton>`. Les jetons durent une heure ; le
client garde le jeton de rafraîchissement pour en obtenir d'autres.

### Essayer depuis un terminal

Avec la démo sur `http://localhost:8080` (`NOOSPHERE_DEMO=true`), MCP Inspector liste les outils et en appelle un, à
partir d'un jeton (`<jeton>` : un jeton d'accès obtenu par le parcours ci-dessous) :

```sh
npx @modelcontextprotocol/inspector@0.21.2 --cli http://localhost:8080/mcp --transport http \
  --header "Authorization: Bearer <jeton>" --method tools/list
npx @modelcontextprotocol/inspector@0.21.2 --cli http://localhost:8080/mcp --transport http \
  --header "Authorization: Bearer <jeton>" --method tools/call --tool-name search --tool-arg query=billing
```

Un script écrit avec le SDK TypeScript officiel (`@modelcontextprotocol/sdk`) déroule tout le parcours avec un
`OAuthClientProvider` : `StreamableHTTPClientTransport(new URL("http://localhost:8080/mcp"), {authProvider})`, puis
`connect()` lève `UnauthorizedError` après avoir appelé `redirectToAuthorization(url)` ; ouvrez l'adresse,
autorisez, et appelez `transport.finishAuth(code)` avec le `code` de la redirection avant de vous reconnecter.

## Ce qu'un agent peut faire

Un agent voit le **minimum** de trois choses : ce que voit la personne, le **plafond** fixé par les administrateurs,
et les **portées** accordées sur la page de consentement.

| Portée | Permet | Accordée à |
| ------ | ------ | ---------- |
| `docs:read` | Lire les fiches et pages que voit la personne, dans la limite du plafond | tout le monde |
| `docs:draft` | Écrire des brouillons et les soumettre comme propositions | membres et administrateurs (pas les invités) |
| `docs:read:restricted` | Lire les fiches restreintes que voit la personne | seulement sous le plafond `asUser` |

| Plafond | Fiches visibles de tous | Notes protégées | Fiches restreintes que voit la personne |
| ------- | ----------------------- | --------------- | --------------------------------------- |
| `public` (défaut) | oui | non (`protectedNotes: withheld`) | non, même si la personne les voit |
| `public+protected` | oui | oui, quand la personne les lit | non |
| `asUser` | oui | oui, quand la personne les lit | avec `docs:read:restricted` |

- L'agent d'un invité ne lit que ce qui est partagé avec l'invité.
- Chaque refus dit pourquoi : `Refused (ceiling)` (plafond), `Refused (visibility)` (visibilité),
  `Refused (protected notes)` (notes protégées), `Refused (secret)` ou `Refused (role)` (rôle). La raison est renvoyée
  à l'agent et inscrite dans son activité.
- Les lectures de fiches restreintes et de notes protégées par un agent sont inscrites au
  [journal des accès](acces.md#journal-des-accès) au nom de la personne, avec le détail `AI agent: <nom>`.

### Brouillons et propositions

`draft_change` écrit une proposition **brouillon** (`authorKind: "agent"`, `onBehalfOf` : la personne,
`agentName`). Tant qu'elle n'est pas soumise, seuls la personne et les administrateurs la voient ; elle est listée
dans **Mes agents**, pas dans l'espace de revue. L'agent (`submit_draft`) ou la personne (depuis Mes agents) la
soumet ; les équipes propriétaires l'acceptent ou la refusent comme toute proposition, et l'agent lit la décision et
le motif avec `get_proposal_status`. Les secrets sont refusés dans les brouillons comme partout. Un agent ne publie,
ne supprime et ne restaure jamais rien lui-même.

## Outils

Les noms et descriptions sont écrits pour les modèles (en anglais) ; chaque appel écrit une ligne d'activité.

| Outil | Arguments | Réponse |
| ----- | --------- | ------- |
| `search` | `query`, `kinds?` (application, flow, dataObject, team, domain, page) | `{query, count, results: [{kind, id, title, subtitle, url}]}` (20 premiers) |
| `get_record` | `kind`, `id` | Markdown avec un en-tête YAML : champs, flux entrants et sortants, données, pages, notes protégées si permis |
| `get_page` | `id` | La page en Markdown avec un en-tête ; les liens wiki restent `[[kind:id]]` |
| `list_landscape` | `domain?`, `team?` | Domaines, équipes, applications et flux (ids, noms, cycle de vie, sensibilité) |
| `data_lineage` | `dataObjectId` | Application source, flux qui la portent, applications qui la reçoivent |
| `who_owns` | `kind`, `id` | Équipe propriétaire, son contact et ses membres, qui valide, comment proposer |
| `list_my_drafts` | | Les brouillons et propositions ouvertes de cet agent |
| `draft_change` | `kind`, `id` ou null, `operation` (create, update, delete), `fields`, `comment?` | `{saved, draftId, changedFields, warnings, next}`, ou les erreurs (`secretRefused`) |
| `submit_draft` | `draftId`, `title`, `comment?` | `{submitted, proposalId, validators, link}` |
| `get_proposal_status` | `id` | Statut, décision, motif de refus, validateurs |

Pour une modification, `fields` est fusionné avec la fiche actuelle (par exemple `{"description": "…"}`) ; pour une
création, c'est la fiche entière. Un second `draft_change` sur la même fiche met à jour le même brouillon.

Erreurs : un appel refusé est un résultat d'outil avec `isError: true` et la raison. Une connexion en pause,
révoquée ou en attente reçoit une erreur JSON-RPC de code `-32029`, avec la raison et sa fin.

## Réglages des agents

Les administrateurs les changent dans les réglages (`GET` pour toute personne connectée, `PUT /api/settings/agents`
avec `version`) :

| Réglage | Défaut | Sens |
| ------- | ------ | ---- |
| `enabled` | `true` | Désactivé : les agents reçoivent 401 et aucune nouvelle connexion n'est possible ; connexions et jetons sont gardés et refonctionnent à la réactivation. |
| `ceiling` | `public` | Voir [ce qu'un agent peut faire](#ce-quun-agent-peut-faire). |
| `quotaPerHour` | `600` | Appels par connexion et par heure d'horloge (1 à 100 000). |
| `whoConnects` | `anyMember` | `approvalRequired` : les nouvelles connexions des membres et invités attendent un administrateur. |
| `dynamicRegistration` | `true` | Désactivé : les clients doivent s'identifier par un Client ID Metadata Document. |
| `anonymousLlms` | `false` | Activé : `/llms.txt` et les vues Markdown se lisent sans connexion (fiches visibles de tous, sans notes protégées). |

**Quota.** Au-delà du quota, l'appel reçoit l'erreur JSON-RPC `-32029` (« Quota reached: this agent made N calls
this hour… It is paused until HH:00 UTC »), une ligne de quota est écrite et la connexion est en pause jusqu'à
l'heure suivante.

**Arrêt d'urgence.** `POST /api/agents/stop-all {"confirm": true}` (administrateurs) révoque d'un coup toutes les
connexions et tous les jetons, annule les autorisations en cours, écrit une ligne d'activité par connexion et un
avertissement dans le journal du serveur. Chacun reconnecte ensuite ses agents.

L'activité est gardée 90 jours (`NOOSPHERE_AGENT_ACTIVITY_DAYS`) ; les jetons expirés sont supprimés chaque jour.

## Mes agents et la console

- **Mes agents** (chaque personne) : ses connexions avec leur état (`pending`, `active`, `paused`, `revoked`), les
  appels de la dernière heure, ce que chaque agent peut faire, son activité, ses brouillons et propositions. La
  révocation prend effet tout de suite.
- **Console** (administrateurs) : toutes les connexions avec leur personne, l'approbation des connexions en attente,
  toute l'activité avec des filtres, et l'arrêt d'urgence.

Les deux sont dans le menu du compte (**Mes agents**, et **Agents IA** pour les administrateurs) ; la page de
consentement est `/oauth/consent`. Le [guide utilisateur](guide-utilisateur.md#agents-ia) les parcourt.

API : voir [API, agents IA](api.md#agents-ia).

## llms.txt et vues Markdown

| Adresse | Contenu |
| ------- | ------- |
| `/llms.txt` | Un index au format [llmstxt.org](https://llmstxt.org) : une section par type, un lien et une ligne par fiche |
| `/llms-full.txt` | Toutes les fiches que le lecteur peut voir, en Markdown, dans un seul fichier |
| `/{kind}/{id}.md` | Une fiche : `application`, `flow`, `data-object`, `team`, `domain` ou `page` |

Lecteurs : une session connectée (les droits de la personne), un jeton porteur avec `docs:read` (les droits de
l'agent ; l'appel compte dans son quota et son activité), ou n'importe qui si `anonymousLlms` est activé. Sinon 401
avec le même en-tête `WWW-Authenticate` que `/mcp`. Une fiche que le lecteur ne peut pas voir donne 403, une fiche
inconnue 404.

## Référence OAuth

Noosphere est le serveur d'autorisation OAuth 2.1 et la ressource protégée de son propre point d'accès MCP.

| Adresse | Rôle |
| ------- | ---- |
| `/.well-known/oauth-protected-resource` (aussi `.../oauth-protected-resource/mcp`) | RFC 9728 : `resource` = `{base}/mcp`, le serveur d'autorisation, les portées |
| `/.well-known/oauth-authorization-server` | Métadonnées RFC 8414 (`client_id_metadata_document_supported: true`) |
| `GET /oauth/authorize` | Code d'autorisation avec PKCE `S256` seulement ; redirige vers la page de consentement `/oauth/consent?request={id}` |
| `POST /oauth/token` | `authorization_code` et `refresh_token` (formulaire, clients publics, sans secret) |
| `POST /oauth/revoke` | RFC 7009 ; révoquer un jeton de rafraîchissement termine toute la connexion |
| `POST /oauth/register` | Enregistrement dynamique RFC 7591 (si activé ; 20 par minute et par adresse) |

- **Clients** : ils s'identifient soit par un **Client ID Metadata Document** (le `client_id` est une adresse https
  qui sert un JSON avec `client_id` égal à cette adresse, `client_name` et `redirect_uris`), soit par un identifiant
  d'enregistrement dynamique. Les documents sont lus en https seulement, 64 Ko au plus, en 5 secondes, sans
  redirection, et jamais depuis une adresse privée, de boucle locale ou lien-local (y compris après résolution DNS).
- **Adresses de retour** : https, `http` sur une adresse de boucle locale (`localhost`, `127.0.0.1`, `[::1]`, tout
  port) pour les clients natifs, ou un schéma privé (`com.example.app:/cb`). `javascript:`, `data:`, `file:` et les
  fragments sont refusés.
- **Jetons** : chaînes aléatoires opaques ; seule leur empreinte SHA-256 est stockée. Les jetons d'accès durent
  1 heure, les jetons de rafraîchissement 30 jours et changent à chaque usage ; réutiliser un ancien jeton de
  rafraîchissement ou un code révoque les jetons qui en sont issus.
- `resource`, s'il est envoyé, doit valoir `{base}/mcp`. Les erreurs suivent la RFC 6749 ; celles qui portent sur
  le client ou son adresse de retour s'affichent sur la page de consentement au lieu d'être renvoyées.

La page de consentement est une route du frontend. Elle appelle (cookie de session, et `X-Requested-With` en POST) :

| Méthode et chemin | Réponse |
| ----------------- | ------- |
| `GET /api/oauth/requests/{id}` | `{clientName, clientKind, clientUri, redirectHost, person, scopes: [{scope, grantable, text}], capabilities, ceiling, approvalRequired, alreadyConnected, expiresAt}` |
| `POST /api/oauth/requests/{id}/approve` | `{scopes?}` (parmi celles demandées), puis `{redirectTo, pendingApproval}` |
| `POST /api/oauth/requests/{id}/deny` | `{redirectTo}` avec `error=access_denied` |

La page envoie ensuite le navigateur vers `redirectTo`. Une demande vaut 10 minutes et n'est décidée qu'une fois
(409 ensuite).
