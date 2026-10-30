using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Noosphere.Domain;

/// <summary>Computed values of the landscape: completeness, sensitivity, ids and text matching.</summary>
public static partial class Rules
{
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 200;
    public const int MaxTextLength = 20_000;
    public const int MaxShortTextLength = 500;

    public static readonly IReadOnlyList<string> Languages = ["en", "fr"];

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    /// <summary>A valid id is a kebab-case slug: lower-case letters and digits separated by single hyphens.</summary>
    public static bool IsValidId(string? id) => id is { Length: > 0 and <= MaxIdLength } && SlugPattern().IsMatch(id);

    /// <summary>Slug built from a name: "Réseau Auto+" becomes "reseau-auto".</summary>
    public static string Slugify(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in RemoveDiacritics(name).ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9')) builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxIdLength) slug = slug[..MaxIdLength].TrimEnd('-');
        return slug.Length == 0 ? "item" : slug;
    }

    /// <summary>Accent- and case-insensitive form used for search and filters.</summary>
    public static string Fold(string? text) => RemoveDiacritics(text ?? "").ToLowerInvariant();

    private static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Share of the recommended fields of an application that are filled: team, tech, description, at least one flow and
    /// lifecycle (always set), rounded to two decimals.
    /// </summary>
    public static double Completeness(Application app, bool hasFlow) =>
        Share(app.TeamId is not null, Filled(app.Tech), Filled(app.Description), hasFlow, true);

    /// <summary>Share of the recommended fields of a flow that are filled: schedule, data objects, owner and description.</summary>
    public static double Completeness(Flow flow) =>
        Share(Filled(flow.Schedule), flow.DataObjectIds.Count > 0, flow.OwnerTeamId is not null, Filled(flow.Description));

    /// <summary>Most sensitive of the carried data objects; <see cref="Sensitivity.Internal"/> when the flow carries none.</summary>
    public static Sensitivity FlowSensitivity(IEnumerable<Sensitivity> carried)
    {
        var list = carried.ToList();
        return list.Count == 0 ? Sensitivity.Internal : list.Max();
    }

    public const int MinReasonLength = 3;
    public const int MaxTagLength = 50;
    public const int MaxTags = 20;
    public const int MaxBodyLength = 200_000;

    /// <summary>Most entries of an access list (teams plus users).</summary>
    public const int MaxAccessEntries = 200;

    /// <summary>Shown instead of protected notes to those who may not read them (history, compare, proposals).</summary>
    public const string ProtectedMask = "••• protected";

    /// <summary>Wiki link kinds, as written in <c>[[kind:id]]</c>.</summary>
    public static readonly IReadOnlyDictionary<string, RecordKind> LinkKinds = new Dictionary<string, RecordKind>(StringComparer.Ordinal)
    {
        ["application"] = RecordKind.Application,
        ["flow"] = RecordKind.Flow,
        ["dataObject"] = RecordKind.DataObject,
        ["page"] = RecordKind.Page,
        ["domain"] = RecordKind.Domain,
        ["team"] = RecordKind.Team,
    };

    [GeneratedRegex(@"\[\[(application|flow|dataObject|page|domain|team):([a-z0-9]+(?:-[a-z0-9]+)*)(?:\|([^\]\r\n]*))?\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex WikiLinkPattern();

    /// <summary>A wiki link found in a page body: <c>[[application:billing|the billing app]]</c>.</summary>
    public sealed record WikiLink(RecordKind Kind, string Id, string? Label)
    {
        /// <summary><c>kind:id</c>, as stored on the page.</summary>
        public string Key => LinkKey(Kind, Id);
    }

    public static string KindName(RecordKind kind) => kind switch
    {
        RecordKind.DataObject => "dataObject",
        _ => kind.ToString().ToLowerInvariant(),
    };

    public static string LinkKey(RecordKind kind, string id) => $"{KindName(kind)}:{id}";

    [GeneratedRegex(@"```[\s\S]*?(?:```|\z)|`[^`\n]*`", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    /// <summary>
    /// Wiki links of a Markdown body, in order of first appearance, without duplicates (by target). Links written in code
    /// (fenced blocks and inline code) are examples, not links: pages show them as text.
    /// </summary>
    public static List<WikiLink> WikiLinks(string? body)
    {
        var links = new List<WikiLink>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in WikiLinkPattern().Matches(CodePattern().Replace(body ?? "", " ")))
        {
            var label = m.Groups[3].Success && m.Groups[3].Value.Trim().Length > 0 ? m.Groups[3].Value.Trim() : null;
            var link = new WikiLink(LinkKinds[m.Groups[1].Value], m.Groups[2].Value, label);
            if (seen.Add(link.Key)) links.Add(link);
        }
        return links;
    }

    private static bool Filled(string? text) => !string.IsNullOrWhiteSpace(text);

    private static double Share(params bool[] fields) => Math.Round(fields.Count(f => f) / (double)fields.Length, 2);
}
