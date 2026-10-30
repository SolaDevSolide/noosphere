using Noosphere.Domain;

namespace Noosphere.Api.Contracts;

// Inputs: PUT replaces the whole record (a missing optional field is cleared) and must carry the version that was read;
// a stale version returns 409. On POST, a missing id is derived from the name ("Customer portal" -> "customer-portal").

public record DomainDto(string Id, string Name, string? Description, int Order, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record DomainInput(string? Id, string? Name, string? Description, int? Order, int? Version);

public record TeamDto(string Id, string Name, string? Description, string? Contact, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record TeamInput(string? Id, string? Name, string? Description, string? Contact, int? Version);

/// <summary>
/// Access list of an application or page. On a restricted record, its teams and users see it; on any application, they
/// read its protected notes; guests named here see the record in full.
/// </summary>
public sealed record AccessListDto(IReadOnlyList<string> TeamIds, IReadOnlyList<string> Usernames);

/// <summary>Access list to save. Leaving <c>access</c> out of a body keeps the current list; send empty arrays to clear it.</summary>
public sealed record AccessListInput(List<string>? TeamIds, List<string>? Usernames);

public enum SensitivityReasonKind { DataObject, Flow }

/// <summary>Why an application has its sensitivity: a data object it is the source of, or a flow it sends or receives.</summary>
public sealed record SensitivityReasonDto(SensitivityReasonKind Kind, string Id, string Name, Sensitivity Sensitivity);

/// <summary>An application the reader may see in full. <c>restricted</c> is always false (true on <see cref="RestrictedApplicationDto"/>).</summary>
/// <param name="SourceOf">Ids of the data objects whose source is this application (derived from <c>DataObject.sourceApplicationId</c>).</param>
/// <param name="Completeness">Share of the recommended fields that are filled, 0 to 1.</param>
/// <param name="Sensitivity">Most sensitive of the data objects it is the source of and of the flows it sends or receives (internal when none).</param>
/// <param name="SensitivityReasons">The five most sensitive of those data objects and flows, highest first.</param>
public record ApplicationDto(
    string Id, bool Restricted, string Name, string DomainId, string? TeamId, ApplicationKind Kind, string? Tech, string? Description,
    Lifecycle Lifecycle, Visibility Visibility, AccessListDto Access, IReadOnlyList<string> SourceOf, double Completeness,
    Sensitivity Sensitivity, IReadOnlyList<SensitivityReasonDto> SensitivityReasons,
    int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>A restricted application, as seen by a reader who may not see it.</summary>
public sealed record RestrictedApplicationDto(string Id, bool Restricted, string DomainId);

/// <summary>An application not shared with a guest: its name and place in the landscape only. <c>shared</c> is always false.</summary>
public sealed record GuestApplicationDto(string Id, bool Restricted, bool Shared, string Name, string DomainId, ApplicationKind Kind);

/// <param name="Access">Optional: left out, the current access list is kept.</param>
/// <param name="ProtectedNotes">Markdown, at most 20,000 characters. Optional: left out (or sent masked), the current notes are kept; an empty text clears them.</param>
public sealed record ApplicationInput(
    string? Id, string? Name, string? DomainId, string? TeamId, ApplicationKind? Kind, string? Tech, string? Description,
    Lifecycle? Lifecycle, Visibility? Visibility, int? Version, AccessListInput? Access = null, string? ProtectedNotes = null);

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

/// <summary>A flow from or to a restricted application the reader may not see.</summary>
public sealed record RestrictedFlowDto(string Id, bool Restricted, string FromId, string ToId, FlowType Type);

/// <summary>A flow none of whose ends is shared with a guest: its name and ends only. <c>shared</c> is always false.</summary>
public sealed record GuestFlowDto(string Id, bool Restricted, bool Shared, string Name, string FromId, string ToId, FlowType Type);

/// <param name="ProtectedNotes">Markdown, at most 20,000 characters. Optional: left out (or sent masked), the current notes are kept; an empty text clears them.</param>
public sealed record FlowInput(
    string? Id, string? Name, string? FromId, string? ToId, FlowType? Type, string? Schedule, List<string>? DataObjectIds,
    string? OwnerTeamId, FlowStatus? Status, string? Volume, string? Description, PlannedChangeDto? PlannedChange, int? Version,
    string? ProtectedNotes = null);

/// <summary>Counts of what the landscape response holds.</summary>
public sealed record LandscapeStatsDto(int Applications, int Flows, int Domains);

/// <summary>
/// Everything the explorer needs, in one call. <c>applications</c> holds <see cref="ApplicationDto"/>,
/// <see cref="RestrictedApplicationDto"/> or (for guests) <see cref="GuestApplicationDto"/> items, <c>flows</c> holds
/// <see cref="FlowDto"/>, <see cref="RestrictedFlowDto"/> or <see cref="GuestFlowDto"/> items (discriminated by
/// <c>restricted</c>, then <c>shared</c>).
/// </summary>
public sealed record LandscapeDto(
    IReadOnlyList<DomainDto> Domains, IReadOnlyList<TeamDto> Teams, IReadOnlyList<object> Applications,
    IReadOnlyList<DataObjectDto> DataObjects, IReadOnlyList<object> Flows, LandscapeStatsDto Stats, DateTimeOffset GeneratedAt);

public enum SearchKind { Application, Flow, DataObject, Team, Domain, Page }

/// <param name="Subtitle">For a page: an excerpt of the body around the match. For an application: domain, team and sensitivity.</param>
/// <param name="Sensitivity">For applications, flows and data objects the reader sees in full.</param>
public sealed record SearchResultDto(SearchKind Kind, string Id, string Title, string? Subtitle, Sensitivity? Sensitivity = null);
