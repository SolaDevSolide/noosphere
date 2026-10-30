using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>
/// What ownership and visibility depend on: the team and visibility of every application, the ends and owner of every
/// flow, the source of every data object and the owner, attachment and visibility of every page.
/// </summary>
public sealed class RecordFacts
{
    private sealed record AppFact(string? TeamId, bool Restricted, string Name);
    private sealed record FlowFact(string FromId, string ToId, string? OwnerTeamId, string Name);
    private sealed record PageFact(string? OwnerTeamId, RecordKind? AttachedKind, string? AttachedId, bool Restricted, string Title);

    private Dictionary<string, AppFact> _apps = [];
    private Dictionary<string, FlowFact> _flows = [];
    private Dictionary<string, (string? Source, string Name)> _objects = [];
    private Dictionary<string, PageFact> _pages = [];
    private Dictionary<string, string> _domains = [];
    private Dictionary<string, string> _teams = [];

    public static async Task<RecordFacts> Load(NoosphereDbContext db, CancellationToken cancel = default) => new()
    {
        _apps = await db.Applications.AsNoTracking()
            .ToDictionaryAsync(a => a.Id, a => new AppFact(a.TeamId, a.Visibility == Visibility.Restricted, a.Name), cancel),
        _flows = await db.Flows.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, f => new FlowFact(f.FromId, f.ToId, f.OwnerTeamId, f.Name), cancel),
        _objects = (await db.DataObjects.AsNoTracking().Select(o => new { o.Id, o.SourceApplicationId, o.Name }).ToListAsync(cancel))
            .ToDictionary(o => o.Id, o => (o.SourceApplicationId, o.Name)),
        _pages = (await db.Pages.AsNoTracking()
            .Select(p => new { p.Id, p.OwnerTeamId, p.AttachedKind, p.AttachedId, p.Visibility, p.Title }).ToListAsync(cancel))
            .ToDictionary(p => p.Id, p => new PageFact(p.OwnerTeamId, p.AttachedKind, p.AttachedId, p.Visibility == Visibility.Restricted, p.Title)),
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

    private bool AppRestricted(string? id) => id is not null && _apps.TryGetValue(id, out var a) && a.Restricted;

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

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>
/// The reader's rights on records: everyone reads; administrators and members of the owning team publish; other members
/// propose; guests only read. Restricted records are changed by administrators only.
/// </summary>
public sealed class Access(Viewer viewer, IReadOnlySet<string> teams, RecordFacts facts)
{
    public Viewer Viewer { get; } = viewer;
    public IReadOnlySet<string> Teams { get; } = teams;
    public RecordFacts Facts { get; } = facts;

    public static async Task<Access> Load(NoosphereDbContext db, ClaimsPrincipal user, CancellationToken cancel = default)
    {
        var viewer = Viewer.From(user);
        return new Access(viewer, await Memberships.TeamIds(db, viewer.Id, cancel), await RecordFacts.Load(db, cancel));
    }

    public bool InTeam(string? teamId) => teamId is not null && Teams.Contains(teamId);

    /// <summary>Hidden from the reader: restricted, and the reader is not an administrator.</summary>
    public bool Hidden(RecordKind kind, string id) => !Viewer.SeesRestricted && Facts.Restricted(kind, id);

    public bool CanPublish(RecordKind kind, VersionedEntity state) => PublishProblem(kind, state) is null;

    public bool CanPropose(RecordType type, VersionedEntity? state) =>
        Viewer.CanEdit && type.Proposable && (Viewer.SeesRestricted || state is null || !Facts.Restricted(type.Kind, state));

    public PermissionsDto Permissions(RecordType type, VersionedEntity state) => new(CanPublish(type.Kind, state), CanPropose(type, state));

    /// <summary>Why the reader may not publish a change to a record in this state; null when they may.</summary>
    public string? PublishProblem(RecordKind kind, VersionedEntity state)
    {
        if (!Viewer.CanEdit) return "Your role does not allow this action.";
        if (Viewer.IsAdmin) return null;
        if (Facts.Restricted(kind, state)) return $"This {Records.Of(kind).Noun} is restricted: only administrators can change it.";
        if (kind is RecordKind.Domain or RecordKind.Team) return "Only administrators change domains and teams.";
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
