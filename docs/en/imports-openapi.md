# Import: OpenAPI

[Français](../fr/imports-openapi.md) · [All imports](imports.md)

OpenAPI 3.x files, in YAML or JSON (Swagger 2.0 is refused: convert it first). The API becomes:

- a **page** "*title* API" (id `<application>-api`) attached to the application, tagged `api` and `openapi`, holding
  a summary of the spec: OpenAPI and API versions, description, servers, every operation (method, path, summary,
  operation id) and the names of the schemas;
- a **flow** "*title*" of type `api` from each consumer to the application, when the file names its consumers.

The application is the `applicationId` option, or else `x-noosphere-application`, or else the API title (`info.title`),
found by id or name. It is created when it does not exist; when it exists, only its empty fields are filled (its
description from `info.description`).

| Extension | Meaning |
| --------- | ------- |
| `x-noosphere-application` | Id or name of the application that provides the API. |
| `x-noosphere-consumers` | Ids or names of the applications that call it: one flow each. Without it, only the page is imported. |

Schemas are listed on the page and not imported as data objects: an API schema is rarely a business data object.
Create data objects for the data the API carries.

## Example

```yaml
openapi: 3.0.3
info:
  title: Claims
  version: 2.4.0
  description: Claim declarations and follow-up for the members of Mutuelle Alizé.
x-noosphere-application: claims
x-noosphere-consumers: [portal, Mobile app]
servers:
  - url: https://api.alize.example/claims/v2
    description: Production
paths:
  /claims:
    post:
      summary: Declare a claim
      operationId: declareClaim
components:
  schemas:
    Claim: { type: object }
```

The plan creates the page `claims-api` "Claims API" on the application `claims`, and the flows `portal-claims-api`
and `mobile-claims-api` (the mobile app is found by its name). The page reads:

```markdown
**OpenAPI 3.0.3** · version 2.4.0

Claim declarations and follow-up for the members of Mutuelle Alizé.

## Servers

| URL | Description |
| --- | --- |
| https://api.alize.example/claims/v2 | Production |

## Operations

1 operation(s) in Claims.

| Method | Path | Summary | Operation id |
| --- | --- | --- | --- |
| POST | `/claims` | Declare a claim | `declareClaim` |
```
