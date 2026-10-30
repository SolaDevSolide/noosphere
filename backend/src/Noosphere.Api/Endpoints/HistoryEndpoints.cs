using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Endpoints;

/// <summary>Revision history of every record (list, snapshot, compare, restore), the trash, and the instance settings.</summary>
public static class HistoryEndpoints
{
    public static void MapHistory(this RouteGroupBuilder api)
    {
        foreach (var type in Records.All)
        {
            var group = api.MapGroup($"/{type.Plural}/{{id}}/history").WithTags("History");

            group.MapGet("", async (string id, ClaimsPrincipal user, NoosphereDbContext db) =>
            {
                var access = await Access.Load(db, user);
                var revisions = await Readable(db, type, id, access);
                var people = await People.Load(db);
                return revisions.Select(r => new HistoryEntryDto(r.Version, r.Operation, people.Ref(r.AuthorId, r.AuthorName), r.AuthorKind,
                    r.CreatedAt, r.Summary, r.ProposalId)).ToList();
            }).WithName($"History{type.Kind}").Produces<ProblemDto>(StatusCodes.Status404NotFound);

            group.MapGet("/{version:int}", async (string id, int version, ClaimsPrincipal user, NoosphereDbContext db) =>
            {
                var access = await Access.Load(db, user);
                var r = Pick(await Readable(db, type, id, access), version, type);
                var people = await People.Load(db);
                return new RevisionDto(r.Kind, r.EntityId, r.Version, r.Operation, people.Ref(r.AuthorId, r.AuthorName), r.AuthorKind,
                    r.CreatedAt, r.Summary, r.ProposalId, Records.ParseObject(r.Snapshot)!);
            }).WithName($"Revision{type.Kind}");

            group.MapGet("/compare", async (string id, [FromQuery] int? from, [FromQuery] int? to, ClaimsPrincipal user, NoosphereDbContext db) =>
            {
                var errors = new FieldErrors();
                if (from is null) errors.Add("from", "This field is required.");
                if (to is null) errors.Add("to", "This field is required.");
                errors.ThrowIfAny();
                var access = await Access.Load(db, user);
                var revisions = await Readable(db, type, id, access);
                var a = Pick(revisions, from!.Value, type);
                var b = Pick(revisions, to!.Value, type);
                return new CompareDto(a.Version, b.Version,
                    Diffs.Compare(type, Records.ParseObject(a.Snapshot), Records.ParseObject(b.Snapshot), access.Facts, access));
            }).WithName($"Compare{type.Kind}");

            group.MapPost("/{version:int}/restore", async (string id, int version, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            {
                var access = await Access.Load(db, user);
                var revision = Pick(await Readable(db, type, id, access), version, type);
                if (revision.Operation == RevisionOperation.Delete) throw ApiProblem.BadRequest("This version is the deletion: pick an earlier one.");
                var current = await type.Find(db, id, tracked: true)
                    ?? throw ApiProblem.Conflict($"This {type.Noun} is deleted: an administrator restores it from the trash.");
                if (current.Version == version) throw ApiProblem.Conflict("This is already the current version.");
                var snapshot = Records.ParseObject(revision.Snapshot)!;
                var now = clock.GetUtcNow();
                if (access.CanPublish(type.Kind, current))
                {
                    new Publisher(db, now).Update(type, current, snapshot, Author.Of(access.Viewer), await References.Load(db),
                        summary: $"Restored version {version}", operation: RevisionOperation.Restore);
                    await db.SaveChangesAsync();
                    return new RestoreResultDto("published", current.Version, null);
                }
                if (!access.CanPropose(type, current)) throw ApiProblem.Forbidden(access.PublishProblem(type.Kind, current)!);
                var proposal = new Proposal
                {
                    Title = $"Restore {type.Title(current)} to version {version}",
                    AuthorId = access.Viewer.Id, AuthorName = access.Viewer.Username, AuthorKind = AuthorKind.User,
                    Status = ProposalStatus.Submitted, CreatedAt = now, UpdatedAt = now, SubmittedAt = now, Version = 1,
                };
                proposal.Changes = await Proposals.BuildChanges(db, proposal.Id,
                    [new ProposalChangeInput(type.Kind, id, ChangeOperation.Update, current.Version, snapshot)], access);
                db.Proposals.Add(proposal);
                await db.SaveChangesAsync();
                return new RestoreResultDto("proposed", null, proposal.Id);
            }).WithName($"Restore{type.Kind}").RequireEditor();
        }

        var trash = api.MapGroup("/trash").WithTags("History").RequireAdmin();
        trash.MapGet("", async (NoosphereDbContext db) =>
        {
            var deleted = await Deleted(db).OrderByDescending(r => r.CreatedAt).ToListAsync();
            var people = await People.Load(db);
            return deleted.Select(r => new TrashItemDto(r.Kind, r.EntityId, Records.Of(r.Kind).Title(Records.ParseObject(r.Snapshot)!),
                r.CreatedAt, people.Ref(r.AuthorId, r.AuthorName), r.Version)).ToList();
        });
        trash.MapPost("/{kind}/{id}/restore", async (string kind, string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var type = Records.Parse(kind) ?? throw ApiProblem.NotFound($"Unknown kind '{kind}'.");
            var revision = await Deleted(db).FirstOrDefaultAsync(r => r.Kind == type.Kind && r.EntityId == id)
                ?? throw ApiProblem.NotFound($"No deleted {type.Noun} with the id '{id}'.");
            var e = await new Publisher(db, clock.GetUtcNow()).Recreate(type, id, Records.ParseObject(revision.Snapshot)!, revision.Version,
                Author.Of(Viewer.From(user)), await References.Load(db), "Restored from the trash");
            await db.SaveChangesAsync();
            return new TrashRestoreResultDto(type.Kind, e.Id, e.Version);
        });

        var settings = api.MapGroup("/settings").WithTags("Settings");
        settings.MapGet("", async (NoosphereDbContext db, Settings config) => InstanceSettingsStore.Dto(await InstanceSettingsStore.Get(db, config)))
            .AllowAnonymous();
        settings.MapPut("", async (SettingsInput input, NoosphereDbContext db, Settings config, TimeProvider clock) =>
        {
            var current = await InstanceSettingsStore.Get(db, config);
            var errors = new FieldErrors();
            var name = errors.Required("instanceName", input.InstanceName);
            var organisation = errors.Optional("organisationName", input.OrganisationName, Rules.MaxNameLength) ?? "";
            var language = input.DefaultLanguage?.Trim().ToLowerInvariant() ?? current.DefaultLanguage;
            if (!Rules.Languages.Contains(language)) errors.Add("defaultLanguage", $"Expected one of: {string.Join(", ", Rules.Languages)}.");
            if (input.Version is null) errors.Add("version", "This field is required.");
            errors.ThrowIfAny();
            if (input.Version != current.Version) throw ApiProblem.Stale();
            if (current.Version == 0) db.InstanceSettings.Add(current);
            (current.InstanceName, current.OrganisationName, current.DefaultLanguage, current.UpdatedAt) = (name, organisation, language, clock.GetUtcNow());
            current.Version++;
            await db.SaveChangesAsync();
            return InstanceSettingsStore.Dto(current);
        }).RequireAdmin();
    }

    /// <summary>Deletion revisions that are still the last revision of their record.</summary>
    private static IQueryable<Revision> Deleted(NoosphereDbContext db) =>
        db.Revisions.AsNoTracking().Where(r => r.Operation == RevisionOperation.Delete
            && !db.Revisions.Any(o => o.Kind == r.Kind && o.EntityId == r.EntityId && o.Version > r.Version));

    /// <summary>Revisions of a record, newest first, when the reader may see it (404 when none, 403 when restricted).</summary>
    private static async Task<List<Revision>> Readable(NoosphereDbContext db, RecordType type, string id, Access access)
    {
        var revisions = await db.Revisions.AsNoTracking().Where(r => r.Kind == type.Kind && r.EntityId == id).OrderByDescending(r => r.Version).ToListAsync();
        if (revisions.Count == 0) throw CatalogEndpoints.NotFound(type, id);
        var restricted = access.Facts.Exists(type.Kind, id)
            ? access.Facts.Restricted(type.Kind, id)
            : access.Facts.Restricted(type.Kind, Records.ParseObject(revisions[0].Snapshot)!);
        if (restricted && !access.Viewer.SeesRestricted) throw ApiProblem.Forbidden($"This {type.Noun} is restricted: only administrators can see its history.");
        return revisions;
    }

    private static Revision Pick(List<Revision> revisions, int version, RecordType type) =>
        revisions.FirstOrDefault(r => r.Version == version) ?? throw ApiProblem.NotFound($"No version {version} of this {type.Noun}.");
}

/// <summary>Instance settings: one row, created on the first save. Until then, the name comes from NOOSPHERE_INSTANCE_NAME.</summary>
public static class InstanceSettingsStore
{
    public static async Task<InstanceSettings> Get(NoosphereDbContext db, Settings config, CancellationToken cancel = default) =>
        await db.InstanceSettings.FirstOrDefaultAsync(cancel)
        ?? new InstanceSettings { InstanceName = config.InstanceName, OrganisationName = "", DefaultLanguage = "en", Version = 0 };

    public static SettingsDto Dto(InstanceSettings s) =>
        new(s.InstanceName, s.OrganisationName, s.DefaultLanguage, s.Version, s.Version == 0 ? null : s.UpdatedAt);
}
