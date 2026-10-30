using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>
/// The whole landscape, read in one go (a landscape holds hundreds to a few thousand records), with the computed values
/// (completeness, sensitivity, sourceOf) and the visibility rules applied when turned into responses.
/// </summary>
public sealed class Snapshot
{
    public required IReadOnlyList<BusinessDomain> Domains { get; init; }
    public required IReadOnlyList<Team> Teams { get; init; }
    public required IReadOnlyList<Application> Applications { get; init; }
    public required IReadOnlyList<DataObject> DataObjects { get; init; }
    public required IReadOnlyList<Flow> Flows { get; init; }

    private Dictionary<string, Application>? _apps;
    private Dictionary<string, DataObject>? _objects;
    private HashSet<string>? _connected;
    private ILookup<string, string>? _sources;

    public IReadOnlyDictionary<string, Application> AppsById => _apps ??= Applications.ToDictionary(a => a.Id);
    public IReadOnlyDictionary<string, DataObject> ObjectsById => _objects ??= DataObjects.ToDictionary(o => o.Id);

    public static async Task<Snapshot> Load(NoosphereDbContext db, CancellationToken cancel = default) => new()
    {
        Domains = await db.Domains.AsNoTracking().OrderBy(d => d.Order).ThenBy(d => d.Id).ToListAsync(cancel),
        Teams = await db.Teams.AsNoTracking().OrderBy(t => t.Id).ToListAsync(cancel),
        Applications = await db.Applications.AsNoTracking().OrderBy(a => a.Id).ToListAsync(cancel),
        DataObjects = await db.DataObjects.AsNoTracking().OrderBy(o => o.Id).ToListAsync(cancel),
        Flows = await db.Flows.AsNoTracking().OrderBy(f => f.Id).ToListAsync(cancel),
    };

    public bool Hidden(Application app, Viewer viewer) => app.Visibility == Visibility.Restricted && !viewer.SeesRestricted;

    public bool Hidden(Flow flow, Viewer viewer) =>
        (AppsById.TryGetValue(flow.FromId, out var from) && Hidden(from, viewer))
        || (AppsById.TryGetValue(flow.ToId, out var to) && Hidden(to, viewer));

    public static DomainDto Dto(BusinessDomain d) => new(d.Id, d.Name, d.Description, d.Order, d.Version, d.CreatedAt, d.UpdatedAt);

    public static TeamDto Dto(Team t) => new(t.Id, t.Name, t.Description, t.Contact, t.Version, t.CreatedAt, t.UpdatedAt);

    public static DataObjectDto Dto(DataObject o) => new(o.Id, o.Name, o.Sensitivity, o.SourceApplicationId, o.Description, o.Version, o.CreatedAt, o.UpdatedAt);

    public ApplicationDto Dto(Application a)
    {
        _connected ??= Flows.SelectMany(f => new[] { f.FromId, f.ToId }).ToHashSet();
        _sources ??= DataObjects.Where(o => o.SourceApplicationId is not null).ToLookup(o => o.SourceApplicationId!, o => o.Id);
        return new(a.Id, false, a.Name, a.DomainId, a.TeamId, a.Kind, a.Tech, a.Description, a.Lifecycle, a.Visibility,
            [.. _sources[a.Id].Order(StringComparer.Ordinal)], Rules.Completeness(a, _connected.Contains(a.Id)), a.Version, a.CreatedAt, a.UpdatedAt);
    }

    public FlowDto Dto(Flow f) => new(f.Id, false, f.Name, f.FromId, f.ToId, f.Type, f.Schedule, f.DataObjectIds,
        Rules.FlowSensitivity(f.DataObjectIds.Where(ObjectsById.ContainsKey).Select(id => ObjectsById[id].Sensitivity)),
        f.OwnerTeamId, f.Status, f.Volume, f.Description,
        f.PlannedChangeSummary is null ? null : new PlannedChangeDto(f.PlannedChangeSummary, f.PlannedChangeDate),
        Rules.Completeness(f), f.Version, f.CreatedAt, f.UpdatedAt);

    /// <summary><see cref="ApplicationDto"/>, or <see cref="RestrictedApplicationDto"/> when the viewer may not see it.</summary>
    public object View(Application a, Viewer viewer) => Hidden(a, viewer) ? new RestrictedApplicationDto(a.Id, true, a.DomainId) : Dto(a);

    /// <summary><see cref="FlowDto"/>, or <see cref="RestrictedFlowDto"/> when one of its ends is hidden from the viewer.</summary>
    public object View(Flow f, Viewer viewer) => Hidden(f, viewer) ? new RestrictedFlowDto(f.Id, true, f.FromId, f.ToId, f.Type) : Dto(f);
}

/// <summary>Filters of the list endpoints and of the landscape (all optional, combined with AND).</summary>
/// <param name="DomainId">Applications of this domain; flows from or to one of them.</param>
/// <param name="TeamId">Applications of this team; flows owned by this team.</param>
/// <param name="Type">Flows of this type.</param>
/// <param name="Status">Flows with this status.</param>
/// <param name="DataObjectId">Flows carrying this data object.</param>
/// <param name="Q">Text contained in the name (accent- and case-insensitive), also in the id, tech and description of applications.</param>
public sealed record Filter(string? DomainId, string? TeamId, FlowType? Type, FlowStatus? Status, string? DataObjectId, string? Q)
{
    private string? Folded => string.IsNullOrWhiteSpace(Q) ? null : Rules.Fold(Q.Trim());

    public bool HasApplicationFilter => DomainId is not null || TeamId is not null || Folded is not null;
    public bool HasFlowFilter => Type is not null || Status is not null || DataObjectId is not null;
    public bool Any => HasApplicationFilter || HasFlowFilter;

    private static bool Contains(string? text, string folded) => text is not null && Rules.Fold(text).Contains(folded, StringComparison.Ordinal);

    public bool Matches(BusinessDomain d) => Folded is not { } q || Contains(d.Name, q) || Contains(d.Id, q);

    public bool Matches(Team t) => Folded is not { } q || Contains(t.Name, q) || Contains(t.Id, q);

    public bool Matches(DataObject o) => Folded is not { } q || Contains(o.Name, q) || Contains(o.Id, q);

    /// <summary>Fields hidden by restriction never match, so that a filter cannot reveal them.</summary>
    public bool Matches(Application a, bool hidden) =>
        (DomainId is null || a.DomainId == DomainId)
        && (TeamId is null || (!hidden && a.TeamId == TeamId))
        && (Folded is not { } q || (!hidden && (Contains(a.Name, q) || Contains(a.Id, q) || Contains(a.Tech, q) || Contains(a.Description, q))));

    /// <summary>Flow criteria of the landscape: type, status and data object.</summary>
    public bool MatchesFlowCriteria(Flow f, bool hidden) =>
        (Type is null || f.Type == Type)
        && (Status is null || (!hidden && f.Status == Status))
        && (DataObjectId is null || (!hidden && f.DataObjectIds.Contains(DataObjectId)));

    /// <summary>Flow list: flow criteria, and with <see cref="DomainId"/>, flows from or to an application of that domain.</summary>
    public bool MatchesFlowList(Flow f, Snapshot s, bool hidden) =>
        MatchesFlowCriteria(f, hidden)
        && (TeamId is null || (!hidden && f.OwnerTeamId == TeamId))
        && (Folded is not { } q || (!hidden && (Contains(f.Name, q) || Contains(f.Id, q))))
        && (DomainId is null || (s.AppsById.TryGetValue(f.FromId, out var from) && from.DomainId == DomainId)
            || (s.AppsById.TryGetValue(f.ToId, out var to) && to.DomainId == DomainId));
}
