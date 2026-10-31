# Demo toolbox

[Français](../fr/demo.md)

With `NOOSPHERE_DEMO=true`, Noosphere adds a toolbox to show its collaboration features alone: switch persona in one
click and make the "other side" act (a colleague, another team, a validator, an AI agent, the guest). Outside demo
mode every `/api/demo/*` address answers 404. Never turn demo mode on for a shared server: anyone can sign in as
any demo account.

In the interface, the header shows a **Demo** marker and a **Demo** button opens the toolbox: personas, the
scenarios below, the demo agent and the reset, each with a notice and a **Show me** link. It is hidden when demo mode
is off. See the [user guide](user-guide.md#demo-toolbox).

The demo landscape also holds an active [link](links.md#demo) to a fictional gestion-mep instance, "MEP Alizé",
with deployments on several applications and a few exchanges. It never calls out; the reset seeds it again.

## Personas

| Persona | Role | What to try |
| ------- | ---- | ----------- |
| `alice` | admin | Open the agents console, change the agent ceiling, try the emergency stop, and read the access log. |
| `bob` | member, Team Finance and Team Payments | Validate David's proposals on Billing, edit Billing while a colleague changes it, and connect an AI agent. |
| `claire` | guest | See the customer portal in full because it is shared with her; everything else shows its outline only. |
| `david` | member, Team CRM | Propose a change to a Finance record, follow its validation, and let an agent draft a change. |

`GET /api/demo/state` returns them (with the text in English and French), the scenarios and the current account.
`POST /api/demo/switch {"username": "bob"}` signs in as that account without a password.

## Scenarios

Each answer carries a `notice: {title, detail, link?}` in the person's language, for the interface to show as a
toast. `link` is a frontend path: `/{applications|flows|data-objects|pages|teams|domains}/{id}`, `/review/{id}`,
`/my-agents`, `/admin/access` or `/`.

| Call | What happens |
| ---- | ------------ |
| `POST /api/demo/simulate/edit {kind, id}` | A colleague of the owning team (an administrator if none) publishes a small change: a new revision under their name. Saving the version you had open now gets 409, and open proposals on it become outdated (`outdatedProposals`). |
| `POST /api/demo/simulate/proposal {kind?, id?}` | A member of another team submits a proposal on a record of your team (one is picked when none is given). |
| `POST /api/demo/simulate/decision {proposalId, decision, reason?}` | A validator of the owning team (or an administrator) accepts or refuses one of your submitted proposals; a refusal has a reason. |
| `POST /api/demo/simulate/agent {scenario}` | The "Demo assistant" agent, connected for you, acts through the same services as MCP: `draft` (search, read, draft only you see), `submit` (the same, then submitted), `refused` (opens a restricted record above the ceiling, writes a token: both refused, with the reason), `quota` (calls until its quota, then paused until the next hour; the demo uses 3 calls). The answer lists the activity lines written. |
| `POST /api/demo/simulate/guest-read {kind, id}` | `claire` reads the record: a restricted record shared with her writes an access log line; a record not shared with her is refused and nothing is logged. |
| `POST /api/demo/reset {"confirm": true}` | Reloads the demo landscape, pages, history and proposals, and removes agent connections and the access log. The accounts, their passwords and sessions stay. |

The command line `noosphere demo reset --yes` reloads the demo too, and also resets the demo accounts.
