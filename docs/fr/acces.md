# Accès, notes protégées et journal des accès

[English](../en/access.md)

Noosphere est ouvert par défaut : chaque membre lit tout le paysage. Cette page décrit les exceptions : les fiches
restreintes, les invités, les notes protégées, le contrôle des secrets et le journal des accès.

## Qui voit une fiche

| Lecteur | Fiche visible de tous | Fiche restreinte |
| ------- | --------------------- | ---------------- |
| Administrateur | en entier | en entier |
| Membre de l'équipe propriétaire | en entier | en entier |
| Membre d'une équipe de la liste d'accès, ou membre nommé dans la liste | en entier | en entier |
| Autre membre | en entier | ébauche : `{id, restricted: true, domainId}` |
| Invité nommé dans la liste d'accès | en entier | en entier |
| Autre invité | aperçu : `{id, restricted: false, shared: false, name, domainId, kind}` | ébauche |

- Les **applications et les pages** portent une liste d'accès : `access: {teamIds: [], usernames: []}` (200 entrées
  au plus). Sur une fiche restreinte, elle nomme qui la voit en plus des administrateurs et de l'équipe propriétaire.
  Sur toute fiche, les invités qu'elle nomme voient la fiche en entier : c'est ainsi qu'on **partage une fiche avec un
  invité**.
- Les **flux** n'ont pas de liste propre. Un flux qui touche une application restreinte est vu par ceux qui voient
  **toutes** ses extrémités restreintes ; les autres reçoivent `{id, restricted: true, fromId, toId, type}`. Un invité
  voit un flux en entier quand l'une de ses extrémités est partagée avec lui, sinon un aperçu (`{id, restricted:
  false, shared: false, name, fromId, toId, type}`).
- Les **pages** non partagées avec un invité sont des aperçus : `{id, restricted: false, shared: false, title,
  parentId, order}`.
- Les **objets de données, domaines et équipes** restent lisibles. Un objet de données dont la source est une
  application restreinte garde son `sourceApplicationId`.
- Les listes, le paysage, la recherche, l'historique et les exports suivent les mêmes règles. La recherche ne
  trouve que le nom d'un aperçu, et rien d'une ébauche.

Les administrateurs et les membres de l'équipe propriétaire publient, fiches restreintes comprises : ils modifient la
liste d'accès et peuvent restreindre une fiche. Un changement qui cacherait la fiche à son propre auteur est refusé
(403). Les membres qui voient une fiche en entier peuvent proposer des changements.

`GET /api/{kind}/{id}/access` répond à « qui peut voir et modifier ? » pour une fiche :
`{visibility, owningTeam, readers: {everyoneExceptGuests, teams, users}, publishers: {teams, admins}, proposers,
protectedNotes: {exists, readable}, canManage}`. `readers.users` sont les personnes qui lisent la fiche en entier en
dehors des équipes citées (personnes nommées d'une fiche restreinte, invités avec qui elle est partagée). Pour un flux,
`readers.teams` sont les équipes qui lisent toutes les extrémités restreintes. Renvoie 403 à qui ne voit pas la fiche
en entier.

Supprimer un compte le retire de toutes les listes d'accès. Une équipe citée dans une liste d'accès ne peut pas être
supprimée (409).

## Notes protégées

Les applications et les flux ont des `protectedNotes` (Markdown, 20 000 caractères au plus) pour les détails
d'exploitation dont tout le monde n'a pas besoin : qui appeler, où se trouve le compte de secours, quel chemin du
coffre contient les identifiants.

- Elles sont lues par les administrateurs, les membres de l'équipe propriétaire et les membres de la liste d'accès
  (pour un flux : de l'une ou l'autre extrémité). **Les invités ne les lisent jamais**, même quand la fiche est
  partagée avec eux.
- Les autres lecteurs reçoivent `protectedNotes: null` et `protectedNotesHidden: true`. Les instantanés de
  l'historique, les comparaisons, les propositions et la recherche affichent `"••• protected"` au lieu du texte, et
  les exports omettent les notes.
- Un `PUT` ou une proposition sans `protectedNotes` (ou avec `"••• protected"`) garde les notes actuelles ; un texte
  vide les efface. Quiconque peut proposer écrit donc des notes sans lire les actuelles ; l'auteur d'une proposition
  voit ce qu'il a écrit, les validateurs voient les deux côtés.

## Les secrets sont refusés

Noosphere documente où sont les secrets, jamais leur valeur. Chaque écriture (fiches, pages, propositions, motifs de
refus, réglages, imports) est contrôlée : clés privées, clés AWS, jetons GitHub, Slack, Stripe et Google, clés de
stockage Azure, JSON Web Tokens, mots de passe dans une URL et affectations `password=`. Une détection renvoie
**422** avec, pour chaque champ :

> This looks like a secret (GitHub token). Noosphere is documentation, not a vault: store it in your secret manager
> and write where it lives instead.

La valeur n'est jamais répétée. Les espaces réservés passent : `<password>`, `***`, `changeme`, `${VAR}`, `$VAR`,
`%VAR%`, `{{var}}`.

## Sensibilité d'une application

Chaque application reçoit une `sensitivity` calculée : la plus sensible des objets de données dont elle est la source
et des flux qui la touchent, `internal` s'il n'y en a aucun. `sensitivityReasons` donne les cinq principales raisons
(`{kind: dataObject|flow, id, name, sensitivity}`). Les flux que le lecteur ne voit pas ne comptent pas. Filtre
`?sensitivity=public|internal|personal|sensitive` sur `/api/applications`, `/api/flows` (le niveau propre du flux)
et `/api/landscape`.

## Exports

`GET /api/export` écrit ce que le lecteur voit en entier (`scope: visible`) : fiches partagées avec un invité, flux
dont les deux extrémités sont exportées, notes protégées seulement là où il peut les lire. Les administrateurs peuvent
demander `?scope=full` (tout ; 403 pour les autres) avec un `reason` facultatif (200 caractères au plus). Réimporter un
fichier sans notes ni listes d'accès les laisse en place.

## Journal des accès

Noosphere écrit une ligne dans le journal quand une personne extérieure à l'équipe propriétaire :

- lit une fiche restreinte (détail, instantané d'historique ou comparaison) : `what: record` ;
- lit des notes protégées : `what: protectedNotes` ;
- exporte des fiches restreintes ou des notes protégées, et pour tout export complet : `what: export`, avec le motif
  ou un résumé en `detail`.

Les lectures des membres de l'équipe propriétaire ne sont pas journalisées. Chaque ligne indique `why` le lecteur
avait accès : `admin`, `owningTeam`, `listedTeam` (avec l'équipe) ou `listedUser`, et `admin: true` pour un
administrateur.

`GET /api/access-log` (administrateurs) renvoie `{items, nextCursor}`, du plus récent au plus ancien, avec le nom
affiché de la personne, le titre, le type et l'équipe propriétaire de la fiche. Filtres : `username`, `kind`,
`entityId`, `what`, `from`, `to` (une date, ou une date et une heure ; `to` inclut toute la journée), `limit` (50,
200 au plus) et `cursor` (le `nextCursor` de la page précédente).

Les lignes plus anciennes que `NOOSPHERE_ACCESS_LOG_DAYS` (365 par défaut) sont supprimées au démarrage puis chaque
jour.
