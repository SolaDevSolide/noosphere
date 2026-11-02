# Import: architecture decision records

[Français](../fr/imports-adr.md) · [All imports](imports.md)

Architecture decision records in Markdown, one file per decision, sent one by one or as a zip archive. Both common
styles are read:

- **MADR**: a YAML front matter (`status`, `date`), or a list `* Status: accepted` and `* Date: 2026-04-15`;
- **Nygard**: a `Date: 2026-03-02` line and a `## Status` section.

Each record becomes a page, as decision records already are in Noosphere:

| ADR | Page |
| --- | ---- |
| file name `0002-invoice-run-on-postgresql.md` | id `adr-0002-invoice-run-on-postgresql` |
| number (from the file name or the title) and title `# 2. Run the invoice batch...` | title `ADR 0002: Run the invoice batch...`, order 2 |
| status and date | a first line `**Status:** accepted · **Date:** 2026-04-15`, and the tags `adr` and the status (`accepted`, `proposed`, `superseded`...) |
| the text | the body, without the front matter and the title |
| front matter `application:` or the `applicationId` option | attached to that application |

The pages go under the `parentPageId` option, or else the "Decision records" page: found by id
(`decision-records`) or title, or else the only top-level page tagged `adr` (in the demo, "Architecture decisions").
It is created when there is none.

Applications named in the text (whole words, case and accents ignored) get a **Records mentioned** section of wiki
links at the end of the page, and the record is attached to the application when it mentions only one. Wiki links
already in the text are kept. The `teamId` option gives the new pages an owning team; without it, a page is owned by
the team of the application it is attached to, and a page attached to nothing is published by administrators only.

## Example

`0002-invoice-run-on-postgresql.md`:

```markdown
---
status: accepted
date: 2026-04-15
deciders: Team Finance
---
# Run the invoice batch on PostgreSQL

## Context and Problem Statement

The monthly invoice run of Billing takes six hours on the current database.

## Decision Outcome

Chosen option: "PostgreSQL partitioning", because Billing already runs on PostgreSQL.
```

becomes the page "ADR 0002: Run the invoice batch on PostgreSQL", tagged `adr` and `accepted`, attached to Billing:

```markdown
**Status:** accepted · **Date:** 2026-04-15

## Context and Problem Statement

The monthly invoice run of Billing takes six hours on the current database.

## Decision Outcome

Chosen option: "PostgreSQL partitioning", because Billing already runs on PostgreSQL.

## Records mentioned

- [[application:billing]]
```
