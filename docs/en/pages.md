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
| `visibility` | `everyone` or `restricted` (administrators, the owning team and the access list; others get `{id, restricted: true, parentId, order}`) |
| `access` | `{teamIds, usernames}`: who sees a restricted page, and the guests it is shared with (see [access](access.md)) |

Saving a page follows the [proposal and validation](proposals.md) rules and creates a [revision](history.md).

## Wiki links

Link records and other pages with double brackets, optionally with a label:

```markdown
The invoice run waits for [[flow:crm-to-billing-customers]].
See [[application:billing|the billing app]] and [[page:glossary]].
```

Kinds: `application`, `flow`, `dataObject`, `page`, `domain`, `team`. On every save the server extracts the links;
links written in code (between backticks or in a fenced block) are examples, not links.
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

## In the interface

The **Pages** button in the header opens the pages area: the tree on the left (a drawer on phones), recently updated
pages, the pages with broken links, and a reminder that every page is also served as plain Markdown for assistants.

- **Tree.** Filter it by title. Members who can publish a page drag it to reorder it, or drop it on another page to
  move it under that page (the change is saved at once, with `POST /api/pages/{id}/move`).
- **Reading a page.** Wiki links show as chips with the record's name and icon; a link whose target does not exist is
  shown in red with a warning, and the page still opens. The side column has the table of contents, **Mentioned in**
  (pages linking to this one) and the links of the page. **Copy as Markdown** copies the text with its front matter;
  **Raw** shows it. **History** opens the [revision history](history.md).
- **Writing.** The editor has a toolbar, a live preview and a settings panel: title, address, parent page, attached
  record, owner team, tags and visibility. Type `[[` to search records and pages and insert a link (`[[flow:cust` looks
  for flows only). Broken links are listed above the preview, in red, but do not block saving. A draft is kept in the
  browser until you publish or cancel.
- **Address.** The address (`/pages/{id}`) is chosen when the page is created, from the title, and does not change
  afterwards, so links to the page keep working.
- **Owner.** Any member can create a page: attach it to a record (its owner then owns the page) or choose an owner
  team. Members of the owning team publish; others **Suggest a change**, which becomes a [proposal](proposals.md).
  The owners can make a page restricted and choose who sees it.
- **Templates.** **New page** offers a blank page and three templates: **Runbook** (symptoms, checks, fix, escalation),
  **Decision record (ADR)** (context, decision, consequences) and **Application overview** (purpose, flows, contacts,
  links). A template adds its tag
  (`runbook`, `adr`, `overview`), shown above the page title.
- **Records.** Applications, flows, data objects (in the lineage panel), teams and domains show their pages and the
  pages that mention them, with a link to create a page attached to the record.
- **Search.** The search box in the header also finds pages.

## Not yet supported

Images and attachments in pages are not supported in this version: a Markdown image is shown as a link to the image.
Diagrams (Mermaid) are not rendered either; write them in a code block for now. Raw HTML in a page is shown as text.
