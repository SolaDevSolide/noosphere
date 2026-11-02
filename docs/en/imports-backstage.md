# Import: Backstage catalog

[Français](../fr/imports-backstage.md) · [All imports](imports.md)

Backstage catalog files (`catalog-info.yaml`), with one or several entities separated by `---`. The file is read as
the description of the records: the fields it gives replace the current ones.

| Backstage | Noosphere |
| --------- | --------- |
| `Domain` | domain |
| `Group` | team (`spec.profile.displayName` or `title` as name, `spec.profile.email` as contact) |
| `System` | application, in the domain of `spec.domain` |
| `Component` | application, in the domain of its `spec.system` |
| `Resource` | application of kind `data` (databases), `bus` (queues, topics) or `file` (buckets, file shares), in the domain of its system |
| `API` | a flow from each consumer (`consumesApis`) to each provider (`providesApis`); for an `asyncapi` API, an event flow from the provider to each consumer |
| `dependsOn`, `dependencyOf` | a flow "Depends on ..." from the component to what it depends on |
| `User`, `Location`, `Template`... | left out, with a warning |

Fields:

- the id is `metadata.name`, or the annotation `noosphere.io/id` when the Noosphere id differs; the name is
  `metadata.title`, or `metadata.name`;
- `metadata.description`, followed by `metadata.links` as a list of links, is the description;
- `spec.owner` (a group, as `group:name`, `name` or `group:namespace/name`) is the owning team; a user as owner is
  left out (teams own records);
- `spec.lifecycle`: `experimental` → `planned`, `production` → `live`, `deprecated` → `retiring` for applications,
  `deprecated` for API flows;
- references between entities may name a kind and a namespace (`api:default/crm-api`); the namespace is ignored.

An API without a provider or a consumer in the file creates no flow (warning). New applications whose system is not
in the file have no domain: they go to the `domainId` option or to the domain **Imported**.

## Example

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
  description: Health coaching sessions booked by the members of Mutuelle Alizé.
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

With a `Component` `crm` that provides `crm-api`, the plan creates the application `coaching` (domain `prevention`,
team `team-prevention`, lifecycle `planned`, the runbook link in its description) and the flow
`coaching-crm-crm-api` "Member lookup" from Coaching to the CRM, owned by Team CRM. Importing the file again changes
nothing.
