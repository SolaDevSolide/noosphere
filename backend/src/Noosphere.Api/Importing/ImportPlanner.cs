using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Importing;

/// <summary>One write of an applied plan: the whole record after the change, in the shape of its API input.</summary>
/// <param name="BaseVersion">Version of the record the update was planned against; null for a creation.</param>
/// <param name="Entry">Index of the entry of the preview it writes.</param>
public sealed record PlannedOperation(RecordKind Kind, string Id, bool Create, int? BaseVersion, JsonObject After, int? Entry = null);

/// <summary>A dry run: the preview, and the writes that applying it makes.</summary>
public sealed record PlanOutcome(IReadOnlyList<string> Formats, IReadOnlyList<ImportFileDto> Files, IReadOnlyList<ImportEntryDto> Entries,
    IReadOnlyList<ImportMessageDto> Warnings, IReadOnlyList<ImportMessageDto> Errors, IReadOnlyList<PlannedOperation> Operations);

/// <summary>
/// Turns uploaded files into a plan. Readers give candidates; each is matched to an existing record by its stable
/// identifier, then by its exact normalized name (close names are only reported); references are resolved; the record
/// after the change is validated with the API rules and the rights of the person, and compared field by field. Nothing
/// is written: <see cref="ImportService"/> stores the plan and applies it.
/// </summary>
public sealed partial class ImportPlanner(NoosphereDbContext db, Access access)
{
    private static readonly RecordKind[] Order = [RecordKind.Domain, RecordKind.Team, RecordKind.Application, RecordKind.DataObject, RecordKind.Flow, RecordKind.Page];

    public const string ImportedDomainKey = "imported";
    public const string ImportedDomainName = "Imported";

    private sealed class Existing(RecordKind kind, VersionedEntity entity, string name, bool hidden)
    {
        public RecordKind Kind { get; } = kind;
        public VersionedEntity Entity { get; } = entity;
        public string Name { get; } = name;
        public string Normalized { get; } = Normalize.Name(name);
        public bool Hidden { get; } = hidden;
    }

    /// <summary>A candidate once matched: the id it gets, and the existing record it updates.</summary>
    private sealed class Planned(Candidate candidate)
    {
        public Candidate Candidate { get; } = candidate;
        public string Id { get; set; } = "";
        public Existing? Match { get; set; }
        public string? MatchedBy { get; set; }
        public List<ImportPossibleMatchDto> Possible { get; } = [];
        public bool Skipped { get; set; }
    }

    private readonly Dictionary<RecordKind, List<Existing>> _existing = [];
    private readonly Dictionary<RecordKind, List<Planned>> _planned = [];
    private readonly List<ImportMessageDto> _warnings = [];
    private readonly List<ImportMessageDto> _errors = [];
    private HashSet<string> _users = [];

    public async Task<PlanOutcome> Plan(IReadOnlyList<ImportFile> uploaded, ImportOptions options, CancellationToken cancel = default)
    {
        var read = new ReadResult();
        var files = new List<ImportFileDto>();
        if (uploaded.Count == 0) read.Fail(new ImportSourceDto("", null, null), "No file: send one or more files, or a zip archive.");
        var expanded = Uploads.Expand(uploaded, read);
        var formats = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in expanded)
        {
            var format = Uploads.Detect(file, options.Format, out var problem);
            var one = new ReadResult();
            if (format is null) one.Fail(new ImportSourceDto(file.Name, null, null), problem!);
            else
            {
                formats.Add(format);
                try
                {
                    ReadFile(file, format, options, one);
                }
                catch (ImportReadException e)
                {
                    one.Fail(new ImportSourceDto(file.Name, e.Line, null), e.Message);
                }
            }
            foreach (var c in one.Candidates) c.Format = format!;
            files.Add(new ImportFileDto(file.Name, format, file.Content.LongLength, Convert.ToHexStringLower(SHA256.HashData(file.Content)), one.Candidates.Count));
            read.Candidates.AddRange(one.Candidates);
            read.Warnings.AddRange(one.Warnings);
            read.Errors.AddRange(one.Errors);
        }
        _warnings.AddRange(read.Warnings);
        _errors.AddRange(read.Errors);
        if (read.Candidates.Count > ImportLimits.MaxEntries)
        {
            _errors.Add(new($"The files hold {read.Candidates.Count} records: at most {ImportLimits.MaxEntries} in one import. Split them.", new("", null, null), null, null));
            return new([.. formats], files, [], _warnings, _errors, []);
        }

        await LoadExisting(cancel);
        CheckOptions(options);
        var candidates = Merge(read.Candidates);
        foreach (var kind in Order.Where(k => k != RecordKind.Flow))
            foreach (var c in candidates.Where(c => c.Kind == kind)) Match(c);
        foreach (var p in All()) ResolveRefs(p);
        ApplyDefaults(options);
        foreach (var c in candidates.Where(c => c.Kind == RecordKind.Flow)) Match(c);
        foreach (var p in All(RecordKind.Flow).Where(p => !p.Skipped)) ResolveRefs(p);
        Mentions();
        var (entries, operations) = Build();
        return new([.. formats], files, entries, _warnings, _errors, operations);
    }

    private static void ReadFile(ImportFile file, string format, ImportOptions options, ReadResult result)
    {
        if (format == ImportFormats.Excel)
        {
            ExcelReader.Read(file.Name, file.Content, result);
            return;
        }
        var text = Uploads.Text(file.Content) ?? throw new ImportReadException("The file is not UTF-8 text.");
        switch (format)
        {
            case ImportFormats.Backstage: BackstageReader.Read(file.Name, text, result); break;
            case ImportFormats.OpenApi: OpenApiReader.Read(file.Name, text, options, result); break;
            case ImportFormats.AsyncApi: AsyncApiReader.Read(file.Name, text, options, result); break;
            case ImportFormats.Adr: AdrReader.Read(file.Name, text, options, result); break;
            case ImportFormats.Mermaid: MermaidReader.Read(file.Name, text, result); break;
            default: throw new ImportReadException($"Unknown format '{format}'.");
        }
    }

    private IEnumerable<Planned> All(params RecordKind[] kinds) =>
        (kinds.Length == 0 ? Order : kinds).SelectMany(k => _planned.GetValueOrDefault(k) ?? []);

    private async Task LoadExisting(CancellationToken cancel)
    {
        void Add(RecordKind kind, VersionedEntity e, string name) =>
            (_existing.TryGetValue(kind, out var list) ? list : _existing[kind] = []).Add(new Existing(kind, e, name, access.Sight(kind, e) == Sight.Hidden));
        foreach (var e in await db.Domains.AsNoTracking().ToListAsync(cancel)) Add(RecordKind.Domain, e, e.Name);
        foreach (var e in await db.Teams.AsNoTracking().ToListAsync(cancel)) Add(RecordKind.Team, e, e.Name);
        foreach (var e in await db.Applications.AsNoTracking().ToListAsync(cancel)) Add(RecordKind.Application, e, e.Name);
        foreach (var e in await db.DataObjects.AsNoTracking().ToListAsync(cancel)) Add(RecordKind.DataObject, e, e.Name);
        foreach (var e in await db.Flows.AsNoTracking().ToListAsync(cancel)) Add(RecordKind.Flow, e, e.Name);
        foreach (var e in await db.Pages.AsNoTracking().ToListAsync(cancel)) Add(RecordKind.Page, e, e.Title);
        _users = (await db.Users.AsNoTracking().Select(u => u.Username).ToListAsync(cancel)).ToHashSet(StringComparer.Ordinal);
        foreach (var kind in Order)
        {
            _existing.TryAdd(kind, []);
            _planned[kind] = [];
        }
    }

    private void CheckOptions(ImportOptions options)
    {
        void Check(string? id, RecordKind kind, string option)
        {
            if (id is not null && !_existing[kind].Any(e => e.Entity.Id == id && !e.Hidden))
                _errors.Add(new($"{option}: no {Records.Of(kind).Noun} with the id '{id}'.", new("", null, null), null, option));
        }
        Check(options.DomainId, RecordKind.Domain, "domainId");
        Check(options.TeamId, RecordKind.Team, "teamId");
        Check(options.ParentPageId, RecordKind.Page, "parentPageId");
    }

    /// <summary>
    /// Candidates describing the same record (same kind and key, or same name without a key) are merged: later fields
    /// replace earlier ones (with a warning when they differ), defaults and references fill what is missing.
    /// </summary>
    private List<Candidate> Merge(List<Candidate> candidates)
    {
        var merged = new List<Candidate>();
        var index = new Dictionary<(RecordKind, string), Candidate>();
        foreach (var c in candidates)
        {
            var key = (c.Kind, c.Key ?? "name:" + Normalize.Name(c.Name));
            if (!index.TryGetValue(key, out var first))
            {
                index[key] = c;
                merged.Add(c);
                continue;
            }
            foreach (var (field, value) in c.Fields)
            {
                if (first.Fields[field] is { } old && !JsonNode.DeepEquals(old, value))
                    _warnings.Add(new($"{Label(c)} is described more than once with different values of {field}: the last one is kept.", c.Source, null, field));
                first.Fields[field] = value?.DeepClone();
            }
            foreach (var (field, value) in c.Defaults)
                if (!first.Defaults.ContainsKey(field)) first.Defaults[field] = value?.DeepClone();
            foreach (var (field, reference) in c.Refs)
                if (!first.Refs.TryGetValue(field, out var old) || (old.Soft && !reference.Soft)) first.Refs[field] = reference;
                else if (first.Additive.Contains(field) || c.Additive.Contains(field))
                    first.Refs[field] = old with { Tokens = [.. old.Tokens.Union(reference.Tokens, StringComparer.Ordinal)] };
            first.Additive.UnionWith(c.Additive);
            if (!c.NameIsDefault && first.NameIsDefault) (first.Name, first.NameIsDefault) = (c.Name, false);
            first.LinkMentions |= c.LinkMentions;
            first.AttachToSingleMention &= c.AttachToSingleMention;
            first.Errors.AddRange(c.Errors);
        }
        return merged;
    }

    private static string Label(Candidate c) => $"{Records.Of(c.Kind).Noun} '{c.Name}'";

    // ---------- Matching ----------

    /// <summary>
    /// The existing record of a candidate: same id (its stable identifier), or else exactly the same normalized name
    /// (flows: the same name between the same applications). Records with a close name are possible matches only.
    /// </summary>
    private void Match(Candidate c)
    {
        var p = new Planned(c);
        var existing = _existing[c.Kind];
        var norm = Normalize.Name(c.Name);
        IEnumerable<Existing> pool = existing;
        if (c.Kind == RecordKind.Flow)
        {
            var from = Resolve(RecordKind.Application, Token(c, "fromId"));
            var to = Resolve(RecordKind.Application, Token(c, "toId"));
            if (from is null || to is null)
            {
                var missing = from is null ? Token(c, "fromId") : Token(c, "toId");
                _warnings.Add(new($"Flow '{c.Name}' skipped: no application '{missing}' in the files or in Noosphere.", c.Source, null, from is null ? "fromId" : "toId"));
                return;
            }
            pool = existing.Where(e => e.Entity is Flow f && f.FromId == from && f.ToId == to);
            if (c.DerivedKeySuffix is { } suffix) c.Key = Normalize.Key(string.Join('-', new[] { from, to, suffix }.Where(x => x.Length > 0)));
        }
        var claimed = _planned[c.Kind].Where(x => x.Match is not null).Select(x => x.Match!).ToHashSet();
        if (c.Key is not null && existing.FirstOrDefault(e => e.Entity.Id == c.Key) is { } byId) (p.Match, p.MatchedBy) = (byId, "identifier");
        else
        {
            var byName = pool.Where(e => !e.Hidden && e.Normalized == norm && norm.Length > 0).ToList();
            if (byName.Count > 1)
            {
                _warnings.Add(new($"{Label(c)} skipped: {byName.Count} existing records have this name ({string.Join(", ", byName.Select(e => e.Entity.Id))}). Give its id in the file.", c.Source, null, "name"));
                return;
            }
            if (byName.Count == 1) (p.Match, p.MatchedBy) = (byName[0], "name");
            // Decision records go under the page the landscape already keeps them in: the only top-level page tagged adr.
            else if (c.Kind == RecordKind.Page && c.Key == AdrReader.ParentKey
                && existing.Where(e => !e.Hidden && e.Entity is Page { ParentId: null } page && page.Tags.Contains("adr", StringComparer.OrdinalIgnoreCase)).ToList() is [var home])
                (p.Match, p.MatchedBy) = (home, "tag");
        }
        if (p.Match is not null && claimed.Contains(p.Match))
        {
            _warnings.Add(new($"{Label(c)} skipped: another entry of the files already matches the existing record '{p.Match.Entity.Id}'.", c.Source, null, null));
            return;
        }
        if (p.Match is null)
        {
            p.Possible.AddRange(pool.Where(e => !e.Hidden && Normalize.Close(e.Normalized, norm)).Take(5).Select(e => new ImportPossibleMatchDto(e.Entity.Id, e.Name)));
            var used = existing.Select(e => e.Entity.Id).Concat(_planned[c.Kind].Select(x => x.Id)).ToHashSet(StringComparer.Ordinal);
            var errors = new FieldErrors();
            p.Id = c.Key is not null && Rules.IsValidId(c.Key) && !used.Contains(c.Key) ? c.Key : Catalog.NewId(null, c.Name, used, errors);
        }
        else p.Id = p.Match.Entity.Id;
        _planned[c.Kind].Add(p);
    }

    private static string? Token(Candidate c, string field) => c.Refs.TryGetValue(field, out var r) && r.Tokens.Count > 0 ? r.Tokens[0] : null;

    /// <summary>
    /// Id of the record a token names: a key of the files, an existing id, a name of the files, an existing name (one
    /// only), in this order. Null when nothing matches.
    /// </summary>
    private string? Resolve(RecordKind kind, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var key = Normalize.Key(token);
        var planned = _planned[kind];
        if (planned.FirstOrDefault(p => !p.Skipped && p.Candidate.Key == key) is { } byKey) return byKey.Id;
        if (_existing[kind].FirstOrDefault(e => e.Entity.Id == token.Trim()) is { } byId) return byId.Entity.Id;
        var norm = Normalize.Name(token);
        if (planned.FirstOrDefault(p => !p.Skipped && Normalize.Name(p.Candidate.Name) == norm) is { } byName) return byName.Id;
        var named = _existing[kind].Where(e => !e.Hidden && e.Normalized == norm).ToList();
        if (named.Count == 1) return named[0].Entity.Id;
        return _existing[kind].FirstOrDefault(e => e.Entity.Id == key)?.Entity.Id;
    }

    /// <summary>References of a candidate turned into ids, in its fields (or defaults, for soft references).</summary>
    private void ResolveRefs(Planned p)
    {
        var c = p.Candidate;
        foreach (var (field, reference) in c.Refs)
        {
            var target = reference.Soft ? c.Defaults : c.Fields;
            var ids = new List<string>();
            foreach (var token in reference.Tokens)
                if (Resolve(reference.Kind, token) is { } id) ids.Add(id);
                else if (!reference.Soft)
                    _warnings.Add(new($"{Label(c)}: no {Records.Of(reference.Kind).Noun} '{token}' in the files or in Noosphere, so {field} is left out.", c.Source, null, field));
            if (field == "dataObjectIds")
            {
                if (ids.Count > 0) target[field] = new JsonArray([.. ids.Distinct().Select(i => (JsonNode)JsonValue.Create(i))]);
            }
            else if (ids.Count > 0)
            {
                if (field == "attachedTo") target[field] = new JsonObject { ["kind"] = Rules.KindName(reference.Kind), ["id"] = ids[0] };
                else target[field] = ids[0];
            }
        }
    }

    /// <summary>
    /// What a new record needs and the files did not give: the default domain (or an "Imported" domain) for applications,
    /// the default team for owned records.
    /// </summary>
    private void ApplyDefaults(ImportOptions options)
    {
        var withoutDomain = _planned[RecordKind.Application].Where(p => p.Match is null && Missing(p.Candidate, "domainId")).ToList();
        if (withoutDomain.Count > 0)
        {
            var domain = options.DomainId;
            if (domain is null)
            {
                var imported = new Candidate(RecordKind.Domain, ImportedDomainKey, ImportedDomainName, withoutDomain[0].Candidate.Source) { NameIsDefault = true, Format = withoutDomain[0].Candidate.Format };
                imported.Defaults["description"] = "Applications imported without a domain. Move them to their domain.";
                if (Resolve(RecordKind.Domain, ImportedDomainKey) is { } found) domain = found;
                else
                {
                    Match(imported);
                    domain = _planned[RecordKind.Domain][^1].Id;
                }
                _warnings.Add(new($"{withoutDomain.Count} new application(s) have no domain: they go to the domain '{ImportedDomainName}'. Give a default domain (domainId) to choose it.",
                    withoutDomain[0].Candidate.Source, null, "domainId"));
            }
            foreach (var p in withoutDomain) p.Candidate.Defaults["domainId"] = domain;
        }
        if (options.TeamId is { } team)
            foreach (var p in All(RecordKind.Application, RecordKind.Flow, RecordKind.Page).Where(p => p.Match is null))
            {
                var field = p.Candidate.Kind == RecordKind.Application ? "teamId" : "ownerTeamId";
                if (Missing(p.Candidate, field)) p.Candidate.Defaults[field] = team;
            }
    }

    private static bool Missing(Candidate c, string field) => c.Fields[field] is null && c.Defaults[field] is null;

    // ---------- Mentions (ADR) ----------

    [GeneratedRegex(@"\n## Records mentioned\n[\s\S]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MentionSection();

    /// <summary>
    /// Pages that ask for it get a "Records mentioned" section: wiki links to the applications whose name (three
    /// characters at least, as a whole word, case and accents ignored) appears in the body and that are not linked yet.
    /// </summary>
    private void Mentions()
    {
        var apps = _existing[RecordKind.Application].Where(e => !e.Hidden).Select(e => (e.Entity.Id, e.Name))
            .Concat(_planned[RecordKind.Application].Where(p => p.Match is null).Select(p => (p.Id, p.Candidate.Name)))
            .Where(a => Normalize.Name(a.Item2).Length >= 3)
            .ToList();
        foreach (var p in _planned[RecordKind.Page].Where(p => p.Candidate.LinkMentions))
        {
            var c = p.Candidate;
            if (ReaderKit.Str(c.Fields["body"]) is not { } body) continue;
            body = MentionSection().Replace(body, "\n");
            var linked = Rules.WikiLinks(body).Where(l => l.Kind == RecordKind.Application).Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
            var text = " " + Normalize.Name(body) + " ";
            var mentioned = apps.Where(a => !linked.Contains(a.Id) && text.Contains(" " + Normalize.Name(a.Item2) + " ", StringComparison.Ordinal))
                .Select(a => a.Id).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
            var all = linked.Concat(mentioned).Distinct().ToList();
            if (mentioned.Count > 0)
                body = body.TrimEnd() + "\n\n## Records mentioned\n\n" + string.Concat(mentioned.Select(id => $"- [[application:{id}]]\n"));
            c.Fields["body"] = body.TrimEnd() + "\n";
            if (c.AttachToSingleMention && !c.Refs.ContainsKey("attachedTo") && all.Count == 1)
                c.Defaults["attachedTo"] = new JsonObject { ["kind"] = "application", ["id"] = all[0] };
        }
    }

    // ---------- Build ----------

    private (List<ImportEntryDto>, List<PlannedOperation>) Build()
    {
        var entries = new List<ImportEntryDto>();
        var operations = new List<PlannedOperation>();
        var lookups = new List<(RecordKind Kind, string Id)>();
        var refs = Watched(PlannedReferences(), lookups);
        var looked = new List<List<(RecordKind Kind, string Id)>>();
        foreach (var p in OrderedForWrite())
        {
            lookups.Clear();
            var c = p.Candidate;
            var type = Records.Of(c.Kind);
            var index = entries.Count;
            var nameField = c.Kind == RecordKind.Page ? "title" : "name";
            JsonObject? before = null;
            JsonObject after;
            if (p.Match is { } match)
            {
                before = type.Snapshot(match.Entity);
                before.Remove("protectedNotes");
                after = before.DeepClone().AsObject();
                if (!c.NameIsDefault) after[nameField] = c.Name;
            }
            else after = new JsonObject { ["id"] = p.Id, [nameField] = c.Name };
            foreach (var (field, value) in c.Fields)
                after[field] = c.Additive.Contains(field) && value is JsonArray list ? Union(after[field], list) : value?.DeepClone();
            foreach (var (field, value) in c.Defaults)
                if (Diffs.Value(after, field) is null) after[field] = value?.DeepClone();

            var entity = type.Blank();
            entity.Id = p.Id;
            var fieldErrors = new FieldErrors();
            type.Apply(entity, after, fieldErrors, refs);
            looked.Add([.. lookups]);
            var errorCount = _errors.Count;
            foreach (var (field, message) in fieldErrors.All) _errors.Add(new($"{Label(c)}: {field}: {message}", c.Source, index, field));
            foreach (var (field, message) in c.Errors) _errors.Add(new($"{Label(c)}: {message}", c.Source, index, field));
            var diff = Diffs.Compare(type, before, after, access.Facts, access);
            var action = p.Match is null ? "create" : diff.Count == 0 ? "unchanged" : "update";
            if (action != "unchanged" && !fieldErrors.Any)
            {
                var problem = (p.Match is { } m ? access.PublishProblem(c.Kind, m.Entity) : null) ?? access.PublishProblem(c.Kind, entity);
                if (problem is not null) _errors.Add(new($"{Label(c)}: {problem}", c.Source, index, null));
            }
            entries.Add(new ImportEntryDto(index, action, c.Kind, p.Id, c.Kind == RecordKind.Page ? ReaderKit.Str(after["title"]) ?? c.Name : ReaderKit.Str(after["name"]) ?? c.Name,
                p.MatchedBy, diff, p.Possible, c.Source, _errors.Count > errorCount, []));
            if (action != "unchanged") operations.Add(new PlannedOperation(c.Kind, p.Id, p.Match is null, p.Match?.Entity.Version, after, index));
        }
        // An entry requires the creations it refers to: leaving one out would leave a reference to nothing.
        var created = entries.Where(e => e.Action == "create").ToDictionary(e => (e.Kind, e.Id), e => e.Index);
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Action == "unchanged") continue;
            var requires = looked[i].Select(l => created.TryGetValue(l, out var at) ? at : -1).Where(at => at >= 0 && at != i).Distinct().Order().ToList();
            if (requires.Count > 0) entries[i] = entries[i] with { Requires = requires };
        }
        return (entries, operations);
    }

    /// <summary>The references, noting every id the validation looks up (the records an entry refers to).</summary>
    private static References Watched(References refs, List<(RecordKind Kind, string Id)> lookups) => new(
        new LookedUp(refs.Domains, RecordKind.Domain, lookups), new LookedUp(refs.Teams, RecordKind.Team, lookups),
        new LookedUp(refs.Applications, RecordKind.Application, lookups), new LookedUp(refs.DataObjects, RecordKind.DataObject, lookups),
        new LookedUp(refs.Flows, RecordKind.Flow, lookups), new LookedUp(refs.Pages, RecordKind.Page, lookups), refs.PageParents, refs.Users);

    /// <summary>A set of ids that notes the ids it is asked about.</summary>
    private sealed class LookedUp(ISet<string> inner, RecordKind kind, List<(RecordKind Kind, string Id)> lookups) : ISet<string>
    {
        public bool Contains(string item)
        {
            lookups.Add((kind, item));
            return inner.Contains(item);
        }

        public int Count => inner.Count;
        public bool IsReadOnly => inner.IsReadOnly;
        public bool Add(string item) => inner.Add(item);
        void ICollection<string>.Add(string item) => inner.Add(item);
        public void Clear() => inner.Clear();
        public void CopyTo(string[] array, int arrayIndex) => inner.CopyTo(array, arrayIndex);
        public bool Remove(string item) => inner.Remove(item);
        public IEnumerator<string> GetEnumerator() => inner.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => inner.GetEnumerator();
        public void ExceptWith(IEnumerable<string> other) => inner.ExceptWith(other);
        public void IntersectWith(IEnumerable<string> other) => inner.IntersectWith(other);
        public bool IsProperSubsetOf(IEnumerable<string> other) => inner.IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<string> other) => inner.IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<string> other) => inner.IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<string> other) => inner.IsSupersetOf(other);
        public bool Overlaps(IEnumerable<string> other) => inner.Overlaps(other);
        public bool SetEquals(IEnumerable<string> other) => inner.SetEquals(other);
        public void SymmetricExceptWith(IEnumerable<string> other) => inner.SymmetricExceptWith(other);
        public void UnionWith(IEnumerable<string> other) => inner.UnionWith(other);
    }

    private static JsonArray Union(JsonNode? current, JsonArray added)
    {
        var values = new List<string>();
        foreach (var node in (current as JsonArray ?? []).Concat(added))
            if (ReaderKit.Str(node) is { } s && !values.Contains(s, StringComparer.OrdinalIgnoreCase)) values.Add(s);
        return new JsonArray([.. values.Select(v => (JsonNode)JsonValue.Create(v))]);
    }

    /// <summary>Ids that records may refer to: those in the database and those the plan creates.</summary>
    private References PlannedReferences()
    {
        HashSet<string> Ids(RecordKind kind) => [.. _existing[kind].Select(e => e.Entity.Id), .. _planned[kind].Select(p => p.Id)];
        var parents = _existing[RecordKind.Page].ToDictionary(e => e.Entity.Id, e => ((Page)e.Entity).ParentId);
        foreach (var p in _planned[RecordKind.Page])
            parents[p.Id] = ReaderKit.Str(p.Candidate.Fields["parentId"]) ?? ReaderKit.Str(p.Candidate.Defaults["parentId"])
                ?? (p.Match?.Entity as Page)?.ParentId;
        return new References(Ids(RecordKind.Domain), Ids(RecordKind.Team), Ids(RecordKind.Application), Ids(RecordKind.DataObject),
            Ids(RecordKind.Flow), Ids(RecordKind.Page), parents, _users);
    }

    /// <summary>Kinds in dependency order; pages after the pages they are placed under.</summary>
    private IEnumerable<Planned> OrderedForWrite()
    {
        foreach (var kind in Order.Where(k => k != RecordKind.Page))
            foreach (var p in _planned[kind]) yield return p;
        var pages = _planned[RecordKind.Page];
        var done = new HashSet<string>(StringComparer.Ordinal);
        var ids = pages.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        string? Parent(Planned p) => ReaderKit.Str(p.Candidate.Fields["parentId"]) ?? ReaderKit.Str(p.Candidate.Defaults["parentId"]);
        while (done.Count < pages.Count)
        {
            var ready = pages.Where(p => !done.Contains(p.Id) && (Parent(p) is not { } parent || !ids.Contains(parent) || done.Contains(parent) || parent == p.Id)).ToList();
            // A cycle between imported pages: write the rest as they come (validation reports the cycle).
            if (ready.Count == 0) ready = pages.Where(p => !done.Contains(p.Id)).ToList();
            foreach (var p in ready)
            {
                done.Add(p.Id);
                yield return p;
            }
        }
    }
}
