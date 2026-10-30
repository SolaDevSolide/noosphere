# Format d'échange v1

[English](../en/exchange-format.md)

Un fichier Noosphere décrit tout un paysage, en JSON ou en YAML (même contenu). C'est ce qu'écrivent
`GET /api/export` et `noosphere export`, et ce que lisent `POST /api/import` et `noosphere import`. Les clés sont en
anglais. Schéma : [docs/format/noosphere-v1.schema.json](../format/noosphere-v1.schema.json) (JSON Schema 2020-12).

## Enveloppe

```yaml
formatVersion: noosphere/v1
source:
  name: Noosphere              # informatif, ignoré à l'import
  url: https://noosphere.exemple.fr
  exportedAt: 2026-10-30T09:00:00Z
mode: full
items:
  - kind: Domain               # Domain, Team, Application, DataObject ou Flow
    id: finance                # kebab-case, unique par type
    metadata:
      name: Finance
      description: Facturation, paiements et comptabilité.   # facultatif, markdown
    spec:
      order: 4
```

`mode: full` signifie que le fichier contient tout le paysage dont il est issu. Les éléments peuvent venir dans
n'importe quel ordre.

## `spec` selon le type

| Type | Clés de `spec` |
| ---- | -------------- |
| `Domain` | `order` (entier) |
| `Team` | `contact` (adresse e-mail ou canal) |
| `Application` | `domainId` (obligatoire), `teamId`, `kind` (`app` par défaut, `bus`, `saas`, `data`, `file`, `external`), `tech`, `lifecycle` (`live` par défaut, `planned`, `retiring`, `retired`), `visibility` (`everyone` par défaut, `restricted`) |
| `DataObject` | `sensitivity` (`internal` par défaut, `public`, `personal`, `sensitive`), `sourceApplicationId` |
| `Flow` | `fromId`, `toId` (obligatoires), `type` (`api` par défaut, `event`, `batch`), `schedule`, `dataObjectIds` (liste), `ownerTeamId`, `status` (`live` par défaut, `planned`, `deprecated`), `volume`, `plannedChange` (`summary`, `date` au format AAAA-MM-JJ) |

Les valeurs calculées (complétude, sensibilité des flux, `sourceOf`) ne figurent pas dans le fichier.

## Exemple

```yaml
  - kind: Flow
    id: crm-to-billing-customers
    metadata:
      name: Customers
    spec:
      fromId: crm
      toId: billing
      type: batch
      schedule: Every night 02:00
      dataObjectIds: [customer]
      ownerTeamId: team-crm
      status: live
      volume: 15,000 lines
      plannedChange:
        summary: Billing 4.3 on 4 Nov changes the target API to v4
        date: 2026-11-04
```

La démonstration « Alizé » est un exemple complet : exportez-la depuis une instance de démonstration.

## Règles d'import

1. Tout le fichier est d'abord vérifié : l'enveloppe, chaque élément (type, identifiant, clés connues, valeurs) et
   chaque référence (vers un élément du fichier ou un enregistrement déjà en base). À la moindre erreur, l'import
   s'arrête et rien n'est écrit. Chaque erreur porte `index`, `kind`, `id`, `path` (comme `items[3].spec.fromId`) et
   `message`.
2. Ensuite chaque élément est créé, ou mis à jour si un enregistrement de ce type et de cet identifiant existe (sa
   `version` augmente). Les éléments identiques à l'enregistrement comptent comme `unchanged`.
3. Un import ne supprime jamais rien.

`dryRun=true` (API) ou `--dry-run` (ligne de commande) ne fait que les vérifications et les comptes. Seuls les
administrateurs importent.
