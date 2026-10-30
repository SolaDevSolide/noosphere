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
    [FromQuery] string? DataObjectId, [FromQuery] string? Q)
{
    public Filter ToFilter()
    {
        var errors = new FieldErrors();
        var type = ParseEnum<FlowType>(errors, "type", Type);
        var status = ParseEnum<FlowStatus>(errors, "status", Status);
        errors.ThrowIfAny();
        return new Filter(Blank(DomainId), Blank(TeamId), type, status, Blank(DataObjectId), Blank(Q));
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
/// CRUD of domains, teams, applications, data objects and flows. Everyone signed in reads; members and administrators
/// create and edit; administrators delete.
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
        domains.MapGet("/{id}", async (string id, NoosphereDbContext db) => Snapshot.Dto(await Find(db.Domains, id, "domain")));
        domains.MapPost("", CreateDomain).RequireEditor().Produces<DomainDto>(StatusCodes.Status201Created);
        domains.MapPut("/{id}", UpdateDomain).RequireEditor();
        domains.MapDelete("/{id}", DeleteDomain).RequireAdmin();

        var teams = api.MapGroup("/teams").WithTags("Teams");
        teams.MapGet("", async ([AsParameters] FilterQuery query, NoosphereDbContext db) =>
        {
            var filter = query.ToFilter();
            return (await db.Teams.AsNoTracking().OrderBy(t => t.Id).ToListAsync()).Where(filter.Matches).Select(Snapshot.Dto).ToList();
        });
        teams.MapGet("/{id}", async (string id, NoosphereDbContext db) => Snapshot.Dto(await Find(db.Teams, id, "team")));
        teams.MapPost("", CreateTeam).RequireEditor().Produces<TeamDto>(StatusCodes.Status201Created);
        teams.MapPut("/{id}", UpdateTeam).RequireEditor();
        teams.MapDelete("/{id}", DeleteTeam).RequireAdmin();

        var apps = api.MapGroup("/applications").WithTags("Applications");
        apps.MapGet("", async ([AsParameters] FilterQuery query, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var filter = query.ToFilter();
            var viewer = Viewer.From(user);
            var s = await Snapshot.Load(db);
            return Results.Ok(s.Applications.Where(a => filter.Matches(a, s.Hidden(a, viewer))).Select(a => s.View(a, viewer)).ToList());
        }).Produces<List<ApplicationDto>>();
        apps.MapGet("/{id}", async (string id, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var s = await Snapshot.Load(db);
            var app = s.AppsById.GetValueOrDefault(id) ?? throw NotFound("application", id);
            return Results.Ok(s.View(app, Viewer.From(user)));
        }).Produces<ApplicationDto>().Produces<ProblemDto>(StatusCodes.Status404NotFound);
        apps.MapPost("", CreateApplication).RequireEditor().Produces<ApplicationDto>(StatusCodes.Status201Created);
        apps.MapPut("/{id}", UpdateApplication).RequireEditor();
        apps.MapDelete("/{id}", DeleteApplication).RequireAdmin();

        var objects = api.MapGroup("/data-objects").WithTags("Data objects");
        objects.MapGet("", async ([AsParameters] FilterQuery query, NoosphereDbContext db) =>
        {
            var filter = query.ToFilter();
            return (await db.DataObjects.AsNoTracking().OrderBy(o => o.Id).ToListAsync()).Where(filter.Matches).Select(Snapshot.Dto).ToList();
        });
        objects.MapGet("/{id}", async (string id, NoosphereDbContext db) => Snapshot.Dto(await Find(db.DataObjects, id, "data object")));
        objects.MapPost("", CreateDataObject).RequireEditor().Produces<DataObjectDto>(StatusCodes.Status201Created);
        objects.MapPut("/{id}", UpdateDataObject).RequireEditor();
        objects.MapDelete("/{id}", DeleteDataObject).RequireAdmin();

        var flows = api.MapGroup("/flows").WithTags("Flows");
        flows.MapGet("", async ([AsParameters] FilterQuery query, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var filter = query.ToFilter();
            var viewer = Viewer.From(user);
            var s = await Snapshot.Load(db);
            return Results.Ok(s.Flows.Where(f => filter.MatchesFlowList(f, s, s.Hidden(f, viewer))).Select(f => s.View(f, viewer)).ToList());
        }).Produces<List<FlowDto>>();
        flows.MapGet("/{id}", async (string id, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var s = await Snapshot.Load(db);
            var flow = s.Flows.FirstOrDefault(f => f.Id == id) ?? throw NotFound("flow", id);
            return Results.Ok(s.View(flow, Viewer.From(user)));
        }).Produces<FlowDto>().Produces<ProblemDto>(StatusCodes.Status404NotFound);
        flows.MapPost("", CreateFlow).RequireEditor().Produces<FlowDto>(StatusCodes.Status201Created);
        flows.MapPut("/{id}", UpdateFlow).RequireEditor();
        flows.MapDelete("/{id}", DeleteFlow).RequireAdmin();
    }

    // ---------- Domains ----------

    private static async Task<IResult> CreateDomain(DomainInput input, NoosphereDbContext db, TimeProvider clock)
    {
        var errors = new FieldErrors();
        var e = new BusinessDomain();
        Catalog.Apply(e, input, errors);
        e.Id = Catalog.NewId(input.Id, e.Name, await Ids(db.Domains), errors);
        errors.ThrowIfAny();
        Created(e, clock);
        db.Domains.Add(e);
        await db.SaveChangesAsync();
        return Results.Created($"/api/domains/{e.Id}", Snapshot.Dto(e));
    }

    private static async Task<DomainDto> UpdateDomain(string id, DomainInput input, NoosphereDbContext db, TimeProvider clock)
    {
        var e = await Find(db.Domains, id, "domain", tracked: true);
        CheckVersion(e, input.Id, input.Version);
        var errors = new FieldErrors();
        Catalog.Apply(e, input, errors);
        errors.ThrowIfAny();
        Updated(e, clock);
        await db.SaveChangesAsync();
        return Snapshot.Dto(e);
    }

    private static async Task<IResult> DeleteDomain(string id, NoosphereDbContext db)
    {
        var e = await Find(db.Domains, id, "domain", tracked: true);
        var apps = await db.Applications.CountAsync(a => a.DomainId == id);
        if (apps > 0) throw ApiProblem.Conflict($"This domain still holds {apps} application(s). Move or delete them first.");
        db.Domains.Remove(e);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---------- Teams ----------

    private static async Task<IResult> CreateTeam(TeamInput input, NoosphereDbContext db, TimeProvider clock)
    {
        var errors = new FieldErrors();
        var e = new Team();
        Catalog.Apply(e, input, errors);
        e.Id = Catalog.NewId(input.Id, e.Name, await Ids(db.Teams), errors);
        errors.ThrowIfAny();
        Created(e, clock);
        db.Teams.Add(e);
        await db.SaveChangesAsync();
        return Results.Created($"/api/teams/{e.Id}", Snapshot.Dto(e));
    }

    private static async Task<TeamDto> UpdateTeam(string id, TeamInput input, NoosphereDbContext db, TimeProvider clock)
    {
        var e = await Find(db.Teams, id, "team", tracked: true);
        CheckVersion(e, input.Id, input.Version);
        var errors = new FieldErrors();
        Catalog.Apply(e, input, errors);
        errors.ThrowIfAny();
        Updated(e, clock);
        await db.SaveChangesAsync();
        return Snapshot.Dto(e);
    }

    private static async Task<IResult> DeleteTeam(string id, NoosphereDbContext db)
    {
        var e = await Find(db.Teams, id, "team", tracked: true);
        var apps = await db.Applications.CountAsync(a => a.TeamId == id);
        var flows = await db.Flows.CountAsync(f => f.OwnerTeamId == id);
        if (apps + flows > 0)
            throw ApiProblem.Conflict($"This team still owns {apps} application(s) and {flows} flow(s). Reassign them first.");
        db.Teams.Remove(e);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---------- Applications ----------

    private static async Task<IResult> CreateApplication(ApplicationInput input, NoosphereDbContext db, TimeProvider clock)
    {
        var errors = new FieldErrors();
        var refs = await References.Load(db);
        var e = new Application();
        Catalog.Apply(e, input, errors, refs);
        e.Id = Catalog.NewId(input.Id, e.Name, refs.Applications, errors);
        errors.ThrowIfAny();
        Created(e, clock);
        db.Applications.Add(e);
        await db.SaveChangesAsync();
        return Results.Created($"/api/applications/{e.Id}", (await Snapshot.Load(db)).Dto(e));
    }

    private static async Task<ApplicationDto> UpdateApplication(string id, ApplicationInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock)
    {
        var e = await Find(db.Applications, id, "application", tracked: true);
        ForbidRestricted(e, user);
        CheckVersion(e, input.Id, input.Version);
        var errors = new FieldErrors();
        Catalog.Apply(e, input, errors, await References.Load(db));
        errors.ThrowIfAny();
        Updated(e, clock);
        await db.SaveChangesAsync();
        return (await Snapshot.Load(db)).Dto(e);
    }

    private static async Task<IResult> DeleteApplication(string id, NoosphereDbContext db)
    {
        var e = await Find(db.Applications, id, "application", tracked: true);
        var flows = await db.Flows.CountAsync(f => f.FromId == id || f.ToId == id);
        var objects = await db.DataObjects.CountAsync(o => o.SourceApplicationId == id);
        if (flows + objects > 0)
            throw ApiProblem.Conflict($"This application still has {flows} flow(s) and is the source of {objects} data object(s). Remove them first.");
        db.Applications.Remove(e);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---------- Data objects ----------

    private static async Task<IResult> CreateDataObject(DataObjectInput input, NoosphereDbContext db, TimeProvider clock)
    {
        var errors = new FieldErrors();
        var refs = await References.Load(db);
        var e = new DataObject();
        Catalog.Apply(e, input, errors, refs);
        e.Id = Catalog.NewId(input.Id, e.Name, refs.DataObjects, errors);
        errors.ThrowIfAny();
        Created(e, clock);
        db.DataObjects.Add(e);
        await db.SaveChangesAsync();
        return Results.Created($"/api/data-objects/{e.Id}", Snapshot.Dto(e));
    }

    private static async Task<DataObjectDto> UpdateDataObject(string id, DataObjectInput input, NoosphereDbContext db, TimeProvider clock)
    {
        var e = await Find(db.DataObjects, id, "data object", tracked: true);
        CheckVersion(e, input.Id, input.Version);
        var errors = new FieldErrors();
        Catalog.Apply(e, input, errors, await References.Load(db));
        errors.ThrowIfAny();
        Updated(e, clock);
        await db.SaveChangesAsync();
        return Snapshot.Dto(e);
    }

    private static async Task<IResult> DeleteDataObject(string id, NoosphereDbContext db)
    {
        var e = await Find(db.DataObjects, id, "data object", tracked: true);
        var flows = await db.Flows.CountAsync(f => f.DataObjectIds.Contains(id));
        if (flows > 0) throw ApiProblem.Conflict($"{flows} flow(s) still carry this data object. Remove it from them first.");
        db.DataObjects.Remove(e);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---------- Flows ----------

    private static async Task<IResult> CreateFlow(FlowInput input, NoosphereDbContext db, TimeProvider clock)
    {
        var errors = new FieldErrors();
        var e = new Flow();
        Catalog.Apply(e, input, errors, await References.Load(db));
        e.Id = Catalog.NewId(input.Id, e.Name, await Ids(db.Flows), errors);
        errors.ThrowIfAny();
        Created(e, clock);
        db.Flows.Add(e);
        await db.SaveChangesAsync();
        return Results.Created($"/api/flows/{e.Id}", (await Snapshot.Load(db)).Dto(e));
    }

    private static async Task<FlowDto> UpdateFlow(string id, FlowInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock)
    {
        var e = await Find(db.Flows, id, "flow", tracked: true);
        var viewer = Viewer.From(user);
        if (!viewer.SeesRestricted && await db.Applications.AnyAsync(a => (a.Id == e.FromId || a.Id == e.ToId) && a.Visibility == Visibility.Restricted))
            throw ApiProblem.Forbidden("This flow touches a restricted application: only administrators can change it.");
        CheckVersion(e, input.Id, input.Version);
        var errors = new FieldErrors();
        Catalog.Apply(e, input, errors, await References.Load(db));
        errors.ThrowIfAny();
        Updated(e, clock);
        await db.SaveChangesAsync();
        return (await Snapshot.Load(db)).Dto(e);
    }

    private static async Task<IResult> DeleteFlow(string id, NoosphereDbContext db)
    {
        db.Flows.Remove(await Find(db.Flows, id, "flow", tracked: true));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ---------- Helpers ----------

    private static ApiProblem NotFound(string what, string id) => ApiProblem.NotFound($"No {what} with the id '{id}'.");

    private static async Task<T> Find<T>(DbSet<T> set, string id, string what, bool tracked = false) where T : LandscapeEntity =>
        await (tracked ? set : set.AsNoTracking()).FirstOrDefaultAsync(e => e.Id == id) ?? throw NotFound(what, id);

    private static async Task<HashSet<string>> Ids<T>(DbSet<T> set) where T : LandscapeEntity =>
        (await set.Select(e => e.Id).ToListAsync()).ToHashSet();

    private static void ForbidRestricted(Application app, ClaimsPrincipal user)
    {
        if (app.Visibility == Visibility.Restricted && !Viewer.From(user).SeesRestricted)
            throw ApiProblem.Forbidden("This application is restricted: only administrators can change it.");
    }

    /// <summary>A PUT carries the version that was read (400 without it, 409 when someone saved in between).</summary>
    private static void CheckVersion(LandscapeEntity e, string? bodyId, int? version)
    {
        var errors = new FieldErrors();
        if (bodyId is { Length: > 0 } && bodyId != e.Id) errors.Add("id", "Ids cannot be changed.");
        if (version is null) errors.Add("version", "This field is required.");
        errors.ThrowIfAny();
        if (version != e.Version) throw ApiProblem.Stale();
    }

    private static void Created(LandscapeEntity e, TimeProvider clock)
    {
        e.CreatedAt = e.UpdatedAt = clock.GetUtcNow();
        e.Version = 1;
    }

    private static void Updated(LandscapeEntity e, TimeProvider clock)
    {
        e.UpdatedAt = clock.GetUtcNow();
        e.Version++;
    }
}
