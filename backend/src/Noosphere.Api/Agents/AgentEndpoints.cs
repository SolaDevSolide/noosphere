using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Agents;

/// <summary>Agents console (administrators), "My agents" (everyone), agent settings and the emergency stop.</summary>
public static class AgentEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MyProposalsLimit = 100;

    public static void MapAgents(this RouteGroupBuilder api)
    {
        var settings = api.MapGroup("/settings/agents").WithTags("AI agents");
        settings.MapGet("", async (HttpRequest request, NoosphereDbContext db, Settings config) =>
            AgentSettingsStore.Dto(await AgentSettingsStore.Get(db), PublicAddress.Of(request, config)));
        settings.MapPut("", async (AgentSettingsInput input, HttpRequest request, NoosphereDbContext db, Settings config, TimeProvider clock, ILoggerFactory logs, ClaimsPrincipal user) =>
        {
            var current = await AgentSettingsStore.Get(db);
            var errors = new FieldErrors();
            if (input.Version is null) errors.Add("version", "This field is required.");
            if (input.QuotaPerHour is { } q && q is < AgentSettingsStore.MinQuota or > AgentSettingsStore.MaxQuota)
                errors.Add("quotaPerHour", $"Between {AgentSettingsStore.MinQuota} and {AgentSettingsStore.MaxQuota}.");
            errors.ThrowIfAny();
            if (input.Version != current.Version) throw ApiProblem.Stale();
            if (current.Version == 0) db.AgentSettings.Add(current);
            current.Enabled = input.Enabled ?? current.Enabled;
            current.Ceiling = input.Ceiling ?? current.Ceiling;
            current.QuotaPerHour = input.QuotaPerHour ?? current.QuotaPerHour;
            current.WhoConnects = input.WhoConnects ?? current.WhoConnects;
            current.DynamicRegistration = input.DynamicRegistration ?? current.DynamicRegistration;
            current.AnonymousLlms = input.AnonymousLlms ?? current.AnonymousLlms;
            current.UpdatedAt = clock.GetUtcNow();
            current.Version++;
            await db.SaveChangesAsync();
            logs.CreateLogger("Noosphere.Agents").LogInformation("Agent settings changed by {User}: enabled {Enabled}, ceiling {Ceiling}, quota {Quota}/h, {Who}, registration {Registration}, anonymous llms {Anonymous}.",
                Viewer.From(user).Username, current.Enabled, current.Ceiling, current.QuotaPerHour, current.WhoConnects, current.DynamicRegistration, current.AnonymousLlms);
            return AgentSettingsStore.Dto(current, PublicAddress.Of(request, config));
        }).RequireAdmin();

        var agents = api.MapGroup("/agents").WithTags("AI agents");
        agents.MapGet("", async (NoosphereDbContext db, TimeProvider clock) =>
            await Dtos(db, await db.AgentConnections.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(), clock.GetUtcNow()))
            .RequireAdmin();

        api.MapGet("/me/agents", async (ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock, HttpRequest request, Settings config) =>
        {
            var access = await Access.Load(db, user);
            var me = access.Viewer.Id;
            var connections = await db.AgentConnections.AsNoTracking().Where(c => c.UserId == me).OrderByDescending(c => c.CreatedAt).ToListAsync();
            var proposals = await Proposals.Query(db).AsNoTracking()
                .Where(p => p.AuthorId == me && p.AuthorKind == AuthorKind.Agent)
                .OrderByDescending(p => p.UpdatedAt).Take(MyProposalsLimit).ToListAsync();
            var people = await People.Load(db);
            var drafts = new List<ProposalDto>();
            var others = new List<ProposalDto>();
            foreach (var p in proposals) (p.Status == ProposalStatus.Draft ? drafts : others).Add(await Proposals.Dto(db, p, access, people));
            var s = await AgentSettingsStore.Get(db);
            return new MyAgentsDto(await Dtos(db, connections, clock.GetUtcNow()), drafts, others,
                new AgentSettingsSummaryDto(s.Enabled, s.Ceiling, s.WhoConnects, s.QuotaPerHour, AgentSettingsStore.McpUrl(PublicAddress.Of(request, config))));
        }).WithTags("AI agents");

        agents.MapPost("/{id:guid}/revoke", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var viewer = Viewer.From(user);
            var connection = await Mine(db, id, viewer, tracked: true);
            var now = clock.GetUtcNow();
            if (connection.RevokedAt is null)
            {
                connection.RevokedAt = now;
                await OAuthEndpoints.RevokeTokens(db, connection.Id, now);
                db.AgentActivities.Add(new AgentActivity { At = now, ConnectionId = connection.Id, Tool = "revoke", Kind = AgentActivityKind.Revoked, Summary = $"Revoked by {viewer.Username}." });
                await db.SaveChangesAsync();
            }
            return (await Dtos(db, [connection], now))[0];
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound);

        agents.MapPost("/{id:guid}/approve", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var connection = await db.AgentConnections.FirstOrDefaultAsync(c => c.Id == id) ?? throw NotFound(id);
            if (connection.RevokedAt is not null) throw ApiProblem.Conflict("This connection was revoked.");
            if (!connection.PendingApproval) throw ApiProblem.Conflict("This connection is already approved.");
            var now = clock.GetUtcNow();
            (connection.PendingApproval, connection.ApprovedAt, connection.ApprovedBy) = (false, now, Viewer.From(user).Username);
            await db.SaveChangesAsync();
            return (await Dtos(db, [connection], now))[0];
        }).RequireAdmin().Produces<ProblemDto>(StatusCodes.Status404NotFound).Produces<ProblemDto>(StatusCodes.Status409Conflict);

        agents.MapGet("/{id:guid}/capabilities", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var connection = await Mine(db, id, Viewer.From(user));
            var person = await db.Users.AsNoTracking().FirstAsync(u => u.Id == connection.UserId);
            var s = await AgentSettingsStore.Get(db);
            var access = await AgentAccess.For(db, person, connection.Scopes, s);
            return new AgentCapabilitiesDto(connection.Id, connection.ClientName, new UserRefDto(person.Username, person.DisplayName), s.Ceiling, connection.Scopes,
                Capabilities.Of(access, s, connection.Scopes, person.DisplayName));
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound);

        agents.MapGet("/activity", async ([FromQuery] Guid? connectionId, [FromQuery] string? kind, [FromQuery] string? cursor, [FromQuery] int? limit,
            ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var viewer = Viewer.From(user);
            var errors = new FieldErrors();
            var query = db.AgentActivities.AsNoTracking();
            if (!viewer.IsAdmin)
            {
                var mine = db.AgentConnections.Where(c => c.UserId == viewer.Id).Select(c => c.Id);
                query = query.Where(a => mine.Contains(a.ConnectionId));
            }
            if (connectionId is { } c)
            {
                if (!viewer.IsAdmin && !await db.AgentConnections.AnyAsync(x => x.Id == c && x.UserId == viewer.Id)) throw NotFound(c);
                query = query.Where(a => a.ConnectionId == c);
            }
            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (Enum.TryParse<AgentActivityKind>(kind.Trim(), ignoreCase: true, out var k) && Enum.IsDefined(k)) query = query.Where(a => a.Kind == k);
                else errors.Add("kind", "Expected read, draft, proposal, refused, quota or revoked.");
            }
            long? before = null;
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                if (long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var b)) before = b;
                else errors.Add("cursor", "Pass the nextCursor of the previous page.");
            }
            var size = limit ?? DefaultPageSize;
            if (size is < 1 or > MaxPageSize) errors.Add("limit", $"Between 1 and {MaxPageSize}.");
            errors.ThrowIfAny();
            if (before is { } until) query = query.Where(a => a.Id < until);
            var rows = await query.OrderByDescending(a => a.Id).Take(size + 1).ToListAsync();
            var items = await Activity(db, rows.Take(size).ToList(), await Access.Load(db, user));
            return new AgentActivityPageDto(items, rows.Count > size ? items[^1].Id.ToString(CultureInfo.InvariantCulture) : null);
        });

        agents.MapPost("/stop-all", async (StopAllInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock, ILoggerFactory logs) =>
        {
            if (input.Confirm != true) throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["confirm"] = ["Confirm the emergency stop with confirm: true."] });
            var viewer = Viewer.From(user);
            var now = clock.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var live = await db.AgentConnections.Where(c => c.RevokedAt == null).ToListAsync();
            foreach (var c in live)
            {
                c.RevokedAt = now;
                db.AgentActivities.Add(new AgentActivity { At = now, ConnectionId = c.Id, Tool = "stop-all", Kind = AgentActivityKind.Revoked, Summary = $"Emergency stop by {viewer.Username}: access revoked." });
            }
            await db.SaveChangesAsync();
            var tokens = await db.AgentTokens.Where(t => t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
            // Authorizations in progress cannot be completed either.
            await db.OAuthRequests.Where(r => r.ExpiresAt > now).ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, now));
            await transaction.CommitAsync();
            logs.CreateLogger("Noosphere.Agents").LogWarning("Emergency stop by {User}: {Connections} agent connection(s) and {Tokens} token(s) revoked.", viewer.Username, live.Count, tokens);
            return new StopAllResultDto(live.Count, tokens, now);
        }).RequireAdmin();
    }

    private static ApiProblem NotFound(Guid id) => ApiProblem.NotFound($"No agent connection with the id '{id}'.");

    /// <summary>A connection of the reader (any for an administrator); 404 otherwise.</summary>
    private static async Task<AgentConnection> Mine(NoosphereDbContext db, Guid id, Viewer viewer, bool tracked = false)
    {
        var query = tracked ? db.AgentConnections : db.AgentConnections.AsNoTracking();
        var connection = await query.FirstOrDefaultAsync(c => c.Id == id);
        return connection is not null && (viewer.IsAdmin || connection.UserId == viewer.Id) ? connection : throw NotFound(id);
    }

    public static AgentState State(AgentConnection c, DateTimeOffset now) =>
        c.RevokedAt is not null ? AgentState.Revoked
        : c.PendingApproval ? AgentState.Pending
        : c.PausedUntil is { } p && p > now ? AgentState.Paused
        : AgentState.Active;

    public static async Task<List<AgentConnectionDto>> Dtos(NoosphereDbContext db, IReadOnlyList<AgentConnection> connections, DateTimeOffset now)
    {
        var ids = connections.Select(c => c.Id).ToList();
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        var calls = await db.AgentActivities.Where(a => ids.Contains(a.ConnectionId) && a.At >= hour && a.Kind != AgentActivityKind.Quota && a.Kind != AgentActivityKind.Revoked)
            .GroupBy(a => a.ConnectionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var drafts = await db.Proposals.Where(p => p.AgentConnectionId != null && ids.Contains(p.AgentConnectionId.Value) && p.Status == ProposalStatus.Draft)
            .GroupBy(p => p.AgentConnectionId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var lastIds = await db.AgentActivities.Where(a => ids.Contains(a.ConnectionId)).GroupBy(a => a.ConnectionId).Select(g => g.Max(a => a.Id)).ToListAsync();
        var last = await db.AgentActivities.AsNoTracking().Where(a => lastIds.Contains(a.Id)).ToListAsync();
        var people = await People.Load(db);
        var facts = await RecordFacts.Load(db);
        return [.. connections.Select(c => new AgentConnectionDto(c.Id, c.ClientId, c.ClientName, c.ClientUri, people.Ref(c.UserId, "")!, c.Scopes,
            State(c, now), c.CreatedAt, c.LastUsedAt, c.RevokedAt, c.PausedUntil, c.ApprovedAt, calls.GetValueOrDefault(c.Id), drafts.GetValueOrDefault(c.Id),
            last.FirstOrDefault(a => a.ConnectionId == c.Id) is { } a ? ActivityDto(a, c.ClientName, people.Ref(c.UserId, "")!, facts, null) : null, c.Demo))];
    }

    /// <summary>Activity lines with the agent's name, its person and the record's title (when the reader may see it).</summary>
    public static async Task<List<AgentActivityDto>> Activity(NoosphereDbContext db, IReadOnlyList<AgentActivity> rows, Access? reader)
    {
        var ids = rows.Select(r => r.ConnectionId).Distinct().ToList();
        var connections = await db.AgentConnections.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        var people = await People.Load(db);
        var facts = reader?.Facts ?? await RecordFacts.Load(db);
        return [.. rows.Select(r => connections.TryGetValue(r.ConnectionId, out var c)
            ? ActivityDto(r, c.ClientName, people.Ref(c.UserId, "")!, facts, reader)
            : ActivityDto(r, "?", new UserRefDto("", ""), facts, reader))];
    }

    private static AgentActivityDto ActivityDto(AgentActivity a, string agent, UserRefDto person, RecordFacts facts, Access? reader) =>
        new(a.Id, a.At, a.ConnectionId, agent, person, a.Tool, a.Kind, a.Summary, a.RecordKind, a.RecordId,
            a.RecordKind is { } k && a.RecordId is { } id && (reader is null || !reader.Hidden(k, id)) ? facts.Title(k, id) : null);
}
