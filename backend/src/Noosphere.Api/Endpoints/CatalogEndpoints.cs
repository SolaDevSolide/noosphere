using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Endpoints;

/// <summary>Query string filters, shared by the list endpoints and the landscape.</summary>
public sealed record FilterQuery(
    [FromQuery] string? DomainId, [FromQuery] string? TeamId, [FromQuery] string? Type, [FromQuery] string? Status,
    [FromQuery] string? DataObjectId, [FromQuery] string? Q, [FromQuery] string? Sensitivity)
{
    public Filter ToFilter()
    {
        var errors = new FieldErrors();
        var type = ParseEnum<FlowType>(errors, "type", Type);
        var status = ParseEnum<FlowStatus>(errors, "status", Status);
        var sensitivity = ParseEnum<Domain.Sensitivity>(errors, "sensitivity", Sensitivity);
        errors.ThrowIfAny();
        return new Filter(Blank(DomainId), Blank(TeamId), type, status, Blank(DataObjectId), Blank(Q), sensitivity);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static T? ParseEnum<T>(FieldErrors errors, string field, string? value) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        errors.Add(field, $"Expected one of: {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}.");
        return null;
    }
}

/// <summary>
/// CRUD of domains, teams, applications, data objects and flows. Everyone signed in reads. Administrators and members of
/// the owning team create, edit and delete (each change adds a revision); other members send proposals instead (403 here).
/// Domains and teams are changed by administrators only.
/// </summary>
public static class CatalogEndpoints
{
    public static void MapCatalog(this RouteGroupBuilder api)
    {
        var domains = api.MapGroup("/domains").WithTags("Domains");
        domains.MapGet("", async ([AsParameters] FilterQuery query, NoosphereDbContext db) =>
        {
            var filter = query.ToFilter();
            return (await db.Domains.AsNoTracking().OrderBy(d => d.Order).ThenBy(d => d.Id).ToListAsync()).Where(filter.Matches).Select(Snapshot.Dto).ToList();
        });
        domains.MapGet("/{id}", async (string id, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var e = (BusinessDomain)await Find(db, Records.Domain, id);
            var access = await Access.Load(db, user);
            return new DomainDetailDto(Snapshot.Dto(e), await RecordContext.Of(db, access, Records.Domain, e));
        });
        domains.MapPost("", (DomainInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Create(Records.Domain, input, user, db, clock, (e, _) => Task.FromResult<object>(Snapshot.Dto((BusinessDomain)e))))
            .RequireEditor().Produces<DomainDto>(StatusCodes.Status201Created);
        domains.MapPut("/{id}", async (string id, DomainInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Snapshot.Dto((BusinessDomain)await Update(Records.Domain, id, input, input.Id, input.Version, user, db, clock)))
            .RequireEditor();
        domains.MapDelete("/{id}", (string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) => Delete(Records.Domain, id, user, db, clock))
            .RequireEditor();

        var teams = api.MapGroup("/teams").WithTags("Teams");
        teams.MapGet("", async ([AsParameters] FilterQuery query, NoosphereDbContext db) =>
        {
            var filter = query.ToFilter();
            return (await db.Teams.AsNoTracking().OrderBy(t => t.Id).ToListAsync()).Where(filter.Matches).Select(Snapshot.Dto).ToList();
        });
        teams.MapGet("/{id}", async (string id, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var e = (Team)await Find(db, Records.Team, id);
            var access = await Access.Load(db, user);
            return new TeamDetailDto(Snapshot.Dto(e), await RecordContext.Of(db, access, Records.Team, e), await Members(db, id));
        });
        teams.MapPost("", (TeamInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Create(Records.Team, input, user, db, clock, (e, _) => Task.FromResult<object>(Snapshot.Dto((Team)e))))
            .RequireEditor().Produces<TeamDto>(StatusCodes.Status201Created);
        teams.MapPut("/{id}", async (string id, TeamInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Snapshot.Dto((Team)await Update(Records.Team, id, input, input.Id, input.Version, user, db, clock)))
            .RequireEditor();
        teams.MapDelete("/{id}", (string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) => Delete(Records.Team, id, user, db, clock))
            .RequireEditor();
        teams.MapGet("/{id}/members", async (string id, NoosphereDbContext db) =>
        {
            await Find(db, Records.Team, id);
            return await Members(db, id);
        });
        teams.MapPut("/{id}/members", async (string id, TeamMembersInput input, NoosphereDbContext db) =>
        {
            await Find(db, Records.Team, id);
            var names = (input.Usernames ?? []).Select(Accounts.NormalizeUsername).Where(n => n.Length > 0).Distinct().ToList();
            var users = await db.Users.Where(u => names.Contains(u.Username)).Select(u => new { u.Id, u.Username }).ToListAsync();
            var errors = new FieldErrors();
            foreach (var unknown in names.Where(n => users.All(u => u.Username != n))) errors.Add("usernames", $"No user named '{unknown}'.");
            errors.ThrowIfAny();
            var ids = users.Select(u => u.Id).ToHashSet();
            var current = await db.TeamMembers.Where(m => m.TeamId == id).ToListAsync();
            db.TeamMembers.RemoveRange(current.Where(m => !ids.Contains(m.UserId)));
            foreach (var user in ids.Where(u => current.All(m => m.UserId != u))) db.TeamMembers.Add(new TeamMember { UserId = user, TeamId = id });
            await db.SaveChangesAsync();
            return await Members(db, id);
        }).RequireAdmin();

        var apps = api.MapGroup("/applications").WithTags("Applications");
        apps.MapGet("", async ([AsParameters] FilterQuery query, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var filter = query.ToFilter();
            var access = await Access.Load(db, user);
            var s = await Snapshot.Load(db);
            return Results.Ok(LandscapeQueries.Applications(s, access, filter).Select(a => s.View(a, access)).ToList());
        }).Produces<List<ApplicationDto>>();
        apps.MapGet("/{id}", async (string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var s = await Snapshot.Load(db);
            var app = s.AppsById.GetValueOrDefault(id) ?? throw NotFound(Records.Application, id);
            var access = await Access.Load(db, user);
            if (access.Sight(RecordKind.Application, app) != Sight.Full) return Results.Ok(s.View(app, access));
            var notes = Notes(access, RecordKind.Application, app, app.ProtectedNotes);
            await AccessLogs.RecordRead(db, access, RecordKind.Application, app, notes.Shown, clock.GetUtcNow());
            return Results.Ok(new ApplicationDetailDto(s.Dto(app, access), await RecordContext.Of(db, access, Records.Application, app))
                { ProtectedNotes = notes.Value, ProtectedNotesHidden = notes.Hidden });
        }).Produces<ApplicationDetailDto>().Produces<ProblemDto>(StatusCodes.Status404NotFound);
        apps.MapPost("", (ApplicationInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Create(Records.Application, input, user, db, clock, async (e, access) => (await Snapshot.Load(db)).Dto((Application)e, access)))
            .RequireEditor().Produces<ApplicationDto>(StatusCodes.Status201Created);
        apps.MapPut("/{id}", async (string id, ApplicationInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var e = await Update(Records.Application, id, input, input.Id, input.Version, user, db, clock);
            return (await Snapshot.Load(db)).Dto((Application)e, await Access.Load(db, user));
        }).RequireEditor();
        apps.MapDelete("/{id}", (string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) => Delete(Records.Application, id, user, db, clock))
            .RequireEditor();

        var objects = api.MapGroup("/data-objects").WithTags("Data objects");
        objects.MapGet("", async ([AsParameters] FilterQuery query, NoosphereDbContext db) =>
        {
            var filter = query.ToFilter();
            return (await db.DataObjects.AsNoTracking().OrderBy(o => o.Id).ToListAsync()).Where(filter.Matches).Select(Snapshot.Dto).ToList();
        });
        objects.MapGet("/{id}", async (string id, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var e = (DataObject)await Find(db, Records.DataObject, id);
            var access = await Access.Load(db, user);
            return new DataObjectDetailDto(Snapshot.Dto(e), await RecordContext.Of(db, access, Records.DataObject, e));
        });
        objects.MapPost("", (DataObjectInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Create(Records.DataObject, input, user, db, clock, (e, _) => Task.FromResult<object>(Snapshot.Dto((DataObject)e))))
            .RequireEditor().Produces<DataObjectDto>(StatusCodes.Status201Created);
        objects.MapPut("/{id}", async (string id, DataObjectInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Snapshot.Dto((DataObject)await Update(Records.DataObject, id, input, input.Id, input.Version, user, db, clock)))
            .RequireEditor();
        objects.MapDelete("/{id}", (string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) => Delete(Records.DataObject, id, user, db, clock))
            .RequireEditor();

        var flows = api.MapGroup("/flows").WithTags("Flows");
        flows.MapGet("", async ([AsParameters] FilterQuery query, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var filter = query.ToFilter();
            var access = await Access.Load(db, user);
            var s = await Snapshot.Load(db);
            return Results.Ok(s.Flows.Where(f => filter.MatchesFlowList(f, s, access.Sight(RecordKind.Flow, f))).Select(f => s.View(f, access)).ToList());
        }).Produces<List<FlowDto>>();
        flows.MapGet("/{id}", async (string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var s = await Snapshot.Load(db);
            var flow = s.Flows.FirstOrDefault(f => f.Id == id) ?? throw NotFound(Records.Flow, id);
            var access = await Access.Load(db, user);
            if (access.Sight(RecordKind.Flow, flow) != Sight.Full) return Results.Ok(s.View(flow, access));
            var notes = Notes(access, RecordKind.Flow, flow, flow.ProtectedNotes);
            await AccessLogs.RecordRead(db, access, RecordKind.Flow, flow, notes.Shown, clock.GetUtcNow());
            return Results.Ok(new FlowDetailDto(s.Dto(flow), await RecordContext.Of(db, access, Records.Flow, flow))
                { ProtectedNotes = notes.Value, ProtectedNotesHidden = notes.Hidden });
        }).Produces<FlowDetailDto>().Produces<ProblemDto>(StatusCodes.Status404NotFound);
        flows.MapPost("", (FlowInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Create(Records.Flow, input, user, db, clock, async (e, _) => (await Snapshot.Load(db)).Dto((Flow)e)))
            .RequireEditor().Produces<FlowDto>(StatusCodes.Status201Created);
        flows.MapPut("/{id}", async (string id, FlowInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var e = await Update(Records.Flow, id, input, input.Id, input.Version, user, db, clock);
            return (await Snapshot.Load(db)).Dto((Flow)e);
        }).RequireEditor();
        flows.MapDelete("/{id}", (string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) => Delete(Records.Flow, id, user, db, clock))
            .RequireEditor();
    }

    private static async Task<List<TeamMemberDto>> Members(NoosphereDbContext db, string teamId) =>
        await db.TeamMembers.Where(m => m.TeamId == teamId)
            .Join(db.Users, m => m.UserId, u => u.Id, (_, u) => u)
            .OrderBy(u => u.Username).Select(u => new TeamMemberDto(u.Username, u.DisplayName, u.Role)).ToListAsync();

    // ---------- Shared write paths (also used by pages) ----------

    public static ApiProblem NotFound(RecordType type, string id) => ApiProblem.NotFound($"No {type.Noun} with the id '{id}'.");

    public static async Task<VersionedEntity> Find(NoosphereDbContext db, RecordType type, string id, bool tracked = false) =>
        await type.Find(db, id, tracked) ?? throw NotFound(type, id);

    /// <summary>Protected notes as the reader gets them: the text for its readers, null (and hidden) for anyone else.</summary>
    public static (string? Value, bool Hidden, bool Shown) Notes(Access access, RecordKind kind, VersionedEntity e, string? notes)
    {
        if (notes is null) return (null, false, false);
        return access.ReadsProtected(kind, e) ? (notes, false, true) : (null, true, false);
    }

    /// <summary>Creates a record when the reader may publish it (the owner is that of the new record).</summary>
    public static async Task<IResult> Create<TInput>(RecordType type, TInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock,
        Func<VersionedEntity, Access, Task<object>> dto)
    {
        var access = await Access.Load(db, user);
        var publisher = new Publisher(db, clock.GetUtcNow());
        var e = await publisher.Create(type, Records.ToSnapshot(input), Author.Of(access.Viewer), await References.Load(db));
        access.RequirePublish(type.Kind, e);
        access.RequireStillVisible(type, e);
        await db.SaveChangesAsync();
        return Results.Created($"/api/{type.Plural}/{e.Id}", await dto(e, await Access.Load(db, user)));
    }

    /// <summary>
    /// Replaces a record when the reader may publish changes to it (the owner is that of the current record). The body
    /// carries the version that was read (400 without it, 409 when someone saved in between).
    /// </summary>
    public static async Task<VersionedEntity> Update<TInput>(RecordType type, string id, TInput input, string? bodyId, int? version,
        ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock)
    {
        var e = await Find(db, type, id, tracked: true);
        var access = await Access.Load(db, user);
        access.RequirePublish(type.Kind, e);
        var errors = new FieldErrors();
        if (bodyId is { Length: > 0 } && bodyId != e.Id) errors.Add("id", "Ids cannot be changed.");
        if (version is null) errors.Add("version", "This field is required.");
        errors.ThrowIfAny();
        if (version != e.Version) throw ApiProblem.Stale();
        new Publisher(db, clock.GetUtcNow()).Update(type, e, Records.ToSnapshot(input), Author.Of(access.Viewer), await References.Load(db));
        access.RequireStillVisible(type, e);
        await db.SaveChangesAsync();
        return e;
    }

    /// <summary>Deletes a record when the reader may publish changes to it. Its history is kept (trash).</summary>
    public static async Task<IResult> Delete(RecordType type, string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock)
    {
        var e = await Find(db, type, id, tracked: true);
        var access = await Access.Load(db, user);
        access.RequirePublish(type.Kind, e);
        await new Publisher(db, clock.GetUtcNow()).Delete(type, e, Author.Of(access.Viewer));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
