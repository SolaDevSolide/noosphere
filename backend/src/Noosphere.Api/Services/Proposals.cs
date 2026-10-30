using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>Display names of accounts, for authors and validators. A deleted account is shown by the username it had.</summary>
public sealed class People(IReadOnlyDictionary<Guid, (string Username, string DisplayName)> users)
{
    public static async Task<People> Load(NoosphereDbContext db, CancellationToken cancel = default) =>
        new((await db.Users.AsNoTracking().Select(u => new { u.Id, u.Username, u.DisplayName }).ToListAsync(cancel))
            .ToDictionary(u => u.Id, u => (u.Username, u.DisplayName)));

    public UserRefDto? Ref(Guid? id, string? name) =>
        id is { } key && users.TryGetValue(key, out var u) ? new UserRefDto(u.Username, u.DisplayName)
        : id is null ? null
        : new UserRefDto(name ?? "", name ?? "");
}

/// <summary>
/// Change proposals: any member proposes, the owning teams validate (one validator per team, or one administrator), and
/// an accepted proposal is published with one revision per change.
/// </summary>
public static class Proposals
{
    public const int MaxChanges = 50;

    public static IQueryable<Proposal> Query(NoosphereDbContext db) =>
        db.Proposals.Include(p => p.Changes.OrderBy(c => c.Position)).Include(p => p.Approvals).AsSplitQuery();

    public static async Task<Proposal> Find(NoosphereDbContext db, Guid id, Access access, bool tracked = false, CancellationToken cancel = default)
    {
        var query = Query(db);
        var proposal = await (tracked ? query : query.AsNoTracking()).FirstOrDefaultAsync(p => p.Id == id, cancel);
        if (proposal is null || !Visible(proposal, access)) throw ApiProblem.NotFound($"No proposal with the id '{id}'.");
        return proposal;
    }

    /// <summary>Drafts are seen by their author only; other proposals by those who see all their records in full (and the author).</summary>
    public static bool Visible(Proposal p, Access access) =>
        p.AuthorId == access.Viewer.Id
        || (p.Status != ProposalStatus.Draft && !p.Changes.Any(c => Hidden(c, access)));

    private static bool Hidden(ProposalChange c, Access access) =>
        (c.TargetId is { } id && access.Facts.Exists(c.Kind, id) && access.Sight(c.Kind, id) != Sight.Full)
        || (Records.ParseObject(c.After) is { } after && access.Sight(c.Kind, after) != Sight.Full);

    // ---------- Owners and validators ----------

    /// <summary>Owning team of each change's target (null: administrators only).</summary>
    public static List<string?> Owners(Proposal p, RecordFacts facts) =>
        [.. p.Changes.Select(c => c.Operation == ChangeOperation.Create || c.TargetId is null || !facts.Exists(c.Kind, c.TargetId)
            ? (Records.ParseObject(c.After ?? c.Before) is { } snapshot ? facts.Owner(c.Kind, snapshot) : null)
            : facts.Owner(c.Kind, c.TargetId)).Distinct()];

    /// <summary>The reader may accept or refuse: submitted, not their own, and they are an administrator or in an owning team that has not approved yet.</summary>
    public static bool CanDecide(Proposal p, Access access)
    {
        if (p.Status != ProposalStatus.Submitted || p.AuthorId == access.Viewer.Id || !access.Viewer.CanEdit) return false;
        if (access.Viewer.IsAdmin) return true;
        return Owners(p, access.Facts).Any(t => t is not null && access.InTeam(t) && p.Approvals.All(a => a.TeamId != t));
    }

    private static bool IsValidator(Proposal p, Access access) =>
        access.Viewer.CanEdit && (access.Viewer.IsAdmin || Owners(p, access.Facts).Any(access.InTeam));

    public static bool CanEdit(Proposal p, Access access) =>
        p.AuthorId == access.Viewer.Id && p.Status is ProposalStatus.Draft or ProposalStatus.Submitted;

    // ---------- Reading ----------

    public static async Task<ProposalDto> Dto(NoosphereDbContext db, Proposal p, Access access, People people, CancellationToken cancel = default)
    {
        var changes = new List<ProposalChangeDto>();
        foreach (var c in p.Changes.OrderBy(c => c.Position))
        {
            var type = Records.Of(c.Kind);
            var current = c.TargetId is null || c.Operation == ChangeOperation.Create ? null : await type.Find(db, c.TargetId, tracked: false, cancel);
            var before = Records.ParseObject(c.Before);
            var after = Records.ParseObject(c.After);
            var title = c.Operation == ChangeOperation.Create
                ? (after is null ? null : type.Title(after))
                : current is not null ? type.Title(current) : before is null ? null : type.Title(before);
            var outdated = c.Operation != ChangeOperation.Create && (current is null || current.Version != c.BaseVersion);
            // Protected notes are masked for those who may not read them; the author still sees the notes they wrote.
            var readable = current is not null ? access.ReadsProtected(c.Kind, current)
                : (after ?? before) is { } state && access.ReadsProtected(c.Kind, state);
            var ownNotes = p.AuthorId == access.Viewer.Id && !Diffs.Same(before, after, ProtectedNotesMask.Field);
            var diff = ProtectedNotesMask.Mask(Diffs.Compare(type, before, after, access.Facts, access), !readable, !readable && !ownNotes);
            changes.Add(new ProposalChangeDto(c.Kind, c.TargetId ?? (after?["id"] as JsonValue)?.GetValue<string>(), title, c.Operation,
                c.BaseVersion, current?.Version, outdated, readable ? before : ProtectedNotesMask.Mask(before),
                readable || ownNotes ? after : ProtectedNotesMask.Mask(after), diff, c.Conflicts));
        }
        var owners = Owners(p, access.Facts);
        var ownerTeams = owners.OfType<string>().Order(StringComparer.Ordinal).Select(t =>
        {
            var approval = p.Approvals.FirstOrDefault(a => a.TeamId == t);
            return new ProposalOwnerDto(t, access.Facts.TeamName(t) ?? t, approval is null ? null : people.Ref(approval.UserId, approval.UserName), approval?.ApprovedAt);
        }).ToList();
        var open = p.Status is ProposalStatus.Draft or ProposalStatus.Submitted;
        return new ProposalDto(p.Id, p.Title, p.Comment, p.Status, p.AuthorKind, people.Ref(p.AuthorId, p.AuthorName)!, p.OnBehalfOf,
            p.CreatedAt, p.SubmittedAt, people.Ref(p.DecidedById, p.DecidedByName), p.DecidedAt, p.RefusalReason, p.Version,
            open && changes.Any(c => c.Outdated), ownerTeams, owners.Contains(null), CanDecide(p, access), CanEdit(p, access), changes);
    }

    // ---------- Writing ----------

    /// <summary>Validates the changes of a proposal and builds them, with their before snapshots.</summary>
    public static async Task<List<ProposalChange>> BuildChanges(NoosphereDbContext db, Guid proposalId, List<ProposalChangeInput>? inputs, Access access, CancellationToken cancel = default)
    {
        var errors = new FieldErrors();
        if (inputs is null || inputs.Count == 0) errors.Add("changes", "A proposal holds at least one change.");
        else if (inputs.Count > MaxChanges) errors.Add("changes", $"At most {MaxChanges} changes.");
        errors.ThrowIfAny();

        var refs = await References.Load(db, cancel);
        var changes = new List<ProposalChange>();
        var targets = new HashSet<(RecordKind, string)>();
        for (var i = 0; i < inputs!.Count; i++)
        {
            var input = inputs[i];
            var at = $"changes[{i}].";
            if (input.Kind is not { } kind) { errors.Add(at + "kind", "This field is required."); continue; }
            if (input.Operation is not { } operation) { errors.Add(at + "operation", "This field is required."); continue; }
            var type = Records.Of(kind);
            if (!type.Proposable) { errors.Add(at + "kind", "Domains and teams are changed by administrators, not through proposals."); continue; }

            var change = new ProposalChange { ProposalId = proposalId, Position = i, Kind = kind, Operation = operation };
            VersionedEntity? current = null;
            if (operation is ChangeOperation.Update or ChangeOperation.Delete)
            {
                var id = input.TargetId?.Trim();
                if (string.IsNullOrEmpty(id)) { errors.Add(at + "targetId", "This field is required."); continue; }
                current = await type.Find(db, id, tracked: false, cancel);
                if (current is null) { errors.Add(at + "targetId", $"Unknown {type.Noun} '{id}'."); continue; }
                if (!access.CanPropose(type, current))
                    throw ApiProblem.Forbidden(access.Viewer.CanEdit ? $"This {type.Noun} is restricted: you are not on its access list." : "Your role does not allow this action.");
                if (!targets.Add((kind, id))) { errors.Add(at + "targetId", "The same record is changed twice in this proposal."); continue; }
                if (input.BaseVersion is not { } baseVersion) { errors.Add(at + "baseVersion", "This field is required."); continue; }
                var before = baseVersion == current.Version ? type.Snapshot(current)
                    : Records.ParseObject(await db.Revisions.Where(r => r.Kind == kind && r.EntityId == id && r.Version == baseVersion)
                        .Select(r => r.Snapshot).FirstOrDefaultAsync(cancel));
                if (before is null) { errors.Add(at + "baseVersion", $"No version {baseVersion} of this {type.Noun}."); continue; }
                (change.TargetId, change.BaseVersion, change.Before) = (id, baseVersion, Records.Serialize(before));
            }
            if (operation is ChangeOperation.Create or ChangeOperation.Update)
            {
                if (input.After is null) { errors.Add(at + "after", "This field is required."); continue; }
                var scratch = type.Blank();
                var fieldErrors = new FieldErrors();
                if (operation == ChangeOperation.Create)
                {
                    var requested = input.After["id"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
                    var name = (kind == RecordKind.Page ? input.After["title"] : input.After["name"]) is JsonValue n && n.TryGetValue<string>(out var t) ? t : "";
                    try
                    {
                        scratch.Id = Catalog.NewId(requested, name, refs.Of(kind), fieldErrors);
                    }
                    catch (ApiProblem p) when (p.Status == 409)
                    {
                        fieldErrors.Add("id", p.Message);
                    }
                }
                else scratch.Id = change.TargetId!;
                var after = input.After;
                if (current is not null) after = ProtectedNotesMask.KeepHidden(type, current, after);
                type.Apply(scratch, after, fieldErrors, refs);
                fieldErrors.CopyTo(errors, $"{at}after.");
                if (fieldErrors.Any) continue;
                access.RequireStillVisible(type, scratch);
                if (operation == ChangeOperation.Create) refs.Add(kind, scratch.Id);
                change.After = Records.Serialize(type.Snapshot(scratch));
            }
            changes.Add(change);
        }
        errors.ThrowIfAny();
        return changes;
    }

    /// <summary>Accepts on behalf of the reader's owning teams (or all, for an administrator); publishes once every owner accepted.</summary>
    public static async Task Accept(NoosphereDbContext db, Proposal p, Access access, DateTimeOffset now, CancellationToken cancel = default)
    {
        if (p.Status != ProposalStatus.Submitted) throw ApiProblem.Conflict("Only a proposal waiting for validation can be accepted.");
        if (p.AuthorId == access.Viewer.Id) throw ApiProblem.Forbidden("You cannot validate your own proposal.");
        if (!CanDecide(p, access)) throw ApiProblem.Forbidden("Only administrators and members of the owning teams validate this proposal.");

        foreach (var c in p.Changes.Where(c => c.Operation != ChangeOperation.Create))
        {
            var current = await Records.Of(c.Kind).Find(db, c.TargetId!, tracked: false, cancel);
            if (current is null || current.Version != c.BaseVersion)
                throw new ApiProblem(409, current is null
                    ? "A record of this proposal was deleted in the meantime. The author should update the proposal."
                    : "A record of this proposal changed in the meantime. The author should update the proposal from the latest version.", outdated: true);
        }

        var owners = Owners(p, access.Facts);
        if (!access.Viewer.IsAdmin)
        {
            foreach (var team in owners.OfType<string>().Where(t => access.InTeam(t) && p.Approvals.All(a => a.TeamId != t)))
            {
                var approval = new ProposalApproval { ProposalId = p.Id, TeamId = team, UserId = access.Viewer.Id, UserName = access.Viewer.Username, ApprovedAt = now };
                p.Approvals.Add(approval);
                db.ProposalApprovals.Add(approval);
            }
            if (owners.Contains(null) || owners.OfType<string>().Any(t => p.Approvals.All(a => a.TeamId != t)))
            {
                p.Version++;
                p.UpdatedAt = now;
                return;
            }
        }

        var publisher = new Publisher(db, now);
        var refs = await References.Load(db, cancel);
        var author = new Author(p.AuthorId, p.AuthorName, p.AuthorKind);
        foreach (var c in p.Changes.OrderBy(c => c.Position))
        {
            var type = Records.Of(c.Kind);
            var prefix = $"changes[{c.Position}].after.";
            switch (c.Operation)
            {
                case ChangeOperation.Create:
                    var created = await publisher.Create(type, Records.ParseObject(c.After)!, author, refs, p.Id, p.Title, prefix, cancel);
                    c.TargetId = created.Id;
                    break;
                case ChangeOperation.Update:
                    publisher.Update(type, (await type.Find(db, c.TargetId!, tracked: true, cancel))!, Records.ParseObject(c.After)!, author, refs, p.Id, p.Title, fieldPrefix: prefix);
                    break;
                default:
                    await publisher.Delete(type, (await type.Find(db, c.TargetId!, tracked: true, cancel))!, author, p.Id, p.Title, cancel);
                    break;
            }
        }
        p.Status = ProposalStatus.Accepted;
        (p.DecidedById, p.DecidedByName, p.DecidedAt) = (access.Viewer.Id, access.Viewer.Username, now);
        p.Version++;
        p.UpdatedAt = now;
    }

    public static void Refuse(Proposal p, Access access, string? reason, DateTimeOffset now)
    {
        var text = reason?.Trim() ?? "";
        if (text.Length is < Rules.MinReasonLength or > Rules.MaxShortTextLength)
            throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["reason"] = [$"Give a reason of {Rules.MinReasonLength} to {Rules.MaxShortTextLength} characters."] });
        if (p.Status != ProposalStatus.Submitted) throw ApiProblem.Conflict("Only a proposal waiting for validation can be refused.");
        if (p.AuthorId == access.Viewer.Id) throw ApiProblem.Forbidden("You cannot refuse your own proposal: withdraw it instead.");
        if (!IsValidator(p, access)) throw ApiProblem.Forbidden("Only administrators and members of the owning teams validate this proposal.");
        var errors = new FieldErrors();
        errors.CheckSecret("reason", text);
        errors.ThrowIfAny();
        p.Status = ProposalStatus.Refused;
        p.RefusalReason = text;
        (p.DecidedById, p.DecidedByName, p.DecidedAt) = (access.Viewer.Id, access.Viewer.Username, now);
        p.Version++;
        p.UpdatedAt = now;
    }

    /// <summary>
    /// Moves the changes onto the latest version of their targets: fields the author changed keep the author's value, the
    /// others take the latest value. A field changed on both sides (to different values) is listed in <c>conflicts</c>.
    /// Approvals already given are dropped.
    /// </summary>
    public static async Task Rebase(NoosphereDbContext db, Proposal p, Access access, DateTimeOffset now, CancellationToken cancel = default)
    {
        RequireAuthorOpen(p, access);
        var refs = await References.Load(db, cancel);
        var errors = new FieldErrors();
        foreach (var c in p.Changes.Where(c => c.Operation != ChangeOperation.Create))
        {
            var type = Records.Of(c.Kind);
            var current = await type.Find(db, c.TargetId!, tracked: false, cancel)
                ?? throw ApiProblem.Conflict($"The {type.Noun} '{c.TargetId}' was deleted: remove this change from the proposal.");
            if (current.Version == c.BaseVersion) continue;
            var latest = type.Snapshot(current);
            var conflicts = new List<string>();
            if (c.Operation == ChangeOperation.Update)
            {
                var before = Records.ParseObject(c.Before);
                var mine = Records.ParseObject(c.After)!;
                var merged = (JsonObject)latest.DeepClone();
                foreach (var field in type.Fields.Select(f => f.Field))
                {
                    if (Diffs.Same(before, mine, field)) continue;
                    if (mine[field] is { } value) merged[field] = value.DeepClone();
                    else merged.Remove(field);
                    if (!Diffs.Same(before, latest, field) && !Diffs.Same(latest, mine, field)) conflicts.Add(field);
                }
                var scratch = type.Blank();
                scratch.Id = current.Id;
                var fieldErrors = new FieldErrors();
                type.Apply(scratch, merged, fieldErrors, refs);
                fieldErrors.CopyTo(errors, $"changes[{c.Position}].after.");
                c.After = Records.Serialize(type.Snapshot(scratch));
            }
            (c.Before, c.BaseVersion, c.Conflicts) = (Records.Serialize(latest), current.Version, conflicts);
        }
        errors.ThrowIfAny();
        ClearApprovals(db, p);
        p.Version++;
        p.UpdatedAt = now;
    }

    public static void RequireAuthorOpen(Proposal p, Access access)
    {
        if (p.AuthorId != access.Viewer.Id) throw ApiProblem.Forbidden("Only the author changes this proposal.");
        if (p.Status is not (ProposalStatus.Draft or ProposalStatus.Submitted)) throw ApiProblem.Conflict("This proposal is closed.");
    }

    public static void ClearApprovals(NoosphereDbContext db, Proposal p)
    {
        db.ProposalApprovals.RemoveRange(p.Approvals);
        p.Approvals.Clear();
    }

    /// <summary>Open proposals the reader may decide on, and their own open ones.</summary>
    public static async Task<InboxDto> Inbox(NoosphereDbContext db, Access access, CancellationToken cancel = default)
    {
        var mine = await db.Proposals.CountAsync(p => p.AuthorId == access.Viewer.Id && (p.Status == ProposalStatus.Draft || p.Status == ProposalStatus.Submitted), cancel);
        var submitted = await Query(db).AsNoTracking().Where(p => p.Status == ProposalStatus.Submitted && p.AuthorId != access.Viewer.Id).ToListAsync(cancel);
        return new InboxDto(submitted.Count(p => Visible(p, access) && CanDecide(p, access)), mine);
    }
}

/// <summary>Protected notes as shown to those who may not read them: <c>••• protected</c> instead of the text.</summary>
public static class ProtectedNotesMask
{
    public const string Field = "protectedNotes";

    private static bool HasNotes(JsonObject? snapshot) => Diffs.Value(snapshot, Field) is not null;

    /// <summary>A copy of the snapshot with its protected notes masked (the snapshot itself when it has none).</summary>
    public static JsonObject? Mask(JsonObject? snapshot)
    {
        if (snapshot is null || !HasNotes(snapshot)) return snapshot;
        var copy = (JsonObject)snapshot.DeepClone();
        copy[Field] = Rules.ProtectedMask;
        return copy;
    }

    /// <summary>The protected notes entry of a diff with its values masked: the reader learns that they changed, not how.</summary>
    public static List<DiffEntryDto> Mask(List<DiffEntryDto> diff, bool before, bool after) =>
        [.. diff.Select(d => d.Field != Field ? d : d with
        {
            Before = before && d.Before is not null ? JsonValue.Create(Rules.ProtectedMask) : d.Before,
            After = after && d.After is not null ? JsonValue.Create(Rules.ProtectedMask) : d.After,
        })];

    /// <summary>
    /// A proposed state written by someone who may not read the protected notes (or the access list) leaves them out:
    /// they keep their current value. A value they do write replaces the notes.
    /// </summary>
    public static JsonObject KeepHidden(RecordType type, VersionedEntity current, JsonObject after)
    {
        var snapshot = type.Snapshot(current);
        var copy = (JsonObject)after.DeepClone();
        foreach (var field in new[] { Field, "access" })
        {
            if (!snapshot.ContainsKey(field)) continue;
            var value = copy[field];
            if (value is null || (value is JsonValue v && v.TryGetValue<string>(out var text) && text.Trim() == Rules.ProtectedMask))
                copy[field] = snapshot[field]?.DeepClone();
        }
        return copy;
    }
}
