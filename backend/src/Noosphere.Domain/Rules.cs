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

    private static bool Filled(string? text) => !string.IsNullOrWhiteSpace(text);

    private static double Share(params bool[] fields) => Math.Round(fields.Count(f => f) / (double)fields.Length, 2);
}
