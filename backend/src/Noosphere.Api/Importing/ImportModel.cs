using System.Text.Json.Nodes;
using Noosphere.Api.Contracts;
using Noosphere.Domain;

namespace Noosphere.Api.Importing;

/// <summary>Formats read by the import, by the name used in the API and the command line.</summary>
public static class ImportFormats
{
    public const string Backstage = "backstage", OpenApi = "openapi", AsyncApi = "asyncapi", Adr = "adr", Mermaid = "mermaid", Excel = "excel";

    public static readonly IReadOnlyList<string> All = [Backstage, OpenApi, AsyncApi, Adr, Mermaid, Excel];
}

/// <summary>Limits of one import. A file or an archive beyond them is refused before it is read.</summary>
public static class ImportLimits
{
    /// <summary>The whole upload (all files of the request).</summary>
    public const long MaxUploadBytes = 20 * 1024 * 1024;
    /// <summary>One file, once extracted from its archive.</summary>
    public const long MaxFileBytes = 5 * 1024 * 1024;
    /// <summary>Files after the archives are opened.</summary>
    public const int MaxFiles = 200;
    /// <summary>Everything extracted from the archives of one import.</summary>
    public const long MaxExtractedBytes = 50 * 1024 * 1024;
    /// <summary>Highest compression ratio of an archive entry (zip bombs compress far better).</summary>
    public const int MaxCompressionRatio = 100;
    /// <summary>Records in one plan.</summary>
    public const int MaxEntries = 5000;
    /// <summary>Rows read from one sheet of a workbook.</summary>
    public const int MaxRows = 10_000;
    /// <summary>Nodes of one YAML or JSON document (mappings, sequences and scalars).</summary>
    public const int MaxNodes = 200_000;
    /// <summary>Nesting depth of one YAML or JSON document.</summary>
    public const int MaxDepth = 64;
    /// <summary>Documents of one YAML file (Backstage files hold several).</summary>
    public const int MaxDocuments = 1000;
    /// <summary>Time a plan can be applied after the dry run.</summary>
    public static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(30);
}

/// <summary>Options of an import, from the query string or the command line.</summary>
/// <param name="Format">Format of every file; guessed per file when null.</param>
/// <param name="DomainId">Domain of the applications created without one (an "Imported" domain otherwise).</param>
/// <param name="TeamId">Owning team of the records created without one.</param>
/// <param name="ApplicationId">Application an OpenAPI or AsyncAPI file describes, or ADRs are attached to.</param>
/// <param name="ParentPageId">Parent page of imported decision records ("Decision records" otherwise).</param>
public sealed record ImportOptions(string? Format = null, string? DomainId = null, string? TeamId = null, string? ApplicationId = null, string? ParentPageId = null);

/// <summary>A file to import: its name (with its path inside an archive) and content.</summary>
public sealed record ImportFile(string Name, byte[] Content);

/// <summary>A reference from a candidate to another record, by key, id or name; resolved when the plan is made.</summary>
/// <param name="Tokens">Several for a list field (<c>dataObjectIds</c>).</param>
/// <param name="Soft">Only fills an empty field (never replaces a value), and is dropped silently when it does not resolve.</param>
public sealed record CandidateRef(RecordKind Kind, IReadOnlyList<string> Tokens, bool Soft = false);

/// <summary>
/// A record read from a file, before matching: what the source says about it. <see cref="Fields"/> replace the values of
/// an existing record; <see cref="Defaults"/> only fill its empty fields (sources that only mention a record, such as the
/// provider of an OpenAPI file). Fields absent from both are left as they are: an import never clears a field.
/// </summary>
public sealed class Candidate(RecordKind kind, string? key, string name, ImportSourceDto source)
{
    public RecordKind Kind { get; } = kind;
    /// <summary>Stable identifier from the source (Backstage name, C4 alias, Excel id...), as a slug; null when the source has none.</summary>
    public string? Key { get; set; } = key is null ? null : Normalize.Key(key);
    public string Name { get; set; } = name.Trim();
    public ImportSourceDto Source { get; } = source;
    public string Format { get; set; } = "";
    /// <summary>
    /// For a flow whose source has no identifier: its id is made of the ids its ends resolve to and this suffix
    /// (<c>portal-claims-api</c>), so that importing the same file again finds it.
    /// </summary>
    public string? DerivedKeySuffix { get; set; }
    /// <summary>The name is only used for a creation (the record is mentioned, not described).</summary>
    public bool NameIsDefault { get; set; }
    public JsonObject Fields { get; } = [];
    public JsonObject Defaults { get; } = [];
    public Dictionary<string, CandidateRef> Refs { get; } = [];
    /// <summary>List fields whose values are added to the current ones (tags, data objects).</summary>
    public HashSet<string> Additive { get; } = [];
    /// <summary>The page body gets a "Records mentioned" section with wiki links to the applications it names.</summary>
    public bool LinkMentions { get; set; }
    /// <summary>Attach the page to the one application its body mentions, when no attachment is given.</summary>
    public bool AttachToSingleMention { get; set; }
    public List<(string Field, string Message)> Errors { get; } = [];

    public Candidate Set(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Fields[field] = value.Trim();
        return this;
    }

    public Candidate Default(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Defaults[field] = value.Trim();
        return this;
    }

    public Candidate Ref(string field, RecordKind kind, string? token, bool soft = false)
    {
        if (!string.IsNullOrWhiteSpace(token)) Refs[field] = new CandidateRef(kind, [token.Trim()], soft);
        return this;
    }
}

/// <summary>What a reader got from one file: candidates, and messages about the file.</summary>
public sealed class ReadResult
{
    public List<Candidate> Candidates { get; } = [];
    public List<ImportMessageDto> Warnings { get; } = [];
    public List<ImportMessageDto> Errors { get; } = [];

    public void Warn(ImportSourceDto source, string message) => Warnings.Add(new(message, source, null, null));

    public void Fail(ImportSourceDto source, string message) => Errors.Add(new(message, source, null, null));

    public Candidate Add(Candidate c)
    {
        Candidates.Add(c);
        return c;
    }
}

/// <summary>Raised by a reader when a file cannot be read at all.</summary>
public sealed class ImportReadException(string message, int? line = null) : Exception(message)
{
    public int? Line { get; } = line;
}

public static class Normalize
{
    /// <summary>A stable identifier as an id: already a slug, or slugified.</summary>
    public static string Key(string key) => Rules.IsValidId(key.Trim()) ? key.Trim() : Rules.Slugify(key);

    /// <summary>
    /// Form of a name used to match records: accents and case folded, every run of other characters than letters and
    /// digits made one space. "Réseau Auto+" and "reseau  auto" are the same name.
    /// </summary>
    public static string Name(string? name)
    {
        var folded = Rules.Fold(name);
        var builder = new System.Text.StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
        }
        return builder.ToString().Trim();
    }

    /// <summary>
    /// Two different normalized names are close when one is a prefix of the other ("billing" and "billing service"), or
    /// when few edits separate them (at most a fifth of the longer one).
    /// </summary>
    public static bool Close(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0 || a == b) return false;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        if (shorter.Length >= 4 && (longer.StartsWith(shorter + " ", StringComparison.Ordinal) || longer.EndsWith(" " + shorter, StringComparison.Ordinal))) return true;
        if (longer.Length - shorter.Length > longer.Length / 5) return false;
        return Distance(a, b) <= Math.Max(1, longer.Length / 5);
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
