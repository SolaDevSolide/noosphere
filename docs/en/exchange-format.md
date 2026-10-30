# Exchange format v1

[Français](../fr/exchange-format.md)

A Noosphere file describes a whole landscape, in JSON or YAML (same content). It is what `GET /api/export` and
`noosphere export` write and what `POST /api/import` and `noosphere import` read. Schema:
[docs/format/noosphere-v1.schema.json](../format/noosphere-v1.schema.json) (JSON Schema 2020-12).

## Envelope

```yaml
formatVersion: noosphere/v1
source:
  name: Noosphere              # informative, ignored on import
  url: https://noosphere.example.com
  exportedAt: 2026-10-30T09:00:00Z
mode: full
items:
  - kind: Domain               # Domain, Team, Application, DataObject, Flow or Page
    id: finance                # kebab-case, unique per kind
    metadata:
      name: Finance
      description: Billing, payments and accounting.   # optional, markdown
    spec:
      order: 4
```

`mode: full` means that the file holds the whole landscape it was exported from. Items may come in any order.

## Spec per kind

| Kind | `spec` keys |
| ---- | ----------- |
| `Domain` | `order` (integer) |
| `Team` | `contact` (e-mail or channel) |
| `Application` | `domainId` (required), `teamId`, `kind` (`app` default, `bus`, `saas`, `data`, `file`, `external`), `tech`, `lifecycle` (`live` default, `planned`, `retiring`, `retired`), `visibility` (`everyone` default, `restricted`) |
| `DataObject` | `sensitivity` (`internal` default, `public`, `personal`, `sensitive`), `sourceApplicationId` |
| `Flow` | `fromId`, `toId` (required), `type` (`api` default, `event`, `batch`), `schedule`, `dataObjectIds` (list), `ownerTeamId`, `status` (`live` default, `planned`, `deprecated`), `volume`, `plannedChange` (`summary`, `date` as YYYY-MM-DD) |
| `Page` | `body` (Markdown), `parentId` (a page), `order` (integer), `attachedTo` (`kind`: `application`, `flow`, `dataObject`, `domain` or `team`, and `id`), `ownerTeamId`, `tags` (list), `visibility` (`everyone` default, `restricted`) |

For a `Page`, `metadata.name` is the title and there is no `metadata.description`: the text is `spec.body`. Wiki
links in the body (`[[application:billing]]`) are kept as written; a link to a record that does not exist is
imported and shown as broken. Images are not part of the format yet.

Computed values (completeness, flow sensitivity, `sourceOf`, page links) are not part of the file, and neither are
the history, proposals, accounts and team memberships.

## Example

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

The whole "Alizé" demo is a complete example: export it from a demo instance.

A page:

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

Pages are exported after their parent, so that a file reads top-down.

## Import rules

1. The whole file is validated first: the envelope, every item (kind, id, known keys, values), and every reference
   (to items of the file or records already in the database). Any error stops the import and nothing is written.
   Each error has `index`, `kind`, `id`, `path` (such as `items[3].spec.fromId`) and `message`.
2. Then every item is created, or updated when a record of that kind and id exists (its `version` goes up). Items
   identical to the record are counted as `unchanged`. Each created or updated record gets a revision ("Imported",
   by the importing administrator).
3. An import never deletes anything.

`dryRun=true` (API) or `--dry-run` (CLI) only runs the checks and counts. Only administrators import.
