# Import: Mermaid diagrams

[Français](../fr/imports-mermaid.md) · [All imports](imports.md)

Mermaid diagrams in `.mmd` or `.mermaid` files, or in the ` ```mermaid ` blocks of a Markdown file (with the
`format=mermaid` option). Two kinds of diagrams are read:

- `flowchart` (or `graph`): every node is an application, every edge a flow. Chains (`a --> b --> c`), groups
  (`a & b --> c`), labels (`-->|label|`, `-- label -->`) and both-way edges (`<-->`, two flows) are read. A node drawn
  as a database (`id[(Name)]`) is of kind `data`.
- C4 (`C4Context`, `C4Container`, `C4Component`): `System`, `Container` and `Component` elements are applications
  (`...Db` of kind `data`, `...Queue` of kind `bus`, `..._Ext` of kind `external`; the technology of a container is
  its `tech`), `Rel`, `Rel_U`, `Rel_D`, `Rel_L`, `Rel_R`, `Rel_Back` and `BiRel` are flows. People (`Person`) are
  left out, with their relations.

The id of an application is the node id or the C4 alias. A flow is named after its label, or "*A* to *B*"; its type
is guessed from the label and the technology: an event when it mentions Kafka, a queue, a topic or an event, or when
one end is a bus; a batch for files and SFTP; an API otherwise. Its id is made of the ids of its ends
(`portal-booking`).

A `subgraph` or a C4 boundary whose title is the name of an existing domain puts its new applications in that domain.
A diagram describes the landscape loosely: applications and flows that exist are only filled in where empty (their
name, technology and type are kept).

## Example

```text
C4Container
title Coaching booking
Person(member, "Member")
System_Boundary(c1, "Customer") {
  Container(portal, "Customer portal", "React")
  Container(booking, "Booking", "Java", "Books coaching sessions")
  ContainerDb(bookingdb, "Booking database", "PostgreSQL")
}
System_Ext(calendar, "Calendar service")
Rel(member, portal, "Uses", "HTTPS")
Rel(portal, booking, "Books sessions", "REST")
Rel(booking, bookingdb, "Reads and writes", "JDBC")
Rel_R(booking, calendar, "Publishes slots", "Kafka")
```

In the demo landscape, `portal` is the existing customer portal (unchanged); the plan creates Booking (Java) in the
domain Customer, Booking database (kind `data`), Calendar service (kind `external`, in the domain Imported) and the
flows `portal-booking` (API), `booking-bookingdb` (API) and `booking-calendar` (event). The member and the relation
"Uses" are left out.
