using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
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
    public const string VisibleScope = "visible", FullScope = "full";
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
        [Application] = ["domainId", "teamId", "kind", "tech", "lifecycle", "visibility", "access", "protectedNotes"],
        [DataObject] = ["sensitivity", "sourceApplicationId"],
        [Flow] = ["fromId", "toId", "type", "schedule", "dataObjectIds", "ownerTeamId", "status", "volume", "plannedChange", "protectedNotes"],
        [Page] = ["body", "parentId", "order", "attachedTo", "ownerTeamId", "tags", "visibility", "access"],
    };

    private static readonly string[] KeptWhenAbsent = ["access", "protectedNotes"];

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // ---------- Export ----------

    /// <summary>
    /// The landscape as the reader sees it in full: restricted records they may not see are left out (not stubbed), and so
    /// are, for a guest, the records not shared with them; a data object's source, a page's parent or attachment left out
    /// are dropped. Protected notes are written only where the reader may read them. <paramref name="reads"/> collects the
    /// restricted records and protected notes written that the reader's teams do not own (for the access log).
    /// </summary>
    public static ExchangeEnvelopeDto Export(Snapshot s, IReadOnlyList<Domain.Page> pages, Access access, ExchangeSourceDto source,
        string scope = VisibleScope, List<(RecordKind Kind, string Id)>? reads = null)
    {
        void Read(RecordKind kind, VersionedEntity e, bool notes)
        {
            if (reads is not null && !access.InTeam(access.Facts.Owner(kind, e)) && (notes || access.Facts.Restricted(kind, e))) reads.Add((kind, e.Id));
        }
        var items = new List<ExchangeItemDto>();
        items.AddRange(s.Domains.Select(d => Item(Domain, d, new JsonObject { ["order"] = d.Order })));
        items.AddRange(s.Teams.Select(t => Item(Team, t, Spec(("contact", t.Contact)))));
        var apps = s.Applications.Where(a => access.Sight(RecordKind.Application, a) == Sight.Full).ToList();
        var appIds = apps.Select(a => a.Id).ToHashSet();
        items.AddRange(apps.Select(a =>
        {
            var spec = Spec(("domainId", a.DomainId), ("teamId", a.TeamId), ("kind", Name(a.Kind)), ("tech", a.Tech),
                ("lifecycle", Name(a.Lifecycle)), ("visibility", Name(a.Visibility)));
            if (AccessSpec(a.AccessTeamIds, a.AccessUsernames) is { } list) spec["access"] = list;
            var notes = a.ProtectedNotes is not null && access.ReadsProtected(RecordKind.Application, a);
            if (notes) spec["protectedNotes"] = a.ProtectedNotes;
            Read(RecordKind.Application, a, notes);
            return Item(Application, a, spec);
        }));
        items.AddRange(s.DataObjects.Select(o => Item(DataObject, o, Spec(
            ("sensitivity", Name(o.Sensitivity)),
            ("sourceApplicationId", o.SourceApplicationId is { } src && appIds.Contains(src) ? src : null)))));
        var flows = s.Flows.Where(f => access.Sight(RecordKind.Flow, f) == Sight.Full && appIds.Contains(f.FromId) && appIds.Contains(f.ToId)).ToList();
        var flowIds = flows.Select(f => f.Id).ToHashSet();
        items.AddRange(flows.Select(f =>
        {
            var spec = Spec(("fromId", f.FromId), ("toId", f.ToId), ("type", Name(f.Type)), ("schedule", f.Schedule));
            spec["dataObjectIds"] = new JsonArray([.. f.DataObjectIds.Select(id => (JsonNode)JsonValue.Create(id))]);
            if (f.OwnerTeamId is not null) spec["ownerTeamId"] = f.OwnerTeamId;
            spec["status"] = Name(f.Status);
            if (f.Volume is not null) spec["volume"] = f.Volume;
            if (f.PlannedChangeSummary is not null)
                spec["plannedChange"] = Spec(("summary", f.PlannedChangeSummary),
                    ("date", f.PlannedChangeDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            var notes = f.ProtectedNotes is not null && access.ReadsProtected(RecordKind.Flow, f);
            if (notes) spec["protectedNotes"] = f.ProtectedNotes;
            Read(RecordKind.Flow, f, notes);
            return Item(Flow, f, spec);
        }));
        var visible = pages.Where(p => access.Sight(RecordKind.Page, p) == Sight.Full).ToList();
        var visibleIds = visible.Select(p => p.Id).ToHashSet();
        bool Exported(RecordKind kind, string id) => kind switch
        {
            RecordKind.Application => appIds.Contains(id),
            RecordKind.Flow => flowIds.Contains(id),
            RecordKind.Page => visibleIds.Contains(id),
            _ => true,
        };
        // Parents first, so that a file reads top-down; a parent hidden from the reader is left out.
        items.AddRange(PageOrder(visible).Select(p =>
        {
            var spec = new JsonObject { ["body"] = p.Body };
            if (p.ParentId is { } parent && visibleIds.Contains(parent)) spec["parentId"] = parent;
            spec["order"] = p.Order;
            if (p.AttachedKind is { } k && p.AttachedId is { } id && Exported(k, id)) spec["attachedTo"] = new JsonObject { ["kind"] = Rules.KindName(k), ["id"] = id };
            if (p.OwnerTeamId is not null) spec["ownerTeamId"] = p.OwnerTeamId;
            if (p.Tags.Count > 0) spec["tags"] = new JsonArray([.. p.Tags.Select(t => (JsonNode)JsonValue.Create(t))]);
            spec["visibility"] = Name(p.Visibility);
            if (AccessSpec(p.AccessTeamIds, p.AccessUsernames) is { } list) spec["access"] = list;
            Read(RecordKind.Page, p, false);
            return new ExchangeItemDto(Page, p.Id, new ExchangeMetadataDto(p.Title, null), spec);
        }));
        return new ExchangeEnvelopeDto(FormatVersion, source, FullMode, scope, items);
    }

    private static JsonObject? AccessSpec(List<string> teams, List<string> users) => teams.Count + users.Count == 0 ? null : new JsonObject
    {
        ["teamIds"] = new JsonArray([.. teams.Select(t => (JsonNode)JsonValue.Create(t))]),
        ["usernames"] = new JsonArray([.. users.Select(u => (JsonNode)JsonValue.Create(u))]),
    };

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

    /// <param name="Keeps">Fields absent from the file, which keep their current value on an update: <c>access</c>, <c>protectedNotes</c>.</param>
    private sealed record Parsed(int Index, string Kind, string Id, VersionedEntity Entity, Action<VersionedEntity, FieldErrors, References> Apply,
        string? Parent, IReadOnlySet<string> Keeps);

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
            Union(known.Flows, parsed, Flow), Union(known.Pages, parsed, Page), parents, known.Users);
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
                    Keep(p.Keeps, current, p.Entity);
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
        Unknown(envelope, ["formatVersion", "source", "mode", "scope", "items"], "", (path, msg) => Envelope(path, msg));
        if (Text(envelope["formatVersion"]) != FormatVersion)
            Envelope("formatVersion", $"Expected \"{FormatVersion}\" (got {Show(envelope["formatVersion"])}).");
        if (Text(envelope["mode"]) != FullMode)
            Envelope("mode", $"Expected \"{FullMode}\" (got {Show(envelope["mode"])}).");
        if (envelope["scope"] is { } scope && Text(scope) is not (VisibleScope or FullScope))
            Envelope("scope", $"Expected \"{VisibleScope}\" or \"{FullScope}\" (got {Show(scope)}).");
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
                parsed.Add(new Parsed(i, kind!, id!, entry.Value.Entity, entry.Value.Apply, kind == Page ? Text(specObject["parentId"]) : null,
                    KeptWhenAbsent.Where(k => SpecFields[kind!].Contains(k) && !specObject.ContainsKey(k)).ToHashSet()));
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
                    Enum<Lifecycle>(spec["lifecycle"], "lifecycle", error), Enum<Visibility>(spec["visibility"], "visibility", error), null,
                    AccessInput(spec["access"], error), Text(spec["protectedNotes"]));
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
                    Text(spec["ownerTeamId"]), tags, Enum<Visibility>(spec["visibility"], "visibility", error), null, AccessInput(spec["access"], error));
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
                    Text(spec["volume"]), description, change, null, Text(spec["protectedNotes"]));
                return (new Flow { Id = id }, (e, err, r) => Catalog.Apply((Flow)e, input, err, r));
            }
        }
    }

    /// <summary><c>access: {teamIds: [...], usernames: [...]}</c>; null when absent.</summary>
    private static AccessListInput? AccessInput(JsonNode? node, Action<string, string> error)
    {
        if (node is null) return null;
        if (node is not JsonObject list)
        {
            error("access", "Expected an object with teamIds and usernames.");
            return null;
        }
        Unknown(list, ["teamIds", "usernames"], "access.", error);
        List<string>? Strings(string field)
        {
            if (list[field] is null) return [];
            if (list[field] is not JsonArray array)
            {
                error($"access.{field}", "Expected an array of texts.");
                return [];
            }
            var values = new List<string>();
            foreach (var value in array)
                if (Text(value) is { } text) values.Add(text);
                else error($"access.{field}", "Expected an array of texts.");
            return values;
        }
        return new AccessListInput(Strings("teamIds"), Strings("usernames"));
    }

    /// <summary>Fields absent from the file keep the value of the record being updated.</summary>
    private static void Keep(IReadOnlySet<string> keeps, VersionedEntity current, VersionedEntity imported)
    {
        switch (current, imported)
        {
            case (Domain.Application c, Domain.Application i):
                if (keeps.Contains("access")) (i.AccessTeamIds, i.AccessUsernames) = (c.AccessTeamIds, c.AccessUsernames);
                if (keeps.Contains("protectedNotes")) i.ProtectedNotes = c.ProtectedNotes;
                break;
            case (Domain.Flow c, Domain.Flow i):
                if (keeps.Contains("protectedNotes")) i.ProtectedNotes = c.ProtectedNotes;
                break;
            case (Domain.Page c, Domain.Page i):
                if (keeps.Contains("access")) (i.AccessTeamIds, i.AccessUsernames) = (c.AccessTeamIds, c.AccessUsernames);
                break;
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
                && x.Tags.SequenceEqual(y.Tags) && x.Visibility == y.Visibility
                && x.AccessTeamIds.SequenceEqual(y.AccessTeamIds) && x.AccessUsernames.SequenceEqual(y.AccessUsernames),
            (BusinessDomain x, BusinessDomain y) => x.Order == y.Order,
            (Team x, Team y) => x.Contact == y.Contact,
            (Application x, Application y) => x.DomainId == y.DomainId && x.TeamId == y.TeamId && x.Kind == y.Kind && x.Tech == y.Tech
                && x.Lifecycle == y.Lifecycle && x.Visibility == y.Visibility && x.ProtectedNotes == y.ProtectedNotes
                && x.AccessTeamIds.SequenceEqual(y.AccessTeamIds) && x.AccessUsernames.SequenceEqual(y.AccessUsernames),
            (DataObject x, DataObject y) => x.Sensitivity == y.Sensitivity && x.SourceApplicationId == y.SourceApplicationId,
            (Flow x, Flow y) => x.FromId == y.FromId && x.ToId == y.ToId && x.Type == y.Type && x.Schedule == y.Schedule
                && x.DataObjectIds.SequenceEqual(y.DataObjectIds) && x.OwnerTeamId == y.OwnerTeamId && x.Status == y.Status
                && x.Volume == y.Volume && x.PlannedChangeSummary == y.PlannedChangeSummary && x.PlannedChangeDate == y.PlannedChangeDate
                && x.ProtectedNotes == y.ProtectedNotes,
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
                (y.AccessTeamIds, y.AccessUsernames) = (x.AccessTeamIds, x.AccessUsernames);
                break;
            case (BusinessDomain x, BusinessDomain y):
                y.Order = x.Order;
                break;
            case (Team x, Team y):
                y.Contact = x.Contact;
                break;
            case (Application x, Application y):
                (y.DomainId, y.TeamId, y.Kind, y.Tech, y.Lifecycle, y.Visibility) = (x.DomainId, x.TeamId, x.Kind, x.Tech, x.Lifecycle, x.Visibility);
                (y.AccessTeamIds, y.AccessUsernames, y.ProtectedNotes) = (x.AccessTeamIds, x.AccessUsernames, x.ProtectedNotes);
                break;
            case (DataObject x, DataObject y):
                (y.Sensitivity, y.SourceApplicationId) = (x.Sensitivity, x.SourceApplicationId);
                break;
            case (Flow x, Flow y):
                (y.FromId, y.ToId, y.Type, y.Schedule, y.DataObjectIds, y.OwnerTeamId, y.Status, y.Volume, y.PlannedChangeSummary, y.PlannedChangeDate) =
                    (x.FromId, x.ToId, x.Type, x.Schedule, x.DataObjectIds, x.OwnerTeamId, x.Status, x.Volume, x.PlannedChangeSummary, x.PlannedChangeDate);
                y.ProtectedNotes = x.ProtectedNotes;
                break;
        }
    }
}
