using Noosphere.Api.Contracts;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>Read models of the explorer: the landscape in one response, and the search box.</summary>
public static class LandscapeQueries
{
    public const int SearchLimit = 20;

    /// <summary>
    /// Filters narrow the result: application filters (domainId, teamId, q) keep the matching applications and the flows
    /// touching them; flow filters (type, status, dataObjectId) keep the matching flows and only the applications at their
    /// ends. Flows always come with both ends. Domains are those of the returned applications when a filter is set.
    /// </summary>
    public static LandscapeDto Landscape(Snapshot s, Access access, Filter filter, DateTimeOffset now)
    {
        var matching = Applications(s, access, filter).Select(a => a.Id).ToHashSet();
        var flows = s.Flows
            .Where(f => filter.MatchesFlowCriteria(f, access.Sight(RecordKind.Flow, f))
                && (!filter.HasApplicationFilter || matching.Contains(f.FromId) || matching.Contains(f.ToId)))
            .ToList();
        var ends = flows.SelectMany(f => new[] { f.FromId, f.ToId }).ToHashSet();
        var kept = !filter.Any ? s.Applications.Select(a => a.Id).ToHashSet()
            : filter.HasFlowFilter ? ends
            : [.. matching, .. ends];
        var apps = s.Applications.Where(a => kept.Contains(a.Id)).ToList();
        var domainIds = apps.Select(a => a.DomainId).ToHashSet();
        var domains = s.Domains.Where(d => !filter.Any || domainIds.Contains(d.Id)).Select(Snapshot.Dto).ToList();
        return new LandscapeDto(
            domains,
            [.. s.Teams.Select(Snapshot.Dto)],
            [.. apps.Select(a => s.View(a, access))],
            [.. s.DataObjects.Select(Snapshot.Dto)],
            [.. flows.Select(f => s.View(f, access))],
            new LandscapeStatsDto(apps.Count, flows.Count, domains.Count),
            now);
    }

    /// <summary>Applications matching the application filters, as the reader may see them.</summary>
    public static IEnumerable<Application> Applications(Snapshot s, Access access, Filter filter) =>
        s.Applications.Where(a => filter.Matches(a, access.Sight(RecordKind.Application, a), s.ApplicationSensitivity(a.Id, access).Level));

    /// <summary>
    /// Top <see cref="SearchLimit"/> matches, accent- and case-insensitive: names starting with the text first, then names
    /// containing it, then other fields (id, tech, description, protected notes for their readers). Restricted records are
    /// never matched for those who cannot see them; for a guest, records not shared with them match by name only.
    /// </summary>
    public static List<SearchResultDto> Search(Snapshot s, Access access, string? text, IReadOnlyList<Page>? pages = null)
    {
        var q = Rules.Fold(text?.Trim());
        if (q.Length == 0) return [];
        var domains = s.Domains.ToDictionary(d => d.Id, d => d.Name);
        var teams = s.Teams.ToDictionary(t => t.Id, t => t.Name);
        string AppName(string id) => s.AppsById.TryGetValue(id, out var a) && access.Sight(RecordKind.Application, a) != Sight.Hidden ? a.Name : id;

        var hits = new List<(int Score, int KindOrder, SearchResultDto Result)>();
        void Consider(SearchKind kind, string id, string name, string? subtitle, Sensitivity? sensitivity, params string?[] others)
        {
            var folded = Rules.Fold(name);
            var score = folded.StartsWith(q, StringComparison.Ordinal) ? 0
                : folded.Contains(q, StringComparison.Ordinal) ? 1
                : others.Append(id).Any(o => o is not null && Rules.Fold(o).Contains(q, StringComparison.Ordinal)) ? 2
                : -1;
            if (score >= 0) hits.Add((score, (int)kind, new SearchResultDto(kind, id, name, subtitle, sensitivity)));
        }
        static string Join(params string?[] parts) => string.Join(" · ", parts.Where(x => !string.IsNullOrEmpty(x)));
        static string Level(Sensitivity level) => level.ToString().ToLowerInvariant();

        foreach (var a in s.Applications)
        {
            var sight = access.Sight(RecordKind.Application, a);
            if (sight == Sight.Hidden) continue;
            var domain = domains.GetValueOrDefault(a.DomainId);
            if (sight == Sight.Outline)
            {
                // A guest's outline: the name and domain only.
                Consider(SearchKind.Application, a.Id, a.Name, domain, null);
                continue;
            }
            var level = s.ApplicationSensitivity(a.Id, access).Level;
            // Protected notes are searched only for those who may read them.
            Consider(SearchKind.Application, a.Id, a.Name,
                Join(domain, a.TeamId is null ? null : teams.GetValueOrDefault(a.TeamId), Level(level)), level,
                a.Tech, a.Description, access.ReadsProtected(RecordKind.Application, a) ? a.ProtectedNotes : null);
        }
        foreach (var f in s.Flows)
        {
            var sight = access.Sight(RecordKind.Flow, f);
            if (sight == Sight.Hidden) continue;
            var subtitle = $"{AppName(f.FromId)} → {AppName(f.ToId)}";
            if (sight == Sight.Outline) Consider(SearchKind.Flow, f.Id, f.Name, subtitle, null);
            else Consider(SearchKind.Flow, f.Id, f.Name, subtitle, s.FlowSensitivity(f),
                f.Description, access.ReadsProtected(RecordKind.Flow, f) ? f.ProtectedNotes : null);
        }
        foreach (var o in s.DataObjects)
            Consider(SearchKind.DataObject, o.Id, o.Name,
                o.SourceApplicationId is null ? null : AppName(o.SourceApplicationId), o.Sensitivity, o.Description);
        foreach (var t in s.Teams)
            Consider(SearchKind.Team, t.Id, t.Name, t.Contact, null, t.Description);
        foreach (var d in s.Domains)
            Consider(SearchKind.Domain, d.Id, d.Name, d.Description, null, d.Description);
        // Pages: the title, then the body; the subtitle is an excerpt of the body around the match.
        foreach (var p in pages ?? [])
        {
            var sight = access.Sight(RecordKind.Page, p);
            if (sight == Sight.Hidden) continue;
            if (sight == Sight.Outline) Consider(SearchKind.Page, p.Id, p.Title, null, null);
            else Consider(SearchKind.Page, p.Id, p.Title, PageQueries.Excerpt(p.Body, q), null, p.Body);
        }

        return [.. hits.OrderBy(h => h.Score).ThenBy(h => h.KindOrder).ThenBy(h => h.Result.Title, StringComparer.OrdinalIgnoreCase)
            .Take(SearchLimit).Select(h => h.Result)];
    }
}
