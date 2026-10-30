namespace Noosphere.Domain;

/// <summary>What an application is: an application proper, a message bus, a SaaS, a data store, a file exchange or a third party.</summary>
public enum ApplicationKind { App, Bus, Saas, Data, File, External }

public enum Lifecycle { Planned, Live, Retiring, Retired }

/// <summary><c>Restricted</c> applications are only shown in full to administrators; others see their id and domain.</summary>
public enum Visibility { Everyone, Restricted }

/// <summary>Ordered from the least to the most sensitive: the sensitivity of a flow is the maximum of what it carries.</summary>
public enum Sensitivity { Public, Internal, Personal, Sensitive }

public enum FlowType { Api, Event, Batch }

public enum FlowStatus { Planned, Live, Deprecated }

public enum Role { Admin, Member, Guest }

/// <summary>Common part of every landscape record: a stable human-readable id (kebab-case slug), timestamps and an optimistic concurrency version.</summary>
public abstract class LandscapeEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
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
