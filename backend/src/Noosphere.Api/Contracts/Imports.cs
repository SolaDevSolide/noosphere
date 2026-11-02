using Noosphere.Domain;

namespace Noosphere.Api.Contracts;

/// <summary>Where an entry, a warning or an error comes from.</summary>
/// <param name="File">File name, with its path inside a zip archive: <c>adr.zip/0001-use-postgres.md</c>.</param>
/// <param name="Line">Line in the file (1-based), when known.</param>
/// <param name="Place">Place in the document: <c>documents[1]</c>, <c>paths./invoices.get</c>, <c>Applications!A4</c>.</param>
public sealed record ImportSourceDto(string File, int? Line, string? Place);

/// <summary>A file read by an import.</summary>
/// <param name="Format"><c>backstage</c>, <c>openapi</c>, <c>asyncapi</c>, <c>adr</c>, <c>mermaid</c> or <c>excel</c>; null when the file was not read.</param>
/// <param name="Sha256">SHA-256 of the file content, in hexadecimal.</param>
public sealed record ImportFileDto(string Name, string? Format, long Size, string Sha256, int Entries);

/// <summary>An existing record with a close name, never merged automatically.</summary>
public sealed record ImportPossibleMatchDto(string Id, string Name);

/// <summary>One record of the plan.</summary>
/// <param name="Action"><c>create</c>, <c>update</c> or <c>unchanged</c>.</param>
/// <param name="Kind">Kind of record.</param>
/// <param name="Id">Id of the record: the existing one for an update, the new one for a creation.</param>
/// <param name="MatchedBy">How an existing record was found: <c>identifier</c>, <c>name</c>, or <c>tag</c> (the top-level page tagged <c>adr</c>, for decision records); null for a creation.</param>
/// <param name="Changes">Field-level diff: every field set on a creation, the changed fields on an update.</param>
/// <param name="PossibleMatches">Existing records whose name is close: check them before applying (never merged).</param>
/// <param name="HasErrors">The entry has errors (see the plan's errors): the plan cannot be applied.</param>
public sealed record ImportEntryDto(int Index, string Action, RecordKind Kind, string Id, string Name, string? MatchedBy,
    IReadOnlyList<DiffEntryDto> Changes, IReadOnlyList<ImportPossibleMatchDto> PossibleMatches, ImportSourceDto Source, bool HasErrors);

/// <summary>A warning (something left out or guessed) or an error (the plan cannot be applied).</summary>
/// <param name="Entry">Index of the entry concerned, when there is one.</param>
/// <param name="Field">Field concerned, when there is one.</param>
public sealed record ImportMessageDto(string Message, ImportSourceDto Source, int? Entry, string? Field);

public sealed record ImportCountsDto(int Create, int Update, int Unchanged, int Warnings, int Errors);

/// <summary>An import plan: the preview of a dry run, applied later by id.</summary>
/// <param name="Status"><c>planned</c>, <c>applied</c>, <c>rejected</c> or <c>expired</c>.</param>
/// <param name="Outcome">Why applying was refused, for a rejected plan.</param>
/// <param name="CanApply">The plan is planned, not expired and without errors.</param>
public sealed record ImportPlanDto(Guid Id, ImportStatus Status, string Username, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? ClosedAt, string? Outcome, IReadOnlyList<string> Formats, IReadOnlyList<ImportFileDto> Files, ImportCountsDto Counts,
    IReadOnlyList<ImportEntryDto> Entries, IReadOnlyList<ImportMessageDto> Warnings, IReadOnlyList<ImportMessageDto> Errors, bool CanApply);

/// <summary>An import in the list of imports (without its entries).</summary>
public sealed record ImportSummaryDto(Guid Id, ImportStatus Status, string Username, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? ClosedAt, string? Outcome, IReadOnlyList<string> Formats, ImportCountsDto Counts);
