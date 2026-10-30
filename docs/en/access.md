# Access, protected notes and the access log

[Français](../fr/acces.md)

Noosphere is open by default: every member reads the whole landscape. This page covers the exceptions:
restricted records, guests, protected notes, the secret check and the access log. The
[user guide](user-guide.md#restricted-records-guests-and-protected-notes) shows where each one appears in the
interface.

## Who sees a record

| Reader | Record visible to everyone | Restricted record |
| ------ | -------------------------- | ----------------- |
| Administrator | in full | in full |
| Member of the owning team | in full | in full |
| Member of a team on the access list, or member named on it | in full | in full |
| Other member | in full | stub: `{id, restricted: true, domainId}` |
| Guest named on the access list | in full | in full |
| Other guest | outline: `{id, restricted: false, shared: false, name, domainId, kind}` | stub |

- **Applications and pages** carry an access list: `access: {teamIds: [], usernames: []}` (at most 200 entries).
  On a restricted record, it names who sees it besides the administrators and the owning team. On any record, the
  guests named on it see the record in full: that is how a record is **shared with a guest**.
- **Flows** have no list of their own. A flow touching a restricted application is seen by those who see **every**
  restricted end; the others get `{id, restricted: true, fromId, toId, type}`. A guest sees a flow in full when one
  of its ends is shared with them, and an outline (`{id, restricted: false, shared: false, name, fromId, toId,
  type}`) otherwise.
- **Pages** not shared with a guest are outlines: `{id, restricted: false, shared: false, title, parentId, order}`.
- **Data objects, domains and teams** are always readable. A data object whose source is a restricted application
  keeps its `sourceApplicationId`.
- Lists, the landscape, search, history and exports follow the same rules. Search matches the name only of an
  outline, and nothing of a stub.

Administrators and members of the owning team publish, restricted records included: they edit the access list and
can make a record restricted. A change that would hide the record from its own author is refused (403). Members
who see a record in full may propose changes to it.

`GET /api/{kind}/{id}/access` answers "who can see and edit?" for one record:
`{visibility, owningTeam, readers: {everyoneExceptGuests, teams, users}, publishers: {teams, admins}, proposers,
protectedNotes: {exists, readable}, canManage}`. `readers.users` are the people who read the record in full outside
the listed teams (named users of a restricted record, guests it is shared with). For a flow, `readers.teams` are the
teams that read every restricted end. It returns 403 to those who do not see the record in full.

Deleting an account removes it from every access list. A team named on an access list cannot be deleted (409).

## Protected notes

Applications and flows have `protectedNotes` (Markdown, up to 20,000 characters) for operational details that not
everyone needs: who to call, where the break-glass account lives, which vault path holds the credentials.

- They are read by administrators, members of the owning team and members on the access list (for a flow: of either
  end). **Guests never read them**, even when the record is shared with them.
- Other readers get `protectedNotes: null` and `protectedNotesHidden: true`. History snapshots, compares, proposals
  and search show `"••• protected"` instead of the text, and exports leave the notes out.
- A `PUT` or a proposal that leaves `protectedNotes` out (or sends `"••• protected"`) keeps the current notes; an
  empty text clears them. Anyone who may propose can thus write notes without reading the current ones; the author
  of a proposal sees what they wrote, the validators see both sides.

## Secrets are refused

Noosphere documents where secrets live, never their value. Every write (records, pages, proposals, refusal reasons,
settings, imports) is checked for private keys, AWS keys, GitHub, Slack, Stripe and Google tokens, Azure storage
keys, JSON Web Tokens, passwords in URLs and `password=` assignments. A match returns **422** with, for each field:

> This looks like a secret (GitHub token). Noosphere is documentation, not a vault: store it in your secret manager
> and write where it lives instead.

The value is never repeated. Placeholders pass: `<password>`, `***`, `changeme`, `${VAR}`, `$VAR`, `%VAR%`,
`{{var}}`.

## Sensitivity of an application

Each application gets a computed `sensitivity`: the most sensitive of the data objects it is the source of and of
the flows touching it, `internal` when there is none. `sensitivityReasons` lists the top five reasons
(`{kind: dataObject|flow, id, name, sensitivity}`). Flows the reader cannot see do not count. Filter with
`?sensitivity=public|internal|personal|sensitive` on `/api/applications`, `/api/flows` (the flow's own level) and
`/api/landscape`.

## Exports

`GET /api/export` writes what the reader sees in full (`scope: visible`): records shared with a guest, flows whose
two ends are exported, protected notes only where the reader may read them. Administrators may ask for
`?scope=full` (everything, other readers get 403) with an optional `reason` (up to 200 characters). Re-importing a
file without notes or access lists keeps those in place.

## Access log

Noosphere writes a line to the access log when someone outside the owning team:

- reads a restricted record (detail, history snapshot or compare): `what: record`;
- reads protected notes: `what: protectedNotes`;
- exports restricted records or protected notes, and for every full export: `what: export`, with the reason or a
  summary as `detail`.

Reads by members of the owning team are not logged. Each line records `why` the reader had access: `admin`,
`owningTeam`, `listedTeam` (with the team) or `listedUser`, and `admin: true` when the reader is an administrator.

`GET /api/access-log` (administrators) returns `{items, nextCursor}`, newest first, with the user's display name,
the record's title, kind and owning team. Filters: `username`, `kind`, `entityId`, `what`, `from`, `to` (a date, or
a date and time; `to` includes the whole day), `limit` (50, at most 200) and `cursor` (the `nextCursor` of the
previous page).

Lines older than `NOOSPHERE_ACCESS_LOG_DAYS` (365 by default) are deleted at startup and every day.
