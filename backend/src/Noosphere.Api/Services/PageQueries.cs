using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>Owner, permissions, attached pages and mentions of a record.</summary>
public static class RecordContext
{
    public static async Task<RecordContextDto> Of(NoosphereDbContext db, Access access, RecordType type, VersionedEntity e, CancellationToken cancel = default)
    {
        var kind = type.Kind;
        var key = Rules.LinkKey(kind, e.Id);
        var attached = await db.Pages.AsNoTracking().Where(p => p.AttachedKind == kind && p.AttachedId == e.Id)
            .Select(p => new { p.Id, p.Title }).ToListAsync(cancel);
        var mentions = await db.Pages.AsNoTracking().Where(p => p.Links.Contains(key))
            .Select(p => new { p.Id, p.Title }).ToListAsync(cancel);
        // Pages inherit nothing from the record: each one is listed when the reader may see it.
        return new RecordContextDto(
            access.Facts.Owner(kind, e),
            access.Permissions(type, e),
            [.. attached.Where(p => !access.Hidden(RecordKind.Page, p.Id)).OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).Select(p => new PageRefDto(p.Id, p.Title))],
            [.. mentions.Where(p => !access.Hidden(RecordKind.Page, p.Id)).OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).Select(p => new PageRefDto(p.Id, p.Title))]);
    }
}

/// <summary>Read models of documentation pages.</summary>
public static partial class PageQueries
{
    public static AttachedToDto? Attached(Page p) => p.AttachedKind is { } k && p.AttachedId is { } id ? new AttachedToDto(k, id) : null;

    public static PageDto Dto(Page p) => new(p.Id, false, p.Title, p.Body, p.ParentId, p.Order, Attached(p), p.OwnerTeamId, p.Tags,
        p.Visibility, Snapshot.AccessDto(p), p.Version, p.CreatedAt, p.UpdatedAt);

    public static RestrictedPageDto Stub(Page p) => new(p.Id, true, p.ParentId, p.Order);

    public static GuestPageDto GuestStub(Page p) => new(p.Id, false, false, p.Title, p.ParentId, p.Order);

    /// <summary>The stub the reader gets instead of the page, or null when they see it in full.</summary>
    public static object? StubFor(Page p, Access access) => access.Sight(RecordKind.Page, p) switch
    {
        Sight.Hidden => Stub(p),
        Sight.Outline => GuestStub(p),
        _ => null,
    };

    /// <summary>Links of a body resolved against the records, as the reader may see them.</summary>
    public static List<PageLinkDto> Links(string body, Access access) =>
        [.. Rules.WikiLinks(body).Select(l =>
        {
            var exists = access.Facts.Exists(l.Kind, l.Id);
            var hidden = exists && access.Hidden(l.Kind, l.Id);
            return new PageLinkDto(l.Kind, l.Id, exists && !hidden ? access.Facts.Title(l.Kind, l.Id) : null, exists, hidden, l.Label);
        })];

    public static async Task<PageDetailDto> Detail(NoosphereDbContext db, Page p, Access access, CancellationToken cancel = default)
    {
        var all = await db.Pages.AsNoTracking().Select(x => new { x.Id, x.Title, x.ParentId, x.Order, x.Links }).ToListAsync(cancel);
        var byId = all.ToDictionary(x => x.Id);
        bool Visible(string id) => !access.Hidden(RecordKind.Page, id);
        var key = Rules.LinkKey(RecordKind.Page, p.Id);
        var backlinks = all.Where(x => x.Id != p.Id && x.Links.Contains(key) && Visible(x.Id))
            .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).Select(x => new PageRefDto(x.Id, x.Title)).ToList();
        var ancestors = new List<PageRefDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { p.Id };
        for (var at = p.ParentId; at is not null && seen.Add(at) && byId.TryGetValue(at, out var parent); at = parent.ParentId)
            ancestors.Insert(0, new PageRefDto(parent.Id, Visible(parent.Id) ? parent.Title : ""));
        var children = all.Where(x => x.ParentId == p.Id && Visible(x.Id))
            .OrderBy(x => x.Order).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).Select(x => new PageRefDto(x.Id, x.Title)).ToList();
        var attachedTitle = p.AttachedKind is { } k && p.AttachedId is { } id && !access.Hidden(k, id) ? access.Facts.Title(k, id) : null;
        var attachedRestricted = p.AttachedKind is { } ak && p.AttachedId is { } aid && access.Facts.Restricted(ak, aid);
        return new PageDetailDto(Dto(p), Links(p.Body, access), backlinks, ancestors, children, attachedTitle, attachedRestricted,
            access.Facts.Owner(RecordKind.Page, p), access.Permissions(Records.Page, p));
    }

    public static PageSummaryDto Summary(Page p, RecordFacts facts) => new(p.Id, false, p.Title, p.ParentId, p.Order, Attached(p), p.OwnerTeamId, p.Tags,
        p.Visibility, Rules.WikiLinks(p.Body).Count(l => !facts.Exists(l.Kind, l.Id)), p.Version, p.UpdatedAt);

    /// <summary>The page tree: siblings by order, then title. Pages whose parent is missing are shown at the top level.</summary>
    public static List<PageTreeNodeDto> Tree(IReadOnlyList<Page> pages, Access access)
    {
        var ids = pages.Select(p => p.Id).ToHashSet();
        var children = pages.ToLookup(p => p.ParentId is { } parent && ids.Contains(parent) ? parent : null);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        List<PageTreeNodeDto> Level(string? parent) =>
            [.. children[parent].OrderBy(p => p.Order).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .Where(p => visited.Add(p.Id))
                .Select(p => access.Hidden(RecordKind.Page, p.Id)
                    ? new PageTreeNodeDto(p.Id, true, null, p.Order, null, Level(p.Id))
                    : new PageTreeNodeDto(p.Id, false, p.Title, p.Order, Attached(p), Level(p.Id)))];
        return Level(null);
    }

    /// <summary>The page as Markdown with a YAML front matter (for tools and agents).</summary>
    public static string Raw(Page p, RecordFacts facts)
    {
        static string Quote(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal) + "\"";
        var b = new StringBuilder("---\n");
        b.Append("id: ").Append(p.Id).Append('\n');
        b.Append("title: ").Append(Quote(p.Title)).Append('\n');
        if (p.ParentId is not null) b.Append("parent: ").Append(p.ParentId).Append('\n');
        if (Attached(p) is { } a) b.Append("attachedTo: ").Append(Rules.LinkKey(a.Kind, a.Id)).Append('\n');
        if (facts.Owner(RecordKind.Page, p) is { } owner) b.Append("owner: ").Append(owner).Append('\n');
        if (p.Tags.Count > 0) b.Append("tags: [").Append(string.Join(", ", p.Tags.Select(Quote))).Append("]\n");
        b.Append("visibility: ").Append(p.Visibility.ToString().ToLowerInvariant()).Append('\n');
        b.Append("version: ").Append(p.Version.ToString(CultureInfo.InvariantCulture)).Append('\n');
        b.Append("updatedAt: ").Append(p.UpdatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append('\n');
        var links = Rules.WikiLinks(p.Body);
        if (links.Count > 0)
        {
            b.Append("links:\n");
            foreach (var l in links) b.Append("  - ").Append(l.Key).Append(facts.Exists(l.Kind, l.Id) ? "" : "  # broken").Append('\n');
        }
        b.Append("---\n\n").Append(p.Body);
        if (!p.Body.EndsWith('\n')) b.Append('\n');
        return b.ToString();
    }

    public const int ExcerptLength = 140;

    /// <summary>Part of a body around the first match of <paramref name="folded"/>, on one line; the start of the body when none.</summary>
    public static string Excerpt(string body, string? folded)
    {
        var text = PlainText(body);
        var at = folded is null ? -1 : Rules.Fold(text).IndexOf(folded, StringComparison.Ordinal);
        var start = at < 0 ? 0 : Math.Max(0, at - ExcerptLength / 3);
        // Start and end on word boundaries.
        if (start > 0 && text.LastIndexOf(' ', start) is var space and >= 0) start = space + 1;
        var end = Math.Min(text.Length, start + ExcerptLength);
        if (end < text.Length && text.LastIndexOf(' ', end) is var last && last > start) end = last;
        return (start > 0 ? "…" : "") + text[start..end].Trim() + (end < text.Length ? "…" : "");
    }

    /// <summary>A Markdown body as one line of text: wiki links become their label (or id), markup characters go.</summary>
    public static string PlainText(string body)
    {
        var text = WikiLinkText().Replace(body, m => m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.Trim() : m.Groups[1].Value);
        text = MarkdownLink().Replace(text, "$1");
        text = Markup().Replace(text, " ");
        return string.Join(' ', text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\[\[[a-zA-Z]+:([a-z0-9-]+)(?:\|([^\]\r\n]*))?\]\]")]
    private static partial System.Text.RegularExpressions.Regex WikiLinkText();

    [System.Text.RegularExpressions.GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial System.Text.RegularExpressions.Regex MarkdownLink();

    [System.Text.RegularExpressions.GeneratedRegex(@"(^|\s)#{1,6}\s|[*_`>|]|^\s*[-+]\s|(?<=\s)-{3,}(?=\s)", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex Markup();
}
