using System.Globalization;
using System.Text.Json;
using Noosphere.Api.Contracts;
using Noosphere.Domain;

namespace Noosphere.Api.Importing;

/// <summary>
/// Excel workbooks (.xlsx) laid out as the template (<c>GET /api/imports/template.xlsx</c>): one sheet per kind
/// (Domains, Teams, Applications, Data objects, Flows), a header row with the field names of the exchange format, one
/// record per row. References are written as ids or names. Empty cells leave the field as it is.
/// </summary>
public static class ExcelReader
{
    /// <summary>One column of a sheet: its header, the field it fills, a reference kind or the values it accepts.</summary>
    public sealed record Column(string Header, string Field, string Help, RecordKind? Ref = null, IReadOnlyList<string>? Values = null, bool List = false, bool Number = false);

    public sealed record SheetSpec(string Name, RecordKind Kind, IReadOnlyList<Column> Columns);

    private static List<string> Names<T>() where T : struct, Enum => [.. Enum.GetValues<T>().Select(v => JsonNamingPolicy.CamelCase.ConvertName(v.ToString()))];

    private static readonly Column Id = new("id", "id", "Optional stable id (kebab-case). Matches an existing record first; derived from the name for a new one.");
    private static readonly Column Name = new("name", "name", "Required.");
    private static readonly Column Description = new("description", "description", "Markdown.");

    public static readonly IReadOnlyList<SheetSpec> Sheets =
    [
        new("Domains", RecordKind.Domain, [Id, Name, Description, new("order", "order", "Whole number: position in lists.", Number: true)]),
        new("Teams", RecordKind.Team, [Id, Name, new("contact", "contact", "E-mail address or chat channel."), Description]),
        new("Applications", RecordKind.Application,
        [
            Id, Name,
            new("domainId", "domainId", "Domain id or name. Required for a new application (or the default domain of the import).", RecordKind.Domain),
            new("teamId", "teamId", "Owning team id or name.", RecordKind.Team),
            new("kind", "kind", "Kind of application.", Values: Names<ApplicationKind>()),
            new("tech", "tech", "Technologies, free text."),
            new("lifecycle", "lifecycle", "Lifecycle.", Values: Names<Lifecycle>()),
            Description,
        ]),
        new("Data objects", RecordKind.DataObject,
        [
            Id, Name,
            new("sensitivity", "sensitivity", "Sensitivity.", Values: Names<Sensitivity>()),
            new("sourceApplicationId", "sourceApplicationId", "Application holding the source of truth: id or name.", RecordKind.Application),
            Description,
        ]),
        new("Flows", RecordKind.Flow,
        [
            Id, Name,
            new("fromId", "fromId", "Required: sending application, id or name.", RecordKind.Application),
            new("toId", "toId", "Required: receiving application, id or name.", RecordKind.Application),
            new("type", "type", "Type of flow.", Values: Names<FlowType>()),
            new("schedule", "schedule", "Free text, e.g. Every night 02:00."),
            new("dataObjectIds", "dataObjectIds", "Data objects carried: ids or names, separated by commas.", RecordKind.DataObject, List: true),
            new("ownerTeamId", "ownerTeamId", "Owning team id or name.", RecordKind.Team),
            new("status", "status", "Status.", Values: Names<FlowStatus>()),
            new("volume", "volume", "Free text, e.g. 15,000 lines."),
            Description,
        ]),
    ];

    /// <summary>Other spellings accepted in the header row.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["domain"] = "domainId", ["team"] = "teamId", ["owner"] = "teamId", ["technology"] = "tech", ["source"] = "sourceApplicationId",
        ["sourceapplication"] = "sourceApplicationId", ["from"] = "fromId", ["to"] = "toId", ["dataobjects"] = "dataObjectIds",
        ["ownerteam"] = "ownerTeamId", ["title"] = "name",
    };

    public static void Read(string file, byte[] content, ReadResult result)
    {
        var sheets = Xlsx.Read(content);
        var known = 0;
        foreach (var sheet in sheets)
        {
            var spec = Sheets.FirstOrDefault(s => Normalize.Name(s.Name) == Normalize.Name(sheet.Name));
            if (spec is null)
            {
                if (Normalize.Name(sheet.Name) is not ("read me" or "readme" or "lisez moi"))
                    result.Warn(new ImportSourceDto(file, null, sheet.Name), $"Sheet '{sheet.Name}' skipped: expected {string.Join(", ", Sheets.Select(s => s.Name))}.");
                continue;
            }
            known++;
            if (sheet.Rows.Count == 0) continue;
            var (headerRow, headerCells) = sheet.Rows[0];
            var columns = new Dictionary<int, Column>();
            foreach (var (index, header) in headerCells)
            {
                var key = header.Replace(" ", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
                var field = Aliases.GetValueOrDefault(key) ?? key;
                if (spec.Columns.FirstOrDefault(c => string.Equals(c.Field, field, StringComparison.OrdinalIgnoreCase)) is { } column) columns[index] = column;
                else result.Warn(new ImportSourceDto(file, headerRow, $"{sheet.Name}!{Xlsx.ColumnName(index)}{headerRow}"), $"Column '{header}' is not read.");
            }
            if (!columns.Values.Any(c => c.Field == "name"))
            {
                result.Fail(new ImportSourceDto(file, headerRow, $"{sheet.Name}!A{headerRow}"), $"Sheet '{sheet.Name}' has no 'name' column.");
                continue;
            }
            foreach (var (row, cells) in sheet.Rows.Skip(1))
            {
                string? Cell(string field) => columns.Where(c => c.Value.Field == field).Select(c => cells.GetValueOrDefault(c.Key)).FirstOrDefault(v => v is not null);
                string Where(string field) => $"{sheet.Name}!{Xlsx.ColumnName(columns.FirstOrDefault(c => c.Value.Field == field).Key)}{row}";
                var source = new ImportSourceDto(file, row, $"{sheet.Name}!A{row}");
                var name = Cell("name");
                if (name is null)
                {
                    result.Fail(source, $"Row {row} of '{sheet.Name}' has no name.");
                    continue;
                }
                var candidate = result.Add(new Candidate(spec.Kind, Cell("id"), name, source));
                foreach (var column in spec.Columns.Where(c => c.Field is not ("id" or "name")))
                {
                    if (Cell(column.Field) is not { } value) continue;
                    if (column.Ref is { } kind)
                    {
                        candidate.Refs[column.Field] = column.List
                            ? new CandidateRef(kind, [.. value.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)])
                            : new CandidateRef(kind, [value]);
                    }
                    else if (column.Values is { } allowed)
                    {
                        var match = allowed.FirstOrDefault(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
                        if (match is null) result.Errors.Add(new($"{column.Header}: '{value}' is not one of {string.Join(", ", allowed)}.", source with { Place = Where(column.Field) }, null, column.Field));
                        else candidate.Set(column.Field, match);
                    }
                    else if (column.Number)
                    {
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n == Math.Floor(n) && Math.Abs(n) < int.MaxValue)
                            candidate.Fields[column.Field] = (int)n;
                        else result.Errors.Add(new($"{column.Header}: expected a whole number (got '{value}').", source with { Place = Where(column.Field) }, null, column.Field));
                    }
                    else candidate.Set(column.Field, value);
                }
            }
        }
        if (known == 0) result.Fail(new ImportSourceDto(file, null, null), $"No sheet of the template found: expected {string.Join(", ", Sheets.Select(s => s.Name))}. Download the template.");
    }

    /// <summary>The template: a "Read me" sheet, then one sheet per kind with its header row.</summary>
    public static byte[] Template()
    {
        List<IReadOnlyList<string>> readme =
        [
            ["Noosphere import template", ""],
            ["How to use it", "Fill one sheet per kind of record, one record per row, then import the file (dry run first). Empty cells leave a field as it is: an import never clears a field and never deletes a record."],
            ["Matching", "A row updates the existing record with the same id, or else with exactly the same name (case and accents ignored). Close names are reported as possible matches, never merged."],
            ["References", "Domains, teams, applications and data objects are referred to by id or by name, in the file or already in Noosphere."],
            ["", ""],
            ["Sheet / column", "Meaning"],
        ];
        foreach (var sheet in Sheets)
            foreach (var column in sheet.Columns)
                readme.Add([$"{sheet.Name} / {column.Header}", column.Values is null ? column.Help : $"{column.Help} One of: {string.Join(", ", column.Values)}."]);
        var sheets = new List<Xlsx.SheetToWrite> { new("Read me", readme, [28, 110], Header: false) };
        sheets.AddRange(Sheets.Select(s => new Xlsx.SheetToWrite(s.Name, [s.Columns.Select(c => c.Header).ToList()],
            [.. s.Columns.Select(c => c.Field is "description" ? 50 : c.Field is "name" ? 28 : 18)])));
        return Xlsx.Write(sheets);
    }

    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
