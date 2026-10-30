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

An owner can restrict an application or a page and list the teams and people who see it; the others see only that
it exists. Naming a guest on a record shares it with them. Protected notes hold operational details (who to call,
where the break-glass account lives) for the owners and the access list. **Who can see and edit?** on a record
explains who reads, publishes and proposes it. Pasting a token, key or password is refused: write where the secret
lives instead. Details: [access, protected notes and the access log](access.md).

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
