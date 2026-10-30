using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>An access list: the teams and users who see a restricted record, and read its protected notes.</summary>
public sealed record AccessList(IReadOnlyList<string> TeamIds, IReadOnlyList<string> Usernames)
{
    public static readonly AccessList Empty = new([], []);

    public bool IsEmpty => TeamIds.Count == 0 && Usernames.Count == 0;

    public static AccessList Of(Application a) => new(a.AccessTeamIds, a.AccessUsernames);

    public static AccessList Of(Page p) => new(p.AccessTeamIds, p.AccessUsernames);

    /// <summary>The <c>access</c> object of a snapshot; empty when absent.</summary>
    public static AccessList Of(JsonNode? node) => node is JsonObject o
        ? new(Strings(o["teamIds"]), Strings(o["usernames"]))
        : Empty;

    private static List<string> Strings(JsonNode? node) =>
        node is JsonArray a ? [.. a.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).OfType<string>()] : [];
}

/// <summary>How much of a record the reader sees.</summary>
public enum Sight
{
    /// <summary>A restricted record the reader is not allowed to see: a stub with its id only (and domain or ends).</summary>
    Hidden,
    /// <summary>A guest's view of a record not shared with them: its name and place in the landscape only.</summary>
    Outline,
    /// <summary>The whole record (protected notes aside).</summary>
    Full,
}

/// <summary>
/// What ownership and visibility depend on: the team, visibility and access list of every application, the ends and
/// owner of every flow, the source of every data object and the owner, attachment, visibility and access list of every page.
/// </summary>
public sealed class RecordFacts
{
    private sealed record AppFact(string? TeamId, bool Restricted, string Name, AccessList Access, bool HasNotes);
    private sealed record FlowFact(string FromId, string ToId, string? OwnerTeamId, string Name, bool HasNotes);
    private sealed record PageFact(string? OwnerTeamId, RecordKind? AttachedKind, string? AttachedId, bool Restricted, string Title, AccessList Access);

    private Dictionary<string, AppFact> _apps = [];
    private Dictionary<string, FlowFact> _flows = [];
    private Dictionary<string, (string? Source, string Name)> _objects = [];
    private Dictionary<string, PageFact> _pages = [];
    private Dictionary<string, string> _domains = [];
    private Dictionary<string, string> _teams = [];

    public static async Task<RecordFacts> Load(NoosphereDbContext db, CancellationToken cancel = default) => new()
    {
        _apps = (await db.Applications.AsNoTracking()
                .Select(a => new { a.Id, a.TeamId, a.Visibility, a.Name, a.AccessTeamIds, a.AccessUsernames, HasNotes = a.ProtectedNotes != null })
                .ToListAsync(cancel))
            .ToDictionary(a => a.Id, a => new AppFact(a.TeamId, a.Visibility == Visibility.Restricted, a.Name,
                new AccessList(a.AccessTeamIds, a.AccessUsernames), a.HasNotes)),
        _flows = (await db.Flows.AsNoTracking()
                .Select(f => new { f.Id, f.FromId, f.ToId, f.OwnerTeamId, f.Name, HasNotes = f.ProtectedNotes != null }).ToListAsync(cancel))
            .ToDictionary(f => f.Id, f => new FlowFact(f.FromId, f.ToId, f.OwnerTeamId, f.Name, f.HasNotes)),
        _objects = (await db.DataObjects.AsNoTracking().Select(o => new { o.Id, o.SourceApplicationId, o.Name }).ToListAsync(cancel))
            .ToDictionary(o => o.Id, o => (o.SourceApplicationId, o.Name)),
        _pages = (await db.Pages.AsNoTracking()
            .Select(p => new { p.Id, p.OwnerTeamId, p.AttachedKind, p.AttachedId, p.Visibility, p.Title, p.AccessTeamIds, p.AccessUsernames }).ToListAsync(cancel))
            .ToDictionary(p => p.Id, p => new PageFact(p.OwnerTeamId, p.AttachedKind, p.AttachedId, p.Visibility == Visibility.Restricted, p.Title,
                new AccessList(p.AccessTeamIds, p.AccessUsernames))),
        _domains = await db.Domains.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, cancel),
        _teams = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, cancel),
    };

    public bool Exists(RecordKind kind, string id) => kind switch
    {
        RecordKind.Application => _apps.ContainsKey(id),
        RecordKind.Flow => _flows.ContainsKey(id),
        RecordKind.DataObject => _objects.ContainsKey(id),
        RecordKind.Page => _pages.ContainsKey(id),
        RecordKind.Domain => _domains.ContainsKey(id),
        _ => _teams.ContainsKey(id),
    };

    /// <summary>Name or title of an existing record, whatever the reader may see; null when missing.</summary>
    public string? Title(RecordKind kind, string id) => kind switch
    {
        RecordKind.Application => _apps.GetValueOrDefault(id)?.Name,
        RecordKind.Flow => _flows.GetValueOrDefault(id)?.Name,
        RecordKind.DataObject => _objects.TryGetValue(id, out var o) ? o.Name : null,
        RecordKind.Page => _pages.GetValueOrDefault(id)?.Title,
        RecordKind.Domain => _domains.GetValueOrDefault(id),
        _ => _teams.GetValueOrDefault(id),
    };

    public string? TeamName(string? id) => id is null ? null : _teams.GetValueOrDefault(id);

    public IEnumerable<string> TeamIds => _teams.Keys;

    internal bool AppRestricted(string? id) => id is not null && _apps.TryGetValue(id, out var a) && a.Restricted;

    /// <summary>Access list of a stored application (empty when missing).</summary>
    public AccessList AppAccess(string? id) => id is not null && _apps.TryGetValue(id, out var a) ? a.Access : AccessList.Empty;

    /// <summary>Access list of a stored page (empty when missing).</summary>
    public AccessList PageAccess(string? id) => id is not null && _pages.TryGetValue(id, out var p) ? p.Access : AccessList.Empty;

    /// <summary>Ends of a stored flow; null when missing.</summary>
    public (string FromId, string ToId)? Ends(string id) => _flows.TryGetValue(id, out var f) ? (f.FromId, f.ToId) : null;

    /// <summary>The stored record has protected notes (applications and flows only).</summary>
    public bool HasProtectedNotes(RecordKind kind, string id) => kind switch
    {
        RecordKind.Application => _apps.TryGetValue(id, out var a) && a.HasNotes,
        RecordKind.Flow => _flows.TryGetValue(id, out var f) && f.HasNotes,
        _ => false,
    };

    /// <summary>Restricted applications and pages, and flows touching a restricted application.</summary>
    public IEnumerable<(RecordKind Kind, string Id)> RestrictedRecords() =>
        _apps.Where(a => a.Value.Restricted).Select(a => (RecordKind.Application, a.Key))
            .Concat(_flows.Keys.Where(f => Restricted(RecordKind.Flow, f)).Select(f => (RecordKind.Flow, f)))
            .Concat(_pages.Where(p => p.Value.Restricted).Select(p => (RecordKind.Page, p.Key)));

    /// <summary>Applications and flows with protected notes.</summary>
    public IEnumerable<(RecordKind Kind, string Id)> ProtectedRecords() =>
        _apps.Where(a => a.Value.HasNotes).Select(a => (RecordKind.Application, a.Key))
            .Concat(_flows.Where(f => f.Value.HasNotes).Select(f => (RecordKind.Flow, f.Key)));

    /// <summary>True when the record (as stored) is restricted: a restricted application or page, or a flow touching a restricted application.</summary>
    public bool Restricted(RecordKind kind, string id) => kind switch
    {
        RecordKind.Application => AppRestricted(id),
        RecordKind.Flow => _flows.TryGetValue(id, out var f) && (AppRestricted(f.FromId) || AppRestricted(f.ToId)),
        RecordKind.Page => _pages.TryGetValue(id, out var p) && p.Restricted,
        _ => false,
    };

    /// <summary>Same as <see cref="Restricted(RecordKind, string)"/>, for a record state (a snapshot or an unsaved entity).</summary>
    public bool Restricted(RecordKind kind, VersionedEntity e) => e switch
    {
        Application a => a.Visibility == Visibility.Restricted,
        Flow f => AppRestricted(f.FromId) || AppRestricted(f.ToId),
        Page p => p.Visibility == Visibility.Restricted,
        _ => Restricted(kind, e.Id),
    };

    public bool Restricted(RecordKind kind, JsonObject snapshot) => kind switch
    {
        RecordKind.Application => Text(snapshot["visibility"]) == "restricted",
        RecordKind.Flow => AppRestricted(Text(snapshot["fromId"])) || AppRestricted(Text(snapshot["toId"])),
        RecordKind.Page => Text(snapshot["visibility"]) == "restricted",
        _ => false,
    };

    /// <summary>
    /// Owning team of a record state: an application's team; a flow's owner team, or else the team of its source
    /// application; the team of a data object's source application; a page's owner team, or else the owner of the record it is
    /// attached to. Null (administrators) for domains, teams and records without one.
    /// </summary>
    public string? Owner(RecordKind kind, VersionedEntity e) => e switch
    {
        Application a => a.TeamId,
        Flow f => f.OwnerTeamId ?? AppTeam(f.FromId),
        DataObject o => AppTeam(o.SourceApplicationId),
        Page p => p.OwnerTeamId ?? (p.AttachedKind is { } k && p.AttachedId is { } id ? Owner(k, id) : null),
        _ => null,
    };

    /// <summary>Owner of a stored record, by id.</summary>
    public string? Owner(RecordKind kind, string id) => kind switch
    {
        RecordKind.Application => AppTeam(id),
        RecordKind.Flow => _flows.TryGetValue(id, out var f) ? f.OwnerTeamId ?? AppTeam(f.FromId) : null,
        RecordKind.DataObject => _objects.TryGetValue(id, out var o) ? AppTeam(o.Source) : null,
        // A page attached to another page is refused, so this does not recurse further than one level.
        RecordKind.Page => _pages.TryGetValue(id, out var p)
            ? p.OwnerTeamId ?? (p.AttachedKind is { } k and not RecordKind.Page && p.AttachedId is { } a ? Owner(k, a) : null)
            : null,
        _ => null,
    };

    /// <summary>Owner of a record state given as a snapshot (a proposed creation).</summary>
    public string? Owner(RecordKind kind, JsonObject s) => kind switch
    {
        RecordKind.Application => Text(s["teamId"]),
        RecordKind.Flow => Text(s["ownerTeamId"]) ?? AppTeam(Text(s["fromId"])),
        RecordKind.DataObject => AppTeam(Text(s["sourceApplicationId"])),
        RecordKind.Page => Text(s["ownerTeamId"])
            ?? (s["attachedTo"] is JsonObject a && Records.Parse(Text(a["kind"])) is { Kind: var k and not RecordKind.Page } && Text(a["id"]) is { } id
                ? Owner(k, id) : null),
        _ => null,
    };

    private string? AppTeam(string? id) => id is not null && _apps.TryGetValue(id, out var a) ? a.TeamId : null;

    internal static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>
/// The reader's rights on records. Everyone reads, except restricted records (administrators, the owning team and the
/// access list only) and, for guests, records not shared with them by name (outline only). Administrators and members of
/// the owning team publish; other members propose; guests only read. Protected notes are read by administrators, the
/// owning team and the access list.
/// </summary>
public sealed class Access(Viewer viewer, IReadOnlySet<string> teams, RecordFacts facts)
{
    public Viewer Viewer { get; } = viewer;
    public IReadOnlySet<string> Teams { get; } = teams;
    public RecordFacts Facts { get; } = facts;
    /// <summary>For an AI agent (or an anonymous reader of the Markdown views): what the ceiling and the scopes leave of the person's rights.</summary>
    public AgentLimits? Limits { get; init; }

    /// <summary>The same reader, limited by an agent's ceiling and scopes: min(person, ceiling, scopes).</summary>
    public Access WithLimits(AgentLimits limits) => new(Viewer, Teams, Facts) { Limits = limits };

    public static async Task<Access> Load(NoosphereDbContext db, ClaimsPrincipal user, CancellationToken cancel = default)
    {
        var viewer = Viewer.From(user);
        return new Access(viewer, await Memberships.TeamIds(db, viewer.Id, cancel), await RecordFacts.Load(db, cancel));
    }

    /// <summary>Rights of the command line and the system: everything.</summary>
    public static async Task<Access> System(NoosphereDbContext db, CancellationToken cancel = default) =>
        new(Viewer.System, new HashSet<string>(), await RecordFacts.Load(db, cancel));

    public bool IsAdmin => Viewer.IsAdmin;
    public bool IsGuest => Viewer.Role == Role.Guest;

    public bool InTeam(string? teamId) => teamId is not null && Teams.Contains(teamId);

    /// <summary>The reader is on the list, by name or through one of their teams.</summary>
    public bool Listed(AccessList list) => list.Usernames.Contains(Viewer.Username, StringComparer.Ordinal) || list.TeamIds.Any(Teams.Contains);

    private Services.Sight Of(string? owner, bool restricted, AccessList list)
    {
        // Under the ceiling, restricted records are invisible to an agent even when its person sees them.
        if (restricted && Limits is { SeesRestricted: false }) return Services.Sight.Hidden;
        if (Viewer.IsAdmin) return Services.Sight.Full;
        if (restricted) return InTeam(owner) || Listed(list) ? Services.Sight.Full : Services.Sight.Hidden;
        if (!IsGuest) return Services.Sight.Full;
        // Guests see the outline, and in full what is shared with them by name.
        return list.Usernames.Contains(Viewer.Username, StringComparer.Ordinal) ? Services.Sight.Full : Services.Sight.Outline;
    }

    private Services.Sight App(string? id) => Of(Facts.Owner(RecordKind.Application, id ?? ""), Facts.AppRestricted(id), Facts.AppAccess(id));

    /// <summary>
    /// A flow is hidden when one of its restricted ends is hidden: it needs the right to see all of them. A guest sees it in
    /// full when one of its ends is shared with them.
    /// </summary>
    private Services.Sight Flow(string? fromId, string? toId)
    {
        var (from, to) = (App(fromId), App(toId));
        if (from == Services.Sight.Hidden || to == Services.Sight.Hidden) return Services.Sight.Hidden;
        if (!IsGuest || Viewer.IsAdmin) return Services.Sight.Full;
        return from == Services.Sight.Full || to == Services.Sight.Full ? Services.Sight.Full : Services.Sight.Outline;
    }

    /// <summary>How much of a stored record the reader sees. Data objects, domains and teams are seen by everyone.</summary>
    public Services.Sight Sight(RecordKind kind, string id) => kind switch
    {
        RecordKind.Application => App(id),
        RecordKind.Flow => Facts.Ends(id) is { } ends ? Flow(ends.FromId, ends.ToId) : Of(null, false, AccessList.Empty),
        RecordKind.Page => Of(Facts.Owner(RecordKind.Page, id), Facts.Restricted(RecordKind.Page, id), Facts.PageAccess(id)),
        _ => Services.Sight.Full,
    };

    /// <summary>Same as <see cref="Sight(RecordKind, string)"/>, for a record state (an entity being saved or read).</summary>
    public Services.Sight Sight(RecordKind kind, VersionedEntity e) => e switch
    {
        Application a => Of(a.TeamId, a.Visibility == Visibility.Restricted, AccessList.Of(a)),
        Flow f => Flow(f.FromId, f.ToId),
        Page p => Of(Facts.Owner(RecordKind.Page, p), p.Visibility == Visibility.Restricted, AccessList.Of(p)),
        _ => Services.Sight.Full,
    };

    /// <summary>Same as <see cref="Sight(RecordKind, string)"/>, for a snapshot (a revision or a proposed state).</summary>
    public Services.Sight Sight(RecordKind kind, JsonObject s) => kind switch
    {
        RecordKind.Application or RecordKind.Page => Of(Facts.Owner(kind, s), RecordFacts.Text(s["visibility"]) == "restricted", AccessList.Of(s["access"])),
        RecordKind.Flow => Flow(RecordFacts.Text(s["fromId"]), RecordFacts.Text(s["toId"])),
        _ => Services.Sight.Full,
    };

    /// <summary>Hidden from the reader: restricted, and the reader is neither an administrator nor the owning team nor on the access list.</summary>
    public bool Hidden(RecordKind kind, string id) => Sight(kind, id) == Services.Sight.Hidden;

    public bool SeesFully(RecordKind kind, string id) => Sight(kind, id) == Services.Sight.Full;

    /// <summary>Guests never read protected notes, even when named on the access list.</summary>
    private bool NotesReader(string? owner, AccessList list) =>
        Limits is not { ReadsProtectedNotes: false } && (Viewer.IsAdmin || (!IsGuest && (InTeam(owner) || Listed(list))));

    /// <summary>
    /// The reader may read the protected notes of a record in this state: they see it in full, and they are an
    /// administrator, or a member (not a guest) in the owning team or on the access list (for a flow: of one of its ends).
    /// </summary>
    public bool ReadsProtected(RecordKind kind, VersionedEntity e) => e switch
    {
        Application a => Sight(kind, a) == Services.Sight.Full && NotesReader(a.TeamId, AccessList.Of(a)),
        Flow f => Sight(kind, f) == Services.Sight.Full
            && (NotesReader(Facts.Owner(kind, f), Facts.AppAccess(f.FromId)) || NotesReader(null, Facts.AppAccess(f.ToId))),
        _ => false,
    };

    /// <summary>Same as <see cref="ReadsProtected(RecordKind, VersionedEntity)"/>, for a stored record.</summary>
    public bool ReadsProtected(RecordKind kind, string id) => kind switch
    {
        RecordKind.Application => App(id) == Services.Sight.Full && NotesReader(Facts.Owner(kind, id), Facts.AppAccess(id)),
        RecordKind.Flow => Facts.Ends(id) is { } ends && Flow(ends.FromId, ends.ToId) == Services.Sight.Full
            && (NotesReader(Facts.Owner(kind, id), Facts.AppAccess(ends.FromId)) || NotesReader(null, Facts.AppAccess(ends.ToId))),
        _ => false,
    };

    /// <summary>Same as <see cref="ReadsProtected(RecordKind, VersionedEntity)"/>, for a snapshot.</summary>
    public bool ReadsProtected(RecordKind kind, JsonObject s) => kind switch
    {
        RecordKind.Application => Sight(kind, s) == Services.Sight.Full && NotesReader(Facts.Owner(kind, s), AccessList.Of(s["access"])),
        RecordKind.Flow => Sight(kind, s) == Services.Sight.Full
            && (NotesReader(Facts.Owner(kind, s), Facts.AppAccess(RecordFacts.Text(s["fromId"]))) || NotesReader(null, Facts.AppAccess(RecordFacts.Text(s["toId"])))),
        _ => false,
    };

    public bool CanPublish(RecordKind kind, VersionedEntity state) => PublishProblem(kind, state) is null;

    /// <summary>Members propose changes to every record they see in full (not domains and teams).</summary>
    public bool CanPropose(RecordType type, VersionedEntity? state) =>
        Viewer.CanEdit && type.Proposable && (state is null || Sight(type.Kind, state) == Services.Sight.Full);

    public PermissionsDto Permissions(RecordType type, VersionedEntity state) => new(CanPublish(type.Kind, state), CanPropose(type, state));

    /// <summary>Why the reader may not publish a change to a record in this state; null when they may.</summary>
    public string? PublishProblem(RecordKind kind, VersionedEntity state)
    {
        if (!Viewer.CanEdit) return "Your role does not allow this action.";
        if (Viewer.IsAdmin) return null;
        if (kind is RecordKind.Domain or RecordKind.Team) return "Only administrators change domains and teams.";
        if (Sight(kind, state) == Services.Sight.Hidden) return $"This {Records.Of(kind).Noun} is restricted: you are not on its access list.";
        var owner = Facts.Owner(kind, state);
        if (owner is null) return $"This {Records.Of(kind).Noun} has no owning team: only administrators publish its changes. Send a change proposal instead.";
        return InTeam(owner) ? null
            : $"Only members of {Facts.TeamName(owner) ?? owner} publish changes to this {Records.Of(kind).Noun}. Send a change proposal instead.";
    }

    /// <summary>403 unless the reader may publish a change to a record in this state.</summary>
    public void RequirePublish(RecordKind kind, VersionedEntity state)
    {
        if (PublishProblem(kind, state) is { } problem) throw ApiProblem.Forbidden(problem);
    }

    /// <summary>403 when the reader would no longer see the record in this new state (they would lock themselves out).</summary>
    public void RequireStillVisible(RecordType type, VersionedEntity state)
    {
        if (Sight(type.Kind, state) == Services.Sight.Hidden)
            throw ApiProblem.Forbidden($"You would no longer see this {type.Noun}: add yourself or one of your teams to its access list.");
    }
}

/// <summary>
/// The limits of an agent on top of its person's rights. Restricted records need the <c>asUser</c> ceiling and the
/// <c>docs:read:restricted</c> scope; protected notes need the <c>public+protected</c> or <c>asUser</c> ceiling.
/// </summary>
public sealed record AgentLimits(AgentCeiling Ceiling, bool RestrictedScope)
{
    public bool SeesRestricted => Ceiling == AgentCeiling.AsUser && RestrictedScope;
    public bool ReadsProtectedNotes => Ceiling != AgentCeiling.Public;

    /// <summary>An anonymous reader of <c>/llms.txt</c>: records visible to everyone, without protected notes.</summary>
    public static readonly AgentLimits Anonymous = new(AgentCeiling.Public, false);
}

/// <summary>Team memberships of users.</summary>
public static class Memberships
{
    public static async Task<IReadOnlySet<string>> TeamIds(NoosphereDbContext db, Guid userId, CancellationToken cancel = default) =>
        (await db.TeamMembers.Where(m => m.UserId == userId).Select(m => m.TeamId).ToListAsync(cancel)).ToHashSet();

    /// <summary>Replaces the teams of a user. 400 when a team does not exist.</summary>
    public static async Task Set(NoosphereDbContext db, Guid userId, IEnumerable<string> teamIds, string field = "teamIds", CancellationToken cancel = default)
    {
        var wanted = teamIds.Select(t => t?.Trim() ?? "").Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var known = (await db.Teams.Where(t => wanted.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancel)).ToHashSet();
        var errors = new FieldErrors();
        foreach (var unknown in wanted.Where(t => !known.Contains(t))) errors.Add(field, $"Unknown team '{unknown}'.");
        errors.ThrowIfAny();
        var current = await db.TeamMembers.Where(m => m.UserId == userId).ToListAsync(cancel);
        db.TeamMembers.RemoveRange(current.Where(m => !wanted.Contains(m.TeamId)));
        foreach (var team in wanted.Where(t => current.All(m => m.TeamId != t)))
            db.TeamMembers.Add(new TeamMember { UserId = userId, TeamId = team });
    }
}
