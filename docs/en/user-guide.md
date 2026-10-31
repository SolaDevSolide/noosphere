# User guide: pages, proposals, history and settings

[Français](../fr/guide-utilisateur.md)

Everyone signed in reads the whole landscape. Anyone with a member account can suggest changes, and the team that owns a
record validates them.

## Who publishes and who proposes

| You are                                   | On a record your team owns | On any other record    |
| ----------------------------------------- | -------------------------- | ---------------------- |
| Administrator                             | **Edit** (publishes)       | **Edit** (publishes)   |
| Member of the owning team                 | **Edit** (publishes)       | **Suggest a change**   |
| Member of another team, or without a team | **Suggest a change**       | **Suggest a change**   |
| Guest                                     | read only                  | read only              |

The owner of an application is its team. A flow belongs to its owner team, or else to the team of the application it
starts from. A record without an owning team is validated by administrators.

### Suggest a change

1. Open an application or a flow and choose **Suggest a change**.
2. Change the fields as usual, give the proposal a title and say **why** in the comment: the validators read it.
3. **Send proposal**. Readers keep seeing the published version until the owning team accepts it.

Members of the owning team can also tick **Propose instead** in the edit form, to have a colleague review a change
before it is published.

## The review area

The **Review** button in the header shows how many proposals wait for you. The review area has three tabs:

- **To validate**: proposals on records your teams own (all of them for administrators).
- **My proposals**: what you proposed, open or decided.
- **Decided**: accepted and refused proposals.

A proposal shows each changed record **side by side**, the published version on the left and the proposed one on the
right. Long Markdown texts, such as descriptions, show an **inline highlighted diff**: removed words are struck
through in red, added words are highlighted in green.

- **Accept and publish** applies the change and creates a new version of the record.
- **Refuse** needs a reason (3 to 500 characters), which the author sees.
- The author can **Withdraw** an open proposal.

### Outdated proposals

If someone published a new version of the record after the proposal was written, the proposal is marked outdated and
cannot be accepted as it is. Its author chooses **Update from latest**: their changed fields are kept on top of the
latest version, and the fields that also changed in the meantime are marked **Changed on both sides**.

## Documentation pages

Open **Pages** in the header to read and write runbooks, decisions and overviews. Pages follow the same rule as
records: the owning team (the page's owner team, or else the owner of the record it is attached to) publishes, and
other members **Suggest a change**. Any member can create a page, attached to a record or owned by a team they
choose. Type `[[` in the editor to link a record or another page; links to something that does not exist are shown
in red but do not block publishing. See [Pages](pages.md) for the details.

## History

Every published change creates a version: a direct save, an accepted proposal or a restore. The discreet **History**
button on a record opens a side panel with its versions, their author and date, and a link to the proposal that led
to a version.

- **Compare**: tick two versions, then choose **Compare** to see the fields that changed between them.
- **Restore**: choose **Restore** on an older version and confirm. Owners publish it again as a new version; anyone
  else sends a proposal to the owners.

## Restricted records, guests and protected notes

Under the title of every record, a chip says who can see it: **Visible to everyone**, or **Restricted · 2 teams,
1 person**. Click it to open **Who can see and edit?**: who reads the record, who publishes and validates its changes,
who may propose one, who reads its protected notes, and which attached pages stay public. Details: [access, protected
notes and the access log](access.md).

- **Restrict an application or a page**: owners choose **Restrict this record** (or **Change who can see it**) in that
  window. Pick teams and people with the search field; the owning team and administrators always see it. The preview
  says who will see the record and what the others will see. Pages attached to the record keep their own visibility:
  the dialog lists the ones that stay public. **Make visible to everyone** lifts the restriction.
- **Share with a guest**: on a public record, owners name the guests who may open it in full. Guests see the outline
  of the landscape (application names and domains); a record not shared with them shows a short notice instead.
  Restricted records are hidden from guests entirely, and guests never read protected notes.
- **Let another team publish**: opens the edit form on the **Team** field (**Owner** on a flow). Saving hands the
  record to that team; members who cannot publish send it as a proposal.
- **Protected notes**: a section of applications and flows for operational details (production addresses, who to
  call, where credentials live). The owning team, the access list and administrators read them; the others see a lock
  and, in history and comparisons, `••• protected` instead of the text. A member who cannot read them may write
  replacement notes; leaving the field empty keeps the current ones.
- **Sensitivity**: an application is as sensitive as the most sensitive data it is the source of, or that its flows
  carry. The badge next to the visibility chip lists the reasons. In the landscape, **Filter → Applications ·
  sensitivity at least** dims the less sensitive applications, and search shows the level after each application.
- **Secrets are refused**: pasting a token, a private key or a password in any field (records, pages, proposals,
  settings, imports) is refused with a message under the field; nothing is stored. Write where the secret lives
  instead. Placeholders such as `<password>`, `***`, `${VAR}` or `{{var}}` are fine.

### Export

**Import and export → Export the landscape** offers **What you can see** (restricted records you cannot see are left
out; protected notes only where you can read them) and, for administrators, **Full export** with every restricted
record and protected note. A full export is recorded in the access log, with an optional reason.

### The Access page

Administrators open **Access** in the account menu: the rule and its two exceptions, the roles and how many people
hold each, the records shared with each guest, what each team owns and restricted, and the **access log** (every
opening of a restricted record or of protected notes by someone outside the owning team, every administrator read,
every full export). Filter it by person, record, kind, what and dates; lines are kept 365 days.

## AI agents

An AI agent (Claude, Claude Code, Copilot, Cursor, any MCP client) works with your account: it reads what you see,
within the ceiling set by your administrator, and writes drafts and proposals. It never publishes on its own.

1. Open **My agents** in the account menu and copy the **MCP server address**. The guides under it show the
   configuration for an MCP client, the command line and VS Code.
2. The agent opens a Noosphere page, **Connection request**, that lists what it will and will not be able to do.
   Untick "Let it write drafts" to give it reading only, then choose **Allow** or **Deny**.
3. If your administrator requires an approval, the page says so and the agent waits; **Cancel the request** withdraws
   it. Otherwise the agent shows up in **My agents**.

**My agents** lists each connection with its state (active, paused at its hourly quota, waiting for approval), what
it can do, and **Revoke**, which cuts its access at once. Below are the drafts and proposals your agents wrote (submit,
open or discard them) and every call they made, kept 90 days. Guests cannot connect agents.

In the review area, a proposal written by an agent carries the chip **AI agent for** the person, with the agent's name,
and its page explains who wrote it. Validators decide as for any proposal; a refusal reason goes to the agent and to
the person.

Administrators open **AI agents** in the account menu: the **emergency stop** (revokes every connection at once), every
connection with its person (approve or refuse waiting ones, revoke), what the selected agent can do, all activity
with filters by kind and by agent, and the settings: MCP server on or off, the ceiling, the hourly quota, who may
connect, dynamic registration and llms.txt without signing in. See [AI agents](agents.md) for the details.

## Links and deployments

Noosphere can be linked to other applications, such as a deployment tool or a release pipeline. Their deployments
then show on application and flow records, in a **Deployments and changes** block after the description: upcoming
ones first, then the last three, each naming the application that sent it. Select an entry to open it in that
application; entries are read only here. Guests do not see this block.

Administrators manage links on the **Links** page of the account menu: pair an application with a single-use code,
compare the four-word fingerprint shown on both screens before confirming, choose what each application may do,
read the exchange log, renew keys or cut a link. See [Links](links.md).

## Demo toolbox

On a demo instance (`NOOSPHERE_DEMO=true`) the header shows a **Demo** marker and a **Demo** button stays in the
bottom corner (a bottom sheet on phones). It opens the toolbox:

- **Be someone else**: switch persona without a password. You stay on the page when the new persona may see it
  (untick "Stay on this page when I switch" to always go back to the landscape).
- **Simulate colleagues**: a colleague edits the record you are on (your next save gets the conflict, and open
  proposals on it become outdated), a colleague sends you a proposal, a validator accepts or refuses your latest
  proposal, the guest reads a shared record.
- **AI agent**: a demo agent acting for you writes a draft, submits a proposal, is refused above the ceiling, or hits
  its quota.
- **Reset demo data**, after a confirmation.

Each action shows a notice with a **Show me** link to what changed. See [Demo toolbox](demo.md).

## Teams

Administrators manage team memberships on the **Users** page (the **Teams** of each account) and on a team record
(**Manage members**). A team record lists the applications and flows the team owns.

## Trash

Deleted records keep their history. Administrators find them in **Trash** (account menu) and can restore them.

## Instance settings

Administrators open **Settings** in the account menu to set:

- the **instance name**, shown in the header and in the browser tab (default "Noosphere");
- the **organisation name**, shown under it (when empty, the header shows "Flow map");
- the **default language** of new accounts and of the sign-in page.

The technical name `noosphere` (container, CLI, exchange format) does not change.
