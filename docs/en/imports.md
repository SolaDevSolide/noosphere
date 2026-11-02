# Imports from other formats

[Français](../fr/imports.md)

Noosphere reads the documentation teams already have and turns it into records of the catalogue. Every import is a
**dry run first**: it shows what would be created, updated or left unchanged, and nothing is written until the same
plan is applied. An import **never deletes** anything.

| Format | Files | Becomes | Page |
| ------ | ----- | ------- | ---- |
| Backstage | `catalog-info.yaml` (one or several documents) | domains, teams, applications, flows | [Backstage](imports-backstage.md) |
| OpenAPI 3.x | `.yaml`, `.json` | a page on the application, flows from its consumers | [OpenAPI](imports-openapi.md) |
| AsyncAPI 2.x and 3.x | `.yaml`, `.json` | event flows through the broker, data objects | [AsyncAPI](imports-asyncapi.md) |
| ADR (MADR, Nygard) | `.md` | decision record pages | [ADR](imports-adr.md) |
| Mermaid (flowchart, C4) | `.mmd`, `.mermaid` | applications and flows | [Mermaid](imports-mermaid.md) |
| Excel | `.xlsx`, from the [template](imports-excel.md#template) | domains, teams, applications, data objects, flows | [Excel](imports-excel.md) |

Several files can be sent together, and zip archives of them (for example a folder of ADRs). The format of each file
is recognised from its extension and its content; the `format` option forces it. A Noosphere export is imported
with the [exchange import](exchange-format.md) instead.

## Dry run, preview, apply

1. **Dry run.** `POST /api/imports` with the files (`multipart/form-data`, field `files`). Noosphere reads them, finds
   the existing records, checks every change with the same rules as the record forms, and stores the result as a
   **plan**. It answers `201` with the plan.
2. **Preview.** The plan lists its entries, each with its action (`create`, `update` or `unchanged`), the field-level
   diff (every field set on a creation, the changed fields on an update), how the existing record was found
   (`matchedBy`), the **possible matches** (existing records with a close name) and where the entry comes from: file
   (with its path in the archive), line and place (`documents[3]`, `channels.billing.invoice.issued.subscribe`,
   `Applications!A4`). Warnings say what was left out or guessed; errors say what prevents the import.
3. **Apply.** `POST /api/imports/{id}/apply` writes exactly the plan that was shown, or part of it: the body
   `{ "skip": [3, 7] }` leaves out the entries with those indexes (creations and updates). An entry lists in
   `requires` the entries creating the records it refers to (its domain, its team, the ends of a flow...): an entry
   kept may not require one left out. The applied plan lists the entries left out in `skipped`.

A plan is applied once, by the person who made it, within **30 minutes**, and only if the catalogue has not changed
since the dry run:

| Situation | Answer |
| --------- | ------ |
| Applied | `200`, the plan with `status: applied` |
| Plan of someone else, or unknown | `404` |
| Applied or rejected already | `409` |
| A record was created, changed, deleted or restored since the dry run (by anyone) | `409`, and the plan becomes `rejected`: run the dry run again |
| More than 30 minutes after the dry run | `410`, the plan becomes `expired` |
| The plan has errors | `422` |
| An entry kept requires an entry left out, or every change is left out | `422`, the plan stays `planned` |

## Matching

Each entry of a file is matched to an existing record of the same kind:

1. by its **stable identifier**: the Noosphere id given by the source (Backstage `metadata.name` or the annotation
   `noosphere.io/id`, the C4 alias, the Excel `id` column, the ADR file name...);
2. otherwise by its **exact name**, once normalized: case, accents and punctuation are ignored, so `Réseau Auto+` and
   `reseau auto` are the same name. A flow matches a flow with the same name between the same two applications.

Existing records whose name is only **close** (one is the other plus a word, or a few letters differ) are shown as
possible matches, and never merged: the entry creates a new record. When several existing records have the same
name, or two entries match the same record, the entry is skipped with a warning: give its id in the file.

References between records (a team, a domain, the ends of a flow, the data objects it carries) are written as ids or
names: Noosphere looks in the files first, then in the catalogue. A reference that matches nothing is left out with a
warning; a flow whose end is unknown is skipped.

## What an import changes

- It **creates** records and **updates** fields; it never deletes a record and never clears a field. A field the
  file does not give keeps its value. Tags and the data objects of a flow are added to the current ones.
- Records a file only **mentions** (the application of an OpenAPI file, the broker of an AsyncAPI file, the nodes of
  a Mermaid diagram, the messages of an AsyncAPI file) are created when they do not exist; when they exist, only
  their empty fields are filled.
- A new application without a domain goes to the domain given by the `domainId` option, or else to a domain
  **Imported** (created when needed, with a warning). The `teamId` option is the owning team of the new
  applications, flows and pages that have none.
- Visibility, access lists and protected notes are never set by an import: restricted records are made by
  administrators, by hand. Text that looks like a secret (a token, a key, a password) is an error, as in the forms.

## Rights

Administrators and members import; guests cannot (`403`). Every change of the plan is checked as if it were made by
hand: members publish changes to the records their teams own (set `teamId` to one of your teams for new records),
and only administrators create or change domains and teams. A change the person may not publish is an error of the
plan; send a [proposal](proposals.md) for it instead.

## Options

| Option | Meaning |
| ------ | ------- |
| `format` | `backstage`, `openapi`, `asyncapi`, `adr`, `mermaid` or `excel`: read every file as this format. |
| `domainId` | Domain of the new applications without one (an existing domain). |
| `teamId` | Owning team of the new records without one (an existing team). |
| `applicationId` | Application an OpenAPI or AsyncAPI file describes, or decision records are attached to (id or name). |
| `parentPageId` | Page under which decision records go (an existing page). |

## Limits

| Limit | Value |
| ----- | ----- |
| One upload (all files) | 20 MB |
| One file, once extracted from its archive | 5 MB |
| Files, archives opened | 200 |
| Everything extracted from the archives of one import | 50 MB |
| Compression ratio of an archive entry (zip bombs) | 100 |
| Records in one plan | 5,000 |
| Rows of one sheet | 10,000 |
| Nodes and depth of a YAML or JSON document | 200,000 and 64 levels |
| YAML documents in one file | 1,000 |

An archive inside an archive is not opened. YAML is read safely: aliases (`*name`) are refused, so that a small file
cannot expand into a huge one (the exchange import refuses them too).

## Record of imports

Every dry run is kept, applied or not: `GET /api/imports` lists one's own imports, and every import for
administrators, with its author, time, formats, counts, status and the reason of a refusal; `GET /api/imports/{id}`
gives the whole preview, with the name, size and SHA-256 of every file. Each record written gets a revision by the
person who applied the plan, with the summary `Imported (backstage, import 1a2b3c4d)`, in its
[history](history.md). The server log has a line for every plan, application and refusal.

## API

| Request | Does |
| ------- | ---- |
| `POST /api/imports?format=&domainId=&teamId=&applicationId=&parentPageId=` | Dry run: reads the files, stores and returns the plan (`201`). |
| `GET /api/imports/{id}` | The plan (its author only). |
| `POST /api/imports/{id}/apply` | Applies the plan; `{ "skip": [3, 7] }` leaves entries out. |
| `GET /api/imports?limit=50` | Recent imports. |
| `GET /api/imports/template.xlsx` | The [Excel template](imports-excel.md#template). |

```sh
curl -b cookies -H "X-Requested-With: fetch" -F files=@catalog-info.yaml -F files=@adr.zip \
  "https://noosphere.example.com/api/imports?teamId=team-crm"
curl -b cookies -H "X-Requested-With: fetch" -X POST https://noosphere.example.com/api/imports/<id>/apply
```

An entry of the plan:

```json
{
  "index": 5,
  "action": "update",
  "kind": "application",
  "id": "crm",
  "name": "CRM",
  "matchedBy": "identifier",
  "changes": [
    { "field": "description", "label": "Description", "kind": "markdown",
      "before": "Customer master data and the history of every interaction.",
      "after": "Customer relationship management of Mutuelle Alizé." }
  ],
  "possibleMatches": [],
  "source": { "file": "catalog-info.yaml", "line": 49, "place": "documents[4]" },
  "hasErrors": false,
  "requires": []
}
```

## Command line

```sh
docker compose exec noosphere noosphere imports plan /data/catalog-info.yaml /data/adr.zip --team team-crm
docker compose exec noosphere noosphere imports apply <plan-id> --skip 3,7
docker compose exec noosphere noosphere imports template --output /data/template.xlsx
```

`imports plan` prints the plan (`--json` for all of it) and exits 1 when it has errors. Plans of the command line
belong to `system`, which has every right. See [command line](cli.md).
