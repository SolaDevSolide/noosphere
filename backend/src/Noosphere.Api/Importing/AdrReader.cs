using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Noosphere.Api.Contracts;
using Noosphere.Domain;
using static Noosphere.Api.Importing.ReaderKit;

namespace Noosphere.Api.Importing;

/// <summary>
/// Architecture decision records in Markdown, MADR (front matter or a <c>* Status:</c> list) or Nygard style (a
/// <c>## Status</c> section, a <c>Date:</c> line). Each becomes a page "ADR 0001: title" tagged <c>adr</c> and its
/// status, under the <c>parentPageId</c> option, or else the "Decision records" page (found by id or title, or else the
/// only top-level page tagged <c>adr</c>; created otherwise), with a "Records mentioned" section of
/// wiki links to the applications it names. It is attached to the application given by the front matter
/// (<c>application:</c>) or the <c>applicationId</c> option, or else to the only application it mentions.
/// </summary>
public static partial class AdrReader
{
    public const string ParentKey = "decision-records";
    public const string ParentTitle = "Decision records";

    [GeneratedRegex(@"^\s*(?:[*-]\s*)?\**(?<field>Status|Date)\**\s*:\**\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FieldLine();

    [GeneratedRegex(@"^##\s+Status\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex StatusSection();

    [GeneratedRegex(@"^(?:adr[-_ ]?)?(?<n>\d{1,5})(?:[-_. ]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Number();

    [GeneratedRegex(@"^(?:adr[-_ ]?\d+\s*[:.-]\s*|\d+\.\s+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitlePrefix();

    public static void Read(string file, string text, ImportOptions options, ReadResult result)
    {
        var source = new ImportSourceDto(file, 1, null);
        var lines = text.ReplaceLineEndings("\n").Split('\n').ToList();
        JsonObject? front = null;
        var bodyStart = 0;
        if (lines.Count > 0 && lines[0].Trim() == "---")
        {
            var end = lines.FindIndex(1, l => l.Trim() is "---" or "...");
            if (end < 0) throw new ImportReadException("The front matter (---) is not closed.", 1);
            front = ParseYaml(string.Join('\n', lines.Skip(1).Take(end - 1))).Documents.FirstOrDefault()?.Root as JsonObject;
            bodyStart = end + 1;
        }
        var titleIndex = lines.FindIndex(bodyStart, l => l.StartsWith("# ", StringComparison.Ordinal));
        if (titleIndex < 0)
        {
            result.Fail(source, "No title: an ADR starts with a '# Title' heading.");
            return;
        }
        var heading = lines[titleIndex][2..].Trim();
        var body = string.Join('\n', lines.Skip(bodyStart).Where((_, i) => i + bodyStart != titleIndex)).Trim();

        var status = Str(front?["status"]) ?? Field(body, "status") ?? Section(body);
        var date = Str(front?["date"]) ?? Field(body, "date");
        var stem = Path.GetFileNameWithoutExtension(file.Split('/')[^1]);
        var number = Number().Match(stem) is { Success: true } m ? m.Groups["n"].Value
            : Number().Match(heading) is { Success: true } h ? h.Groups["n"].Value : null;
        var title = TitlePrefix().Replace(heading, "").Trim();
        if (title.Length == 0) title = heading;
        var pageTitle = number is null ? title : $"ADR {int.Parse(number, CultureInfo.InvariantCulture):D4}: {title}";

        var header = new List<string>();
        if (status is not null) header.Add($"**Status:** {status}");
        if (date is not null) header.Add($"**Date:** {date}");
        if (header.Count > 0 && !body.StartsWith("**Status:**", StringComparison.Ordinal)) body = string.Join(" · ", header) + "\n\n" + body;

        var key = stem.StartsWith("adr", StringComparison.OrdinalIgnoreCase) ? stem : $"adr-{stem}";
        var page = result.Add(new Candidate(RecordKind.Page, key, pageTitle, source));
        page.Fields["body"] = body.TrimEnd() + "\n";
        var tags = new JsonArray("adr");
        if (StatusTag(status) is { } tag) tags.Add(tag);
        page.Fields["tags"] = tags;
        page.Additive.Add("tags");
        if (number is not null) page.Fields["order"] = int.Parse(number, CultureInfo.InvariantCulture);
        page.LinkMentions = true;
        page.Ref("parentId", RecordKind.Page, options.ParentPageId ?? ParentKey);
        if ((Str(front?["application"]) ?? options.ApplicationId) is { } application) page.Ref("attachedTo", RecordKind.Application, application);
        else page.AttachToSingleMention = true;

        if (options.ParentPageId is null)
        {
            var parent = result.Add(new Candidate(RecordKind.Page, ParentKey, ParentTitle, source) { NameIsDefault = true });
            parent.Defaults["body"] = "Architecture decision records (ADR): one page per decision, with its context, the options and the consequences. A decision is never deleted; a later ADR replaces it.\n";
            parent.Defaults["tags"] = new JsonArray("adr");
        }
    }

    /// <summary>A <c>Status: accepted</c> line (MADR 2 lists, Nygard's date line).</summary>
    private static string? Field(string body, string field)
    {
        foreach (Match m in FieldLine().Matches(body))
            if (string.Equals(m.Groups["field"].Value, field, StringComparison.OrdinalIgnoreCase)) return m.Groups["value"].Value.Trim('*', ' ');
        return null;
    }

    /// <summary>Nygard: the first line of the <c>## Status</c> section.</summary>
    private static string? Section(string body)
    {
        var m = StatusSection().Match(body);
        if (!m.Success) return null;
        return body[(m.Index + m.Length)..].Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
    }

    /// <summary>First word of the status, as a tag: "Superseded by ADR 5" gives <c>superseded</c>.</summary>
    private static string? StatusTag(string? status)
    {
        var word = new string((status ?? "").Trim().TakeWhile(char.IsLetter).ToArray()).ToLowerInvariant();
        return word.Length is > 0 and <= 30 ? word : null;
    }
}
