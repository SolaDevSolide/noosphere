# Import : Excel

[English](../en/imports-excel.md) · [Tous les imports](imports.md)

Les classeurs Excel (`.xlsx`) organisés comme le modèle : une feuille par type d'enregistrement, une ligne d'en-tête,
un enregistrement par ligne. Les autres formats de tableur (`.xls`, `.ods`, `.csv`) sont refusés : enregistrez le
fichier comme classeur Excel.

## Modèle

`GET /api/imports/template.xlsx` (ou `noosphere imports template`) télécharge le modèle : une feuille **Read me** qui
explique chaque colonne et ses valeurs acceptées, puis les feuilles ci-dessous, avec leur ligne d'en-tête. Les
colonnes sont les champs du [format d'échange](exchange-format.md) ; les noms des feuilles et des colonnes sont en
anglais.

| Feuille | Colonnes |
| ------- | -------- |
| Domains | `id`, `name`, `description`, `order` |
| Teams | `id`, `name`, `contact`, `description` |
| Applications | `id`, `name`, `domainId`, `teamId`, `kind`, `tech`, `lifecycle`, `description` |
| Data objects | `id`, `name`, `sensitivity`, `sourceApplicationId`, `description` |
| Flows | `id`, `name`, `fromId`, `toId`, `type`, `schedule`, `dataObjectIds`, `ownerTeamId`, `status`, `volume`, `description` |

- `name` est obligatoire ; `id` est facultatif (il est déduit du nom pour un nouvel enregistrement).
- Les références (`domainId`, `teamId`, `sourceApplicationId`, `fromId`, `toId`, `ownerTeamId`) sont des ids ou des
  noms, du classeur ou du catalogue ; `dataObjectIds` en liste plusieurs, séparés par des virgules.
- `kind`, `lifecycle`, `sensitivity`, `type` et `status` prennent les valeurs du format d'échange (`app`, `live`,
  `personal`, `batch`...), sans tenir compte de la casse. Une autre valeur est une erreur qui désigne sa cellule
  (`Applications!E4`).
- Une cellule vide laisse le champ tel quel. Les en-têtes peuvent aussi s'écrire `domain`, `team`, `from`, `to`,
  `data objects`, `owner team`, `source application`. Les feuilles et colonnes non listées sont ignorées, avec un
  avertissement.

## Exemple

Feuille **Applications** :

| id | name | domainId | teamId | kind | tech | lifecycle | description |
| -- | ---- | -------- | ------ | ---- | ---- | --------- | ----------- |
| coaching | Coaching | Prevention | Team Prevention | app | Kotlin | planned | Séances de coaching santé. |
| | Billing | | | | .NET 9, PostgreSQL | | |

Feuille **Flows** :

| id | name | fromId | toId | type | schedule | dataObjectIds | ownerTeamId | status |
| -- | ---- | ------ | ---- | ---- | -------- | ------------- | ----------- | ------ |
| coaching-crm | Member lookup | coaching | CRM | api | On demand | customer, Coaching session | team-prevention | planned |

La deuxième ligne d'Applications n'a pas d'id : elle est rapprochée par son nom de l'application Billing existante, et
seule sa technologie est mise à jour. Le flux désigne le CRM par son nom et les objets de données par id et par nom.
