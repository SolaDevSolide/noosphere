# Pages

[Français](../fr/pages.md)

Documentation pages are written in Markdown: runbooks, architecture decisions, domain overviews, glossaries. A page
stands on its own or is **attached** to an application, a flow, a data object, a domain or a team, and pages are
organised in a **tree**.

## Fields

| Field | Description |
| ----- | ----------- |
| `id` | kebab-case slug, derived from the title when not given |
| `title` | required |
| `body` | Markdown |
| `parentId` | parent page; empty for a top-level page. A page cannot be placed under itself or one of its sub-pages. |
| `order` | position among its siblings (then by title) |
| `attachedTo` | `{kind, id}`, with `kind` one of `application`, `flow`, `dataObject`, `domain`, `team` |
| `ownerTeamId` | owning team; when empty, the owner of the attached record owns the page (administrators when none) |
| `tags` | up to 20 short texts |
| `visibility` | `everyone` or `restricted` (administrators only; others get `{id, restricted: true, parentId, order}`) |

Saving a page follows the [proposal and validation](proposals.md) rules and creates a [revision](history.md).

## Wiki links

Link records and other pages with double brackets, optionally with a label:

```markdown
The invoice run waits for [[flow:crm-to-billing-customers]].
See [[application:billing|the billing app]] and [[page:glossary]].
```

Kinds: `application`, `flow`, `dataObject`, `page`, `domain`, `team`. On every save the server extracts the links.
`GET /api/pages/{id}` returns them resolved (`links: [{kind, id, title, exists, restricted, label}]`); a link whose
target does not exist has `exists: false` and is shown as broken. It also returns `backlinks` (pages linking to this
page), `ancestors` (breadcrumb), `children` and the reader's `permissions`.

Records list the pages attached to them (`pages`) and the pages that link to them (`mentionedIn`).

## Endpoints

| Method and path | Description |
| --------------- | ----------- |
| `GET /api/pages?attachedKind=&attachedId=&tag=&q=` | list without bodies, with the number of broken links |
| `GET /api/pages/tree` | the tree: `[{id, title, order, attachedTo, children}]` |
| `GET /api/pages/{id}` | the page with links, backlinks, ancestors and children |
| `GET /api/pages/{id}/raw` | `text/markdown` with a YAML front matter (id, title, parent, attachedTo, owner, tags, version, links) |
| `POST /api/pages`, `PUT /api/pages/{id}`, `DELETE /api/pages/{id}` | create, replace (with `version`), delete (not while it has sub-pages) |
| `POST /api/pages/{id}/move` | `{parentId, order, version?}` |

Search (`/api/search`) includes pages: the title first, then the body, with an excerpt around the match. Pages are
part of the [exchange format](exchange-format.md) as the `Page` kind.

## Not yet supported

Images and attachments in pages are not supported in this version.
