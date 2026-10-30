using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>Ids that a record may refer to. The API uses those in the database; an import adds those of the file.</summary>
public sealed record References(ISet<string> Domains, ISet<string> Teams, ISet<string> Applications, ISet<string> DataObjects)
{
    public static async Task<References> Load(NoosphereDbContext db, CancellationToken cancel = default) => new(
        (await db.Domains.Select(x => x.Id).ToListAsync(cancel)).ToHashSet(),
        (await db.Teams.Select(x => x.Id).ToListAsync(cancel)).ToHashSet(),
        (await db.Applications.Select(x => x.Id).ToListAsync(cancel)).ToHashSet(),
        (await db.DataObjects.Select(x => x.Id).ToListAsync(cancel)).ToHashSet());
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
