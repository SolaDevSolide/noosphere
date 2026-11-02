# Import : diagrammes Mermaid

[English](../en/imports-mermaid.md) · [Tous les imports](imports.md)

Les diagrammes Mermaid des fichiers `.mmd` ou `.mermaid`, ou des blocs ` ```mermaid ` d'un fichier Markdown (avec
l'option `format=mermaid`). Deux sortes de diagrammes sont lues :

- `flowchart` (ou `graph`) : chaque nœud est une application, chaque arête un flux. Les chaînes (`a --> b --> c`),
  les groupes (`a & b --> c`), les libellés (`-->|libellé|`, `-- libellé -->`) et les arêtes dans les deux sens
  (`<-->`, deux flux) sont lus. Un nœud dessiné comme une base de données (`id[(Nom)]`) est de type `data`.
- C4 (`C4Context`, `C4Container`, `C4Component`) : les éléments `System`, `Container` et `Component` sont des
  applications (`...Db` de type `data`, `...Queue` de type `bus`, `..._Ext` de type `external` ; la technologie d'un
  conteneur est sa `tech`), `Rel`, `Rel_U`, `Rel_D`, `Rel_L`, `Rel_R`, `Rel_Back` et `BiRel` sont des flux. Les
  personnes (`Person`) sont laissées de côté, avec leurs relations.

L'id d'une application est l'id du nœud ou l'alias C4. Un flux porte le nom de son libellé, ou « *A* to *B* » ; son
type est déduit du libellé et de la technologie : un événement quand il cite Kafka, une file, un topic ou un événement,
ou quand une extrémité est un bus ; un batch pour les fichiers et le SFTP ; une API sinon. Son id est fait des ids de
ses extrémités (`portal-booking`).

Un `subgraph` ou une frontière C4 dont le titre est le nom d'un domaine existant met ses nouvelles applications dans
ce domaine. Un diagramme décrit le paysage à grands traits : les applications et les flux qui existent ne sont
complétés que là où ils sont vides (leur nom, leur technologie et leur type sont gardés).

## Exemple

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

Dans le paysage de démo, `portal` est le portail client existant (inchangé) ; le plan crée Booking (Java) dans le
domaine Customer, Booking database (type `data`), Calendar service (type `external`, dans le domaine Imported) et les
flux `portal-booking` (API), `booking-bookingdb` (API) et `booking-calendar` (événement). L'adhérent et la relation
« Uses » sont laissés de côté.
