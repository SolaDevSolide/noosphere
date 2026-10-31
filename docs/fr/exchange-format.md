# Format d'échange v1

[English](../en/exchange-format.md)

Un fichier Noosphere décrit tout un paysage, en JSON ou en YAML (même contenu). C'est ce qu'écrivent
`GET /api/export` et `noosphere export`, et ce que lisent `POST /api/import` et `noosphere import`. Les clés sont en
anglais. Schéma : [docs/format/noosphere-v1.schema.json](../format/noosphere-v1.schema.json) (JSON Schema 2020-12).

Ce format de fichier sert à l'import et à l'export. Les échanges en direct avec une instance de gestion-mep
(catalogue, avis de changement, déploiements) passent par le [protocole de liaison](liaisons.md#résumé-du-protocole),
signé et chiffré ; son catalogue porte les mêmes identifiants.

## Enveloppe

```yaml
formatVersion: noosphere/v1
source:
  name: Noosphere              # informatif, ignoré à l'import
  url: https://noosphere.exemple.fr
  exportedAt: 2026-10-30T09:00:00Z
mode: full
scope: visible                 # visible ou full ; informatif, ignoré à l'import
items:
  - kind: Domain               # Domain, Team, Application, DataObject, Flow ou Page
    id: finance                # kebab-case, unique par type
    metadata:
      name: Finance
      description: Facturation, paiements et comptabilité.   # facultatif, markdown
    spec:
      order: 4
```

`mode: full` signifie que le fichier contient tout le paysage dont il est issu. Les éléments peuvent venir dans
n'importe quel ordre. `scope: visible` signifie qu'il contient ce que la personne qui exporte voit en entier ;
`scope: full` (administrateurs seulement) contient tout, enregistrements restreints et notes protégées compris.

## `spec` selon le type

| Type | Clés de `spec` |
| ---- | -------------- |
| `Domain` | `order` (entier) |
| `Team` | `contact` (adresse e-mail ou canal) |
| `Application` | `domainId` (obligatoire), `teamId`, `kind` (`app` par défaut, `bus`, `saas`, `data`, `file`, `external`), `tech`, `lifecycle` (`live` par défaut, `planned`, `retiring`, `retired`), `visibility` (`everyone` par défaut, `restricted`), `access` (`teamIds`, `usernames`), `protectedNotes` (Markdown) |
| `DataObject` | `sensitivity` (`internal` par défaut, `public`, `personal`, `sensitive`), `sourceApplicationId` |
| `Flow` | `fromId`, `toId` (obligatoires), `type` (`api` par défaut, `event`, `batch`), `schedule`, `dataObjectIds` (liste), `ownerTeamId`, `status` (`live` par défaut, `planned`, `deprecated`), `volume`, `plannedChange` (`summary`, `date` au format AAAA-MM-JJ), `protectedNotes` (Markdown) |
| `Page` | `body` (Markdown), `parentId` (une page), `order` (entier), `attachedTo` (`kind` : `application`, `flow`, `dataObject`, `domain` ou `team`, et `id`), `ownerTeamId`, `tags` (liste), `visibility` (`everyone` par défaut, `restricted`), `access` (`teamIds`, `usernames`) |

Pour une `Page`, `metadata.name` est le titre et il n'y a pas de `metadata.description` : le texte est dans
`spec.body`. Les liens wiki du texte (`[[application:billing]]`) sont gardés tels quels ; un lien vers un
enregistrement absent est importé et affiché comme cassé. Les images ne font pas encore partie du format.

`access` liste les équipes et les personnes qui voient un enregistrement restreint, et les invités avec qui il est
partagé ; une équipe ou une personne inconnue est une erreur d'import. `protectedNotes` n'est écrit que là où la
personne qui exporte peut les lire. Quand l'une de ces clés est absente, l'import garde la valeur actuelle ; un
`protectedNotes` vide efface les notes. Voir [accès](acces.md).

Les valeurs calculées (complétude, sensibilité, `sourceOf`, liens des pages) ne figurent pas dans le
fichier, pas plus que l'historique, les propositions, les comptes et les appartenances aux équipes.

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

Une page :

```yaml
  - kind: Page
    id: replay-nightly-import
    metadata:
      name: Replay the nightly customer import
    spec:
      body: |
        Use this runbook when [[flow:crm-to-billing-customers]] did not run.
      parentId: operations
      order: 1
      attachedTo: { kind: flow, id: crm-to-billing-customers }
      tags: [runbook, billing]
      visibility: everyone
```

Les pages sont exportées après leur parente, pour que le fichier se lise de haut en bas.

## Règles d'import

1. Tout le fichier est d'abord vérifié : l'enveloppe, chaque élément (type, identifiant, clés connues, valeurs) et
   chaque référence (vers un élément du fichier ou un enregistrement déjà en base). À la moindre erreur, l'import
   s'arrête et rien n'est écrit. Chaque erreur porte `index`, `kind`, `id`, `path` (comme `items[3].spec.fromId`) et
   `message`.
2. Ensuite chaque élément est créé, ou mis à jour si un enregistrement de ce type et de cet identifiant existe (sa
   `version` augmente). Les éléments identiques à l'enregistrement comptent comme `unchanged`. Chaque enregistrement
   créé ou modifié reçoit une révision (« Imported », au nom de l'administrateur qui importe).
3. Un import ne supprime jamais rien.
4. Une valeur qui ressemble à un secret est une erreur, signalée sans la valeur (voir
   [accès](acces.md#les-secrets-sont-refusés)).

`dryRun=true` (API) ou `--dry-run` (ligne de commande) ne fait que les vérifications et les comptes. Seuls les
administrateurs importent.
