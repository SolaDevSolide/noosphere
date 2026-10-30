# Historique

[English](../en/history.md)

Chaque modification publiée crée une **révision** : un enregistrement direct, une [proposition](proposals.md)
acceptée, un import, une restauration ou une suppression. Une révision note la version, l'auteur, la date, un résumé
et la proposition d'origine s'il y en a une. Elle contient un instantané des champs modifiables de l'enregistrement,
sous la forme du corps de son `PUT`.

Lors de la mise à jour vers cette version, la migration crée une révision par enregistrement existant, au nom de
`system`.

## Panneau d'historique

Un bouton discret « Historique » sur chaque fiche et chaque page ouvre le panneau. Il liste les versions, de la plus
récente à la plus ancienne, en compare deux au choix et en restaure une.

| Méthode et chemin | Qui | Réponse |
| ----------------- | --- | ------- |
| `GET /api/{kind}/{id}/history` | connecté | `[{version, operation, author, authorKind, createdAt, summary, proposalId}]`, la plus récente d'abord |
| `GET /api/{kind}/{id}/history/{version}` | connecté | la révision avec son `snapshot` |
| `GET /api/{kind}/{id}/history/compare?from=&to=` | connecté | `{from, to, diff}`, la même comparaison par champ que les propositions |
| `POST /api/{kind}/{id}/history/{version}/restore` | membre, admin | `{outcome: "published", version}` ou `{outcome: "proposed", proposalId}` |

`{kind}` vaut `applications`, `flows`, `data-objects`, `pages`, `domains` ou `teams`. `operation` vaut `create`,
`update`, `delete` ou `restore`.

Restaurer suit les mêmes règles que modifier : un responsable (ou un administrateur) publie l'ancienne version comme
une nouvelle révision ; tout autre membre obtient à la place une proposition à valider. L'historique d'un
enregistrement restreint n'est montré qu'aux administrateurs.

## Enregistrements supprimés

Supprimer un enregistrement garde son historique, qui se termine par une révision `delete`. Les administrateurs
listent les enregistrements supprimés avec `GET /api/trash` (`[{kind, entityId, title, deletedAt, deletedBy, version}]`)
et en restaurent un avec `POST /api/trash/{kind}/{id}/restore`, où `kind` vaut `application`, `flow`, `dataObject`,
`page`, `domain` ou `team`. L'enregistrement revient dans son dernier état, sous une nouvelle version, si ce à quoi il
fait référence existe encore.

Un enregistrement recréé avec l'identifiant d'un enregistrement supprimé poursuit son historique.
