using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Exchange;

/// <summary>
/// Exchange format v1: the whole landscape as a list of items <c>{kind, id, metadata, spec}</c> in an envelope, written in
/// JSON or YAML (same content). Schema: docs/format/noosphere-v1.schema.json. An import validates the whole file first
/// (nothing is written when there is any error), then creates or updates every item by id. It never deletes.
/// </summary>
public static class ExchangeFormat
{
    public const string FormatVersion = "noosphere/v1";
    public const string FullMode = "full";
    public const string Domain = "Domain", Team = "Team", Application = "Application", DataObject = "DataObject", Flow = "Flow", Page = "Page";

    /// <summary>Kinds in dependency order: an item only refers to kinds listed before it (and pages to other pages).</summary>
    public static readonly IReadOnlyList<string> Kinds = [Domain, Team, Application, DataObject, Flow, Page];

    private static readonly Dictionary<string, RecordKind> RecordKinds = new()
    {
        [Domain] = RecordKind.Domain, [Team] = RecordKind.Team, [Application] = RecordKind.Application,
        [DataObject] = RecordKind.DataObject, [Flow] = RecordKind.Flow, [Page] = RecordKind.Page,
    };

    private static readonly Dictionary<string, string[]> SpecFields = new()
    {
        [Domain] = ["order"],
        [Team] = ["contact"],
        [Application] = ["domainId", "teamId", "kind", "tech", "lifecycle", "visibility"],
        [DataObject] = ["sensitivity", "sourceApplicationId"],
        [Flow] = ["fromId", "toId", "type", "schedule", "dataObjectIds", "ownerTeamId", "status", "volume", "plannedChange"],
        [Page] = ["body", "parentId", "order", "attachedTo", "ownerTeamId", "tags", "visibility"],
    };

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ---------- Export ----------

    /// <summary>
    /// The landscape as the viewer sees it: restricted applications and pages, and flows touching restricted
    /// applications, are left out for those who may not see them (and so is a hidden data object source).
    /// </summary>
    public static ExchangeEnvelopeDto Export(Snapshot s, IReadOnlyList<Domain.Page> pages, Viewer viewer, ExchangeSourceDto source)
    {
        var items = new List<ExchangeItemDto>();
        items.AddRange(s.Domains.Select(d => Item(Domain, d, new JsonObject { ["order"] = d.Order })));
        items.AddRange(s.Teams.Select(t => Item(Team, t, Spec(("contact", t.Contact)))));
        items.AddRange(s.Applications.Where(a => !s.Hidden(a, viewer)).Select(a => Item(Application, a, Spec(
            ("domainId", a.DomainId), ("teamId", a.TeamId), ("kind", Name(a.Kind)), ("tech", a.Tech),
            ("lifecycle", Name(a.Lifecycle)), ("visibility", Name(a.Visibility))))));
        items.AddRange(s.DataObjects.Select(o => Item(DataObject, o, Spec(
            ("sensitivity", Name(o.Sensitivity)),
            ("sourceApplicationId", o.SourceApplicationId is { } src && s.AppsById.TryGetValue(src, out var app) && !s.Hidden(app, viewer) ? src : null)))));
        items.AddRange(s.Flows.Where(f => !s.Hidden(f, viewer)).Select(f =>
        {
            var spec = Spec(("fromId", f.FromId), ("toId", f.ToId), ("type", Name(f.Type)), ("schedule", f.Schedule));
            spec["dataObjectIds"] = new JsonArray([.. f.DataObjectIds.Select(id => (JsonNode)JsonValue.Create(id))]);
            if (f.OwnerTeamId is not null) spec["ownerTeamId"] = f.OwnerTeamId;
            spec["status"] = Name(f.Status);
            if (f.Volume is not null) spec["volume"] = f.Volume;
            if (f.PlannedChangeSummary is not null)
                spec["plannedChange"] = Spec(("summary", f.PlannedChangeSummary),
                    ("date", f.PlannedChangeDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            return Item(Flow, f, spec);
        }));
        var visible = pages.Where(p => !PageQueries.Hidden(p, viewer)).ToList();
        var visibleIds = visible.Select(p => p.Id).ToHashSet();
        // Parents first, so that a file reads top-down; a parent hidden from the viewer is left out.
        items.AddRange(PageOrder(visible).Select(p =>
        {
            var spec = new JsonObject { ["body"] = p.Body };
            if (p.ParentId is { } parent && visibleIds.Contains(parent)) spec["parentId"] = parent;
            spec["order"] = p.Order;
            if (p.AttachedKind is { } k && p.AttachedId is { } id) spec["attachedTo"] = new JsonObject { ["kind"] = Rules.KindName(k), ["id"] = id };
            if (p.OwnerTeamId is not null) spec["ownerTeamId"] = p.OwnerTeamId;
            if (p.Tags.Count > 0) spec["tags"] = new JsonArray([.. p.Tags.Select(t => (JsonNode)JsonValue.Create(t))]);
            spec["visibility"] = Name(p.Visibility);
            return new ExchangeItemDto(Page, p.Id, new ExchangeMetadataDto(p.Title, null), spec);
        }));
        return new ExchangeEnvelopeDto(FormatVersion, source, FullMode, items);
    }

    private static List<Domain.Page> PageOrder(List<Domain.Page> pages)
    {
        var ids = pages.Select(p => p.Id).ToHashSet();
        var children = pages.ToLookup(p => p.ParentId is { } parent && ids.Contains(parent) ? parent : null);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<Domain.Page> Walk(string? parent) =>
            children[parent].OrderBy(p => p.Order).ThenBy(p => p.Id, StringComparer.Ordinal).Where(p => seen.Add(p.Id)).SelectMany(p => Walk(p.Id).Prepend(p));
        return Walk(null).ToList();
    }

    private static ExchangeItemDto Item(string kind, LandscapeEntity e, JsonObject spec) =>
        new(kind, e.Id, new ExchangeMetadataDto(e.Name, e.Description), spec);

    private static JsonObject Spec(params (string Key, string? Value)[] fields)
    {
        var spec = new JsonObject();
        foreach (var (key, value) in fields)
            if (value is not null) spec[key] = value;
        return spec;
    }

    private static string Name<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    public static string ToJson(ExchangeEnvelopeDto envelope) => JsonSerializer.Serialize(envelope, Json) + "\n";

    public static string ToYaml(ExchangeEnvelopeDto envelope) => Yaml.FromJson(JsonSerializer.SerializeToNode(envelope, Json));

    // ---------- Parsing ----------

    /// <summary>Reads a JSON or YAML document (<paramref name="format"/> <c>json</c> or <c>yaml</c>, guessed when null). 400 when unreadable.</summary>
    public static JsonNode? Parse(string text, string? format)
    {
        var yaml = format is "yaml" || (format is null && !text.TrimStart().StartsWith('{'));
        try
        {
            return yaml ? Yaml.ToJson(text) : JsonNode.Parse(text);
        }
        catch (JsonException e)
        {
            throw ApiProblem.BadRequest($"The file is not valid JSON: {Short(e.Message)}");
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            throw ApiProblem.BadRequest($"The file is not valid YAML: {Short(e.Message)}");
        }
    }

    private static string Short(string message) => message.Length <= 300 ? message : message[..300] + "...";

    // ---------- Import ----------

    private sealed record Parsed(int Index, string Kind, string Id, VersionedEntity Entity, Action<VersionedEntity, FieldErrors, References> Apply, string? Parent = null);

    /// <summary>
    /// Validates the whole document, then (unless <paramref name="dryRun"/> or on error) creates or updates every item,
    /// with a revision for each one written (author: <paramref name="author"/>, the system by default).
    /// </summary>
    public static async Task<ImportResultDto> Import(NoosphereDbContext db, JsonNode? root, bool dryRun, DateTimeOffset now,
        Author? author = null, CancellationToken cancel = default)
    {
        var errors = new List<ImportErrorDto>();
        var parsed = ReadEnvelope(root, errors);
        if (errors.Count > 0) return new(0, 0, 0, errors, dryRun, false);

        var known = await References.Load(db, cancel);
        var parents = new Dictionary<string, string?>(known.PageParents);
        foreach (var p in parsed.Where(p => p.Kind == Page)) parents[p.Id] = p.Parent;
        var refs = new References(
            Union(known.Domains, parsed, Domain), Union(known.Teams, parsed, Team),
            Union(known.Applications, parsed, Application), Union(known.DataObjects, parsed, DataObject),
            Union(known.Flows, parsed, Flow), Union(known.Pages, parsed, Page), parents);
        var seen = new HashSet<(string, string)>();
        foreach (var p in parsed)
        {
            if (!seen.Add((p.Kind, p.Id)))
                errors.Add(new(p.Index, p.Kind, p.Id, $"items[{p.Index}].id", $"Duplicate {p.Kind} '{p.Id}' in the file."));
            var fieldErrors = new FieldErrors();
            p.Apply(p.Entity, fieldErrors, refs);
            errors.AddRange(fieldErrors.All.Select(e => new ImportErrorDto(p.Index, p.Kind, p.Id, $"items[{p.Index}].{Path(e.Field)}", e.Message)));
        }
        if (errors.Count > 0) return new(0, 0, 0, errors, dryRun, false);

        int created = 0, updated = 0, unchanged = 0;
        var publisher = new Publisher(db, now);
        var written = new List<(VersionedEntity Entity, RevisionOperation Operation)>();
        foreach (var kind in Kinds)
        {
            var batch = parsed.Where(p => p.Kind == kind).ToList();
            if (batch.Count == 0) continue;
            var ids = batch.Select(p => p.Id).ToList();
            var existing = (await Existing(db, kind, ids, cancel)).ToDictionary(e => e.Id);
            foreach (var p in batch)
            {
                if (existing.TryGetValue(p.Id, out var current))
                {
                    if (Same(current, p.Entity)) { unchanged++; continue; }
                    Copy(p.Entity, current);
                    current.UpdatedAt = now;
                    current.Version++;
                    written.Add((current, RevisionOperation.Update));
                    updated++;
                }
                else
                {
                    var last = await db.Revisions.Where(r => r.Kind == RecordKinds[kind] && r.EntityId == p.Id).MaxAsync(r => (int?)r.Version, cancel) ?? 0;
                    p.Entity.CreatedAt = p.Entity.UpdatedAt = now;
                    p.Entity.Version = last + 1;
                    db.Add(p.Entity);
                    written.Add((p.Entity, last == 0 ? RevisionOperation.Create : RevisionOperation.Restore));
                    created++;
                }
            }
        }
        foreach (var (entity, operation) in written)
            publisher.AddRevision(Records.Of(RecordKinds[KindOf(entity)]), entity, operation, author ?? Author.System, null, "Imported");
        if (!dryRun) await db.SaveChangesAsync(cancel);
        else db.ChangeTracker.Clear();
        return new(created, updated, unchanged, [], dryRun, !dryRun);
    }

    private static string KindOf(VersionedEntity e) => e switch
    {
        BusinessDomain => Domain,
        Domain.Team _ => Team,
        Domain.Application _ => Application,
        Domain.DataObject _ => DataObject,
        Domain.Flow _ => Flow,
        _ => Page,
    };

    private static HashSet<string> Union(ISet<string> known, List<Parsed> parsed, string kind) =>
        [.. known, .. parsed.Where(p => p.Kind == kind).Select(p => p.Id)];

    /// <summary>Error field (as named by the API) to its place in an item.</summary>
    private static string Path(string field) => field is "name" or "description" ? $"metadata.{field}" : $"spec.{field}";

    private static List<Parsed> ReadEnvelope(JsonNode? root, List<ImportErrorDto> errors)
    {
        void Envelope(string path, string message) => errors.Add(new(null, null, null, path, message));
        if (root is not JsonObject envelope)
        {
            Envelope("$", "The document must be an object with formatVersion, source, mode and items.");
            return [];
        }
        Unknown(envelope, ["formatVersion", "source", "mode", "items"], "", (path, msg) => Envelope(path, msg));
        if (Text(envelope["formatVersion"]) != FormatVersion)
            Envelope("formatVersion", $"Expected \"{FormatVersion}\" (got {Show(envelope["formatVersion"])}).");
        if (Text(envelope["mode"]) != FullMode)
            Envelope("mode", $"Expected \"{FullMode}\" (got {Show(envelope["mode"])}).");
        if (envelope["source"] is not null and not JsonObject) Envelope("source", "Expected an object.");
        if (envelope["items"] is not JsonArray items)
        {
            Envelope("items", "Expected an array of items.");
            return [];
        }
        if (errors.Count > 0) return [];

        var parsed = new List<Parsed>();
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var at = $"items[{i}]";
            if (items[i] is not JsonObject item)
            {
                errors.Add(new(i, null, null, at, "Expected an object with kind, id, metadata and spec."));
                continue;
            }
            var kind = Text(item["kind"]);
            var id = Text(item["id"]);
            void Error(string path, string message) => errors.Add(new(index, kind, id, path, message));
            var before = errors.Count;
            Unknown(item, ["kind", "id", "metadata", "spec"], at + ".", Error);
            if (kind is null || !Kinds.Contains(kind)) Error($"{at}.kind", $"Expected one of: {string.Join(", ", Kinds)}.");
            if (!Rules.IsValidId(id))
                Error($"{at}.id", $"Expected a kebab-case id (lower-case letters, digits, single hyphens, at most {Rules.MaxIdLength} characters).");
            var metadata = item["metadata"] as JsonObject;
            if (metadata is null) Error($"{at}.metadata", "Expected an object with a name.");
            else Unknown(metadata, kind == Page ? ["name"] : ["name", "description"], $"{at}.metadata.", Error);
            var spec = item["spec"] ?? new JsonObject();
            if (spec is not JsonObject specObject)
            {
                Error($"{at}.spec", "Expected an object.");
                continue;
            }
            if (kind is not null && SpecFields.TryGetValue(kind, out var fields)) Unknown(specObject, fields, $"{at}.spec.", Error);
            if (errors.Count > before) continue;

            var name = Text(metadata!["name"]);
            var description = Text(metadata["description"]);
            var entry = Read(kind!, id!, name, description, specObject, (field, message) => Error($"{at}.spec.{field}", message));
            if (entry is not null && errors.Count == before)
                parsed.Add(new Parsed(i, kind!, id!, entry.Value.Entity, entry.Value.Apply, kind == Page ? Text(specObject["parentId"]) : null));
        }
        return parsed;
    }

    /// <summary>Spec of one item turned into the API input of its kind, validated later with the API rules.</summary>
    private static (VersionedEntity Entity, Action<VersionedEntity, FieldErrors, References> Apply)? Read(
        string kind, string id, string? name, string? description, JsonObject spec, Action<string, string> error)
    {
        switch (kind)
        {
            case Domain:
            {
                var order = Integer(spec["order"], "order", error);
                var input = new DomainInput(id, name, description, order, null);
                return (new BusinessDomain { Id = id }, (e, err, _) => Catalog.Apply((BusinessDomain)e, input, err));
            }
            case Team:
            {
                var input = new TeamInput(id, name, description, Text(spec["contact"]), null);
                return (new Team { Id = id }, (e, err, _) => Catalog.Apply((Team)e, input, err));
            }
            case Application:
            {
                var input = new ApplicationInput(id, name, Text(spec["domainId"]), Text(spec["teamId"]),
                    Enum<ApplicationKind>(spec["kind"], "kind", error), Text(spec["tech"]), description,
                    Enum<Lifecycle>(spec["lifecycle"], "lifecycle", error), Enum<Visibility>(spec["visibility"], "visibility", error), null);
                return (new Application { Id = id }, (e, err, r) => Catalog.Apply((Application)e, input, err, r));
            }
            case DataObject:
            {
                var input = new DataObjectInput(id, name, Enum<Sensitivity>(spec["sensitivity"], "sensitivity", error),
                    Text(spec["sourceApplicationId"]), description, null);
                return (new DataObject { Id = id }, (e, err, r) => Catalog.Apply((DataObject)e, input, err, r));
            }
            case Page:
            {
                AttachedToDto? attached = null;
                if (spec["attachedTo"] is JsonObject target)
                {
                    Unknown(target, ["kind", "id"], "attachedTo.", error);
                    if (Enum<RecordKind>(target["kind"], "attachedTo.kind", error) is { } k) attached = new AttachedToDto(k, Text(target["id"]) ?? "");
                }
                else if (spec["attachedTo"] is not null) error("attachedTo", "Expected an object with kind and id.");
                List<string>? tags = null;
                if (spec["tags"] is JsonArray list)
                {
                    tags = [];
                    foreach (var value in list)
                        if (Text(value) is { } text) tags.Add(text);
                        else error("tags", "Expected an array of texts.");
                }
                else if (spec["tags"] is not null) error("tags", "Expected an array of texts.");
                var input = new PageInput(id, name, Text(spec["body"]), Text(spec["parentId"]), Integer(spec["order"], "order", error), attached,
                    Text(spec["ownerTeamId"]), tags, Enum<Visibility>(spec["visibility"], "visibility", error), null);
                return (new Domain.Page { Id = id }, (e, err, r) => Catalog.Apply((Domain.Page)e, input, err, r));
            }
            default:
            {
                List<string>? objects = null;
                if (spec["dataObjectIds"] is JsonArray array)
                {
                    objects = [];
                    foreach (var value in array)
                        if (Text(value) is { } text) objects.Add(text);
                        else error("dataObjectIds", "Expected an array of data object ids.");
                }
                else if (spec["dataObjectIds"] is not null) error("dataObjectIds", "Expected an array of data object ids.");
                PlannedChangeDto? change = null;
                if (spec["plannedChange"] is JsonObject planned)
                {
                    Unknown(planned, ["summary", "date"], "plannedChange.", error);
                    DateOnly? date = null;
                    if (Text(planned["date"]) is { } raw)
                    {
                        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) date = d;
                        else error("plannedChange.date", "Expected a date as YYYY-MM-DD.");
                    }
                    change = new PlannedChangeDto(Text(planned["summary"]) ?? "", date);
                }
                else if (spec["plannedChange"] is not null) error("plannedChange", "Expected an object with a summary.");
                var input = new FlowInput(id, name, Text(spec["fromId"]), Text(spec["toId"]), Enum<FlowType>(spec["type"], "type", error),
                    Text(spec["schedule"]), objects, Text(spec["ownerTeamId"]), Enum<FlowStatus>(spec["status"], "status", error),
                    Text(spec["volume"]), description, change, null);
                return (new Flow { Id = id }, (e, err, r) => Catalog.Apply((Flow)e, input, err, r));
            }
        }
    }

    private static void Unknown(JsonObject obj, IReadOnlyCollection<string> allowed, string prefix, Action<string, string> error)
    {
        foreach (var (key, _) in obj)
            if (!allowed.Contains(key)) error(prefix + key, $"Unknown property '{key}'. Expected: {string.Join(", ", allowed)}.");
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s
        : node is JsonValue n && n.GetValueKind() is JsonValueKind.Number ? n.ToJsonString() : null;

    private static string Show(JsonNode? node) => node is null ? "nothing" : node.ToJsonString();

    private static int? Integer(JsonNode? node, string field, Action<string, string> error)
    {
        if (node is null) return null;
        if (node is JsonValue v
            && int.TryParse(v.GetValueKind() is JsonValueKind.Number ? v.ToJsonString() : Text(v), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;
        error(field, "Expected a whole number.");
        return null;
    }

    private static T? Enum<T>(JsonNode? node, string field, Action<string, string> error) where T : struct, Enum
    {
        if (node is null) return null;
        var text = Text(node);
        foreach (var value in System.Enum.GetValues<T>())
            if (Name(value) == text) return value;
        error(field, $"Expected one of: {string.Join(", ", System.Enum.GetValues<T>().Select(Name))}.");
        return null;
    }

    private static async Task<List<VersionedEntity>> Existing(NoosphereDbContext db, string kind, List<string> ids, CancellationToken cancel) => kind switch
    {
        Domain => [.. await db.Domains.Where(e => ids.Contains(e.Id)).ToListAsync(cancel)],
        Team => [.. await db.Teams.Where(e => ids.Contains(e.Id)).ToListAsync(cancel)],
        Application => [.. await db.Applications.Where(e => ids.Contains(e.Id)).ToListAsync(cancel)],
        DataObject => [.. await db.DataObjects.Where(e => ids.Contains(e.Id)).ToListAsync(cancel)],
        Flow => [.. await db.Flows.Where(e => ids.Contains(e.Id)).ToListAsync(cancel)],
        _ => [.. await db.Pages.Where(e => ids.Contains(e.Id)).ToListAsync(cancel)],
    };

    private static bool Same(VersionedEntity a, VersionedEntity b) =>
        (a is not LandscapeEntity la || b is not LandscapeEntity lb || (la.Name == lb.Name && la.Description == lb.Description)) && (a, b) switch
        {
            (Domain.Page x, Domain.Page y) => x.Title == y.Title && x.Body == y.Body && x.ParentId == y.ParentId && x.Order == y.Order
                && x.AttachedKind == y.AttachedKind && x.AttachedId == y.AttachedId && x.OwnerTeamId == y.OwnerTeamId
                && x.Tags.SequenceEqual(y.Tags) && x.Visibility == y.Visibility,
            (BusinessDomain x, BusinessDomain y) => x.Order == y.Order,
            (Team x, Team y) => x.Contact == y.Contact,
            (Application x, Application y) => x.DomainId == y.DomainId && x.TeamId == y.TeamId && x.Kind == y.Kind && x.Tech == y.Tech
                && x.Lifecycle == y.Lifecycle && x.Visibility == y.Visibility,
            (DataObject x, DataObject y) => x.Sensitivity == y.Sensitivity && x.SourceApplicationId == y.SourceApplicationId,
            (Flow x, Flow y) => x.FromId == y.FromId && x.ToId == y.ToId && x.Type == y.Type && x.Schedule == y.Schedule
                && x.DataObjectIds.SequenceEqual(y.DataObjectIds) && x.OwnerTeamId == y.OwnerTeamId && x.Status == y.Status
                && x.Volume == y.Volume && x.PlannedChangeSummary == y.PlannedChangeSummary && x.PlannedChangeDate == y.PlannedChangeDate,
            _ => false,
        };

    private static void Copy(VersionedEntity from, VersionedEntity to)
    {
        if (from is LandscapeEntity lf && to is LandscapeEntity lt) (lt.Name, lt.Description) = (lf.Name, lf.Description);
        switch (from, to)
        {
            case (Domain.Page x, Domain.Page y):
                (y.Title, y.Body, y.ParentId, y.Order, y.AttachedKind, y.AttachedId, y.OwnerTeamId, y.Tags, y.Visibility, y.Links) =
                    (x.Title, x.Body, x.ParentId, x.Order, x.AttachedKind, x.AttachedId, x.OwnerTeamId, x.Tags, x.Visibility, x.Links);
                break;
            case (BusinessDomain x, BusinessDomain y):
                y.Order = x.Order;
                break;
            case (Team x, Team y):
                y.Contact = x.Contact;
                break;
            case (Application x, Application y):
                (y.DomainId, y.TeamId, y.Kind, y.Tech, y.Lifecycle, y.Visibility) = (x.DomainId, x.TeamId, x.Kind, x.Tech, x.Lifecycle, x.Visibility);
                break;
            case (DataObject x, DataObject y):
                (y.Sensitivity, y.SourceApplicationId) = (x.Sensitivity, x.SourceApplicationId);
                break;
            case (Flow x, Flow y):
                (y.FromId, y.ToId, y.Type, y.Schedule, y.DataObjectIds, y.OwnerTeamId, y.Status, y.Volume, y.PlannedChangeSummary, y.PlannedChangeDate) =
                    (x.FromId, x.ToId, x.Type, x.Schedule, x.DataObjectIds, x.OwnerTeamId, x.Status, x.Volume, x.PlannedChangeSummary, x.PlannedChangeDate);
                break;
        }
    }
}
