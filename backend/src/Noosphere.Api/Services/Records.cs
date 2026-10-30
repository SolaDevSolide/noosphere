using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>A field of a record, as compared in diffs and merged by a rebase.</summary>
/// <param name="Field">Key in the snapshot (the PUT body).</param>
/// <param name="RefKind">For <see cref="DiffKind.Ref"/> fields: the kind referred to (null for <c>attachedTo</c>, which names its kind).</param>
public sealed record FieldSpec(string Field, string Label, DiffKind Kind, RecordKind? RefKind = null);

/// <summary>
/// One kind of versioned record, handled generically by revisions, proposals, history and the trash. A snapshot holds
/// the editable fields in the shape of the kind's API input (PUT body), without <c>version</c>.
/// </summary>
public abstract class RecordType
{
    public abstract RecordKind Kind { get; }
    /// <summary>Path segment of the API: <c>applications</c>, <c>data-objects</c>...</summary>
    public abstract string Plural { get; }
    /// <summary>Lower-case name used in messages: "application", "data object"...</summary>
    public abstract string Noun { get; }
    public abstract IReadOnlyList<FieldSpec> Fields { get; }
    /// <summary>Domains and teams are changed by administrators only, never through proposals.</summary>
    public bool Proposable => Kind is not (RecordKind.Domain or RecordKind.Team);

    public abstract Task<VersionedEntity?> Find(NoosphereDbContext db, string id, bool tracked, CancellationToken cancel = default);
    public abstract Task<HashSet<string>> Ids(NoosphereDbContext db, CancellationToken cancel = default);
    public abstract VersionedEntity Blank();
    public abstract JsonObject Snapshot(VersionedEntity e);
    /// <summary>Validates <paramref name="input"/> (a snapshot or PUT body) and copies it onto <paramref name="e"/>.</summary>
    public abstract void Apply(VersionedEntity e, JsonObject input, FieldErrors errors, References refs);
    public abstract string Title(VersionedEntity e);
    /// <summary>Name or title in a snapshot.</summary>
    public string Title(JsonObject snapshot) =>
        (Kind == RecordKind.Page ? snapshot["title"] : snapshot["name"]) is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    /// <summary>Throws 409 when other records still refer to it.</summary>
    public abstract Task CheckDelete(NoosphereDbContext db, VersionedEntity e, CancellationToken cancel = default);
    /// <summary>Name taken to derive a new id.</summary>
    public string NameFor(VersionedEntity e) => Title(e);
}

/// <summary>The record types, by kind and by API path.</summary>
public static class Records
{
    /// <summary>Snapshots and proposal bodies: camelCase, enums as camelCase strings (never integers), nulls left out.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private static FieldSpec Text(string field, string label) => new(field, label, DiffKind.Text);
    private static FieldSpec Markdown(string field, string label) => new(field, label, DiffKind.Markdown);
    private static FieldSpec List(string field, string label) => new(field, label, DiffKind.List);
    private static FieldSpec Ref(string field, string label, RecordKind? kind) => new(field, label, DiffKind.Ref, kind);

    public static readonly RecordType Application = new Type<Application, ApplicationInput>(
        RecordKind.Application, "applications", "application",
        [Text("name", "Name"), Ref("domainId", "Domain", RecordKind.Domain), Ref("teamId", "Team", RecordKind.Team), Text("kind", "Kind"),
            Text("tech", "Technology"), Text("lifecycle", "Lifecycle"), Text("visibility", "Visibility"), Markdown("description", "Description")],
        db => db.Applications,
        a => new ApplicationInput(a.Id, a.Name, a.DomainId, a.TeamId, a.Kind, a.Tech, a.Description, a.Lifecycle, a.Visibility, null),
        (e, i, err, refs) => Catalog.Apply(e, i, err, refs),
        e => e.Name,
        async (db, e, cancel) =>
        {
            var flows = await db.Flows.CountAsync(f => f.FromId == e.Id || f.ToId == e.Id, cancel);
            var objects = await db.DataObjects.CountAsync(o => o.SourceApplicationId == e.Id, cancel);
            if (flows + objects > 0)
                throw ApiProblem.Conflict($"This application still has {flows} flow(s) and is the source of {objects} data object(s). Remove them first.");
        });

    public static readonly RecordType Flow = new Type<Flow, FlowInput>(
        RecordKind.Flow, "flows", "flow",
        [Text("name", "Name"), Ref("fromId", "From", RecordKind.Application), Ref("toId", "To", RecordKind.Application), Text("type", "Type"),
            Text("schedule", "Schedule"), List("dataObjectIds", "Data objects"), Ref("ownerTeamId", "Owner team", RecordKind.Team),
            Text("status", "Status"), Text("volume", "Volume"), Text("plannedChange", "Planned change"), Markdown("description", "Description")],
        db => db.Flows,
        f => new FlowInput(f.Id, f.Name, f.FromId, f.ToId, f.Type, f.Schedule, f.DataObjectIds, f.OwnerTeamId, f.Status, f.Volume, f.Description,
            f.PlannedChangeSummary is null ? null : new PlannedChangeDto(f.PlannedChangeSummary, f.PlannedChangeDate), null),
        (e, i, err, refs) => Catalog.Apply(e, i, err, refs),
        e => e.Name,
        (_, _, _) => Task.CompletedTask);

    public static readonly RecordType DataObject = new Type<DataObject, DataObjectInput>(
        RecordKind.DataObject, "data-objects", "data object",
        [Text("name", "Name"), Text("sensitivity", "Sensitivity"), Ref("sourceApplicationId", "Source application", RecordKind.Application),
            Markdown("description", "Description")],
        db => db.DataObjects,
        o => new DataObjectInput(o.Id, o.Name, o.Sensitivity, o.SourceApplicationId, o.Description, null),
        (e, i, err, refs) => Catalog.Apply(e, i, err, refs),
        e => e.Name,
        async (db, e, cancel) =>
        {
            var flows = await db.Flows.CountAsync(f => f.DataObjectIds.Contains(e.Id), cancel);
            if (flows > 0) throw ApiProblem.Conflict($"{flows} flow(s) still carry this data object. Remove it from them first.");
        });

    public static readonly RecordType Page = new Type<Page, PageInput>(
        RecordKind.Page, "pages", "page",
        [Text("title", "Title"), Ref("parentId", "Parent page", RecordKind.Page), Text("order", "Order"), Ref("attachedTo", "Attached to", null),
            Ref("ownerTeamId", "Owner team", RecordKind.Team), List("tags", "Tags"), Text("visibility", "Visibility"), Markdown("body", "Body")],
        db => db.Pages,
        p => new PageInput(p.Id, p.Title, p.Body, p.ParentId, p.Order, p.AttachedKind is { } k && p.AttachedId is { } id ? new AttachedToDto(k, id) : null,
            p.OwnerTeamId, p.Tags, p.Visibility, null),
        (e, i, err, refs) => Catalog.Apply(e, i, err, refs),
        e => e.Title,
        async (db, e, cancel) =>
        {
            var children = await db.Pages.CountAsync(p => p.ParentId == e.Id, cancel);
            if (children > 0) throw ApiProblem.Conflict($"This page still has {children} child page(s). Move or delete them first.");
        });

    public static readonly RecordType Domain = new Type<BusinessDomain, DomainInput>(
        RecordKind.Domain, "domains", "domain",
        [Text("name", "Name"), Text("order", "Order"), Markdown("description", "Description")],
        db => db.Domains,
        d => new DomainInput(d.Id, d.Name, d.Description, d.Order, null),
        (e, i, err, _) => Catalog.Apply(e, i, err),
        e => e.Name,
        async (db, e, cancel) =>
        {
            var apps = await db.Applications.CountAsync(a => a.DomainId == e.Id, cancel);
            if (apps > 0) throw ApiProblem.Conflict($"This domain still holds {apps} application(s). Move or delete them first.");
        });

    public static readonly RecordType Team = new Type<Team, TeamInput>(
        RecordKind.Team, "teams", "team",
        [Text("name", "Name"), Text("contact", "Contact"), Markdown("description", "Description")],
        db => db.Teams,
        t => new TeamInput(t.Id, t.Name, t.Description, t.Contact, null),
        (e, i, err, _) => Catalog.Apply(e, i, err),
        e => e.Name,
        async (db, e, cancel) =>
        {
            var apps = await db.Applications.CountAsync(a => a.TeamId == e.Id, cancel);
            var flows = await db.Flows.CountAsync(f => f.OwnerTeamId == e.Id, cancel);
            var pages = await db.Pages.CountAsync(p => p.OwnerTeamId == e.Id, cancel);
            if (apps + flows + pages > 0)
                throw ApiProblem.Conflict($"This team still owns {apps} application(s), {flows} flow(s) and {pages} page(s). Reassign them first.");
        });

    public static readonly IReadOnlyList<RecordType> All = [Application, Flow, DataObject, Page, Domain, Team];

    public static RecordType Of(RecordKind kind) => All.First(t => t.Kind == kind);

    /// <summary>Kind from its JSON name (<c>dataObject</c>) or its API path (<c>data-objects</c>); null when unknown.</summary>
    public static RecordType? Parse(string? name) =>
        All.FirstOrDefault(t => t.Plural == name || Rules.KindName(t.Kind) == name);

    /// <summary>Serializes an API input into a snapshot (without <c>version</c>).</summary>
    public static JsonObject ToSnapshot<T>(T input)
    {
        var node = JsonSerializer.SerializeToNode(input, Json)!.AsObject();
        node.Remove("version");
        return node;
    }

    public static JsonObject? ParseObject(string? json) => json is null ? null : JsonNode.Parse(json)?.AsObject();

    public static string Serialize(JsonNode node) => node.ToJsonString(Json);

    private sealed class Type<TEntity, TInput>(
        RecordKind kind, string plural, string noun, IReadOnlyList<FieldSpec> fields,
        Func<NoosphereDbContext, DbSet<TEntity>> set,
        Func<TEntity, TInput> toInput,
        Action<TEntity, TInput, FieldErrors, References> apply,
        Func<TEntity, string> title,
        Func<NoosphereDbContext, TEntity, CancellationToken, Task> checkDelete) : RecordType
        where TEntity : VersionedEntity, new()
    {
        public override RecordKind Kind => kind;
        public override string Plural => plural;
        public override string Noun => noun;
        public override IReadOnlyList<FieldSpec> Fields => fields;

        public override async Task<VersionedEntity?> Find(NoosphereDbContext db, string id, bool tracked, CancellationToken cancel = default) =>
            await (tracked ? set(db) : set(db).AsNoTracking()).FirstOrDefaultAsync(e => e.Id == id, cancel);

        public override async Task<HashSet<string>> Ids(NoosphereDbContext db, CancellationToken cancel = default) =>
            (await set(db).Select(e => e.Id).ToListAsync(cancel)).ToHashSet();

        public override VersionedEntity Blank() => new TEntity();

        public override JsonObject Snapshot(VersionedEntity e) => ToSnapshot(toInput((TEntity)e));

        public override void Apply(VersionedEntity e, JsonObject input, FieldErrors errors, References refs)
        {
            TInput? parsed;
            try
            {
                parsed = input.Deserialize<TInput>(Json);
            }
            catch (JsonException ex)
            {
                var field = ex.Path is { Length: > 2 } path ? path[2..].Split('.', '[')[0] : "after";
                errors.Add(field, "Invalid value: a known value of the right type is expected.");
                return;
            }
            if (parsed is null)
            {
                errors.Add("after", "Expected an object.");
                return;
            }
            apply((TEntity)e, parsed, errors, refs);
        }

        public override string Title(VersionedEntity e) => title((TEntity)e);

        public override Task CheckDelete(NoosphereDbContext db, VersionedEntity e, CancellationToken cancel = default) => checkDelete(db, (TEntity)e, cancel);
    }
}
