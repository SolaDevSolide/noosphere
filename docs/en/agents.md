# AI agents (MCP)

[Français](../fr/agents.md)

Noosphere has a [Model Context Protocol](https://modelcontextprotocol.io) server at `/mcp`. An AI agent (a desktop
assistant, a coding assistant, an editor extension, a script) connects **for one person**, after that person agrees
on a consent page. It then reads the landscape and prepares drafts with that person's rights, never more, and never
publishes: the owning teams still validate every change.

- [Connect an agent](#connect-an-agent)
- [What an agent may do](#what-an-agent-may-do): scopes, the ceiling, drafts
- [Tools](#tools)
- [Agent settings](#agent-settings), the quota and the emergency stop
- [My agents and the console](#my-agents-and-the-console)
- [llms.txt and Markdown views](#llmstxt-and-markdown-views)
- [OAuth reference](#oauth-reference) for client developers

## Connect an agent

Give the agent the address of the MCP server, shown in **My agents**: `https://noosphere.example.com/mcp`. Clients
that support MCP authorization find everything else themselves: on the first call, Noosphere answers 401 with the
address of its OAuth metadata, the client registers, and your browser opens the consent page.

1. Add the server to the client (examples below), then start it or ask it to connect.
2. Your browser opens Noosphere's consent page. Sign in if needed. The page shows the agent's name, the address it
   returns to, your name, and in plain words what it will be allowed to do under the current ceiling.
3. Choose **Allow** (you may untick drafting) or **Deny**. The browser returns to the agent, which is connected.

If an administrator turned on **approval required**, the connection then waits in the console until an
administrator approves it; the agent gets a clear error until then.

### Configuration examples

Most clients read a JSON file listing the servers. A remote server over HTTP is usually written:

```json
{
  "mcpServers": {
    "noosphere": {
      "type": "http",
      "url": "https://noosphere.example.com/mcp"
    }
  }
}
```

- **Desktop assistants** (for example Claude Desktop): add a custom connector with the URL above in the connector
  settings, or the JSON entry in the configuration file of the application.
- **Command-line coding assistants** (for example Claude Code): `claude mcp add --transport http noosphere
  https://noosphere.example.com/mcp`, then run `/mcp` in a session to sign in.
- **Editors** (for example VS Code): add the server in the editor's MCP configuration (`.vscode/mcp.json`:
  `{"servers": {"noosphere": {"type": "http", "url": "https://noosphere.example.com/mcp"}}}`), then start it from
  the list of servers.

A client that does not support MCP authorization can use a token obtained once through the flow described in the
[OAuth reference](#oauth-reference), sent as `Authorization: Bearer <token>`. Tokens last one hour; the client keeps
the refresh token to get new ones.

### Try it from a terminal

With the demo running on `http://localhost:8080` (`NOOSPHERE_DEMO=true`), the MCP Inspector lists the tools and calls
one, given a token (`<token>`: an access token from the flow below):

```sh
npx @modelcontextprotocol/inspector@0.21.2 --cli http://localhost:8080/mcp --transport http \
  --header "Authorization: Bearer <token>" --method tools/list
npx @modelcontextprotocol/inspector@0.21.2 --cli http://localhost:8080/mcp --transport http \
  --header "Authorization: Bearer <token>" --method tools/call --tool-name search --tool-arg query=billing
```

A script built on the official TypeScript SDK (`@modelcontextprotocol/sdk`) runs the whole flow with an
`OAuthClientProvider`: `StreamableHTTPClientTransport(new URL("http://localhost:8080/mcp"), {authProvider})`, then
`connect()` throws `UnauthorizedError` after calling `redirectToAuthorization(url)`; open the URL, approve, and call
`transport.finishAuth(code)` with the `code` of the redirect before connecting again.

## What an agent may do

An agent sees the **minimum** of three things: what the person sees, the **ceiling** set by the administrators, and
the **scopes** granted on the consent page.

| Scope | Allows | Granted to |
| ----- | ------ | ---------- |
| `docs:read` | Read the records and pages the person sees, within the ceiling | everyone |
| `docs:draft` | Write drafts and submit them as change proposals | members and administrators (not guests) |
| `docs:read:restricted` | Read the restricted records the person sees | only under the `asUser` ceiling |

| Ceiling | Records visible to everyone | Protected notes | Restricted records the person sees |
| ------- | --------------------------- | --------------- | ---------------------------------- |
| `public` (default) | yes | no (`protectedNotes: withheld`) | no, even if the person sees them |
| `public+protected` | yes | yes, when the person reads them | no |
| `asUser` | yes | yes, when the person reads them | with `docs:read:restricted` |

- A guest's agent reads only what is shared with the guest.
- Every refusal says why: `Refused (ceiling)`, `Refused (visibility)`, `Refused (protected notes)`,
  `Refused (secret)` or `Refused (role)`. The reason is returned to the agent and written in its activity.
- An agent's reads of restricted records and protected notes are written to the
  [access log](access.md#access-log) under the person's name, with the detail `AI agent: <name>`.

### Drafts and proposals

`draft_change` writes a **draft** proposal (`authorKind: "agent"`, `onBehalfOf`: the person, `agentName`). Until it
is submitted, only the person and the administrators see it; it is listed in **My agents**, not in the review area.
The agent (`submit_draft`) or the person (from My agents) submits it; the owning teams then accept or refuse it like
any proposal, and the agent reads the decision and the reason with `get_proposal_status`. Secrets are refused in
drafts as everywhere. Agents never publish, delete or restore anything themselves.

## Tools

Names and descriptions are written for models; each call writes one line of activity.

| Tool | Arguments | Answer |
| ---- | --------- | ------ |
| `search` | `query`, `kinds?` (application, flow, dataObject, team, domain, page) | `{query, count, results: [{kind, id, title, subtitle, url}]}` (top 20) |
| `get_record` | `kind`, `id` | Markdown with a YAML front matter: fields, flows in and out, data, pages, protected notes when allowed |
| `get_page` | `id` | The page as Markdown with a front matter; wiki links stay `[[kind:id]]` |
| `list_landscape` | `domain?`, `team?` | Domains, teams, applications and flows (ids, names, lifecycle, sensitivity) |
| `data_lineage` | `dataObjectId` | Source application, flows carrying it, receiving applications |
| `who_owns` | `kind`, `id` | Owning team, its contact and members, who validates, how to propose |
| `list_my_drafts` | | This agent's drafts and open proposals |
| `draft_change` | `kind`, `id` or null, `operation` (create, update, delete), `fields`, `comment?` | `{saved, draftId, changedFields, warnings, next}`, or the errors (`secretRefused`) |
| `submit_draft` | `draftId`, `title`, `comment?` | `{submitted, proposalId, validators, link}` |
| `get_proposal_status` | `id` | Status, decision, refusal reason, validators |

`fields` are merged onto the current record for an update (for example `{"description": "…"}`), and are the whole
record for a create. A second `draft_change` on the same record updates the same draft.

Errors: a refused call is a tool result with `isError: true` and the reason. A paused, revoked or waiting
connection gets a JSON-RPC error, code `-32029`, with the reason and when it ends.

## Agent settings

Administrators change them in the settings (`GET` for everyone signed in, `PUT /api/settings/agents` with
`version`):

| Setting | Default | Meaning |
| ------- | ------- | ------- |
| `enabled` | `true` | Off: agents get 401 and no new connection is possible; connections and tokens are kept and work again when it is back on. |
| `ceiling` | `public` | See [what an agent may do](#what-an-agent-may-do). |
| `quotaPerHour` | `600` | Calls per connection and per clock hour (1 to 100 000). |
| `whoConnects` | `anyMember` | `approvalRequired`: new connections of members and guests wait for an administrator. |
| `dynamicRegistration` | `true` | Off: clients must identify with a Client ID Metadata Document. |
| `anonymousLlms` | `false` | On: `/llms.txt` and the Markdown views are readable without signing in (records visible to everyone, without protected notes). |

**Quota.** Past the quota, the call gets the JSON-RPC error `-32029` ("Quota reached: this agent made N calls this
hour… It is paused until HH:00 UTC"), a quota line is written, and the connection is paused until the next hour.

**Emergency stop.** `POST /api/agents/stop-all {"confirm": true}` (administrators) revokes every connection and token
at once, cancels authorizations in progress, writes one activity line per connection and a warning in the server
log. People connect their agents again afterwards.

Activity is kept 90 days (`NOOSPHERE_AGENT_ACTIVITY_DAYS`); expired tokens are deleted every day.

## My agents and the console

- **My agents** (every person): their connections with state (`pending`, `active`, `paused`, `revoked`), calls in
  the last hour, what each agent can do, its activity, its drafts and proposals. Revoking takes effect at once.
- **Console** (administrators): every connection with its person, approval of waiting connections, all activity
  with filters, and the emergency stop.

API: see [API, AI agents](api.md#ai-agents).

## llms.txt and Markdown views

| Address | Content |
| ------- | ------- |
| `/llms.txt` | An [llmstxt.org](https://llmstxt.org)-style index: one section per kind, a link and a line per record |
| `/llms-full.txt` | Every record the reader may see, as Markdown, in one file |
| `/{kind}/{id}.md` | One record: `application`, `flow`, `data-object`, `team`, `domain` or `page` |

Readers: a signed-in session (the person's rights), a bearer token with `docs:read` (the agent's rights; the call
counts in its quota and activity), or anyone when `anonymousLlms` is on. Otherwise 401 with the same
`WWW-Authenticate` header as `/mcp`. A record the reader may not see gives 403, an unknown one 404.

## OAuth reference

Noosphere is the OAuth 2.1 authorization server and the protected resource of its own MCP endpoint.

| Address | Role |
| ------- | ---- |
| `/.well-known/oauth-protected-resource` (also `.../oauth-protected-resource/mcp`) | RFC 9728: `resource` = `{base}/mcp`, the authorization server, the scopes |
| `/.well-known/oauth-authorization-server` | RFC 8414 metadata (`client_id_metadata_document_supported: true`) |
| `GET /oauth/authorize` | Authorization code with PKCE `S256` only; redirects to the consent page `/oauth/consent?request={id}` |
| `POST /oauth/token` | `authorization_code` and `refresh_token` (form encoded, public clients, no secret) |
| `POST /oauth/revoke` | RFC 7009; revoking a refresh token ends the whole connection |
| `POST /oauth/register` | RFC 7591 dynamic registration (when on; 20 per minute per address) |

- **Clients** identify either with a **Client ID Metadata Document** (the `client_id` is an https URL serving JSON
  with `client_id` equal to that URL, `client_name` and `redirect_uris`), or with an id from dynamic registration.
  Metadata documents are fetched over https only, at most 64 KB, in 5 seconds, without redirects, and never from a
  private, loopback or link-local address (also after DNS resolution).
- **Redirect URIs** are https, `http` on a loopback address (`localhost`, `127.0.0.1`, `[::1]`, any port) for native
  clients, or a private-use scheme (`com.example.app:/cb`). `javascript:`, `data:`, `file:` and fragments are refused.
- **Tokens** are opaque random strings; only their SHA-256 hash is stored. Access tokens last 1 hour, refresh tokens
  30 days and are rotated at each use; reusing an old refresh token or a code revokes the tokens issued from it.
- `resource`, when sent, must be `{base}/mcp`. Errors follow RFC 6749; errors about the client or its redirect URI
  are shown on the consent page instead of being sent back.

The consent page is a frontend route. It calls (session cookie, and `X-Requested-With` on POST):

| Method and path | Answer |
| --------------- | ------ |
| `GET /api/oauth/requests/{id}` | `{clientName, clientKind, clientUri, redirectHost, person, scopes: [{scope, grantable, text}], capabilities, ceiling, approvalRequired, alreadyConnected, expiresAt}` |
| `POST /api/oauth/requests/{id}/approve` | `{scopes?}` (a subset of those asked), then `{redirectTo, pendingApproval}` |
| `POST /api/oauth/requests/{id}/deny` | `{redirectTo}` with `error=access_denied` |

The page then sends the browser to `redirectTo`. A request is valid 10 minutes and is decided once (409 after).
