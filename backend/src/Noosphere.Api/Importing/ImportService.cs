using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Importing;

/// <summary>
/// Stores the plan of a dry run, and applies it later by id: by the same person, before it expires, and only if the
/// catalogue has not changed since (the plan is rejected otherwise). Applying creates and updates records, each with a
/// revision naming the import; it never deletes. Every plan stays in <c>ImportPlans</c> as the record of the import.
/// </summary>
public static class ImportService
{
    /// <summary>Taken while a plan is applied, so that two plans are never applied at the same time.</summary>
    private const long LockKey = 7_313_033;

    private static readonly JsonSerializerOptions Json = Records.Json;

    /// <summary>Id of the last revision: it changes with every write to the catalogue.</summary>
    public static async Task<long> Stamp(NoosphereDbContext db, CancellationToken cancel = default) =>
        await db.Revisions.MaxAsync(r => (long?)r.Id, cancel) ?? 0;

    /// <summary>Runs the dry run and stores its plan.</summary>
    public static async Task<ImportPlanDto> Plan(NoosphereDbContext db, Access access, IReadOnlyList<ImportFile> files, ImportOptions options,
        DateTimeOffset now, ILogger logger, CancellationToken cancel = default)
    {
        await Expire(db, access, now, cancel);
        var stamp = await Stamp(db, cancel);
        var outcome = await new ImportPlanner(db, access).Plan(files, options, cancel);
        var counts = Counts(outcome.Entries, outcome.Warnings.Count, outcome.Errors.Count);
        var plan = new ImportPlan
        {
            UserId = access.Viewer.Id == Guid.Empty ? null : access.Viewer.Id,
            Username = access.Viewer.Id == Guid.Empty ? Author.System.Name : access.Viewer.Username,
            Status = ImportStatus.Planned,
            CreatedAt = now,
            ExpiresAt = now + ImportLimits.PlanLifetime,
            Formats = string.Join(',', outcome.Formats),
            CatalogueStamp = stamp,
            Creates = counts.Create, Updates = counts.Update, Unchanged = counts.Unchanged, Warnings = counts.Warnings, Errors = counts.Errors,
            Preview = JsonSerializer.Serialize(new StoredPreview(outcome.Files, outcome.Entries, outcome.Warnings, outcome.Errors), Json),
            Operations = JsonSerializer.Serialize(outcome.Operations, Json),
        };
        db.ImportPlans.Add(plan);
        await db.SaveChangesAsync(cancel);
        logger.LogInformation("Import {Id} planned by {User}: {Formats}, {Files} file(s), {Create} to create, {Update} to update, {Errors} error(s)",
            plan.Id, plan.Username, plan.Formats, outcome.Files.Count, counts.Create, counts.Update, counts.Errors);
        return Dto(plan, now);
    }

    /// <summary>The plan, for the person who made it (404 for anyone else).</summary>
    public static async Task<ImportPlanDto> Get(NoosphereDbContext db, Access access, Guid id, DateTimeOffset now, CancellationToken cancel = default)
    {
        var plan = await Own(db, access, id, cancel);
        return Dto(plan, now);
    }

    /// <summary>Recent imports: one's own, or everyone's for administrators.</summary>
    public static async Task<List<ImportSummaryDto>> List(NoosphereDbContext db, Access access, DateTimeOffset now, int limit, CancellationToken cancel = default)
    {
        var query = db.ImportPlans.AsNoTracking();
        if (!access.IsAdmin) query = query.Where(p => p.UserId == access.Viewer.Id);
        var plans = await query.OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(limit, 1, 200))
            .Select(p => new { p.Id, p.Status, p.Username, p.CreatedAt, p.ExpiresAt, p.ClosedAt, p.Outcome, p.Formats, p.Creates, p.Updates, p.Unchanged, p.Warnings, p.Errors })
            .ToListAsync(cancel);
        return [.. plans.Select(p => new ImportSummaryDto(p.Id, Status(p.Status, p.ExpiresAt, now), p.Username, p.CreatedAt, p.ExpiresAt, p.ClosedAt, p.Outcome,
            Formats(p.Formats), new ImportCountsDto(p.Creates, p.Updates, p.Unchanged, p.Warnings, p.Errors)))];
    }

    /// <summary>
    /// Applies a stored plan: 404 when it is not the person's, 409 when it was applied or rejected already, or when the
    /// catalogue changed since the dry run (the plan is then rejected), 410 once expired, 422 when it has errors.
    /// </summary>
    public static async Task<ImportPlanDto> Apply(NoosphereDbContext db, Access access, Guid id, DateTimeOffset now, ILogger logger, CancellationToken cancel = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancel);
        await db.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock({LockKey})", cancel);
        var plan = await Own(db, access, id, cancel, tracked: true);
        switch (plan.Status)
        {
            case ImportStatus.Applied: throw ApiProblem.Conflict("This import was applied already.");
            case ImportStatus.Rejected: throw ApiProblem.Conflict($"This import was rejected: {plan.Outcome} Run the dry run again.");
            case ImportStatus.Expired: throw new ApiProblem(410, "This import plan expired. Run the dry run again.");
        }
        async Task<ApiProblem> Close(ImportStatus status, ApiProblem problem)
        {
            (plan.Status, plan.ClosedAt, plan.Outcome, plan.Operations) = (status, now, problem.Message, null);
            await db.SaveChangesAsync(cancel);
            await transaction.CommitAsync(cancel);
            logger.LogInformation("Import {Id} {Status}: {Outcome}", plan.Id, status, problem.Message);
            return problem;
        }
        if (now > plan.ExpiresAt) throw await Close(ImportStatus.Expired, new ApiProblem(410, "This import plan expired. Run the dry run again."));
        if (plan.Errors > 0) throw ApiProblem.Unprocessable($"This import has {plan.Errors} error(s): fix the files and run the dry run again.");
        if (await Stamp(db, cancel) != plan.CatalogueStamp)
            throw await Close(ImportStatus.Rejected, ApiProblem.Conflict("The catalogue changed since the dry run, so this plan may be wrong. Run the dry run again."));

        var operations = JsonSerializer.Deserialize<List<PlannedOperation>>(plan.Operations ?? "[]", Json) ?? [];
        var author = Author.Of(access.Viewer);
        var summary = $"Imported ({plan.Formats.Replace(",", ", ", StringComparison.Ordinal)}, import {plan.Id.ToString()[..8]})";
        try
        {
            var refs = await References.Load(db, cancel);
            var publisher = new Publisher(db, now);
            foreach (var op in operations)
            {
                var type = Records.Of(op.Kind);
                if (op.Create)
                {
                    var created = await publisher.Create(type, op.After, author, refs, summary: summary, cancel: cancel);
                    access.RequirePublish(op.Kind, created);
                    continue;
                }
                var e = await type.Find(db, op.Id, tracked: true, cancel)
                    ?? throw ApiProblem.Conflict($"The {type.Noun} '{op.Id}' no longer exists. Run the dry run again.");
                if (e.Version != op.BaseVersion) throw ApiProblem.Conflict($"The {type.Noun} '{op.Id}' changed since the dry run. Run the dry run again.");
                access.RequirePublish(op.Kind, e);
                publisher.Update(type, e, op.After, author, refs, summary: summary);
                access.RequirePublish(op.Kind, e);
            }
            (plan.Status, plan.ClosedAt, plan.Operations) = (ImportStatus.Applied, now, null);
            await db.SaveChangesAsync(cancel);
            await transaction.CommitAsync(cancel);
        }
        catch (ApiProblem problem)
        {
            await transaction.RollbackAsync(cancel);
            db.ChangeTracker.Clear();
            var again = await db.ImportPlans.FirstAsync(p => p.Id == id, cancel);
            (again.Status, again.ClosedAt, again.Outcome, again.Operations) = (ImportStatus.Rejected, now, Short(problem.Message), null);
            await db.SaveChangesAsync(cancel);
            logger.LogInformation("Import {Id} rejected: {Outcome}", id, problem.Message);
            throw problem.Status == 400 ? ApiProblem.Conflict($"The plan no longer applies: {problem.Message} Run the dry run again.") : problem;
        }
        logger.LogInformation("Import {Id} applied by {User}: {Create} created, {Update} updated", plan.Id, access.Viewer.Username, plan.Creates, plan.Updates);
        return Dto(plan, now);
    }

    private static string Short(string text) => text.Length <= Rules.MaxShortTextLength ? text : text[..Rules.MaxShortTextLength];

    private static async Task<ImportPlan> Own(NoosphereDbContext db, Access access, Guid id, CancellationToken cancel, bool tracked = false)
    {
        var owner = access.Viewer.Id == Guid.Empty ? (Guid?)null : access.Viewer.Id;
        var query = tracked ? db.ImportPlans : db.ImportPlans.AsNoTracking();
        return await query.FirstOrDefaultAsync(p => p.Id == id && p.UserId == owner, cancel)
            ?? throw ApiProblem.NotFound($"No import plan with the id '{id}' of yours.");
    }

    /// <summary>Plans of this person left planned past their expiry are marked expired and lose their operations.</summary>
    private static async Task Expire(NoosphereDbContext db, Access access, DateTimeOffset now, CancellationToken cancel)
    {
        var owner = access.Viewer.Id == Guid.Empty ? (Guid?)null : access.Viewer.Id;
        await db.ImportPlans.Where(p => p.UserId == owner && p.Status == ImportStatus.Planned && p.ExpiresAt < now)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ImportStatus.Expired).SetProperty(p => p.ClosedAt, now).SetProperty(p => p.Operations, (string?)null), cancel);
    }

    private static ImportStatus Status(ImportStatus status, DateTimeOffset expires, DateTimeOffset now) =>
        status == ImportStatus.Planned && now > expires ? ImportStatus.Expired : status;

    private static List<string> Formats(string formats) => [.. formats.Split(',', StringSplitOptions.RemoveEmptyEntries)];

    private static ImportCountsDto Counts(IReadOnlyList<ImportEntryDto> entries, int warnings, int errors) => new(
        entries.Count(e => e.Action == "create"), entries.Count(e => e.Action == "update"), entries.Count(e => e.Action == "unchanged"), warnings, errors);

    private sealed record StoredPreview(IReadOnlyList<ImportFileDto> Files, IReadOnlyList<ImportEntryDto> Entries,
        IReadOnlyList<ImportMessageDto> Warnings, IReadOnlyList<ImportMessageDto> Errors);

    private static ImportPlanDto Dto(ImportPlan plan, DateTimeOffset now)
    {
        var preview = JsonSerializer.Deserialize<StoredPreview>(plan.Preview, Json) ?? new StoredPreview([], [], [], []);
        var status = Status(plan.Status, plan.ExpiresAt, now);
        return new ImportPlanDto(plan.Id, status, plan.Username, plan.CreatedAt, plan.ExpiresAt, plan.ClosedAt, plan.Outcome, Formats(plan.Formats),
            preview.Files, new ImportCountsDto(plan.Creates, plan.Updates, plan.Unchanged, plan.Warnings, plan.Errors),
            preview.Entries, preview.Warnings, preview.Errors, status == ImportStatus.Planned && plan.Errors == 0);
    }
}
