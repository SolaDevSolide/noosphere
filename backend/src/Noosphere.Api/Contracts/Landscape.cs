using Noosphere.Domain;

namespace Noosphere.Api.Contracts;

// Inputs: PUT replaces the whole record (a missing optional field is cleared) and must carry the version that was read;
// a stale version returns 409. On POST, a missing id is derived from the name ("Customer portal" -> "customer-portal").

public record DomainDto(string Id, string Name, string? Description, int Order, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record DomainInput(string? Id, string? Name, string? Description, int? Order, int? Version);

public record TeamDto(string Id, string Name, string? Description, string? Contact, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record TeamInput(string? Id, string? Name, string? Description, string? Contact, int? Version);

/// <summary>An application the reader may see in full. <c>restricted</c> is always false (true on <see cref="RestrictedApplicationDto"/>).</summary>
/// <param name="SourceOf">Ids of the data objects whose source is this application (derived from <c>DataObject.sourceApplicationId</c>).</param>
/// <param name="Completeness">Share of the recommended fields that are filled, 0 to 1.</param>
public record ApplicationDto(
    string Id, bool Restricted, string Name, string DomainId, string? TeamId, ApplicationKind Kind, string? Tech, string? Description,
    Lifecycle Lifecycle, Visibility Visibility, IReadOnlyList<string> SourceOf, double Completeness,
    int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A restricted application, as seen by a reader who is not an administrator.</summary>
public sealed record RestrictedApplicationDto(string Id, bool Restricted, string DomainId);

public sealed record ApplicationInput(
    string? Id, string? Name, string? DomainId, string? TeamId, ApplicationKind? Kind, string? Tech, string? Description,
    Lifecycle? Lifecycle, Visibility? Visibility, int? Version);

public record DataObjectDto(
    string Id, string Name, Sensitivity Sensitivity, string? SourceApplicationId, string? Description,
    int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record DataObjectInput(string? Id, string? Name, Sensitivity? Sensitivity, string? SourceApplicationId, string? Description, int? Version);

/// <param name="Date">ISO date (YYYY-MM-DD).</param>
public sealed record PlannedChangeDto(string Summary, DateOnly? Date);

/// <summary>A flow the reader may see in full. <c>restricted</c> is always false (true on <see cref="RestrictedFlowDto"/>).</summary>
/// <param name="Sensitivity">Most sensitive of the carried data objects (internal when there is none).</param>
public record FlowDto(
    string Id, bool Restricted, string Name, string FromId, string ToId, FlowType Type, string? Schedule, IReadOnlyList<string> DataObjectIds,
    Sensitivity Sensitivity, string? OwnerTeamId, FlowStatus Status, string? Volume, string? Description, PlannedChangeDto? PlannedChange,
    double Completeness, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A flow from or to a restricted application, as seen by a reader who is not an administrator.</summary>
public sealed record RestrictedFlowDto(string Id, bool Restricted, string FromId, string ToId, FlowType Type);

public sealed record FlowInput(
    string? Id, string? Name, string? FromId, string? ToId, FlowType? Type, string? Schedule, List<string>? DataObjectIds,
    string? OwnerTeamId, FlowStatus? Status, string? Volume, string? Description, PlannedChangeDto? PlannedChange, int? Version);

/// <summary>Counts of what the landscape response holds.</summary>
public sealed record LandscapeStatsDto(int Applications, int Flows, int Domains);

/// <summary>
/// Everything the explorer needs, in one call. <c>applications</c> holds <see cref="ApplicationDto"/> or
/// <see cref="RestrictedApplicationDto"/> items, <c>flows</c> holds <see cref="FlowDto"/> or <see cref="RestrictedFlowDto"/> items
/// (discriminated by <c>restricted</c>).
/// </summary>
public sealed record LandscapeDto(
    IReadOnlyList<DomainDto> Domains, IReadOnlyList<TeamDto> Teams, IReadOnlyList<object> Applications,
    IReadOnlyList<DataObjectDto> DataObjects, IReadOnlyList<object> Flows, LandscapeStatsDto Stats, DateTimeOffset GeneratedAt);

public enum SearchKind { Application, Flow, DataObject, Team, Domain, Page }

/// <param name="Subtitle">For a page: an excerpt of the body around the match.</param>
public sealed record SearchResultDto(SearchKind Kind, string Id, string Title, string? Subtitle);
