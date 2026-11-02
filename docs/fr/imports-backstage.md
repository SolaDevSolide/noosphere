# Import : catalogue Backstage

[English](../en/imports-backstage.md) · [Tous les imports](imports.md)

Les fichiers de catalogue Backstage (`catalog-info.yaml`), avec une ou plusieurs entités séparées par `---`. Le
fichier est lu comme la description des enregistrements : les champs qu'il donne remplacent les valeurs actuelles.

| Backstage | Noosphere |
| --------- | --------- |
| `Domain` | domaine |
| `Group` | équipe (`spec.profile.displayName` ou `title` comme nom, `spec.profile.email` comme contact) |
| `System` | application, dans le domaine de `spec.domain` |
| `Component` | application, dans le domaine de son `spec.system` |
| `Resource` | application de type `data` (bases de données), `bus` (files, topics) ou `file` (buckets, partages de fichiers), dans le domaine de son système |
| `API` | un flux de chaque consommateur (`consumesApis`) vers chaque fournisseur (`providesApis`) ; pour une API `asyncapi`, un flux événementiel du fournisseur vers chaque consommateur |
| `dependsOn`, `dependencyOf` | un flux « Depends on ... » du composant vers ce dont il dépend |
| `User`, `Location`, `Template`... | laissés de côté, avec un avertissement |

Champs :

- l'id est `metadata.name`, ou l'annotation `noosphere.io/id` quand l'id Noosphere diffère ; le nom est
  `metadata.title`, ou `metadata.name` ;
- `metadata.description`, suivie de `metadata.links` sous forme de liste de liens, est la description ;
- `spec.owner` (un groupe, écrit `group:nom`, `nom` ou `group:espace/nom`) est l'équipe propriétaire ; un
  utilisateur propriétaire est laissé de côté (ce sont les équipes qui possèdent les enregistrements) ;
- `spec.lifecycle` : `experimental` → `planned`, `production` → `live`, `deprecated` → `retiring` pour les
  applications, `deprecated` pour les flux d'API ;
- les références entre entités peuvent nommer un type et un espace de noms (`api:default/crm-api`) ; l'espace de noms
  est ignoré.

Une API sans fournisseur ou sans consommateur dans le fichier ne crée pas de flux (avertissement). Les nouvelles
applications dont le système n'est pas dans le fichier n'ont pas de domaine : elles vont dans l'option `domainId` ou
dans le domaine **Imported**.

## Exemple

```yaml
apiVersion: backstage.io/v1alpha1
kind: System
metadata:
  name: prevention-platform
  title: Prevention platform
spec:
  owner: team-prevention
  domain: prevention
---
apiVersion: backstage.io/v1alpha1
kind: Component
metadata:
  name: coaching
  title: Coaching
  description: Séances de coaching santé réservées par les adhérents de la Mutuelle Alizé.
  links:
    - url: https://wiki.alize.example/coaching
      title: Runbook
spec:
  type: service
  lifecycle: experimental
  owner: group:default/team-prevention
  system: prevention-platform
  providesApis: [coaching-api]
  consumesApis: [api:crm-api]
---
apiVersion: backstage.io/v1alpha1
kind: API
metadata:
  name: crm-api
  title: Member lookup
spec:
  type: openapi
  lifecycle: production
  owner: team-crm
```

Avec un `Component` `crm` qui fournit `crm-api`, le plan crée l'application `coaching` (domaine `prevention`, équipe
`team-prevention`, cycle de vie `planned`, le lien du runbook dans sa description) et le flux `coaching-crm-crm-api`
« Member lookup » de Coaching vers le CRM, possédé par Team CRM. Importer de nouveau le fichier ne change rien.
