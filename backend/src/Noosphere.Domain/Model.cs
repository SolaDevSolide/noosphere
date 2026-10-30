namespace Noosphere.Domain;

/// <summary>What an application is: an application proper, a message bus, a SaaS, a data store, a file exchange or a third party.</summary>
public enum ApplicationKind { App, Bus, Saas, Data, File, External }

public enum Lifecycle { Planned, Live, Retiring, Retired }

/// <summary>
/// <c>Restricted</c> applications and pages are shown in full to administrators, members of the owning team and the
/// people on their access list; others see their id and domain only.
/// </summary>
public enum Visibility { Everyone, Restricted }

/// <summary>Ordered from the least to the most sensitive: the sensitivity of a flow is the maximum of what it carries.</summary>
public enum Sensitivity { Public, Internal, Personal, Sensitive }

public enum FlowType { Api, Event, Batch }

public enum FlowStatus { Planned, Live, Deprecated }

public enum Role { Admin, Member, Guest }

/// <summary>Kinds of versioned records: each has a history of revisions.</summary>
public enum RecordKind { Application, Flow, DataObject, Page, Domain, Team }

/// <summary>Common part of every versioned record: a stable human-readable id (kebab-case slug), timestamps and an optimistic concurrency version.</summary>
public abstract class VersionedEntity
{
    public string Id { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

/// <summary>A landscape record: a name and an optional markdown description.</summary>
public abstract class LandscapeEntity : VersionedEntity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>A business domain grouping applications (Customer, Finance...).</summary>
public sealed class BusinessDomain : LandscapeEntity
{
    public int Order { get; set; }
}

public sealed class Team : LandscapeEntity
{
    /// <summary>E-mail address or chat channel.</summary>
    public string? Contact { get; set; }
}

public sealed class Application : LandscapeEntity
{
    public string DomainId { get; set; } = "";
    public string? TeamId { get; set; }
    public ApplicationKind Kind { get; set; } = ApplicationKind.App;
    public string? Tech { get; set; }
    public Lifecycle Lifecycle { get; set; } = Lifecycle.Live;
    public Visibility Visibility { get; set; } = Visibility.Everyone;
    /// <summary>Access list: teams whose members see the application when restricted, and read its protected notes.</summary>
    public List<string> AccessTeamIds { get; set; } = [];
    /// <summary>Access list: users who see the application when restricted, read its protected notes, and (guests) see it in full.</summary>
    public List<string> AccessUsernames { get; set; } = [];
    /// <summary>Markdown read only by the owning team, the access list and administrators.</summary>
    public string? ProtectedNotes { get; set; }
}

public sealed class DataObject : LandscapeEntity
{
    public Sensitivity Sensitivity { get; set; } = Sensitivity.Internal;
    /// <summary>The application that is the source of truth for this data. Its <c>sourceOf</c> list is derived from this field.</summary>
    public string? SourceApplicationId { get; set; }
}

public sealed class Flow : LandscapeEntity
{
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
    public FlowType Type { get; set; } = FlowType.Api;
    /// <summary>Free text, e.g. "Every night 02:00".</summary>
    public string? Schedule { get; set; }
    public List<string> DataObjectIds { get; set; } = [];
    public string? OwnerTeamId { get; set; }
    public FlowStatus Status { get; set; } = FlowStatus.Live;
    public string? Volume { get; set; }
    public string? PlannedChangeSummary { get; set; }
    public DateOnly? PlannedChangeDate { get; set; }
    /// <summary>Markdown read only by the owning team, the access lists of the two ends and administrators.</summary>
    public string? ProtectedNotes { get; set; }
}

/// <summary>A local account. Passwords are stored as ASP.NET Core Identity hashes.</summary>
public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Lower case, unique.</summary>
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Role Role { get; set; } = Role.Guest;
    /// <summary><c>en</c> or <c>fr</c>.</summary>
    public string Language { get; set; } = "en";
    public string PasswordHash { get; set; } = "";
    /// <summary>Changed when the password or the role changes: open sessions of the account are closed.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

/// <summary>A user belongs to 0..n teams. Members of a record's owning team publish its changes directly.</summary>
public sealed class TeamMember
{
    public Guid UserId { get; set; }
    public string TeamId { get; set; } = "";
}

/// <summary>A Markdown documentation page, in a tree, optionally attached to a record.</summary>
public sealed class Page : VersionedEntity
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? ParentId { get; set; }
    public int Order { get; set; }
    public RecordKind? AttachedKind { get; set; }
    public string? AttachedId { get; set; }
    public string? OwnerTeamId { get; set; }
    public List<string> Tags { get; set; } = [];
    public Visibility Visibility { get; set; } = Visibility.Everyone;
    /// <summary>Access list: teams whose members see the page when restricted.</summary>
    public List<string> AccessTeamIds { get; set; } = [];
    /// <summary>Access list: users who see the page when restricted, and (guests) see it in full.</summary>
    public List<string> AccessUsernames { get; set; } = [];
    /// <summary>Wiki link targets found in the body, as <c>kind:id</c> (<c>application:billing</c>). Derived from the body on every save.</summary>
    public List<string> Links { get; set; } = [];
}

public enum AuthorKind { User, Agent }

public enum RevisionOperation { Create, Update, Delete, Restore }

/// <summary>
/// One published state of a record: every direct save, accepted proposal, restore and deletion adds one. The snapshot
/// holds the editable fields, in the shape of the API input of its kind. Revisions outlive the record.
/// </summary>
public sealed class Revision
{
    public long Id { get; set; }
    public RecordKind Kind { get; set; }
    public string EntityId { get; set; } = "";
    public int Version { get; set; }
    public RevisionOperation Operation { get; set; }
    /// <summary>JSON object.</summary>
    public string Snapshot { get; set; } = "{}";
    public Guid? AuthorId { get; set; }
    /// <summary>Username when the revision was made (kept when the account is deleted); "system" for the command line and migrations.</summary>
    public string AuthorName { get; set; } = "";
    public AuthorKind AuthorKind { get; set; }
    public Guid? ProposalId { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum ProposalStatus { Draft, Submitted, Accepted, Refused, Withdrawn }

public enum ChangeOperation { Create, Update, Delete }

/// <summary>A change proposal: 1..n changes, validated by the owning teams of their targets (or an administrator).</summary>
public sealed class Proposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Comment { get; set; }
    public ProposalStatus Status { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public AuthorKind AuthorKind { get; set; }
    /// <summary>For agents (later): the user the agent works for.</summary>
    public string? OnBehalfOf { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? DecidedById { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? RefusalReason { get; set; }
    public int Version { get; set; }
    public List<ProposalChange> Changes { get; set; } = [];
    public List<ProposalApproval> Approvals { get; set; } = [];
}

public sealed class ProposalChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProposalId { get; set; }
    public int Position { get; set; }
    public RecordKind Kind { get; set; }
    /// <summary>Null until accepted for a creation without a requested id.</summary>
    public string? TargetId { get; set; }
    public ChangeOperation Operation { get; set; }
    /// <summary>Version of the target the change was written against (update, delete).</summary>
    public int? BaseVersion { get; set; }
    /// <summary>JSON object: the target at <see cref="BaseVersion"/>; null for a creation.</summary>
    public string? Before { get; set; }
    /// <summary>JSON object: the whole record after the change; null for a deletion.</summary>
    public string? After { get; set; }
    /// <summary>Fields changed both by the proposal and on the record, found by the last rebase.</summary>
    public List<string> Conflicts { get; set; } = [];
}

/// <summary>Acceptance of a proposal on behalf of one owning team.</summary>
public sealed class ProposalApproval
{
    public Guid ProposalId { get; set; }
    public string TeamId { get; set; } = "";
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; }
}

/// <summary>Settings of the instance, set by administrators (one row).</summary>
public sealed class InstanceSettings
{
    public int Id { get; set; } = 1;
    public string InstanceName { get; set; } = "Noosphere";
    public string OrganisationName { get; set; } = "";
    public string DefaultLanguage { get; set; } = "en";
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}

/// <summary>What an access log line records.</summary>
public enum AccessLogWhat { Record, ProtectedNotes, Export }

/// <summary>Why the reader was allowed to read: administrator, owning team, a team of the access list, or named on it.</summary>
public enum AccessLogWhy { Admin, OwningTeam, ListedTeam, ListedUser }

/// <summary>
/// One read of a restricted record, of protected notes or of a full export, by someone outside the owning team (or an
/// administrator). Kept <c>ACCESS_LOG_DAYS</c> days (365 by default).
/// </summary>
public sealed class AccessLog
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? UserId { get; set; }
    /// <summary>Username at the time of the read (kept when the account is deleted).</summary>
    public string Username { get; set; } = "";
    /// <summary>Null for an export.</summary>
    public RecordKind? Kind { get; set; }
    public string? EntityId { get; set; }
    public AccessLogWhat What { get; set; }
    public string? Detail { get; set; }
    /// <summary>The reader was an administrator.</summary>
    public bool Admin { get; set; }
    /// <summary>Why the read was allowed; null when no single reason applies (an export of several records).</summary>
    public AccessLogWhy? Why { get; set; }
    /// <summary>For <see cref="AccessLogWhy.ListedTeam"/>: the team of the access list the reader belongs to.</summary>
    public string? WhyTeamId { get; set; }
}
