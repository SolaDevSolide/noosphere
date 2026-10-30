# Pages

[English](../en/pages.md)

Les pages de documentation s'écrivent en Markdown : procédures d'exploitation, décisions d'architecture,
présentations de domaine, glossaires. Une page est autonome ou **rattachée** à une application, un flux, un objet de
données, un domaine ou une équipe, et les pages forment une **arborescence**.

## Champs

| Champ | Description |
| ----- | ----------- |
| `id` | slug en kebab-case, déduit du titre s'il n'est pas donné |
| `title` | obligatoire |
| `body` | Markdown |
| `parentId` | page parente ; vide pour une page de premier niveau. Une page ne peut pas être placée sous elle-même ni sous l'une de ses sous-pages. |
| `order` | position parmi ses sœurs (puis par titre) |
| `attachedTo` | `{kind, id}`, où `kind` vaut `application`, `flow`, `dataObject`, `domain` ou `team` |
| `ownerTeamId` | équipe responsable ; vide, c'est le responsable de l'enregistrement rattaché (les administrateurs s'il n'y en a pas) |
| `tags` | jusqu'à 20 textes courts |
| `visibility` | `everyone` ou `restricted` (administrateurs seulement ; les autres reçoivent `{id, restricted: true, parentId, order}`) |

Enregistrer une page suit les règles de [propositions et validation](proposals.md) et crée une
[révision](history.md).

## Liens wiki

Reliez des enregistrements et d'autres pages avec des doubles crochets, avec un libellé facultatif :

```markdown
La facturation attend [[flow:crm-to-billing-customers]].
Voir [[application:billing|l'application de facturation]] et [[page:glossary]].
```

Types : `application`, `flow`, `dataObject`, `page`, `domain`, `team`. À chaque enregistrement, le serveur extrait
les liens. `GET /api/pages/{id}` les renvoie résolus (`links: [{kind, id, title, exists, restricted, label}]`) ; un
lien dont la cible n'existe pas a `exists: false` et s'affiche comme cassé. La réponse contient aussi `backlinks`
(les pages qui pointent vers celle-ci), `ancestors` (fil d'Ariane), `children` et les `permissions` du lecteur.

Les fiches listent les pages qui leur sont rattachées (`pages`) et celles qui pointent vers elles (`mentionedIn`).

## Routes

| Méthode et chemin | Description |
| ----------------- | ----------- |
| `GET /api/pages?attachedKind=&attachedId=&tag=&q=` | liste sans le texte, avec le nombre de liens cassés |
| `GET /api/pages/tree` | l'arborescence : `[{id, title, order, attachedTo, children}]` |
| `GET /api/pages/{id}` | la page avec ses liens, rétroliens, ancêtres et enfants |
| `GET /api/pages/{id}/raw` | `text/markdown` avec un en-tête YAML (id, titre, parent, rattachement, responsable, étiquettes, version, liens) |
| `POST /api/pages`, `PUT /api/pages/{id}`, `DELETE /api/pages/{id}` | créer, remplacer (avec `version`), supprimer (pas tant qu'elle a des sous-pages) |
| `POST /api/pages/{id}/move` | `{parentId, order, version?}` |

La recherche (`/api/search`) inclut les pages : le titre d'abord, puis le texte, avec un extrait autour du passage
trouvé. Les pages font partie du [format d'échange](exchange-format.md) sous le type `Page`.

## Pas encore pris en charge

Les images et les pièces jointes dans les pages ne sont pas prises en charge dans cette version.
