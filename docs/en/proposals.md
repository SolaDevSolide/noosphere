# Proposals and validation

[Français](../fr/proposals.md)

The rule: **everyone reads, anyone proposes, the owning team validates.**

## Teams and ownership

A user belongs to any number of teams. Administrators manage memberships on the users page, or on a team record
(`PUT /api/teams/{id}/members`).

Every record has an owning team:

| Record | Owning team |
| ------ | ----------- |
| Application | its `teamId` |
| Flow | its `ownerTeamId`, or else the team of its source application (`fromId`) |
| Data object | the team of its source application |
| Page | its `ownerTeamId`, or else the owner of the record it is attached to |
| Domain, team | administrators only |

A record without an owning team is owned by administrators.

## Who publishes, who proposes

- **Administrators** and **members of the owning team** publish directly. Their save still creates a new
  [revision](history.md) that names them.
- **Other members** send a change proposal instead of saving. Owners may also tick "Propose instead" to have their
  change reviewed.
- **Guests** read only: they cannot propose.
- Restricted records are proposed by those who see them, and published by administrators and the owning team.
  Protected notes left out of a proposal are kept (see [access](access.md#protected-notes)).

Record responses carry `owningTeamId` and `permissions: {publish, propose}` for the reader, so the interface shows
either "Edit" or "Suggest a change". A direct `PUT`, `POST` or `DELETE` by someone who may not publish returns 403.

## Proposal lifecycle

```
draft ──submit──▶ submitted ("To validate") ──accept──▶ accepted (published)
                       │                    └─refuse──▶ refused (with a reason)
                       └──────withdraw (author)──────▶ withdrawn
```

- A proposal has a title, an optional comment and **1 to 50 changes**. A change creates, updates or deletes an
  application, a flow, a data object or a page. For an update or a deletion it names the target and the version it was
  written against (`baseVersion`). For a creation or an update it holds the whole record after the change (`after`, in
  the shape of the record's `PUT` body).
- The author edits a proposal while it is a draft or waiting for validation, and can withdraw it.
- **Refusing** needs a reason of 3 to 500 characters, which the author sees.
- Nobody accepts or refuses their own proposal.

## Validators

The validators are the members of the owning team of each target, and the administrators. A proposal that touches
records of several teams needs **one validator from each team**, or one administrator. When a team member accepts,
their teams' approvals are recorded; the changes are published once every owning team has approved. A change on a
record owned by administrators needs an administrator.

When a proposal is accepted, every change is applied in one transaction, and each one creates a revision linked to
the proposal, with the proposal's author as the author.

## Outdated proposals

If a target changed since `baseVersion`, accepting returns **409** with `outdated: true`, and the proposal shows
`outdated`. The author then **updates the proposal from the latest version** (`POST /api/proposals/{id}/rebase`):

- fields the author changed keep the author's value;
- the other fields take the latest value;
- a field changed on both sides, to different values, is listed in the change's `conflicts`, so the author can check it.

Approvals given before a rebase or an edit are dropped.

## Field diff

`GET /api/proposals/{id}` returns each change with its `before` and `after` snapshots and a field diff:
`diff: [{field, label, before, after, kind}]`, where `kind` is `text`, `markdown`, `list` or `ref`. Markdown fields
carry the raw texts, so that the interface can show an inline word diff. `ref` fields also give the names of the
records they refer to (`beforeTitle`, `afterTitle`). Records are compared side by side.

## Inbox

`GET /api/me/inbox` returns `{toValidate, mineOpen}` for the header badge: the proposals the reader may decide on now,
and the reader's own drafts and submitted proposals.

## Agents

Proposals have `authorKind` (`user` or `agent`) and `onBehalfOf`, for AI agents in a later version. They are not used yet.
