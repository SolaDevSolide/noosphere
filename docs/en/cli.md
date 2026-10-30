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
| `noosphere export [--format json\|yaml] [--output <file>]` | Writes the whole landscape, restricted applications included, to standard output or to a file. |
| `noosphere import <file\|-> [--dry-run] [--format json\|yaml]` | Validates a file, then creates or updates its items. `-` reads standard input. The format comes from the extension, or is guessed. |
| `noosphere demo reset --yes` | Replaces the whole landscape with the "Alizé" demo and resets the demo accounts. Demo mode only. |
| `noosphere health [--url <url>]` | Exits 0 when the running server is ready. Used by the image's `HEALTHCHECK`. |
| `noosphere version` | Prints the version. |

## Output

Results go to standard output, messages and errors to standard error. `--json` makes both machine-readable: data
commands (`users list`) print their data, and the others print `{code, message, result}`. Errors are written as
`{code, error, details}`.

## Exit codes

| Code | Meaning |
| ---- | ------- |
| 0 | Success |
| 1 | Failure: invalid file, refused value, demo mode off |
| 2 | Usage error: unknown command or option, missing argument |
| 3 | Database unreachable or configuration missing |
| 4 | Confirmation missing (`--yes`) |
| 5 | Not found: user or file |

## Examples

```sh
noosphere users create dana --role admin --display-name "Dana Smith"
noosphere export --format yaml --output landscape.yaml
noosphere import landscape.yaml --dry-run
docker compose exec -T noosphere noosphere import - < landscape.json
```
