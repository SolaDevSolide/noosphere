using Noosphere.Api.Auth;
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
    public static LandscapeDto Landscape(Snapshot s, Viewer viewer, Filter filter, DateTimeOffset now)
    {
        var matching = s.Applications.Where(a => filter.Matches(a, s.Hidden(a, viewer))).Select(a => a.Id).ToHashSet();
        var flows = s.Flows
            .Where(f => filter.MatchesFlowCriteria(f, s.Hidden(f, viewer))
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
            [.. apps.Select(a => s.View(a, viewer))],
            [.. s.DataObjects.Select(Snapshot.Dto)],
            [.. flows.Select(f => s.View(f, viewer))],
            new LandscapeStatsDto(apps.Count, flows.Count, domains.Count),
            now);
    }

    /// <summary>
    /// Top <see cref="SearchLimit"/> matches, accent- and case-insensitive: names starting with the text first, then names
    /// containing it, then other fields (id, tech, description). Restricted records are never matched for those who cannot see them.
    /// </summary>
    public static List<SearchResultDto> Search(Snapshot s, Viewer viewer, string? text)
    {
        var q = Rules.Fold(text?.Trim());
        if (q.Length == 0) return [];
        var domains = s.Domains.ToDictionary(d => d.Id, d => d.Name);
        var teams = s.Teams.ToDictionary(t => t.Id, t => t.Name);
        string AppName(string id) => s.AppsById.TryGetValue(id, out var a) && !s.Hidden(a, viewer) ? a.Name : id;

        var hits = new List<(int Score, int KindOrder, SearchResultDto Result)>();
        void Consider(SearchKind kind, string id, string name, string? subtitle, params string?[] others)
        {
            var folded = Rules.Fold(name);
            var score = folded.StartsWith(q, StringComparison.Ordinal) ? 0
                : folded.Contains(q, StringComparison.Ordinal) ? 1
                : others.Append(id).Any(o => o is not null && Rules.Fold(o).Contains(q, StringComparison.Ordinal)) ? 2
                : -1;
            if (score >= 0) hits.Add((score, (int)kind, new SearchResultDto(kind, id, name, subtitle)));
        }

        foreach (var a in s.Applications.Where(a => !s.Hidden(a, viewer)))
            Consider(SearchKind.Application, a.Id, a.Name,
                string.Join(" · ", new[] { domains.GetValueOrDefault(a.DomainId), a.TeamId is null ? null : teams.GetValueOrDefault(a.TeamId) }.Where(x => x is not null)),
                a.Tech, a.Description);
        foreach (var f in s.Flows.Where(f => !s.Hidden(f, viewer)))
            Consider(SearchKind.Flow, f.Id, f.Name, $"{AppName(f.FromId)} → {AppName(f.ToId)}", f.Description);
        foreach (var o in s.DataObjects)
            Consider(SearchKind.DataObject, o.Id, o.Name,
                o.SourceApplicationId is null ? null : AppName(o.SourceApplicationId), o.Description);
        foreach (var t in s.Teams)
            Consider(SearchKind.Team, t.Id, t.Name, t.Contact, t.Description);
        foreach (var d in s.Domains)
            Consider(SearchKind.Domain, d.Id, d.Name, d.Description, d.Description);

        return [.. hits.OrderBy(h => h.Score).ThenBy(h => h.KindOrder).ThenBy(h => h.Result.Title, StringComparer.OrdinalIgnoreCase)
            .Take(SearchLimit).Select(h => h.Result)];
    }
}
