# Import: AsyncAPI

[Français](../fr/imports-asyncapi.md) · [All imports](imports.md)

AsyncAPI 2.x and 3.x files, in YAML or JSON. Events go through a broker: an application of kind `bus`.

| AsyncAPI | Noosphere |
| -------- | --------- |
| the file | the application it describes: `applicationId` option, `x-noosphere-application`, or `info.title` |
| `x-noosphere-bus`, or else the first server | the broker, an application of kind `bus` (server `title`, or "*Protocol* broker"; technology from `protocol` and `protocolVersion`) |
| a message (`name`, `title`, `summary`) | a data object; its source is the application when it sends the message; `x-noosphere-sensitivity` sets its sensitivity |
| 2.x `subscribe`, 3.x `send` | an event flow from the application to the broker |
| 2.x `publish`, 3.x `receive` | an event flow from the broker to the application |

A flow is named after its message (or its channel when it carries several), carries its messages as data objects,
and takes the operation summary as description. An existing flow with the same name between the same applications is
completed: its data objects are kept and the new ones added. The application, the broker and the data objects are
only filled in where empty when they exist. Without a server nor `x-noosphere-bus`, only the data objects are
imported (warning). Local `$ref` (`#/components/messages/...`) are followed; references to other files are not.

In AsyncAPI 2, `subscribe` describes what the application **sends** and `publish` what it **receives**: Noosphere
follows the specification.

## Example

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
      summary: An invoice is issued to a member.
      message:
        $ref: '#/components/messages/InvoiceIssued'
components:
  messages:
    InvoiceIssued:
      name: InvoiceIssued
      title: Invoice issued
      summary: An invoice and its amount.
```

In the demo landscape, the plan finds the flow "InvoiceIssued" from Billing to the event bus by its name and adds the
data object `invoiceissued` "Invoice issued" to it (created, with Billing as source). The existing data object
"Invoice" is shown as a possible match, not merged.
