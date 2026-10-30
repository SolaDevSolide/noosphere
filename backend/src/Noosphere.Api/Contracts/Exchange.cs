using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Noosphere.Api.Contracts;

/// <summary>Exchange format v1 (docs/format/noosphere-v1.schema.json).</summary>
/// <param name="FormatVersion">Always <c>noosphere/v1</c>.</param>
/// <param name="Mode">Always <c>full</c>: the file describes the whole landscape it was exported from.</param>
public sealed record ExchangeEnvelopeDto(string FormatVersion, ExchangeSourceDto Source, string Mode, IReadOnlyList<ExchangeItemDto> Items);

public sealed record ExchangeSourceDto(string Name, string? Url, DateTimeOffset ExportedAt);

/// <param name="Kind"><c>Domain</c>, <c>Team</c>, <c>Application</c>, <c>DataObject</c>, <c>Flow</c> or <c>Page</c>.</param>
/// <param name="Spec">Kind-specific fields (see the JSON Schema).</param>
public sealed record ExchangeItemDto(string Kind, string Id, ExchangeMetadataDto Metadata, JsonObject Spec);

public sealed record ExchangeMetadataDto(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description);

/// <summary>Outcome of an import (or of its dry run): counts of items, or the errors that stopped it (nothing written).</summary>
/// <param name="Applied">True when the changes were written (not a dry run, no error).</param>
public sealed record ImportResultDto(int Created, int Updated, int Unchanged, IReadOnlyList<ImportErrorDto> Errors, bool DryRun, bool Applied);

/// <param name="Index">Position of the item in <c>items</c>, or null for an error on the envelope.</param>
/// <param name="Path">JSON path of the faulty value, e.g. <c>items[3].spec.fromId</c>.</param>
public sealed record ImportErrorDto(int? Index, string? Kind, string? Id, string Path, string Message);
