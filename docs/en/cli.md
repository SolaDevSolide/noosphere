# Command line

[Français](../fr/cli.md)

The `noosphere` command is the same executable as the server and reads the same `NOOSPHERE_*` variables. It works
on the database directly: no session is needed. In the container:

```sh
docker compose exec noosphere noosphere help
```

Outside Docker: `dotnet Noosphere.dll <command>`.

| Command | Does |
| ------- | ---- |
| `noosphere migrate` | Applies pending database migrations (with the backup described in [installation](installation.md#upgrades)). |
| `noosphere users list` | Lists the accounts. |
| `noosphere users create <username> [--role admin\|member\|guest] [--display-name <name>] [--language en\|fr] [--password <p>]` | Creates an account (`member` by default). Without `--password`, a random password is generated and printed once. |
| `noosphere users reset-password <username> [--password <p>]` | Sets a new password (generated when none is given) and closes the user's sessions. |
| `noosphere export [--format json\|yaml] [--output <file>]` | Writes the whole landscape and its pages, restricted records and protected notes included (`scope: full`), to standard output or to a file. |
| `noosphere import <file\|-> [--dry-run] [--format json\|yaml]` | Validates a file, then creates or updates its items. `-` reads standard input. The format comes from the extension, or is guessed. |
| `noosphere imports plan <file>... [--format <format>] [--domain <id>] [--team <id>] [--application <id>] [--parent-page <id>]` | Dry run of an [import from other formats](imports.md) (Backstage, OpenAPI, AsyncAPI, ADR, Mermaid, Excel; files or zip archives): prints the plan and its id. Exits 1 when the plan has errors. |
| `noosphere imports apply <plan-id>` | Applies that plan, within 30 minutes and if the catalogue has not changed since. |
| `noosphere imports template [--output <file>]` | Writes the Excel template of the import (`noosphere-import-template.xlsx` by default). |
| `noosphere demo reset --yes` | Replaces the whole landscape, its pages, history and proposals with the "Alizé" demo and resets the demo accounts. Demo mode only. |
| `noosphere link code [--expires 15m] [--capabilities <list>]` | Creates a pairing code for gestion-mep, shown once. See [links](links.md#command-line). |
| `noosphere link list` | Lists the links with gestion-mep instances. |
| `noosphere link confirm <id> --fingerprint "<four words>"` | Confirms a link after comparing fingerprints. |
| `noosphere link renew <id>` | Renews this side's keys of a link. |
| `noosphere link cut <id> --yes` | Cuts a link and erases its keys. |
| `noosphere health [--url <url>]` | Exits 0 when the running server is ready. Used by the image's `HEALTHCHECK`. |
| `noosphere version` | Prints the version. |

## Output

Results go to standard output, messages and errors to standard error. `--json` makes both machine-readable: data
commands (`users list`, `link list`) print their data, and the others print `{code, message, result}`. Errors are written as
`{code, error, details}`.

## Exit codes

| Code | Meaning |
| ---- | ------- |
| 0 | Success |
| 1 | Failure: invalid file, refused value, demo mode off |
| 2 | Usage error: unknown command or option, missing argument |
| 3 | Database unreachable or configuration missing |
| 4 | Confirmation missing (`--yes`) |
| 5 | Not found: user, file or link |

## Examples

```sh
noosphere users create dana --role admin --display-name "Dana Smith"
noosphere export --format yaml --output landscape.yaml
noosphere import landscape.yaml --dry-run
docker compose exec -T noosphere noosphere import - < landscape.json
noosphere imports plan catalog-info.yaml adr.zip --team team-crm
noosphere imports apply 3f1c2b9e-5d0a-4c1e-9b7a-2e8f6d4c1a20
```
