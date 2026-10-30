# History

[Français](../fr/history.md)

Every published change creates a **revision**: a direct save, an accepted [proposal](proposals.md), an import, a
restore or a deletion. A revision records the version, the author, the date, a summary and the proposal that led to it,
if any. It holds a snapshot of the record's editable fields, in the shape of its `PUT` body.

When upgrading to this version, the migration records one revision per existing record, authored by `system`.

## History panel

A discreet "History" button on each record and page opens the panel. It lists the versions, newest first, compares
any two of them and restores one.

| Method and path | Who | Answer |
| --------------- | --- | ------ |
| `GET /api/{kind}/{id}/history` | signed in | `[{version, operation, author, authorKind, createdAt, summary, proposalId}]`, newest first |
| `GET /api/{kind}/{id}/history/{version}` | signed in | the revision with its `snapshot` |
| `GET /api/{kind}/{id}/history/compare?from=&to=` | signed in | `{from, to, diff}`, the same field diff as proposals |
| `POST /api/{kind}/{id}/history/{version}/restore` | member, admin | `{outcome: "published", version}` or `{outcome: "proposed", proposalId}` |

`{kind}` is `applications`, `flows`, `data-objects`, `pages`, `domains` or `teams`. `operation` is `create`,
`update`, `delete` or `restore`.

Restoring follows the same rules as editing: an owner (or an administrator) publishes the old version as a new
revision; any other member gets a submitted proposal instead. The history of a restricted record is shown to
those who see the record; protected notes appear as `"••• protected"` in snapshots and compares for those who may
not read them (see [access](access.md)).

## Deleted records

Deleting a record keeps its history, which ends with a `delete` revision. Administrators list deleted records with
`GET /api/trash` (`[{kind, entityId, title, deletedAt, deletedBy, version}]`) and restore one with
`POST /api/trash/{kind}/{id}/restore`, where `kind` is `application`, `flow`, `dataObject`, `page`, `domain` or
`team`. The record comes back with its last state, under a new version, if what it refers to still exists.

A record created again with the id of a deleted one continues its history.
