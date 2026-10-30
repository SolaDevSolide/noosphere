using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>Who made a change.</summary>
public sealed record Author(Guid? Id, string Name, AuthorKind Kind = AuthorKind.User)
{
    public static readonly Author System = new(null, "system");

    public static Author Of(Viewer viewer) => viewer.Id == Guid.Empty ? System : new(viewer.Id, viewer.Username);
}

/// <summary>
/// Publishes changes to records: validates them, applies them and adds a revision for each. Nothing is saved: the caller
/// checks the rights and calls <c>SaveChanges</c>, so that several changes (a proposal) are written together.
/// </summary>
public sealed class Publisher(NoosphereDbContext db, DateTimeOffset now)
{
    /// <summary>Creates a record from a PUT-shaped body. A missing id is derived from the name.</summary>
    public async Task<VersionedEntity> Create(RecordType type, JsonObject input, Author author, References refs,
        Guid? proposalId = null, string? summary = null, string fieldPrefix = "", CancellationToken cancel = default)
    {
        var errors = new FieldErrors();
        var e = type.Blank();
        var requested = input["id"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        e.Id = Catalog.NewId(requested, NameOf(type, input), await type.Ids(db, cancel), errors);
        type.Apply(e, input, errors, refs);
        Throw(errors, fieldPrefix);
        // A record created again with the id of a deleted one continues its history.
        var last = await LastVersion(type.Kind, e.Id, cancel);
        e.CreatedAt = e.UpdatedAt = now;
        e.Version = last + 1;
        db.Add(e);
        refs.Add(type.Kind, e.Id);
        AddRevision(type, e, last == 0 ? RevisionOperation.Create : RevisionOperation.Restore, author, proposalId, summary);
        return e;
    }

    /// <summary>Replaces the editable fields of a tracked record and bumps its version.</summary>
    public void Update(RecordType type, VersionedEntity e, JsonObject input, Author author, References refs,
        Guid? proposalId = null, string? summary = null, RevisionOperation operation = RevisionOperation.Update, string fieldPrefix = "")
    {
        if (input["id"] is JsonValue v && v.TryGetValue<string>(out var id) && id.Length > 0 && id != e.Id)
            Throw(Single("id", "Ids cannot be changed."), fieldPrefix);
        var errors = new FieldErrors();
        type.Apply(e, input, errors, refs);
        Throw(errors, fieldPrefix);
        e.UpdatedAt = now;
        e.Version++;
        AddRevision(type, e, operation, author, proposalId, summary);
    }

    /// <summary>Deletes a tracked record (409 when still in use). Its history is kept, ending with a deletion revision.</summary>
    public async Task Delete(RecordType type, VersionedEntity e, Author author, Guid? proposalId = null, string? summary = null, CancellationToken cancel = default)
    {
        await type.CheckDelete(db, e, cancel);
        var snapshot = type.Snapshot(e);
        db.Remove(e);
        db.Revisions.Add(NewRevision(type.Kind, e.Id, e.Version + 1, RevisionOperation.Delete, snapshot, author, proposalId, summary));
    }

    /// <summary>Recreates a deleted record from a snapshot, after its last revision.</summary>
    public async Task<VersionedEntity> Recreate(RecordType type, string id, JsonObject snapshot, int lastVersion, Author author, References refs, string? summary, CancellationToken cancel = default)
    {
        if (await type.Find(db, id, tracked: false, cancel) is not null) throw ApiProblem.Conflict($"A {type.Noun} with the id '{id}' exists again.");
        var errors = new FieldErrors();
        var e = type.Blank();
        e.Id = id;
        type.Apply(e, snapshot, errors, refs);
        errors.ThrowIfAny();
        e.CreatedAt = e.UpdatedAt = now;
        e.Version = lastVersion + 1;
        db.Add(e);
        refs.Add(type.Kind, id);
        AddRevision(type, e, RevisionOperation.Restore, author, null, summary);
        return e;
    }

    public void AddRevision(RecordType type, VersionedEntity e, RevisionOperation operation, Author author, Guid? proposalId, string? summary) =>
        db.Revisions.Add(NewRevision(type.Kind, e.Id, e.Version, operation, type.Snapshot(e), author, proposalId, summary));

    private Revision NewRevision(RecordKind kind, string id, int version, RevisionOperation operation, JsonObject snapshot, Author author, Guid? proposalId, string? summary) => new()
    {
        Kind = kind, EntityId = id, Version = version, Operation = operation, Snapshot = Records.Serialize(snapshot),
        AuthorId = author.Id, AuthorName = author.Name, AuthorKind = author.Kind, ProposalId = proposalId,
        Summary = summary is { Length: > Rules.MaxShortTextLength } ? summary[..Rules.MaxShortTextLength] : summary,
        CreatedAt = now,
    };

    private async Task<int> LastVersion(RecordKind kind, string id, CancellationToken cancel) =>
        await db.Revisions.Where(r => r.Kind == kind && r.EntityId == id).MaxAsync(r => (int?)r.Version, cancel) ?? 0;

    private static string NameOf(RecordType type, JsonObject input) =>
        (type.Kind == RecordKind.Page ? input["title"] : input["name"]) is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    private static FieldErrors Single(string field, string message)
    {
        var errors = new FieldErrors();
        errors.Add(field, message);
        return errors;
    }

    /// <summary>Throws the errors, their field names prefixed (<c>changes[0].after.name</c>).</summary>
    private static void Throw(FieldErrors errors, string prefix)
    {
        if (!errors.Any) return;
        var prefixed = new FieldErrors();
        errors.CopyTo(prefixed, prefix);
        prefixed.ThrowIfAny();
    }
}

/// <summary>Field-level comparison of two snapshots of the same kind.</summary>
public static class Diffs
{
    /// <summary>Changed fields, in the order of <see cref="RecordType.Fields"/>. A missing snapshot (creation, deletion) counts as all empty.</summary>
    public static List<DiffEntryDto> Compare(RecordType type, JsonObject? before, JsonObject? after, RecordFacts? facts = null, Access? access = null)
    {
        var diff = new List<DiffEntryDto>();
        foreach (var field in type.Fields)
        {
            var b = Display(field, Value(before, field.Field));
            var a = Display(field, Value(after, field.Field));
            if (JsonNode.DeepEquals(b, a)) continue;
            diff.Add(new DiffEntryDto(field.Field, field.Label, field.Kind, b?.DeepClone(), a?.DeepClone(),
                RefTitle(field, Value(before, field.Field), facts, access), RefTitle(field, Value(after, field.Field), facts, access)));
        }
        return diff;
    }

    /// <summary>A field of a snapshot; empty texts and lists count as null.</summary>
    public static JsonNode? Value(JsonObject? snapshot, string field)
    {
        var node = snapshot?[field];
        return node switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) && s.Length == 0 => null,
            JsonArray { Count: 0 } => null,
            // An empty access list is no access list.
            JsonObject o when field == "access" && o.All(p => p.Value is null or JsonArray { Count: 0 }) => null,
            _ => node,
        };
    }

    public static bool Same(JsonObject? a, JsonObject? b, string field) => JsonNode.DeepEquals(Value(a, field), Value(b, field));

    /// <summary>Planned changes are shown as one text, attachments as <c>kind:id</c>.</summary>
    private static JsonNode? Display(FieldSpec field, JsonNode? value) => (field.Field, value) switch
    {
        ("plannedChange", JsonObject o) => JsonValue.Create(o["date"] is { } date ? $"{o["summary"]} ({date})" : $"{o["summary"]}"),
        ("attachedTo", JsonObject o) => JsonValue.Create($"{o["kind"]}:{o["id"]}"),
        // Access lists are shown as one list: team:team-finance, user:bob.
        ("access", JsonObject o) => new JsonArray([.. AccessList.Of(o).TeamIds.Select(t => (JsonNode)JsonValue.Create("team:" + t)),
            .. AccessList.Of(o).Usernames.Select(u => (JsonNode)JsonValue.Create("user:" + u))]),
        _ => value,
    };

    private static string? RefTitle(FieldSpec field, JsonNode? value, RecordFacts? facts, Access? access)
    {
        if (field.Kind != DiffKind.Ref || facts is null) return null;
        RecordKind kind;
        string? id;
        if (value is JsonObject o)
        {
            var parsed = Records.Parse(o["kind"]?.GetValue<string>());
            if (parsed is null) return null;
            (kind, id) = (parsed.Kind, o["id"]?.GetValue<string>());
        }
        else if (field.RefKind is { } k && value is JsonValue v && v.TryGetValue<string>(out var s)) (kind, id) = (k, s);
        else return null;
        if (id is null || access?.Hidden(kind, id) == true) return null;
        return facts.Title(kind, id);
    }
}
