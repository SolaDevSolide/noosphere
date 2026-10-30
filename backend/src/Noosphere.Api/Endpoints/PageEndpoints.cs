using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Endpoints;

/// <summary>
/// Documentation pages: Markdown with wiki links, in a tree, optionally attached to a record. Same rules as records:
/// owners publish (a page's owner team, or else the owner of the record it is attached to), others propose. Restricted
/// pages are shown to administrators, the owning team and the access list (others get a stub); guests see the title of
/// pages not shared with them.
/// </summary>
public static class PageEndpoints
{
    public static void MapPages(this RouteGroupBuilder api)
    {
        var pages = api.MapGroup("/pages").WithTags("Pages");

        pages.MapGet("", async ([FromQuery] string? attachedKind, [FromQuery] string? attachedId, [FromQuery] string? tag, [FromQuery] string? q,
            ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var access = await Access.Load(db, user);
            var facts = access.Facts;
            RecordKind? kind = null;
            if (!string.IsNullOrWhiteSpace(attachedKind))
                kind = Records.Parse(attachedKind.Trim())?.Kind
                    ?? throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["attachedKind"] = ["Expected application, flow, dataObject, domain or team."] });
            var folded = string.IsNullOrWhiteSpace(q) ? null : Rules.Fold(q.Trim());
            var list = (await db.Pages.AsNoTracking().ToListAsync())
                .Where(p => (kind is null || p.AttachedKind == kind) && (string.IsNullOrWhiteSpace(attachedId) || p.AttachedId == attachedId.Trim()))
                .Where(p => string.IsNullOrWhiteSpace(tag) || (access.SeesFully(RecordKind.Page, p.Id) && p.Tags.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase)))
                .Where(p => folded is null || access.Sight(RecordKind.Page, p) switch
                {
                    Sight.Full => Rules.Fold(p.Title).Contains(folded, StringComparison.Ordinal) || Rules.Fold(p.Body).Contains(folded, StringComparison.Ordinal),
                    Sight.Outline => Rules.Fold(p.Title).Contains(folded, StringComparison.Ordinal),
                    _ => false,
                })
                .OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
                .Select(p => PageQueries.StubFor(p, access) ?? PageQueries.Summary(p, facts))
                .ToList();
            return Results.Ok(list);
        }).Produces<List<PageSummaryDto>>();

        pages.MapGet("/tree", async (ClaimsPrincipal user, NoosphereDbContext db) =>
            PageQueries.Tree(await db.Pages.AsNoTracking().ToListAsync(), await Access.Load(db, user)));

        pages.MapGet("/{id}", async (string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var page = (Page)await CatalogEndpoints.Find(db, Records.Page, id);
            var access = await Access.Load(db, user);
            if (PageQueries.StubFor(page, access) is { } stub) return Results.Ok(stub);
            await AccessLogs.RecordRead(db, access, RecordKind.Page, page, notesShown: false, clock.GetUtcNow());
            return Results.Ok(await PageQueries.Detail(db, page, access));
        }).Produces<PageDetailDto>().Produces<ProblemDto>(StatusCodes.Status404NotFound);

        pages.MapGet("/{id}/raw", async (string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var page = (Page)await CatalogEndpoints.Find(db, Records.Page, id);
            var access = await Access.Load(db, user);
            switch (access.Sight(RecordKind.Page, page))
            {
                case Sight.Hidden: throw ApiProblem.Forbidden("This page is restricted: you are not on its access list.");
                case Sight.Outline: throw ApiProblem.Forbidden("This page is not shared with you.");
            }
            await AccessLogs.RecordRead(db, access, RecordKind.Page, page, notesShown: false, clock.GetUtcNow(), "raw");
            return Results.Text(PageQueries.Raw(page, access.Facts), "text/markdown; charset=utf-8");
        }).Produces<string>(contentType: "text/markdown").Produces<ProblemDto>(StatusCodes.Status403Forbidden);

        pages.MapPost("", (PageInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            CatalogEndpoints.Create(Records.Page, input, user, db, clock, (e, _) => Task.FromResult<object>(PageQueries.Dto((Page)e))))
            .RequireEditor().Produces<PageDto>(StatusCodes.Status201Created);

        pages.MapPut("/{id}", async (string id, PageInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            PageQueries.Dto((Page)await CatalogEndpoints.Update(Records.Page, id, input, input.Id, input.Version, user, db, clock)))
            .RequireEditor();

        pages.MapDelete("/{id}", (string id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            CatalogEndpoints.Delete(Records.Page, id, user, db, clock))
            .RequireEditor();

        pages.MapPost("/{id}/move", async (string id, PageMoveInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var page = (Page)await CatalogEndpoints.Find(db, Records.Page, id, tracked: true);
            var access = await Access.Load(db, user);
            access.RequirePublish(RecordKind.Page, page);
            if (input.Version is { } version && version != page.Version) throw ApiProblem.Stale();
            var body = Records.Page.Snapshot(page);
            if (string.IsNullOrWhiteSpace(input.ParentId)) body.Remove("parentId");
            else body["parentId"] = input.ParentId.Trim();
            body["order"] = input.Order ?? 0;
            new Publisher(db, clock.GetUtcNow()).Update(Records.Page, page, body, Author.Of(access.Viewer), await References.Load(db), summary: "Moved");
            await db.SaveChangesAsync();
            return PageQueries.Dto(page);
        }).RequireEditor();
    }
}
