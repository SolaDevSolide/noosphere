using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Agents;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Endpoints;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Links;

/// <summary>
/// Links with gestion-mep, for administrators: pairing codes, links, confirmation, capabilities, key renewal, cut and the
/// exchange log. The deployments of a record are read by whoever sees it.
/// </summary>
public static class LinkAdminEndpoints
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    /// <summary>The peer's status is read again when a link's page is opened after this long.</summary>
    public static readonly TimeSpan StatusRefresh = TimeSpan.FromMinutes(1);

    public static void MapLinkAdmin(this RouteGroupBuilder api, Settings settings)
    {
        var links = api.MapGroup("/links").WithTags("Links").RequireAdmin();

        links.MapGet("/codes", async (NoosphereDbContext db, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow();
            return (await db.LinkCodes.AsNoTracking().OrderByDescending(c => c.CreatedAt).Take(100).ToListAsync()).Select(c => LinkAdmin.Dto(c, now)).ToList();
        });

        links.MapPost("/codes", async (LinkCodeInput input, ClaimsPrincipal user, LinkAdmin admin, HttpRequest request) =>
        {
            var errors = new FieldErrors();
            var minutes = input.ExpiresInMinutes ?? (int)LinkProtocol.CodeLifetime.TotalMinutes;
            if (minutes is < 1 or > 1440) errors.Add("expiresInMinutes", "Between 1 and 1440 minutes.");
            var capabilities = LinkAdmin.Capabilities(input.Capabilities, errors);
            errors.ThrowIfAny();
            var (code, value) = await admin.CreateCode(Viewer.From(user).Username, TimeSpan.FromMinutes(minutes), capabilities);
            return Results.Created($"/api/links/codes/{code.Id}",
                new LinkCodeCreatedDto(code.Id, value, code.CodeId, code.ExpiresAt, code.Capabilities, PublicAddress.Of(request, settings)));
        }).Produces<LinkCodeCreatedDto>(StatusCodes.Status201Created);

        links.MapGet("", async (NoosphereDbContext db) =>
            (await db.Links.AsNoTracking().OrderBy(l => l.State == LinkState.Cut).ThenByDescending(l => l.PairedAt).ToListAsync()).Select(LinkAdmin.Dto).ToList());

        links.MapGet("/{id:guid}", async (Guid id, [FromQuery] bool? refresh, LinkAdmin admin, TimeProvider clock) =>
        {
            var link = await admin.Find(id);
            var stale = link.PeerCheckedAt is not { } checkedAt || clock.GetUtcNow() - checkedAt > StatusRefresh;
            if (link.State != LinkState.Cut && !link.Demo && (refresh ?? stale)) await admin.RefreshStatus(link);
            return LinkAdmin.Dto(link);
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound);

        links.MapPost("/{id:guid}/confirm", async (Guid id, LinkConfirmInput input, ClaimsPrincipal user, LinkAdmin admin) =>
            LinkAdmin.Dto(await admin.Confirm(await admin.Find(id), input.Fingerprint, Viewer.From(user).Username)))
            .Produces<ProblemDto>(StatusCodes.Status422UnprocessableEntity);

        links.MapPut("/{id:guid}/capabilities", async (Guid id, LinkCapabilitiesInput input, LinkAdmin admin) =>
        {
            var errors = new FieldErrors();
            if (input.Capabilities is null) errors.Add("capabilities", "This field is required.");
            var capabilities = LinkAdmin.Capabilities(input.Capabilities ?? [], errors);
            errors.ThrowIfAny();
            return LinkAdmin.Dto(await admin.SetCapabilities(await admin.Find(id), capabilities));
        });

        links.MapPost("/{id:guid}/renew-keys", async (Guid id, LinkAdmin admin) => LinkAdmin.Dto(await admin.RenewKeys(await admin.Find(id))))
            .Produces<ProblemDto>(StatusCodes.Status502BadGateway);

        links.MapPost("/{id:guid}/cut", async (Guid id, LinkCutInput input, ClaimsPrincipal user, LinkAdmin admin) =>
        {
            if (input.Confirm != true) throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["confirm"] = ["Cutting a link cannot be undone: confirm with confirm: true."] });
            return LinkAdmin.Dto((await admin.Cut(await admin.Find(id), Viewer.From(user).Username)).Link);
        });

        links.MapGet("/{id:guid}/exchanges", async (Guid id, [FromQuery] string? cursor, [FromQuery] int? limit, LinkAdmin admin, NoosphereDbContext db) =>
        {
            await admin.Find(id);
            return await Exchanges(db.LinkExchanges.AsNoTracking().Where(x => x.LinkId == id), cursor, limit);
        });

        // Refused requests whose key is unknown belong to no link.
        links.MapGet("/exchanges", async ([FromQuery] string? cursor, [FromQuery] int? limit, NoosphereDbContext db) =>
            await Exchanges(db.LinkExchanges.AsNoTracking(), cursor, limit));

        foreach (var type in new[] { Records.Application, Records.Flow })
        {
            api.MapGet($"/{type.Plural}/{{id}}/deployments", async (string id, ClaimsPrincipal user, NoosphereDbContext db) =>
            {
                var e = await CatalogEndpoints.Find(db, type, id);
                var access = await Access.Load(db, user);
                switch (access.Sight(type.Kind, e))
                {
                    case Sight.Hidden: throw ApiProblem.Forbidden($"This {type.Noun} is restricted: you are not on its access list.");
                    case Sight.Outline: throw ApiProblem.Forbidden($"This {type.Noun} is not shared with you.");
                }
                return await Deployments.Of(db, access, type.Kind, id);
            })
                .WithTags("Deployments").WithName($"Deployments{type.Kind}")
                .Produces<ProblemDto>(StatusCodes.Status403Forbidden).Produces<ProblemDto>(StatusCodes.Status404NotFound);
        }
    }

    private static async Task<LinkExchangePageDto> Exchanges(IQueryable<LinkExchange> query, string? cursor, int? limit)
    {
        var errors = new FieldErrors();
        long? before = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var c)) before = c;
            else errors.Add("cursor", "Pass the nextCursor of the previous page.");
        }
        var size = limit ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize) errors.Add("limit", $"Between 1 and {MaxPageSize}.");
        errors.ThrowIfAny();
        if (before is { } until) query = query.Where(x => x.Id < until);
        var rows = await query.OrderByDescending(x => x.Id).Take(size + 1).ToListAsync();
        var items = rows.Take(size).Select(x => new LinkExchangeDto(x.Id, x.At, x.LinkId, x.Direction, x.Operation, x.Result, x.Status, x.Reason, x.Detail)).ToList();
        return new LinkExchangePageDto(items, rows.Count > size ? items[^1].Id.ToString(CultureInfo.InvariantCulture) : null);
    }
}
