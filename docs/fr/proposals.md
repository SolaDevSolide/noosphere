# Propositions et validation

[English](../en/proposals.md)

La règle : **tout le monde lit, chacun propose, l'équipe responsable valide.**

## Équipes et responsabilité

Un compte appartient à autant d'équipes que nécessaire. Les administrateurs gèrent les appartenances sur la page des
comptes, ou sur la fiche d'une équipe (`PUT /api/teams/{id}/members`).

Chaque enregistrement a une équipe responsable :

| Enregistrement | Équipe responsable |
| -------------- | ------------------ |
| Application | son `teamId` |
| Flux | son `ownerTeamId`, sinon l'équipe de son application source (`fromId`) |
| Objet de données | l'équipe de son application source |
| Page | son `ownerTeamId`, sinon le responsable de l'enregistrement auquel elle est rattachée |
| Domaine, équipe | les administrateurs seulement |

Un enregistrement sans équipe responsable relève des administrateurs.

## Qui publie, qui propose

- Les **administrateurs** et les **membres de l'équipe responsable** publient directement. Leur enregistrement crée
  quand même une nouvelle [révision](history.md) à leur nom.
- Les **autres membres** envoient une proposition de modification au lieu d'enregistrer. Les responsables peuvent
  aussi cocher « Proposer plutôt » pour faire relire leur modification.
- Les **invités** (`guest`) lisent seulement : ils ne proposent pas.
- Les applications et pages restreintes, et les flux qui touchent une application restreinte, ne sont modifiés que
  par les administrateurs.

Les fiches renvoient `owningTeamId` et `permissions: {publish, propose}` pour le lecteur : l'interface affiche
« Modifier » ou « Proposer une modification ». Un `PUT`, `POST` ou `DELETE` direct par quelqu'un qui ne peut pas
publier renvoie 403.

## Cycle de vie d'une proposition

```
draft ──submit──▶ submitted (« À valider ») ──accept──▶ accepted (publiée)
                       │                     └─refuse──▶ refused (avec un motif)
                       └──────withdraw (auteur)───────▶ withdrawn
```

- Une proposition a un titre, un commentaire facultatif et **1 à 50 modifications**. Une modification crée, modifie
  ou supprime une application, un flux, un objet de données ou une page. Pour une modification ou une suppression,
  elle désigne la cible et la version sur laquelle elle a été écrite (`baseVersion`). Pour une création ou une
  modification, elle contient tout l'enregistrement après la modification (`after`, sous la forme du corps de `PUT`).
- L'auteur modifie sa proposition tant qu'elle est en brouillon ou à valider, et peut la retirer.
- **Refuser** demande un motif de 3 à 500 caractères, que l'auteur voit.
- Personne n'accepte ni ne refuse sa propre proposition.

## Valideurs

Les valideurs sont les membres de l'équipe responsable de chaque cible, et les administrateurs. Une proposition qui
touche des enregistrements de plusieurs équipes demande **un valideur de chaque équipe**, ou un administrateur. Quand
un membre accepte, l'accord de ses équipes est noté ; les modifications sont publiées dès que toutes les équipes
responsables ont donné le leur. Une modification d'un enregistrement relevant des administrateurs demande un
administrateur.

À l'acceptation, toutes les modifications sont appliquées dans une même transaction, et chacune crée une révision
liée à la proposition, au nom de l'auteur de la proposition.

## Propositions dépassées

Si une cible a changé depuis `baseVersion`, accepter renvoie **409** avec `outdated: true`, et la proposition est
marquée `outdated`. L'auteur **met alors la proposition à jour depuis la dernière version**
(`POST /api/proposals/{id}/rebase`) :

- les champs que l'auteur a modifiés gardent sa valeur ;
- les autres champs prennent la dernière valeur ;
- un champ modifié des deux côtés, avec des valeurs différentes, figure dans `conflicts` de la modification, pour que
  l'auteur le vérifie.

Les accords donnés avant une mise à jour ou une modification sont annulés.

## Comparaison par champ

`GET /api/proposals/{id}` renvoie chaque modification avec ses instantanés `before` et `after` et une comparaison par
champ : `diff: [{field, label, before, after, kind}]`, où `kind` vaut `text`, `markdown`, `list` ou `ref`. Les champs
Markdown portent les textes bruts, pour que l'interface montre les mots modifiés dans le texte. Les champs `ref`
donnent aussi le nom des enregistrements visés (`beforeTitle`, `afterTitle`). Les fiches sont comparées côte à côte.

## Boîte de réception

`GET /api/me/inbox` renvoie `{toValidate, mineOpen}` pour le badge de l'en-tête : les propositions que le lecteur peut
trancher maintenant, et ses propres brouillons et propositions à valider.

## Agents

Les propositions ont `authorKind` (`user` ou `agent`) et `onBehalfOf`, pour les agents d'IA d'une prochaine version.
Ils ne servent pas encore.
