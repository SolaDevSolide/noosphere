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
les liens ; un lien écrit dans du code (entre accents graves ou dans un bloc de code) est un exemple, pas un lien. `GET /api/pages/{id}` les renvoie résolus (`links: [{kind, id, title, exists, restricted, label}]`) ; un
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

## Dans l'interface

Le bouton **Pages** de l'en-tête ouvre l'espace des pages : l'arborescence à gauche (un tiroir sur téléphone), les
pages modifiées récemment, les pages qui ont des liens cassés, et un rappel que chaque page est aussi servie en
Markdown brut pour les assistants.

- **Arborescence.** Filtrez-la par titre. Les membres qui peuvent publier une page la glissent pour la réordonner, ou
  la déposent sur une autre page pour la ranger dessous (l'enregistrement est immédiat, par
  `POST /api/pages/{id}/move`).
- **Lire une page.** Les liens wiki s'affichent en pastilles avec le nom et l'icône de la fiche ; un lien dont la
  cible n'existe pas s'affiche en rouge avec un avertissement, et la page s'ouvre quand même. La colonne de droite
  contient le sommaire, **Citée dans** (les pages qui pointent vers celle-ci) et les liens de la page.
  **Copier en Markdown** copie le texte avec son en-tête YAML ; **Brut** l'affiche. **Historique** ouvre
  l'[historique des révisions](history.md).
- **Écrire.** L'éditeur a une barre d'outils, un aperçu en direct et un panneau de réglages : titre, adresse, page
  parente, fiche rattachée, équipe propriétaire, étiquettes et visibilité. Tapez `[[` pour chercher une fiche ou une
  page et insérer un lien (`[[flow:cli` ne cherche que les flux). Les liens cassés sont listés au-dessus de l'aperçu,
  en rouge, sans bloquer l'enregistrement. Un brouillon est gardé dans le navigateur jusqu'à la publication ou
  l'annulation.
- **Adresse.** L'adresse (`/pages/{id}`) est fixée à la création, d'après le titre, et ne change plus ensuite : les
  liens vers la page restent valides.
- **Propriétaire.** Tout membre peut créer une page : rattachez-la à une fiche (son propriétaire possède alors la
  page) ou choisissez une équipe propriétaire. Les membres de l'équipe propriétaire publient ; les autres
  **Proposent une modification**, qui devient une [proposition](proposals.md). Seuls les administrateurs restreignent
  une page.
- **Modèles.** **Nouvelle page** propose une page vide et trois modèles : **Procédure** (symptômes, vérifications,
  correction, escalade), **Décision d’architecture (ADR)** (contexte, décision, conséquences) et **Vue d’ensemble
  d’application** (rôle, flux, contacts, liens). Un
  modèle ajoute son étiquette (`runbook`, `adr`, `overview`), affichée au-dessus du titre.
- **Fiches.** Les applications, les flux, les objets de données (dans le panneau de lignage), les équipes et les
  domaines affichent leurs pages et les pages qui les mentionnent, avec un lien pour créer une page rattachée.
- **Recherche.** La recherche de l'en-tête trouve aussi les pages.

## Pas encore pris en charge

Les images et les pièces jointes dans les pages ne sont pas prises en charge dans cette version : une image Markdown
s'affiche comme un lien vers l'image. Les diagrammes (Mermaid) ne sont pas dessinés non plus ; écrivez-les dans un bloc
de code pour l'instant. Le HTML brut d'une page s'affiche comme du texte.
