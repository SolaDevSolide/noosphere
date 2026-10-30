using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Agents;

/// <summary>
/// Records and pages as Markdown with a YAML front matter, for agents and tools: <c>/{kind}/{id}.md</c>, the
/// <c>get_record</c> tool, <c>/llms.txt</c> (an llmstxt.org index) and <c>/llms-full.txt</c> (every view in one file).
/// Everything is filtered by the reader's rights: hidden and outline records are left out, protected notes appear for
/// their readers only.
/// </summary>
public sealed class MarkdownViews
{
    private readonly Snapshot _s;
    private readonly IReadOnlyList<Page> _pages;
    private readonly Access _access;
    private readonly string _base;
    private readonly Dictionary<string, string> _teams;
    private readonly Dictionary<string, string> _domains;

    private MarkdownViews(Snapshot s, IReadOnlyList<Page> pages, Access access, string baseUrl)
    {
        (_s, _pages, _access, _base) = (s, pages, access, baseUrl);
        _teams = s.Teams.ToDictionary(t => t.Id, t => t.Name);
        _domains = s.Domains.ToDictionary(d => d.Id, d => d.Name);
    }

    public static async Task<MarkdownViews> Load(NoosphereDbContext db, Access access, string baseUrl, CancellationToken cancel = default) =>
        new(await Snapshot.Load(db, cancel), await db.Pages.AsNoTracking().OrderBy(p => p.Title).ToListAsync(cancel), access, baseUrl);

    public Snapshot Snapshot => _s;

    /// <summary>Path segment of a kind in the Markdown views: <c>application</c>, <c>data-object</c>...</summary>
    public static string Segment(RecordKind kind) => kind == RecordKind.DataObject ? "data-object" : Rules.KindName(kind);

    /// <summary>Kind from a path segment or a JSON kind name (<c>data-object</c>, <c>dataObject</c>, <c>applications</c>).</summary>
    public static RecordKind? ParseKind(string? text) => text?.Trim() switch
    {
        "data-object" or "data_object" => RecordKind.DataObject,
        var t => Records.Parse(t)?.Kind,
    };

    public string Url(RecordKind kind, string id) => $"{_base}/{Segment(kind)}/{id}.md";

    private string Link(RecordKind kind, string id, string title) => $"[{Escape(title)}]({Url(kind, id)})";

    /// <summary>The record exists (whatever the reader may see).</summary>
    public bool Exists(RecordKind kind, string id) => kind == RecordKind.Page ? _pages.Any(p => p.Id == id) : _access.Facts.Exists(kind, id);

    public Sight Sight(RecordKind kind, string id) => _access.Sight(kind, id);

    /// <summary>The Markdown view of a record the reader sees in full; null when it does not exist.</summary>
    public string? Record(RecordKind kind, string id) => kind switch
    {
        RecordKind.Application => _s.AppsById.TryGetValue(id, out var a) ? App(a) : null,
        RecordKind.Flow => _s.Flows.FirstOrDefault(f => f.Id == id) is { } f ? FlowView(f) : null,
        RecordKind.DataObject => _s.ObjectsById.TryGetValue(id, out var o) ? ObjectView(o) : null,
        RecordKind.Team => _s.Teams.FirstOrDefault(t => t.Id == id) is { } t ? TeamView(t) : null,
        RecordKind.Domain => _s.Domains.FirstOrDefault(d => d.Id == id) is { } d ? DomainView(d) : null,
        _ => _pages.FirstOrDefault(p => p.Id == id) is { } p ? PageQueries.Raw(p, _access.Facts) : null,
    };

    /// <summary>The record has protected notes the reader may not read.</summary>
    public bool NotesWithheld(RecordKind kind, string id) =>
        _access.Facts.HasProtectedNotes(kind, id) && !_access.ReadsProtected(kind, id);

    /// <summary>The record has protected notes the reader reads (an access-log line).</summary>
    public bool NotesShown(RecordKind kind, string id) =>
        _access.Facts.HasProtectedNotes(kind, id) && _access.ReadsProtected(kind, id);

    // ---------- Records ----------

    private string App(Application a)
    {
        var (level, _) = _s.ApplicationSensitivity(a.Id, _access);
        var b = Front(RecordKind.Application, a.Id, a.Name, a.Version, a.UpdatedAt, $"/applications/{a.Id}");
        Field(b, "domain", a.DomainId);
        Field(b, "team", a.TeamId);
        Field(b, "owner", _access.Facts.Owner(RecordKind.Application, a));
        Field(b, "applicationKind", Lower(a.Kind));
        Field(b, "tech", a.Tech);
        Field(b, "lifecycle", Lower(a.Lifecycle));
        Field(b, "visibility", Lower(a.Visibility));
        Field(b, "sensitivity", Lower(level));
        End(b, RecordKind.Application, a.Id);
        b.Append("# ").Append(a.Name).Append("\n\n");
        Line(b, $"Application of {Domain(a.DomainId)}" + (a.TeamId is null ? "" : $", owned by {Team(a.TeamId)}") + $". Lifecycle: {Lower(a.Lifecycle)}.");
        Body(b, a.Description);
        var outgoing = _s.Flows.Where(f => f.FromId == a.Id && Full(RecordKind.Flow, f.Id)).ToList();
        var incoming = _s.Flows.Where(f => f.ToId == a.Id && Full(RecordKind.Flow, f.Id)).ToList();
        if (outgoing.Count + incoming.Count > 0)
        {
            b.Append("## Flows\n\n");
            foreach (var f in outgoing) b.Append("- Sends ").Append(Link(RecordKind.Flow, f.Id, f.Name)).Append(" to ").Append(AppRef(f.ToId)).Append(FlowFacts(f)).Append('\n');
            foreach (var f in incoming) b.Append("- Receives ").Append(Link(RecordKind.Flow, f.Id, f.Name)).Append(" from ").Append(AppRef(f.FromId)).Append(FlowFacts(f)).Append('\n');
            b.Append('\n');
        }
        var sources = _s.DataObjects.Where(o => o.SourceApplicationId == a.Id).ToList();
        if (sources.Count > 0)
        {
            b.Append("## Source of\n\n");
            foreach (var o in sources) b.Append("- ").Append(Link(RecordKind.DataObject, o.Id, o.Name)).Append(" (").Append(Lower(o.Sensitivity)).Append(")\n");
            b.Append('\n');
        }
        Pages(b, RecordKind.Application, a.Id);
        Notes(b, RecordKind.Application, a, a.ProtectedNotes);
        return b.ToString();
    }

    private string FlowView(Flow f)
    {
        var b = Front(RecordKind.Flow, f.Id, f.Name, f.Version, f.UpdatedAt, $"/flows/{f.Id}");
        Field(b, "from", f.FromId);
        Field(b, "to", f.ToId);
        Field(b, "type", Lower(f.Type));
        Field(b, "status", Lower(f.Status));
        Field(b, "schedule", f.Schedule);
        Field(b, "owner", _access.Facts.Owner(RecordKind.Flow, f));
        Field(b, "sensitivity", Lower(_s.FlowSensitivity(f)));
        if (f.DataObjectIds.Count > 0) b.Append("dataObjects: [").Append(string.Join(", ", f.DataObjectIds)).Append("]\n");
        Field(b, "volume", f.Volume);
        End(b, RecordKind.Flow, f.Id);
        b.Append("# ").Append(f.Name).Append("\n\n");
        Line(b, $"From {AppRef(f.FromId)} to {AppRef(f.ToId)}, {Lower(f.Type)}, {Lower(f.Status)}"
            + (f.Schedule is null ? "" : $", {f.Schedule}") + ".");
        Body(b, f.Description);
        if (f.DataObjectIds.Count > 0)
        {
            b.Append("## Data carried\n\n");
            foreach (var id in f.DataObjectIds)
                b.Append("- ").Append(_s.ObjectsById.TryGetValue(id, out var o) ? $"{Link(RecordKind.DataObject, o.Id, o.Name)} ({Lower(o.Sensitivity)})" : id).Append('\n');
            b.Append('\n');
        }
        if (f.PlannedChangeSummary is { } change)
            b.Append("## Planned change\n\n").Append(change).Append(f.PlannedChangeDate is { } date ? $" ({date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})" : "").Append("\n\n");
        Pages(b, RecordKind.Flow, f.Id);
        Notes(b, RecordKind.Flow, f, f.ProtectedNotes);
        return b.ToString();
    }

    private string ObjectView(DataObject o)
    {
        var b = Front(RecordKind.DataObject, o.Id, o.Name, o.Version, o.UpdatedAt, $"/data-objects/{o.Id}");
        Field(b, "sensitivity", Lower(o.Sensitivity));
        Field(b, "source", o.SourceApplicationId is { } src && !Hidden(RecordKind.Application, src) ? src : null);
        Field(b, "owner", o.SourceApplicationId is { } s && !Hidden(RecordKind.Application, s) ? _access.Facts.Owner(RecordKind.DataObject, o) : null);
        End(b, RecordKind.DataObject, o.Id);
        b.Append("# ").Append(o.Name).Append("\n\n");
        Line(b, $"Data object, {Lower(o.Sensitivity)}" + (o.SourceApplicationId is { } source && !Hidden(RecordKind.Application, source) ? $". Source of truth: {AppRef(source)}." : "."));
        Body(b, o.Description);
        var flows = _s.Flows.Where(f => f.DataObjectIds.Contains(o.Id) && Full(RecordKind.Flow, f.Id)).ToList();
        if (flows.Count > 0)
        {
            b.Append("## Flows carrying it\n\n");
            foreach (var f in flows) b.Append("- ").Append(Link(RecordKind.Flow, f.Id, f.Name)).Append(": ").Append(AppRef(f.FromId)).Append(" → ").Append(AppRef(f.ToId)).Append('\n');
            b.Append('\n');
        }
        Pages(b, RecordKind.DataObject, o.Id);
        return b.ToString();
    }

    private string TeamView(Team t)
    {
        var b = Front(RecordKind.Team, t.Id, t.Name, t.Version, t.UpdatedAt, $"/teams/{t.Id}");
        Field(b, "contact", t.Contact);
        End(b, RecordKind.Team, t.Id);
        b.Append("# ").Append(t.Name).Append("\n\n");
        Body(b, t.Description);
        var apps = _s.Applications.Where(a => a.TeamId == t.Id && Full(RecordKind.Application, a.Id)).ToList();
        if (apps.Count > 0)
        {
            b.Append("## Applications owned\n\n");
            foreach (var a in apps) b.Append("- ").Append(Link(RecordKind.Application, a.Id, a.Name)).Append('\n');
            b.Append('\n');
        }
        var flows = _s.Flows.Where(f => _access.Facts.Owner(RecordKind.Flow, f) == t.Id && Full(RecordKind.Flow, f.Id)).ToList();
        if (flows.Count > 0)
        {
            b.Append("## Flows owned\n\n");
            foreach (var f in flows) b.Append("- ").Append(Link(RecordKind.Flow, f.Id, f.Name)).Append('\n');
            b.Append('\n');
        }
        Pages(b, RecordKind.Team, t.Id);
        return b.ToString();
    }

    private string DomainView(BusinessDomain d)
    {
        var b = Front(RecordKind.Domain, d.Id, d.Name, d.Version, d.UpdatedAt, $"/domains/{d.Id}");
        End(b, RecordKind.Domain, d.Id);
        b.Append("# ").Append(d.Name).Append("\n\n");
        Body(b, d.Description);
        var apps = _s.Applications.Where(a => a.DomainId == d.Id && Full(RecordKind.Application, a.Id)).ToList();
        if (apps.Count > 0)
        {
            b.Append("## Applications\n\n");
            foreach (var a in apps) b.Append("- ").Append(Link(RecordKind.Application, a.Id, a.Name)).Append(a.TeamId is null ? "" : $" ({Team(a.TeamId)})").Append('\n');
            b.Append('\n');
        }
        Pages(b, RecordKind.Domain, d.Id);
        return b.ToString();
    }

    // ---------- llms.txt ----------

    /// <summary>An llmstxt.org index: a title, a summary, then one list of links per kind.</summary>
    public string Index(string instanceName, string organisation)
    {
        var apps = _s.Applications.Where(a => Full(RecordKind.Application, a.Id)).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var flows = _s.Flows.Where(f => Full(RecordKind.Flow, f.Id)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var pages = _pages.Where(p => Full(RecordKind.Page, p.Id)).ToList();
        var b = new StringBuilder("# ").Append(instanceName).Append("\n\n");
        b.Append("> Application landscape").Append(organisation.Length > 0 ? $" of {organisation}" : "").Append(": ")
            .Append(apps.Count).Append(" applications, ").Append(flows.Count).Append(" flows between them, ")
            .Append(_s.DataObjects.Count).Append(" data objects, ").Append(_s.Teams.Count).Append(" teams and ").Append(pages.Count).Append(" documentation pages.\n\n");
        b.Append("Each link is a Markdown view with a YAML front matter. [Everything in one file](").Append(_base).Append("/llms-full.txt). ")
            .Append("Agents can also use the MCP server at ").Append(AgentSettingsStore.McpUrl(_base)).Append(" (OAuth).\n\n");
        Section(b, "Domains", _s.Domains.Select(d => (RecordKind.Domain, d.Id, d.Name, FirstLine(d.Description))));
        Section(b, "Applications", apps.Select(a => (RecordKind.Application, a.Id, a.Name,
            (string?)string.Join(" · ", new[] { Domain(a.DomainId), a.TeamId is null ? null : Team(a.TeamId), Lower(a.Lifecycle) }.Where(x => x is not null)))));
        Section(b, "Flows", flows.Select(f => (RecordKind.Flow, f.Id, f.Name, (string?)$"{AppName(f.FromId)} → {AppName(f.ToId)}, {Lower(f.Type)}")));
        Section(b, "Data objects", _s.DataObjects.Select(o => (RecordKind.DataObject, o.Id, o.Name, (string?)Lower(o.Sensitivity))));
        Section(b, "Teams", _s.Teams.Select(t => (RecordKind.Team, t.Id, t.Name, FirstLine(t.Description))));
        Section(b, "Pages", pages.Select(p => (RecordKind.Page, p.Id, p.Title, (string?)null)));
        return b.ToString();
    }

    /// <summary>The index followed by every Markdown view the reader may see.</summary>
    public string Full(string instanceName, string organisation)
    {
        var b = new StringBuilder(Index(instanceName, organisation));
        foreach (var (kind, id) in Visible())
            b.Append("\n\n<!-- ").Append(Segment(kind)).Append(':').Append(id).Append(" -->\n\n").Append(Record(kind, id));
        return b.ToString();
    }

    /// <summary>Records the reader sees in full, in the order of the index.</summary>
    public IEnumerable<(RecordKind Kind, string Id)> Visible() =>
        _s.Domains.Select(d => (RecordKind.Domain, d.Id))
            .Concat(_s.Applications.Where(a => Full(RecordKind.Application, a.Id)).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(a => (RecordKind.Application, a.Id)))
            .Concat(_s.Flows.Where(f => Full(RecordKind.Flow, f.Id)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => (RecordKind.Flow, f.Id)))
            .Concat(_s.DataObjects.Select(o => (RecordKind.DataObject, o.Id)))
            .Concat(_s.Teams.Select(t => (RecordKind.Team, t.Id)))
            .Concat(_pages.Where(p => Full(RecordKind.Page, p.Id)).Select(p => (RecordKind.Page, p.Id)));

    private void Section(StringBuilder b, string title, IEnumerable<(RecordKind Kind, string Id, string Name, string? Note)> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        b.Append("## ").Append(title).Append("\n\n");
        foreach (var (kind, id, name, note) in list)
            b.Append("- ").Append(Link(kind, id, name)).Append(string.IsNullOrWhiteSpace(note) ? "" : ": " + note).Append('\n');
        b.Append('\n');
    }

    // ---------- Helpers ----------

    private bool Full(RecordKind kind, string id) => _access.Sight(kind, id) == Services.Sight.Full;

    private bool Hidden(RecordKind kind, string id) => _access.Sight(kind, id) == Services.Sight.Hidden;

    private string AppName(string id) => _s.AppsById.TryGetValue(id, out var a) && !Hidden(RecordKind.Application, id) ? a.Name : "a restricted application";

    private string AppRef(string id) => _s.AppsById.TryGetValue(id, out var a) && Full(RecordKind.Application, id)
        ? Link(RecordKind.Application, id, a.Name)
        : AppName(id);

    private string Team(string id) => _teams.GetValueOrDefault(id) ?? id;

    private string Domain(string id) => _domains.GetValueOrDefault(id) ?? id;

    private string FlowFacts(Flow f) =>
        $" ({Lower(f.Type)}{(f.Schedule is null ? "" : ", " + f.Schedule)}"
        + (f.DataObjectIds.Count > 0 ? ", carries " + string.Join(", ", f.DataObjectIds.Select(id => _s.ObjectsById.TryGetValue(id, out var o) ? o.Name : id)) : "") + ")";

    private void Pages(StringBuilder b, RecordKind kind, string id)
    {
        var key = Rules.LinkKey(kind, id);
        var pages = _pages.Where(p => Full(RecordKind.Page, p.Id) && ((p.AttachedKind == kind && p.AttachedId == id) || p.Links.Contains(key))).ToList();
        if (pages.Count == 0) return;
        b.Append("## Pages\n\n");
        foreach (var p in pages) b.Append("- ").Append(Link(RecordKind.Page, p.Id, p.Title)).Append('\n');
        b.Append('\n');
    }

    private void Notes(StringBuilder b, RecordKind kind, VersionedEntity e, string? notes)
    {
        if (notes is null || !_access.ReadsProtected(kind, e)) return;
        b.Append("## Protected notes\n\nRead by the owning team, the access list and administrators only: do not copy them elsewhere.\n\n")
            .Append(notes.TrimEnd()).Append("\n\n");
    }

    private StringBuilder Front(RecordKind kind, string id, string name, int version, DateTimeOffset updatedAt, string uiPath)
    {
        var b = new StringBuilder("---\n");
        b.Append("kind: ").Append(Rules.KindName(kind)).Append('\n');
        b.Append("id: ").Append(id).Append('\n');
        b.Append("name: ").Append(Quote(name)).Append('\n');
        b.Append("version: ").Append(version.ToString(CultureInfo.InvariantCulture)).Append('\n');
        b.Append("updatedAt: ").Append(updatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append('\n');
        b.Append("url: ").Append(_base).Append(uiPath).Append('\n');
        return b;
    }

    private void End(StringBuilder b, RecordKind kind, string id)
    {
        if (NotesWithheld(kind, id)) b.Append("protectedNotes: withheld\n");
        b.Append("---\n\n");
    }

    private static void Field(StringBuilder b, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) b.Append(name).Append(": ").Append(value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.') ? value : Quote(value)).Append('\n');
    }

    private static void Line(StringBuilder b, string text) => b.Append(text).Append("\n\n");

    private static void Body(StringBuilder b, string? markdown)
    {
        if (!string.IsNullOrWhiteSpace(markdown)) b.Append(markdown.TrimEnd()).Append("\n\n");
    }

    private static string? FirstLine(string? text) => text?.Split('\n', 2)[0].Trim() is { Length: > 0 } line ? line : null;

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Escape(string text) => text.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal) + "\"";
}
