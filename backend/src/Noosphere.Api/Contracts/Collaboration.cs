using System.Text.Json.Nodes;
using Noosphere.Domain;

namespace Noosphere.Api.Contracts;

// ---------- People, teams and permissions ----------

/// <summary>A user as shown next to a proposal or a revision.</summary>
public sealed record UserRefDto(string Username, string DisplayName);

public sealed record TeamRefDto(string Id, string Name);

public sealed record TeamMemberDto(string Username, string DisplayName, Role Role);

/// <summary>Replaces the members of a team.</summary>
public sealed record TeamMembersInput(List<string>? Usernames);

/// <summary>What the signed-in user may do with a record.</summary>
/// <param name="Publish">Save, delete or restore directly (administrator, or member of the owning team).</param>
/// <param name="Propose">Send a change proposal (members and administrators, on applications, flows, data objects and pages).</param>
public sealed record PermissionsDto(bool Publish, bool Propose);

/// <summary>A page, as listed on a record or next to another page.</summary>
public sealed record PageRefDto(string Id, string Title);

// ---------- Record details ----------

/// <summary>An application with its owner, the reader's permissions, its pages and its deployments.</summary>
public sealed record ApplicationDetailDto : ApplicationDto
{
    public ApplicationDetailDto(ApplicationDto record, RecordContextDto context) : base(record) =>
        (OwningTeamId, Permissions, Pages, MentionedIn) = (context.OwningTeamId, context.Permissions, context.Pages, context.MentionedIn);

    /// <summary>Team whose members publish changes; null when only administrators do.</summary>
    public string? OwningTeamId { get; init; }
    public PermissionsDto Permissions { get; init; }
    /// <summary>Pages attached to the record.</summary>
    public IReadOnlyList<PageRefDto> Pages { get; init; }
    /// <summary>Pages whose body links to the record.</summary>
    public IReadOnlyList<PageRefDto> MentionedIn { get; init; }
    /// <summary>Markdown read by the owning team, the access list and administrators; null for anyone else (or when there are none).</summary>
    public string? ProtectedNotes { get; init; }
    /// <summary>The record has protected notes the reader may not read.</summary>
    public bool ProtectedNotesHidden { get; init; }
    /// <summary>Deployments received from linked gestion-mep instances: the upcoming ones and the last three past ones.</summary>
    public DeploymentsDto Deployments { get; init; } = new([], []);
}

/// <summary>A flow with its owner (its owner team, or else the team of its source application), permissions, pages and the deployments of its two ends.</summary>
public sealed record FlowDetailDto : FlowDto
{
    public FlowDetailDto(FlowDto record, RecordContextDto context) : base(record) =>
        (OwningTeamId, Permissions, Pages, MentionedIn) = (context.OwningTeamId, context.Permissions, context.Pages, context.MentionedIn);

    /// <summary>Team whose members publish changes; null when only administrators do.</summary>
    public string? OwningTeamId { get; init; }
    public PermissionsDto Permissions { get; init; }
    /// <summary>Pages attached to the record.</summary>
    public IReadOnlyList<PageRefDto> Pages { get; init; }
    /// <summary>Pages whose body links to the record.</summary>
    public IReadOnlyList<PageRefDto> MentionedIn { get; init; }
    /// <summary>Markdown read by the owning team, the access list and administrators; null for anyone else (or when there are none).</summary>
    public string? ProtectedNotes { get; init; }
    /// <summary>The record has protected notes the reader may not read.</summary>
    public bool ProtectedNotesHidden { get; init; }
    /// <summary>Deployments received from linked gestion-mep instances: the upcoming ones and the last three past ones.</summary>
    public DeploymentsDto Deployments { get; init; } = new([], []);
}

/// <summary>A data object with its owner (the team of its source application), permissions and pages.</summary>
public sealed record DataObjectDetailDto : DataObjectDto
{
    public DataObjectDetailDto(DataObjectDto record, RecordContextDto context) : base(record) =>
        (OwningTeamId, Permissions, Pages, MentionedIn) = (context.OwningTeamId, context.Permissions, context.Pages, context.MentionedIn);

    /// <summary>Team whose members publish changes; null when only administrators do.</summary>
    public string? OwningTeamId { get; init; }
    public PermissionsDto Permissions { get; init; }
    /// <summary>Pages attached to the record.</summary>
    public IReadOnlyList<PageRefDto> Pages { get; init; }
    /// <summary>Pages whose body links to the record.</summary>
    public IReadOnlyList<PageRefDto> MentionedIn { get; init; }
}

/// <summary>A domain (administrators only change it) with its pages.</summary>
public sealed record DomainDetailDto : DomainDto
{
    public DomainDetailDto(DomainDto record, RecordContextDto context) : base(record) =>
        (OwningTeamId, Permissions, Pages, MentionedIn) = (context.OwningTeamId, context.Permissions, context.Pages, context.MentionedIn);

    /// <summary>Team whose members publish changes; null when only administrators do.</summary>
    public string? OwningTeamId { get; init; }
    public PermissionsDto Permissions { get; init; }
    /// <summary>Pages attached to the record.</summary>
    public IReadOnlyList<PageRefDto> Pages { get; init; }
    /// <summary>Pages whose body links to the record.</summary>
    public IReadOnlyList<PageRefDto> MentionedIn { get; init; }
}

/// <summary>A team (administrators only change it) with its members and pages.</summary>
public sealed record TeamDetailDto : TeamDto
{
    public TeamDetailDto(TeamDto record, RecordContextDto context, IReadOnlyList<TeamMemberDto> members) : base(record) =>
        (OwningTeamId, Permissions, Pages, MentionedIn, Members) = (context.OwningTeamId, context.Permissions, context.Pages, context.MentionedIn, members);

    /// <summary>Team whose members publish changes; null when only administrators do.</summary>
    public string? OwningTeamId { get; init; }
    public PermissionsDto Permissions { get; init; }
    /// <summary>Pages attached to the record.</summary>
    public IReadOnlyList<PageRefDto> Pages { get; init; }
    /// <summary>Pages whose body links to the record.</summary>
    public IReadOnlyList<PageRefDto> MentionedIn { get; init; }
    public IReadOnlyList<TeamMemberDto> Members { get; init; }
}

/// <summary>Owner, permissions and pages of a record.</summary>
public sealed record RecordContextDto(string? OwningTeamId, PermissionsDto Permissions, IReadOnlyList<PageRefDto> Pages, IReadOnlyList<PageRefDto> MentionedIn);

// ---------- Pages ----------

/// <param name="Kind"><c>application</c>, <c>flow</c>, <c>dataObject</c>, <c>domain</c> or <c>team</c>.</param>
public sealed record AttachedToDto(RecordKind Kind, string Id);

/// <summary>A documentation page the reader may see in full. <c>restricted</c> is always false.</summary>
/// <param name="Body">Markdown. Wiki links: <c>[[application:billing]]</c>, <c>[[page:glossary|the glossary]]</c>.</param>
/// <param name="ParentId">Parent page in the tree; null for a top-level page.</param>
/// <param name="Order">Position among its siblings (ascending, then by title).</param>
/// <param name="OwnerTeamId">Owning team as set on the page; when null, the owner of the attached record owns it.</param>
/// <param name="Access">Access list: on a restricted page, these teams and users see it; guests named here see the page in full.</param>
public record PageDto(
    string Id, bool Restricted, string Title, string Body, string? ParentId, int Order, AttachedToDto? AttachedTo,
    string? OwnerTeamId, IReadOnlyList<string> Tags, Visibility Visibility, AccessListDto Access, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A link found in a page body.</summary>
/// <param name="Title">Name of the target; null when it does not exist or is hidden from the reader.</param>
/// <param name="Exists">False for a broken link.</param>
/// <param name="Restricted">The target exists but the reader may not see it.</param>
/// <param name="Label">Text after the <c>|</c>, when given.</param>
public sealed record PageLinkDto(RecordKind Kind, string Id, string? Title, bool Exists, bool Restricted, string? Label);

/// <summary>A page with its resolved links, backlinks, place in the tree, owner and the reader's permissions.</summary>
public sealed record PageDetailDto : PageDto
{
    public PageDetailDto(PageDto page, IReadOnlyList<PageLinkDto> links, IReadOnlyList<PageRefDto> backlinks, IReadOnlyList<PageRefDto> ancestors,
        IReadOnlyList<PageRefDto> children, string? attachedTitle, bool attachedRestricted, string? owningTeamId, PermissionsDto permissions) : base(page) =>
        (Links, Backlinks, Ancestors, Children, AttachedTitle, AttachedRestricted, OwningTeamId, Permissions) =
        (links, backlinks, ancestors, children, attachedTitle, attachedRestricted, owningTeamId, permissions);

    /// <summary>Wiki links of the body, resolved; <c>exists: false</c> marks a broken link.</summary>
    public IReadOnlyList<PageLinkDto> Links { get; init; }
    /// <summary>Pages whose body links to this page.</summary>
    public IReadOnlyList<PageRefDto> Backlinks { get; init; }
    /// <summary>From the top-level page down to the parent.</summary>
    public IReadOnlyList<PageRefDto> Ancestors { get; init; }
    public IReadOnlyList<PageRefDto> Children { get; init; }
    /// <summary>Name of the attached record (null when missing or hidden).</summary>
    public string? AttachedTitle { get; init; }
    /// <summary>The attached record is restricted: a page visible to everyone may tell more than the record (pages inherit nothing).</summary>
    public bool AttachedRestricted { get; init; }
    /// <summary>Team whose members publish changes; null when only administrators do.</summary>
    public string? OwningTeamId { get; init; }
    public PermissionsDto Permissions { get; init; }
}

/// <summary>A restricted page, as seen by a reader who may not see it.</summary>
public sealed record RestrictedPageDto(string Id, bool Restricted, string? ParentId, int Order);

/// <summary>A page not shared with a guest: its title and place in the tree only. <c>shared</c> is always false.</summary>
public sealed record GuestPageDto(string Id, bool Restricted, bool Shared, string Title, string? ParentId, int Order);

/// <summary>A page in the list (no body).</summary>
/// <param name="BrokenLinks">Number of wiki links whose target does not exist.</param>
public sealed record PageSummaryDto(
    string Id, bool Restricted, string Title, string? ParentId, int Order, AttachedToDto? AttachedTo, string? OwnerTeamId,
    IReadOnlyList<string> Tags, Visibility Visibility, int BrokenLinks, int Version, DateTimeOffset UpdatedAt);

/// <summary>A node of the page tree. A restricted page hidden from the reader has no title.</summary>
public sealed record PageTreeNodeDto(string Id, bool Restricted, string? Title, int Order, AttachedToDto? AttachedTo, IReadOnlyList<PageTreeNodeDto> Children);

/// <param name="Body">Markdown; may be empty.</param>
/// <param name="OwnerTeamId">Optional: when empty, the owner of the attached record owns the page (administrators when none).</param>
/// <param name="Access">Optional: left out, the current access list is kept.</param>
public sealed record PageInput(
    string? Id, string? Title, string? Body, string? ParentId, int? Order, AttachedToDto? AttachedTo, string? OwnerTeamId,
    List<string>? Tags, Visibility? Visibility, int? Version, AccessListInput? Access = null);

/// <param name="ParentId">New parent; null moves the page to the top level.</param>
/// <param name="Version">Optional: when given, a stale version returns 409.</param>
public sealed record PageMoveInput(string? ParentId, int? Order, int? Version);

// ---------- Field diff ----------

public enum DiffKind { Text, Markdown, List, Ref }

/// <summary>One changed field. <c>markdown</c> fields carry the raw texts, for an inline word diff.</summary>
/// <param name="Before">Value before (null when empty or on a creation).</param>
/// <param name="After">Value after (null when empty or on a deletion).</param>
/// <param name="BeforeTitle">For <c>ref</c> fields: the name of the record referred to.</param>
public sealed record DiffEntryDto(string Field, string Label, DiffKind Kind, JsonNode? Before, JsonNode? After, string? BeforeTitle, string? AfterTitle);

// ---------- Proposals ----------

/// <summary>One change of a proposal.</summary>
/// <param name="Kind"><c>application</c>, <c>flow</c>, <c>dataObject</c> or <c>page</c>.</param>
/// <param name="TargetId">Record changed; null for a creation (the id then comes from <c>after.id</c> or the name).</param>
/// <param name="BaseVersion">Version the change was written against: required for update and delete.</param>
/// <param name="After">The whole record after the change, in the shape of its PUT body (create and update).</param>
public sealed record ProposalChangeInput(RecordKind? Kind, string? TargetId, ChangeOperation? Operation, int? BaseVersion, JsonObject? After);

/// <param name="Submit">true sends it for validation at once; otherwise it stays a draft.</param>
/// <param name="Version">On PUT: the proposal version that was read.</param>
public sealed record ProposalInput(string? Title, string? Comment, List<ProposalChangeInput>? Changes, bool? Submit, int? Version);

/// <param name="Reason">Shown to the author: 3 to 500 characters.</param>
public sealed record RefusalInput(string? Reason);

/// <summary>A change with its snapshots and field diff.</summary>
/// <param name="TargetTitle">Name of the target (after the change for a creation).</param>
/// <param name="CurrentVersion">Version of the target now (null when it does not exist).</param>
/// <param name="Outdated">The target changed since <c>baseVersion</c>: accepting returns 409 until the proposal is rebased.</param>
/// <param name="Conflicts">Fields changed both by the proposal and on the record, found by the last rebase.</param>
public sealed record ProposalChangeDto(
    RecordKind Kind, string? TargetId, string? TargetTitle, ChangeOperation Operation, int? BaseVersion, int? CurrentVersion, bool Outdated,
    JsonObject? Before, JsonObject? After, IReadOnlyList<DiffEntryDto> Diff, IReadOnlyList<string> Conflicts);

/// <summary>An owning team whose acceptance the proposal needs.</summary>
public sealed record ProposalOwnerDto(string Id, string Name, UserRefDto? ApprovedBy, DateTimeOffset? ApprovedAt);

/// <summary>A change proposal.</summary>
/// <param name="Status"><c>draft</c>, <c>submitted</c> (shown as "To validate"), <c>accepted</c>, <c>refused</c> or <c>withdrawn</c>.</param>
/// <param name="OwnerTeams">Teams that validate (one validator each). An administrator accepts on behalf of all.</param>
/// <param name="RequiresAdmin">A target is owned by administrators only (no owning team): only an administrator accepts.</param>
/// <param name="CanDecide">The reader may accept or refuse it now.</param>
/// <param name="CanEdit">The reader is the author (for an agent's proposal: the person it acts for) and may still edit, submit or withdraw it.</param>
/// <param name="AuthorKind"><c>user</c>, or <c>agent</c> for a proposal written by an AI agent: <c>author</c> is then the person it acts for.</param>
/// <param name="OnBehalfOf">For an agent's proposal: the person it acts for (the UI shows "AI agent for &lt;person&gt;").</param>
/// <param name="AgentName">For an agent's proposal: the name of the agent (its OAuth client name).</param>
/// <param name="AgentConnectionId">For an agent's proposal: its connection.</param>
public sealed record ProposalDto(
    Guid Id, string Title, string? Comment, ProposalStatus Status, AuthorKind AuthorKind, UserRefDto Author, UserRefDto? OnBehalfOf,
    DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt, UserRefDto? DecidedBy, DateTimeOffset? DecidedAt, string? RefusalReason,
    int Version, bool Outdated, IReadOnlyList<ProposalOwnerDto> OwnerTeams, bool RequiresAdmin, bool CanDecide, bool CanEdit,
    IReadOnlyList<ProposalChangeDto> Changes, string? AgentName = null, Guid? AgentConnectionId = null);

/// <summary>Counts for the header badge.</summary>
/// <param name="ToValidate">Submitted proposals the reader may accept or refuse.</param>
/// <param name="MineOpen">The reader's own proposals in draft or submitted.</param>
public sealed record InboxDto(int ToValidate, int MineOpen);

// ---------- History and trash ----------

/// <summary>One revision of a record, newest first.</summary>
/// <param name="Author">Null for revisions made by the system (migration, command line).</param>
public sealed record HistoryEntryDto(
    int Version, RevisionOperation Operation, UserRefDto? Author, AuthorKind AuthorKind, DateTimeOffset CreatedAt, string? Summary, Guid? ProposalId);

/// <summary>A revision with its snapshot (the record's editable fields, in the shape of its PUT body).</summary>
public sealed record RevisionDto(
    RecordKind Kind, string EntityId, int Version, RevisionOperation Operation, UserRefDto? Author, AuthorKind AuthorKind,
    DateTimeOffset CreatedAt, string? Summary, Guid? ProposalId, JsonObject Snapshot);

public sealed record CompareDto(int From, int To, IReadOnlyList<DiffEntryDto> Diff);

/// <param name="Outcome"><c>published</c> (a new revision) or <c>proposed</c> (a submitted proposal, for non-owners).</param>
public sealed record RestoreResultDto(string Outcome, int? Version, Guid? ProposalId);

/// <summary>A deleted record. Its history is kept; administrators restore it.</summary>
/// <param name="Version">Version of the deletion revision.</param>
public sealed record TrashItemDto(RecordKind Kind, string EntityId, string Title, DateTimeOffset DeletedAt, UserRefDto? DeletedBy, int Version);

public sealed record TrashRestoreResultDto(RecordKind Kind, string Id, int Version);

// ---------- Settings ----------

/// <param name="InstanceName">Shown in the header and the browser tab (default "Noosphere").</param>
/// <param name="OrganisationName">Shown under the instance name; may be empty.</param>
/// <param name="DefaultLanguage">Language of new accounts and of the sign-in page: <c>en</c> or <c>fr</c>.</param>
public sealed record SettingsDto(string InstanceName, string OrganisationName, string DefaultLanguage, int Version, DateTimeOffset? UpdatedAt);

public sealed record SettingsInput(string? InstanceName, string? OrganisationName, string? DefaultLanguage, int? Version);

// ---------- Access ----------

/// <summary>A team as shown in the "Who can see and edit?" window.</summary>
public sealed record AccessTeamDto(string Id, string Name, int MemberCount);

/// <param name="EveryoneExceptGuests">Every member and administrator reads the record (it is not restricted).</param>
/// <param name="Teams">Teams whose members read it: the owning team (when restricted) and the teams of the access list.</param>
/// <param name="Users">People who read it by name, outside those teams (administrators always read).</param>
public sealed record AccessReadersDto(bool EveryoneExceptGuests, IReadOnlyList<AccessTeamDto> Teams, IReadOnlyList<UserRefDto> Users);

/// <param name="Teams">The owning team (empty when only administrators publish).</param>
/// <param name="Admins">Administrators always publish.</param>
public sealed record AccessPublishersDto(IReadOnlyList<AccessTeamDto> Teams, bool Admins);

public sealed record ProtectedNotesAccessDto(bool Exists, bool Readable);

/// <summary><c>members</c>: members who see the record send proposals; <c>none</c>: domains and teams, changed by administrators only.</summary>
public enum Proposers { Members, None }

/// <summary>Who can see and edit a record: the explanation shown by the "Who can see and edit?" window.</summary>
/// <param name="Visibility">For a flow: restricted when one of its ends is.</param>
/// <param name="CanManage">The reader may change the access list (applications and pages they may publish).</param>
public sealed record RecordAccessDto(
    Visibility Visibility, AccessTeamDto? OwningTeam, AccessReadersDto Readers, AccessPublishersDto Publishers, Proposers Proposers,
    ProtectedNotesAccessDto ProtectedNotes, bool CanManage);

/// <summary>One read of a restricted record, of protected notes, or an export.</summary>
/// <param name="Kind">Null for an export.</param>
/// <param name="Title">Name of the record now (null when deleted, or for an export).</param>
/// <param name="What"><c>record</c>, <c>protectedNotes</c> or <c>export</c>.</param>
/// <param name="Detail">More about the read: <c>history v3</c>, <c>scope full</c>...</param>
/// <param name="Admin">The reader was an administrator.</param>
/// <param name="Why"><c>admin</c>, <c>owningTeam</c>, <c>listedTeam</c> (see <c>whyTeam</c>) or <c>listedUser</c>; null for an export by a non-administrator.</param>
/// <param name="WhyTeam">For <c>listedTeam</c>: the team of the access list the reader belongs to.</param>
/// <param name="OwningTeam">Owning team of the record now (null when only administrators own it, or for an export).</param>
public sealed record AccessLogEntryDto(long Id, DateTimeOffset At, UserRefDto User, RecordKind? Kind, string? EntityId, string? Title,
    AccessLogWhat What, string? Detail, bool Admin, AccessLogWhy? Why, TeamRefDto? WhyTeam, TeamRefDto? OwningTeam);

/// <param name="NextCursor">Pass it as <c>cursor</c> for the next (older) page; null on the last page.</param>
public sealed record AccessLogPageDto(IReadOnlyList<AccessLogEntryDto> Items, string? NextCursor);

/// <summary>Someone who can be named on an access list: what the "Restrict" picker lists.</summary>
/// <param name="TeamIds">Teams the person belongs to.</param>
public sealed record PersonDto(string Username, string DisplayName, Role Role, IReadOnlyList<string> TeamIds);

/// <summary>A record named on the Access page.</summary>
/// <param name="OwningTeamId">Null when only administrators own it.</param>
public sealed record AccessRecordDto(RecordKind Kind, string Id, string Title, string? OwningTeamId);

/// <summary>How many accounts hold each role.</summary>
public sealed record RoleCountsDto(int Admins, int Members, int Guests);

/// <summary>The records a guest reads in full, because they are named on their access list.</summary>
public sealed record GuestSharesDto(UserRefDto User, IReadOnlyList<AccessRecordDto> Records);

/// <summary>What a team owns and how many people publish there.</summary>
public sealed record TeamAccessDto(string Id, string Name, int Members, int Applications, int Flows);

/// <summary>The Access page (administrators): roles, restricted records, protected notes, guests and teams.</summary>
/// <param name="Restricted">Restricted applications and pages, and flows touching a restricted application.</param>
/// <param name="ProtectedNotes">Applications and flows with protected notes.</param>
public sealed record AccessOverviewDto(
    RoleCountsDto Roles, IReadOnlyList<AccessRecordDto> Restricted, IReadOnlyList<AccessRecordDto> ProtectedNotes,
    IReadOnlyList<GuestSharesDto> Guests, IReadOnlyList<TeamAccessDto> Teams);
