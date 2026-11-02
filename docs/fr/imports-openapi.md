# Import : OpenAPI

[English](../en/imports-openapi.md) · [Tous les imports](imports.md)

Les fichiers OpenAPI 3.x, en YAML ou en JSON (Swagger 2.0 est refusé : convertissez-le d'abord). L'API devient :

- une **page** « *titre* API » (id `<application>-api`) rattachée à l'application, étiquetée `api` et `openapi`, qui
  résume la spécification : versions OpenAPI et de l'API, description, serveurs, chaque opération (méthode, chemin,
  résumé, operation id) et les noms des schémas ;
- un **flux** « *titre* » de type `api` de chaque consommateur vers l'application, quand le fichier nomme ses
  consommateurs.

L'application est l'option `applicationId`, sinon `x-noosphere-application`, sinon le titre de l'API (`info.title`),
retrouvée par id ou par nom. Elle est créée si elle n'existe pas ; si elle existe, seuls ses champs vides sont
remplis (sa description, depuis `info.description`).

| Extension | Sens |
| --------- | ---- |
| `x-noosphere-application` | Id ou nom de l'application qui fournit l'API. |
| `x-noosphere-consumers` | Ids ou noms des applications qui l'appellent : un flux chacune. Sans elle, seule la page est importée. |

Les schémas sont listés sur la page et ne sont pas importés comme objets de données : un schéma d'API est rarement un
objet de données métier. Créez des objets de données pour les données que transporte l'API.

## Exemple

```yaml
openapi: 3.0.3
info:
  title: Claims
  version: 2.4.0
  description: Déclaration et suivi des sinistres des adhérents de la Mutuelle Alizé.
x-noosphere-application: claims
x-noosphere-consumers: [portal, Mobile app]
servers:
  - url: https://api.alize.example/claims/v2
    description: Production
paths:
  /claims:
    post:
      summary: Déclarer un sinistre
      operationId: declareClaim
components:
  schemas:
    Claim: { type: object }
```

Le plan crée la page `claims-api` « Claims API » sur l'application `claims`, et les flux `portal-claims-api` et
`mobile-claims-api` (l'application mobile est retrouvée par son nom). La page contient :

```markdown
**OpenAPI 3.0.3** · version 2.4.0

Déclaration et suivi des sinistres des adhérents de la Mutuelle Alizé.

## Servers

| URL | Description |
| --- | --- |
| https://api.alize.example/claims/v2 | Production |

## Operations

1 operation(s) in Claims.

| Method | Path | Summary | Operation id |
| --- | --- | --- | --- |
| POST | `/claims` | Déclarer un sinistre | `declareClaim` |
```

Les intitulés de la page sont en anglais, comme les autres textes produits par le serveur.
