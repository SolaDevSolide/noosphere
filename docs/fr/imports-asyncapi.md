# Import : AsyncAPI

[English](../en/imports-asyncapi.md) · [Tous les imports](imports.md)

Les fichiers AsyncAPI 2.x et 3.x, en YAML ou en JSON. Les événements passent par un bus : une application de type
`bus`.

| AsyncAPI | Noosphere |
| -------- | --------- |
| le fichier | l'application qu'il décrit : option `applicationId`, `x-noosphere-application` ou `info.title` |
| `x-noosphere-bus`, sinon le premier serveur | le bus, une application de type `bus` (`title` du serveur, ou « *Protocole* broker » ; technologie d'après `protocol` et `protocolVersion`) |
| un message (`name`, `title`, `summary`) | un objet de données ; sa source est l'application quand elle envoie le message ; `x-noosphere-sensitivity` fixe sa sensibilité |
| `subscribe` en 2.x, `send` en 3.x | un flux événementiel de l'application vers le bus |
| `publish` en 2.x, `receive` en 3.x | un flux événementiel du bus vers l'application |

Un flux porte le nom de son message (ou de son canal quand il en transporte plusieurs), transporte ses messages comme
objets de données et prend le résumé de l'opération comme description. Un flux existant de même nom entre les mêmes
applications est complété : ses objets de données sont gardés et les nouveaux ajoutés. L'application, le bus et les
objets de données ne sont complétés que là où ils sont vides quand ils existent. Sans serveur ni `x-noosphere-bus`,
seuls les objets de données sont importés (avertissement). Les `$ref` locaux (`#/components/messages/...`) sont
suivis ; les références à d'autres fichiers ne le sont pas.

En AsyncAPI 2, `subscribe` décrit ce que l'application **envoie** et `publish` ce qu'elle **reçoit** : Noosphere
suit la spécification.

## Exemple

```yaml
asyncapi: 2.6.0
info:
  title: Billing events
  version: 1.2.0
x-noosphere-application: billing
x-noosphere-bus: bus
channels:
  billing.invoice.issued:
    subscribe:
      summary: Une facture est émise pour un adhérent.
      message:
        $ref: '#/components/messages/InvoiceIssued'
components:
  messages:
    InvoiceIssued:
      name: InvoiceIssued
      title: Invoice issued
      summary: Une facture et son montant.
```

Dans le paysage de démo, le plan retrouve par son nom le flux « InvoiceIssued » de Billing vers le bus d'événements
et lui ajoute l'objet de données `invoiceissued` « Invoice issued » (créé, avec Billing comme source). L'objet de
données existant « Invoice » est montré comme correspondance possible, sans être fusionné.
