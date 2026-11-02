# Import : décisions d'architecture

[English](../en/imports-adr.md) · [Tous les imports](imports.md)

Les décisions d'architecture (ADR) en Markdown, un fichier par décision, envoyés un par un ou dans une archive zip.
Les deux styles courants sont lus :

- **MADR** : un en-tête YAML (`status`, `date`), ou une liste `* Status: accepted` et `* Date: 2026-04-15` ;
- **Nygard** : une ligne `Date: 2026-03-02` et une section `## Status`.

Chaque décision devient une page, comme le sont déjà les décisions dans Noosphere :

| ADR | Page |
| --- | ---- |
| nom de fichier `0002-invoice-run-on-postgresql.md` | id `adr-0002-invoice-run-on-postgresql` |
| numéro (d'après le nom de fichier ou le titre) et titre `# 2. Run the invoice batch...` | titre `ADR 0002: Run the invoice batch...`, ordre 2 |
| statut et date | une première ligne `**Status:** accepted · **Date:** 2026-04-15`, et les étiquettes `adr` et le statut (`accepted`, `proposed`, `superseded`...) |
| le texte | le corps, sans l'en-tête ni le titre |
| `application:` dans l'en-tête, ou l'option `applicationId` | rattachée à cette application |

Les pages vont sous l'option `parentPageId`, sinon sous la page « Decision records » : retrouvée par id
(`decision-records`) ou par titre, sinon la seule page de premier niveau étiquetée `adr` (dans la démo,
« Architecture decisions »). Elle est créée s'il n'y en a pas.

Les applications nommées dans le texte (mots entiers, casse et accents ignorés) donnent une section
**Records mentioned** de liens wiki à la fin de la page, et la décision est rattachée à l'application quand elle n'en
mentionne qu'une. Les liens wiki déjà présents sont gardés. L'option `teamId` donne une équipe propriétaire aux
nouvelles pages ; sans elle, une page appartient à l'équipe de l'application à laquelle elle est rattachée, et une page
rattachée à rien n'est publiée que par les administrateurs.

## Exemple

`0002-invoice-run-on-postgresql.md` :

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

devient la page « ADR 0002: Run the invoice batch on PostgreSQL », étiquetée `adr` et `accepted`, rattachée à Billing :

```markdown
**Status:** accepted · **Date:** 2026-04-15

## Context and Problem Statement

The monthly invoice run of Billing takes six hours on the current database.

## Decision Outcome

Chosen option: "PostgreSQL partitioning", because Billing already runs on PostgreSQL.

## Records mentioned

- [[application:billing]]
```
