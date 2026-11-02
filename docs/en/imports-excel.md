# Import: Excel

[Français](../fr/imports-excel.md) · [All imports](imports.md)

Excel workbooks (`.xlsx`) laid out as the template: one sheet per kind of record, a header row, one record per row.
Other spreadsheet formats (`.xls`, `.ods`, `.csv`) are refused: save the file as an Excel workbook.

## Template

`GET /api/imports/template.xlsx` (or `noosphere imports template`) downloads the template: a **Read me** sheet that
explains every column and its accepted values, then the sheets below, with their header rows. The columns are the
fields of the [exchange format](exchange-format.md).

| Sheet | Columns |
| ----- | ------- |
| Domains | `id`, `name`, `description`, `order` |
| Teams | `id`, `name`, `contact`, `description` |
| Applications | `id`, `name`, `domainId`, `teamId`, `kind`, `tech`, `lifecycle`, `description` |
| Data objects | `id`, `name`, `sensitivity`, `sourceApplicationId`, `description` |
| Flows | `id`, `name`, `fromId`, `toId`, `type`, `schedule`, `dataObjectIds`, `ownerTeamId`, `status`, `volume`, `description` |

- `name` is required; `id` is optional (it is derived from the name for a new record).
- References (`domainId`, `teamId`, `sourceApplicationId`, `fromId`, `toId`, `ownerTeamId`) are ids or names, of the
  workbook or of the catalogue; `dataObjectIds` lists several, separated by commas.
- `kind`, `lifecycle`, `sensitivity`, `type` and `status` take the values of the exchange format (`app`, `live`,
  `personal`, `batch`...), in any case. Another value is an error that points to its cell (`Applications!E4`).
- An empty cell leaves the field as it is. Headers may also be written `domain`, `team`, `from`, `to`,
  `data objects`, `owner team`, `source application`. Sheets and columns not listed are skipped, with a warning.

## Example

Sheet **Applications**:

| id | name | domainId | teamId | kind | tech | lifecycle | description |
| -- | ---- | -------- | ------ | ---- | ---- | --------- | ----------- |
| coaching | Coaching | Prevention | Team Prevention | app | Kotlin | planned | Health coaching sessions. |
| | Billing | | | | .NET 9, PostgreSQL | | |

Sheet **Flows**:

| id | name | fromId | toId | type | schedule | dataObjectIds | ownerTeamId | status |
| -- | ---- | ------ | ---- | ---- | -------- | ------------- | ----------- | ------ |
| coaching-crm | Member lookup | coaching | CRM | api | On demand | customer, Coaching session | team-prevention | planned |

The second row of Applications has no id: it is matched by its name to the existing Billing, and only its technology
is updated. The flow refers to the CRM by name and to the data objects by id and by name.
