using Microsoft.EntityFrameworkCore;
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

    public static DomainDto Dto(BusinessDomain d) => new(d.Id, d.Name, d.Description, d.Order, d.Version, d.CreatedAt, d.UpdatedAt);

    public static TeamDto Dto(Team t) => new(t.Id, t.Name, t.Description, t.Contact, t.Version, t.CreatedAt, t.UpdatedAt);

    public static DataObjectDto Dto(DataObject o) => new(o.Id, o.Name, o.Sensitivity, o.SourceApplicationId, o.Description, o.Version, o.CreatedAt, o.UpdatedAt);

    public static AccessListDto AccessDto(Application a) => new(a.AccessTeamIds, a.AccessUsernames);

    public static AccessListDto AccessDto(Page p) => new(p.AccessTeamIds, p.AccessUsernames);

    public Sensitivity FlowSensitivity(Flow f) =>
        Rules.FlowSensitivity(f.DataObjectIds.Where(ObjectsById.ContainsKey).Select(id => ObjectsById[id].Sensitivity));

    public const int SensitivityReasons = 5;

    private Access? _sensitivityFor;
    private Dictionary<string, (Sensitivity Level, List<SensitivityReasonDto> Reasons)>? _sensitivities;

    /// <summary>
    /// Sensitivity of an application: the most sensitive of the data objects it is the source of and of the flows it sends
    /// or receives (internal when there is none), with the top reasons. Flows hidden from the reader are left out, so that
    /// neither the level nor the reasons reveal them.
    /// </summary>
    public (Sensitivity Level, IReadOnlyList<SensitivityReasonDto> Reasons) ApplicationSensitivity(string appId, Access access)
    {
        if (_sensitivities is null || !ReferenceEquals(_sensitivityFor, access))
        {
            var reasons = Applications.ToDictionary(a => a.Id, _ => new List<(int Order, SensitivityReasonDto Reason)>());
            foreach (var o in DataObjects)
                if (o.SourceApplicationId is { } src && reasons.TryGetValue(src, out var list))
                    list.Add((0, new SensitivityReasonDto(SensitivityReasonKind.DataObject, o.Id, o.Name, o.Sensitivity)));
            foreach (var f in Flows.Where(f => access.Sight(RecordKind.Flow, f) != Sight.Hidden))
            {
                var reason = new SensitivityReasonDto(SensitivityReasonKind.Flow, f.Id, f.Name, FlowSensitivity(f));
                foreach (var end in new[] { f.FromId, f.ToId }.Distinct())
                    if (reasons.TryGetValue(end, out var list)) list.Add((1, reason));
            }
            _sensitivities = reasons.ToDictionary(r => r.Key, r =>
            {
                var top = r.Value.OrderByDescending(x => x.Reason.Sensitivity).ThenBy(x => x.Order)
                    .ThenBy(x => x.Reason.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Reason.Id, StringComparer.Ordinal)
                    .Select(x => x.Reason).ToList();
                return (top.Count == 0 ? Sensitivity.Internal : top[0].Sensitivity, top.Take(SensitivityReasons).ToList());
            });
            _sensitivityFor = access;
        }
        return _sensitivities.TryGetValue(appId, out var found) ? (found.Level, found.Reasons) : (Sensitivity.Internal, []);
    }

    public ApplicationDto Dto(Application a, Access access)
    {
        _connected ??= Flows.SelectMany(f => new[] { f.FromId, f.ToId }).ToHashSet();
        _sources ??= DataObjects.Where(o => o.SourceApplicationId is not null).ToLookup(o => o.SourceApplicationId!, o => o.Id);
        var (level, reasons) = ApplicationSensitivity(a.Id, access);
        return new(a.Id, false, a.Name, a.DomainId, a.TeamId, a.Kind, a.Tech, a.Description, a.Lifecycle, a.Visibility, AccessDto(a),
            [.. _sources[a.Id].Order(StringComparer.Ordinal)], Rules.Completeness(a, _connected.Contains(a.Id)), level, reasons,
            a.Version, a.CreatedAt, a.UpdatedAt);
    }

    public FlowDto Dto(Flow f) => new(f.Id, false, f.Name, f.FromId, f.ToId, f.Type, f.Schedule, f.DataObjectIds, FlowSensitivity(f),
        f.OwnerTeamId, f.Status, f.Volume, f.Description,
        f.PlannedChangeSummary is null ? null : new PlannedChangeDto(f.PlannedChangeSummary, f.PlannedChangeDate),
        Rules.Completeness(f), f.Version, f.CreatedAt, f.UpdatedAt);

    /// <summary>
    /// <see cref="ApplicationDto"/>, <see cref="RestrictedApplicationDto"/> when the reader may not see it, or
    /// <see cref="GuestApplicationDto"/> for a guest it is not shared with.
    /// </summary>
    public object View(Application a, Access access) => access.Sight(RecordKind.Application, a) switch
    {
        Sight.Hidden => new RestrictedApplicationDto(a.Id, true, a.DomainId),
        Sight.Outline => new GuestApplicationDto(a.Id, false, false, a.Name, a.DomainId, a.Kind),
        _ => Dto(a, access),
    };

    /// <summary><see cref="FlowDto"/>, <see cref="RestrictedFlowDto"/> when a restricted end is hidden from the reader, or <see cref="GuestFlowDto"/>.</summary>
    public object View(Flow f, Access access) => access.Sight(RecordKind.Flow, f) switch
    {
        Sight.Hidden => new RestrictedFlowDto(f.Id, true, f.FromId, f.ToId, f.Type),
        Sight.Outline => new GuestFlowDto(f.Id, false, false, f.Name, f.FromId, f.ToId, f.Type),
        _ => Dto(f),
    };
}

/// <summary>Filters of the list endpoints and of the landscape (all optional, combined with AND).</summary>
/// <param name="DomainId">Applications of this domain; flows from or to one of them.</param>
/// <param name="TeamId">Applications of this team; flows owned by this team.</param>
/// <param name="Type">Flows of this type.</param>
/// <param name="Status">Flows with this status.</param>
/// <param name="DataObjectId">Flows carrying this data object.</param>
/// <param name="Q">Text contained in the name (accent- and case-insensitive), also in the id, tech and description of applications.</param>
/// <param name="Sensitivity">Applications at least this sensitive (flows: carrying at least this level).</param>
public sealed record Filter(string? DomainId, string? TeamId, FlowType? Type, FlowStatus? Status, string? DataObjectId, string? Q,
    Sensitivity? Sensitivity = null)
{
    private string? Folded => string.IsNullOrWhiteSpace(Q) ? null : Rules.Fold(Q.Trim());

    public bool HasApplicationFilter => DomainId is not null || TeamId is not null || Folded is not null || Sensitivity is not null;
    public bool HasFlowFilter => Type is not null || Status is not null || DataObjectId is not null;
    public bool Any => HasApplicationFilter || HasFlowFilter;

    private static bool Contains(string? text, string folded) => text is not null && Rules.Fold(text).Contains(folded, StringComparison.Ordinal);

    public bool Matches(BusinessDomain d) => Folded is not { } q || Contains(d.Name, q) || Contains(d.Id, q);

    public bool Matches(Team t) => Folded is not { } q || Contains(t.Name, q) || Contains(t.Id, q);

    public bool Matches(DataObject o) => Folded is not { } q || Contains(o.Name, q) || Contains(o.Id, q);

    /// <summary>Fields the reader may not see never match, so that a filter cannot reveal them.</summary>
    public bool Matches(Application a, Sight sight, Sensitivity level) =>
        (DomainId is null || a.DomainId == DomainId)
        && (TeamId is null || (sight == Sight.Full && a.TeamId == TeamId))
        && (Sensitivity is null || (sight != Sight.Hidden && level >= Sensitivity))
        && (Folded is not { } q || sight switch
        {
            Sight.Full => Contains(a.Name, q) || Contains(a.Id, q) || Contains(a.Tech, q) || Contains(a.Description, q),
            Sight.Outline => Contains(a.Name, q) || Contains(a.Id, q),
            _ => false,
        });

    /// <summary>Flow criteria of the landscape: type, status and data object.</summary>
    public bool MatchesFlowCriteria(Flow f, Sight sight) =>
        (Type is null || f.Type == Type)
        && (Status is null || (sight == Sight.Full && f.Status == Status))
        && (DataObjectId is null || (sight == Sight.Full && f.DataObjectIds.Contains(DataObjectId)));

    /// <summary>Flow list: flow criteria, and with <see cref="DomainId"/>, flows from or to an application of that domain.</summary>
    public bool MatchesFlowList(Flow f, Snapshot s, Sight sight) =>
        MatchesFlowCriteria(f, sight)
        && (TeamId is null || (sight == Sight.Full && f.OwnerTeamId == TeamId))
        && (Sensitivity is null || (sight != Sight.Hidden && s.FlowSensitivity(f) >= Sensitivity))
        && (Folded is not { } q || (sight != Sight.Hidden && (Contains(f.Name, q) || Contains(f.Id, q))))
        && (DomainId is null || (s.AppsById.TryGetValue(f.FromId, out var from) && from.DomainId == DomainId)
            || (s.AppsById.TryGetValue(f.ToId, out var to) && to.DomainId == DomainId));
}
