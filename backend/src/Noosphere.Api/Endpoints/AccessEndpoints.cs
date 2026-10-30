using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Endpoints;

/// <summary>"Who can see and edit?" for every record, and the access log (administrators).</summary>
public static class AccessEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static void MapAccess(this RouteGroupBuilder api)
    {
        foreach (var type in Records.All)
        {
            api.MapGet($"/{type.Plural}/{{id}}/access", async (string id, ClaimsPrincipal user, NoosphereDbContext db) =>
            {
                var e = await CatalogEndpoints.Find(db, type, id);
                var access = await Access.Load(db, user);
                switch (access.Sight(type.Kind, e))
                {
                    case Sight.Hidden: throw ApiProblem.Forbidden($"This {type.Noun} is restricted: you are not on its access list.");
                    case Sight.Outline: throw ApiProblem.Forbidden($"This {type.Noun} is not shared with you.");
                }
                return await Explain(db, access, type, e);
            })
                .WithTags("Access").WithName($"Access{type.Kind}")
                .Produces<ProblemDto>(StatusCodes.Status403Forbidden).Produces<ProblemDto>(StatusCodes.Status404NotFound);
        }

        api.MapGet("/access-log", async ([FromQuery] string? username, [FromQuery] string? kind, [FromQuery] string? entityId,
            [FromQuery] string? what, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? cursor, [FromQuery] int? limit,
            NoosphereDbContext db) =>
        {
            var errors = new FieldErrors();
            var query = db.AccessLogs.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(username))
            {
                var name = Accounts.NormalizeUsername(username);
                query = query.Where(l => l.Username == name);
            }
            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (Records.Parse(kind.Trim()) is { } type) query = query.Where(l => l.Kind == type.Kind);
                else errors.Add("kind", "Expected application, flow, dataObject, page, domain or team.");
            }
            if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(l => l.EntityId == entityId.Trim());
            if (!string.IsNullOrWhiteSpace(what))
            {
                var parsed = Enum.GetValues<AccessLogWhat>().Where(w => string.Equals(w.ToString(), what.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                if (parsed.Count == 1) query = query.Where(l => l.What == parsed[0]);
                else errors.Add("what", "Expected record, protectedNotes or export.");
            }
            if (Time(errors, "from", from, endOfDay: false) is { } start) query = query.Where(l => l.At >= start);
            if (Time(errors, "to", to, endOfDay: true) is { } end) query = query.Where(l => l.At < end);
            long? before = null;
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                if (long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var c)) before = c;
                else errors.Add("cursor", "Pass the nextCursor of the previous page.");
            }
            var size = limit ?? DefaultPageSize;
            if (size is < 1 or > MaxPageSize) errors.Add("limit", $"Between 1 and {MaxPageSize}.");
            errors.ThrowIfAny();
            if (before is { } b) query = query.Where(l => l.Id < b);
            var rows = await query.OrderByDescending(l => l.Id).Take(size + 1).ToListAsync();
            var people = await People.Load(db);
            var facts = await RecordFacts.Load(db);
            TeamRefDto? Team(string? id) => id is null ? null : new TeamRefDto(id, facts.TeamName(id) ?? id);
            var items = rows.Take(size).Select(l => new AccessLogEntryDto(l.Id, l.At, people.Ref(l.UserId, l.Username) ?? new UserRefDto(l.Username, l.Username),
                l.Kind, l.EntityId, l.Kind is { } k && l.EntityId is { } id ? facts.Title(k, id) : null, l.What, l.Detail, l.Admin,
                l.Why, Team(l.WhyTeamId), l.Kind is { } ok && l.EntityId is { } oid ? Team(facts.Owner(ok, oid)) : null)).ToList();
            return new AccessLogPageDto(items, rows.Count > size ? items[^1].Id.ToString(CultureInfo.InvariantCulture) : null);
        }).RequireAdmin().WithTags("Access");
    }

    /// <summary>A date (<c>2026-10-30</c>: the whole day for <c>to</c>) or a date and time with an offset.</summary>
    private static DateTimeOffset? Time(FieldErrors errors, string field, string? text, bool endOfDay)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(endOfDay ? 1 : 0);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
            return endOfDay ? time.AddTicks(1) : time;
        errors.Add(field, "Expected a date (YYYY-MM-DD) or an ISO 8601 date and time.");
        return null;
    }

    /// <summary>
    /// Who reads, publishes and proposes a record. Readers of a restricted record are the owning team, the teams of its
    /// access list and the people who see it by name (for a flow: those who see every restricted end); readers of any
    /// other record are every member, plus the guests who see it in full.
    /// </summary>
    public static async Task<RecordAccessDto> Explain(NoosphereDbContext db, Access access, RecordType type, VersionedEntity e, CancellationToken cancel = default)
    {
        var facts = access.Facts;
        var kind = type.Kind;
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(cancel);
        var memberships = await db.TeamMembers.AsNoTracking().ToListAsync(cancel);
        var teamsOf = memberships.ToLookup(m => m.UserId, m => m.TeamId);
        var counts = memberships.GroupBy(m => m.TeamId).ToDictionary(g => g.Key, g => g.Count());
        AccessTeamDto Team(string id) => new(id, facts.TeamName(id) ?? id, counts.GetValueOrDefault(id));

        var restricted = facts.Restricted(kind, e);
        var owner = facts.Owner(kind, e);
        var teams = new List<string>();
        if (restricted)
        {
            IEnumerable<string> Readers(string? teamOwner, AccessList list) => [.. teamOwner is null ? [] : new[] { teamOwner }, .. list.TeamIds];
            teams = e switch
            {
                Application a => [.. Readers(owner, AccessList.Of(a)).Distinct()],
                Page p => [.. Readers(owner, AccessList.Of(p)).Distinct()],
                // A flow is read by those who see all its restricted ends: the teams that read every one of them.
                Flow f => new[] { f.FromId, f.ToId }.Distinct().Where(facts.AppRestricted)
                    .Select(end => Readers(facts.Owner(RecordKind.Application, end), facts.AppAccess(end)).ToHashSet())
                    .Aggregate((x, y) => [.. x.Intersect(y)]).Order(StringComparer.Ordinal).ToList(),
                _ => [],
            };
        }
        var readers = users
            .Where(u => u.Role != Role.Admin && (restricted || u.Role == Role.Guest))
            .Where(u => !teamsOf[u.Id].Any(teams.Contains))
            .Where(u => new Access(new Viewer(u.Id, u.Username, u.Role), teamsOf[u.Id].ToHashSet(), facts).Sight(kind, e) == Sight.Full)
            .Select(u => new UserRefDto(u.Username, u.DisplayName)).ToList();
        var notes = e switch
        {
            Application a => a.ProtectedNotes is not null,
            Flow f => f.ProtectedNotes is not null,
            _ => false,
        };
        return new RecordAccessDto(
            restricted ? Visibility.Restricted : Visibility.Everyone,
            owner is null ? null : Team(owner),
            new AccessReadersDto(!restricted, [.. teams.Select(Team)], readers),
            new AccessPublishersDto(owner is null || !type.Proposable ? [] : [Team(owner)], true),
            type.Proposable ? Proposers.Members : Proposers.None,
            new ProtectedNotesAccessDto(notes, notes && access.ReadsProtected(kind, e)),
            kind is RecordKind.Application or RecordKind.Page && access.CanPublish(kind, e));
    }
}
