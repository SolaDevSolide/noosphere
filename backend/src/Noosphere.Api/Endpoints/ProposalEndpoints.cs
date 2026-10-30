using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Endpoints;

/// <summary>Change proposals: members propose, owning teams (or administrators) accept or refuse.</summary>
public static class ProposalEndpoints
{
    public const int ListLimit = 200;

    public static void MapProposals(this RouteGroupBuilder api)
    {
        var proposals = api.MapGroup("/proposals").WithTags("Proposals");

        proposals.MapGet("", async ([FromQuery] string? status, [FromQuery] bool? mine, [FromQuery] bool? toValidate,
            ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            ProposalStatus? wanted = null;
            if (!string.IsNullOrWhiteSpace(status))
                wanted = Enum.TryParse<ProposalStatus>(status.Trim(), ignoreCase: true, out var s) && Enum.IsDefined(s) ? s
                    : throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["status"] = ["Expected draft, submitted, accepted, refused or withdrawn."] });
            var access = await Access.Load(db, user);
            var query = Proposals.Query(db).AsNoTracking();
            if (wanted is { } w) query = query.Where(p => p.Status == w);
            if (mine == true) query = query.Where(p => p.AuthorId == access.Viewer.Id);
            if (toValidate == true) query = query.Where(p => p.Status == ProposalStatus.Submitted);
            var list = (await query.OrderByDescending(p => p.SubmittedAt ?? p.CreatedAt).ToListAsync())
                .Where(p => Proposals.Visible(p, access) && (toValidate != true || Proposals.CanDecide(p, access)))
                .Take(ListLimit).ToList();
            var people = await People.Load(db);
            var result = new List<ProposalDto>();
            foreach (var p in list) result.Add(await Proposals.Dto(db, p, access, people));
            return result;
        });

        proposals.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var access = await Access.Load(db, user);
            return await Proposals.Dto(db, await Proposals.Find(db, id, access), access, await People.Load(db));
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound);

        proposals.MapPost("", async (ProposalInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var access = await Access.Load(db, user);
            var now = clock.GetUtcNow();
            var errors = new FieldErrors();
            var proposal = new Proposal
            {
                Title = errors.Required("title", input.Title),
                Comment = errors.Optional("comment", input.Comment, Rules.MaxTextLength),
                AuthorId = access.Viewer.Id, AuthorName = access.Viewer.Username, AuthorKind = AuthorKind.User,
                CreatedAt = now, UpdatedAt = now, Version = 1,
                Status = input.Submit == true ? ProposalStatus.Submitted : ProposalStatus.Draft,
                SubmittedAt = input.Submit == true ? now : null,
            };
            errors.ThrowIfAny();
            proposal.Changes = await Proposals.BuildChanges(db, proposal.Id, input.Changes, access);
            db.Proposals.Add(proposal);
            await db.SaveChangesAsync();
            return Results.Created($"/api/proposals/{proposal.Id}", await Proposals.Dto(db, proposal, access, await People.Load(db)));
        }).RequireEditor().Produces<ProposalDto>(StatusCodes.Status201Created);

        proposals.MapPut("/{id:guid}", async (Guid id, ProposalInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var access = await Access.Load(db, user);
            var proposal = await Proposals.Find(db, id, access, tracked: true);
            Proposals.RequireAuthorOpen(proposal, access);
            var errors = new FieldErrors();
            var title = errors.Required("title", input.Title);
            var comment = errors.Optional("comment", input.Comment, Rules.MaxTextLength);
            if (input.Version is null) errors.Add("version", "This field is required.");
            errors.ThrowIfAny();
            if (input.Version != proposal.Version) throw ApiProblem.Stale();
            var changes = await Proposals.BuildChanges(db, proposal.Id, input.Changes, access);
            db.ProposalChanges.RemoveRange(proposal.Changes);
            proposal.Changes.Clear();
            await db.SaveChangesAsync();
            proposal.Changes.AddRange(changes);
            db.ProposalChanges.AddRange(changes);
            Proposals.ClearApprovals(db, proposal);
            var now = clock.GetUtcNow();
            (proposal.Title, proposal.Comment, proposal.UpdatedAt) = (title, comment, now);
            if (input.Submit == true && proposal.Status == ProposalStatus.Draft) (proposal.Status, proposal.SubmittedAt) = (ProposalStatus.Submitted, now);
            proposal.Version++;
            await db.SaveChangesAsync();
            return await Proposals.Dto(db, proposal, access, await People.Load(db));
        }).RequireEditor();

        proposals.MapPost("/{id:guid}/submit", (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Act(id, user, db, clock, (p, access, now) =>
            {
                Proposals.RequireAuthorOpen(p, access);
                if (p.Status != ProposalStatus.Draft) throw ApiProblem.Conflict("This proposal is already submitted.");
                (p.Status, p.SubmittedAt) = (ProposalStatus.Submitted, now);
                return Task.CompletedTask;
            })).RequireEditor();

        proposals.MapPost("/{id:guid}/withdraw", (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Act(id, user, db, clock, (p, access, now) =>
            {
                Proposals.RequireAuthorOpen(p, access);
                p.Status = ProposalStatus.Withdrawn;
                p.DecidedAt = now;
                return Task.CompletedTask;
            })).RequireEditor();

        proposals.MapPost("/{id:guid}/accept", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await Act(id, user, db, clock, (p, access, now) => Proposals.Accept(db, p, access, now), bump: false);
            await transaction.CommitAsync();
            return result;
        }).RequireEditor().Produces<ProblemDto>(StatusCodes.Status409Conflict);

        proposals.MapPost("/{id:guid}/refuse", (Guid id, RefusalInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Act(id, user, db, clock, (p, access, now) =>
            {
                Proposals.Refuse(p, access, input.Reason, now);
                return Task.CompletedTask;
            }, bump: false)).RequireEditor();

        proposals.MapPost("/{id:guid}/rebase", (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            Act(id, user, db, clock, (p, access, now) => Proposals.Rebase(db, p, access, now), bump: false)).RequireEditor();

        api.MapGet("/me/inbox", async (ClaimsPrincipal user, NoosphereDbContext db) => await Proposals.Inbox(db, await Access.Load(db, user)))
            .WithTags("Proposals");
    }

    /// <summary>Loads a proposal (tracked), runs an action on it, saves and returns it.</summary>
    private static async Task<ProposalDto> Act(Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock,
        Func<Proposal, Access, DateTimeOffset, Task> action, bool bump = true)
    {
        var access = await Access.Load(db, user);
        var proposal = await Proposals.Find(db, id, access, tracked: true);
        var now = clock.GetUtcNow();
        await action(proposal, access, now);
        if (bump)
        {
            proposal.Version++;
            proposal.UpdatedAt = now;
        }
        await db.SaveChangesAsync();
        return await Proposals.Dto(db, proposal, await Access.Load(db, user), await People.Load(db));
    }
}
