using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>
/// Ids that a record may refer to. The API uses those in the database; an import adds those of the file.
/// <see cref="PageParents"/> maps each page to its parent, to refuse cycles in the page tree.
/// </summary>
public sealed record References(
    ISet<string> Domains, ISet<string> Teams, ISet<string> Applications, ISet<string> DataObjects, ISet<string> Flows,
    ISet<string> Pages, IDictionary<string, string?> PageParents, ISet<string> Users)
{
    public static async Task<References> Load(NoosphereDbContext db, CancellationToken cancel = default)
    {
        var pages = await db.Pages.Select(p => new { p.Id, p.ParentId }).ToListAsync(cancel);
        return new(
            (await db.Domains.Select(x => x.Id).ToListAsync(cancel)).ToHashSet(),
            (await db.Teams.Select(x => x.Id).ToListAsync(cancel)).ToHashSet(),
            (await db.Applications.Select(x => x.Id).ToListAsync(cancel)).ToHashSet(),
            (await db.DataObjects.Select(x => x.Id).ToListAsync(cancel)).ToHashSet(),
            (await db.Flows.Select(x => x.Id).ToListAsync(cancel)).ToHashSet(),
            pages.Select(p => p.Id).ToHashSet(),
            pages.ToDictionary(p => p.Id, p => p.ParentId),
            (await db.Users.Select(u => u.Username).ToListAsync(cancel)).ToHashSet());
    }

    public ISet<string> Of(RecordKind kind) => kind switch
    {
        RecordKind.Application => Applications,
        RecordKind.Flow => Flows,
        RecordKind.DataObject => DataObjects,
        RecordKind.Page => Pages,
        RecordKind.Domain => Domains,
        _ => Teams,
    };

    /// <summary>Records the id of a record created in the same batch (proposal, import).</summary>
    public void Add(RecordKind kind, string id) => Of(kind).Add(id);
}

/// <summary>
/// Validation of the landscape records, shared by the API and the import: copies an input onto an entity and records
/// every invalid field (field names as in the API, camelCase).
/// </summary>
public static class Catalog
{
    public static void Apply(BusinessDomain e, DomainInput i, FieldErrors errors)
    {
        e.Name = errors.Required("name", i.Name);
        e.Description = errors.Optional("description", i.Description, Rules.MaxTextLength);
        e.Order = i.Order ?? 0;
    }

    public static void Apply(Team e, TeamInput i, FieldErrors errors)
    {
        e.Name = errors.Required("name", i.Name);
        e.Description = errors.Optional("description", i.Description, Rules.MaxTextLength);
        e.Contact = errors.Optional("contact", i.Contact);
    }

    public static void Apply(Application e, ApplicationInput i, FieldErrors errors, References refs)
    {
        e.Name = errors.Required("name", i.Name);
        e.Description = errors.Optional("description", i.Description, Rules.MaxTextLength);
        e.DomainId = errors.Required("domainId", i.DomainId, Rules.MaxIdLength);
        if (e.DomainId.Length > 0 && !refs.Domains.Contains(e.DomainId)) errors.Add("domainId", $"Unknown domain '{e.DomainId}'.");
        e.TeamId = Reference(errors, "teamId", i.TeamId, refs.Teams, "team");
        e.Kind = i.Kind ?? ApplicationKind.App;
        e.Tech = errors.Optional("tech", i.Tech);
        e.Lifecycle = i.Lifecycle ?? Lifecycle.Live;
        e.Visibility = i.Visibility ?? Visibility.Everyone;
        if (i.Access is { } access) (e.AccessTeamIds, e.AccessUsernames) = AccessList(errors, access, refs);
        e.ProtectedNotes = ProtectedNotes(errors, i.ProtectedNotes, e.ProtectedNotes);
    }

    /// <summary>
    /// Protected notes: left out (or sent masked, as read by someone who may not read them), the current ones are kept;
    /// an empty text clears them.
    /// </summary>
    private static string? ProtectedNotes(FieldErrors errors, string? input, string? current) =>
        input is null || input.Trim() == Rules.ProtectedMask ? current : errors.Optional("protectedNotes", input, Rules.MaxTextLength);

    /// <summary>An access list: known teams and users (lower case), without duplicates.</summary>
    private static (List<string> Teams, List<string> Users) AccessList(FieldErrors errors, AccessListInput input, References refs)
    {
        var teams = (input.TeamIds ?? []).Select(t => t?.Trim() ?? "").Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var users = (input.Usernames ?? []).Select(u => (u ?? "").Trim().ToLowerInvariant()).Where(u => u.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        foreach (var unknown in teams.Where(t => !refs.Teams.Contains(t))) errors.Add("access.teamIds", $"Unknown team '{unknown}'.");
        foreach (var unknown in users.Where(u => !refs.Users.Contains(u))) errors.Add("access.usernames", $"No user named '{unknown}'.");
        if (teams.Count + users.Count > Rules.MaxAccessEntries) errors.Add("access", $"At most {Rules.MaxAccessEntries} teams and users.");
        return (teams, users);
    }

    public static void Apply(DataObject e, DataObjectInput i, FieldErrors errors, References refs)
    {
        e.Name = errors.Required("name", i.Name);
        e.Description = errors.Optional("description", i.Description, Rules.MaxTextLength);
        e.Sensitivity = i.Sensitivity ?? Sensitivity.Internal;
        e.SourceApplicationId = Reference(errors, "sourceApplicationId", i.SourceApplicationId, refs.Applications, "application");
    }

    public static void Apply(Flow e, FlowInput i, FieldErrors errors, References refs)
    {
        e.Name = errors.Required("name", i.Name);
        e.Description = errors.Optional("description", i.Description, Rules.MaxTextLength);
        e.FromId = errors.Required("fromId", i.FromId, Rules.MaxIdLength);
        if (e.FromId.Length > 0 && !refs.Applications.Contains(e.FromId)) errors.Add("fromId", $"Unknown application '{e.FromId}'.");
        e.ToId = errors.Required("toId", i.ToId, Rules.MaxIdLength);
        if (e.ToId.Length > 0 && !refs.Applications.Contains(e.ToId)) errors.Add("toId", $"Unknown application '{e.ToId}'.");
        if (e.FromId.Length > 0 && e.FromId == e.ToId) errors.Add("toId", "A flow links two different applications.");
        e.Type = i.Type ?? FlowType.Api;
        e.Schedule = errors.Optional("schedule", i.Schedule);
        var objects = (i.DataObjectIds ?? []).Select(x => x?.Trim() ?? "").Distinct(StringComparer.Ordinal).ToList();
        foreach (var unknown in objects.Where(x => !refs.DataObjects.Contains(x)))
            errors.Add("dataObjectIds", $"Unknown data object '{unknown}'.");
        e.DataObjectIds = objects;
        e.OwnerTeamId = Reference(errors, "ownerTeamId", i.OwnerTeamId, refs.Teams, "team");
        e.Status = i.Status ?? FlowStatus.Live;
        e.Volume = errors.Optional("volume", i.Volume);
        if (i.PlannedChange is { } change)
        {
            e.PlannedChangeSummary = errors.Required("plannedChange.summary", change.Summary, Rules.MaxShortTextLength);
            e.PlannedChangeDate = change.Date;
        }
        else
        {
            e.PlannedChangeSummary = null;
            e.PlannedChangeDate = null;
        }
        e.ProtectedNotes = ProtectedNotes(errors, i.ProtectedNotes, e.ProtectedNotes);
    }

    public static void Apply(Page e, PageInput i, FieldErrors errors, References refs)
    {
        e.Title = errors.Required("title", i.Title);
        var body = i.Body ?? "";
        if (body.Length > Rules.MaxBodyLength) errors.Add("body", $"At most {Rules.MaxBodyLength} characters.");
        else errors.CheckSecret("body", body);
        e.Body = body;
        e.Order = i.Order ?? 0;
        e.ParentId = Reference(errors, "parentId", i.ParentId, refs.Pages, "page");
        if (e.ParentId is not null && e.Id.Length > 0)
        {
            // Walk up from the new parent: reaching the page itself would make a cycle.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var at = e.ParentId; at is not null && seen.Add(at); at = refs.PageParents.TryGetValue(at, out var up) ? up : null)
                if (at == e.Id)
                {
                    errors.Add("parentId", "A page cannot be placed under itself or one of its own sub-pages.");
                    break;
                }
        }
        if (i.AttachedTo is { } attached)
        {
            var id = attached.Id?.Trim() ?? "";
            if (attached.Kind == RecordKind.Page) errors.Add("attachedTo.kind", "A page is attached to an application, flow, data object, domain or team.");
            else if (id.Length == 0) errors.Add("attachedTo.id", "This field is required.");
            else if (!refs.Of(attached.Kind).Contains(id)) errors.Add("attachedTo.id", $"Unknown {Rules.KindName(attached.Kind)} '{id}'.");
            (e.AttachedKind, e.AttachedId) = (attached.Kind, id);
        }
        else (e.AttachedKind, e.AttachedId) = (null, null);
        e.OwnerTeamId = Reference(errors, "ownerTeamId", i.OwnerTeamId, refs.Teams, "team");
        var tags = (i.Tags ?? []).Select(t => t?.Trim() ?? "").Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tags.Count > Rules.MaxTags) errors.Add("tags", $"At most {Rules.MaxTags} tags.");
        if (tags.Any(t => t.Length > Rules.MaxTagLength)) errors.Add("tags", $"A tag has at most {Rules.MaxTagLength} characters.");
        else errors.CheckSecret("tags", string.Join("\n", tags));
        e.Tags = tags;
        e.Visibility = i.Visibility ?? Visibility.Everyone;
        if (i.Access is { } access) (e.AccessTeamIds, e.AccessUsernames) = AccessList(errors, access, refs);
        e.Links = [.. Rules.WikiLinks(e.Body).Select(l => l.Key)];
    }

    private static string? Reference(FieldErrors errors, string field, string? value, ISet<string> known, string what)
    {
        var id = value?.Trim();
        if (string.IsNullOrEmpty(id)) return null;
        if (!known.Contains(id)) errors.Add(field, $"Unknown {what} '{id}'.");
        return id;
    }

    /// <summary>
    /// Id of a new record: the requested one (a valid slug, not used yet: 409 otherwise), or one derived from the name,
    /// made unique with a numeric suffix.
    /// </summary>
    public static string NewId(string? requested, string name, ISet<string> existing, FieldErrors errors)
    {
        var id = requested?.Trim();
        if (!string.IsNullOrEmpty(id))
        {
            if (!Rules.IsValidId(id))
            {
                errors.Add("id", $"Use lower-case letters, digits and single hyphens (kebab-case), at most {Rules.MaxIdLength} characters.");
                return id;
            }
            if (existing.Contains(id)) throw ApiProblem.Conflict($"The id '{id}' is already used.");
            return id;
        }
        var root = Rules.Slugify(name);
        var candidate = root;
        for (var n = 2; existing.Contains(candidate); n++)
        {
            var suffix = $"-{n}";
            candidate = (root.Length + suffix.Length > Rules.MaxIdLength ? root[..(Rules.MaxIdLength - suffix.Length)].TrimEnd('-') : root) + suffix;
        }
        return candidate;
    }
}
