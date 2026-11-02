using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Links;

/// <summary>The sender of deployment events: a link, or an API key.</summary>
public sealed record DeploymentSender(Guid? LinkId, Guid? ApiKeyId)
{
    public static DeploymentSender Of(Link link) => new(link.Id, null);

    public static DeploymentSender Of(ApiKey key) => new(null, key.Id);
}

/// <summary>The links and API keys deployments came from, to name their source.</summary>
public sealed record DeploymentSources(IReadOnlyDictionary<Guid, Link> Links, IReadOnlyDictionary<Guid, ApiKey> ApiKeys)
{
    public static readonly DeploymentSources None = new(new Dictionary<Guid, Link>(), new Dictionary<Guid, ApiKey>());
}

/// <summary>
/// Deployment events received from gestion-mep (link) or from a tool with an API key: validation, upsert by deployment
/// id per sender, and the deployments of a record.
/// </summary>
public static class Deployments
{
    public const int MaxEvents = 500;
    /// <summary>How many past deployments a record's detail and Markdown view show.</summary>
    public const int RecentInDetail = 3;

    private static readonly JsonSerializerOptions EnvironmentJson = new(JsonSerializerDefaults.Web);

    /// <summary>Reads <c>{events:[...]}</c>; field errors are named <c>events[0].phase</c>.</summary>
    public static List<DeploymentEvent> Parse(JsonObject body, FieldErrors errors)
    {
        var events = new List<DeploymentEvent>();
        if (body["events"] is not JsonArray array)
        {
            errors.Add("events", "An array of deployment events is expected.");
            return events;
        }
        if (array.Count > MaxEvents) errors.Add("events", $"At most {MaxEvents} events per request.");
        for (var i = 0; i < array.Count && i < MaxEvents; i++)
        {
            var at = $"events[{i}].";
            if (array[i] is not JsonObject e)
            {
                errors.Add($"events[{i}]", "An object is expected.");
                continue;
            }
            var id = Text(e, "deploymentId");
            if (id is not { Length: > 0 and <= 100 }) errors.Add(at + "deploymentId", "Required, at most 100 characters.");
            var title = Text(e, "title")?.Trim();
            if (title is not { Length: > 0 and <= Rules.MaxNameLength }) errors.Add(at + "title", $"Required, at most {Rules.MaxNameLength} characters.");
            var phase = Enum<DeploymentPhase>(e["phase"]);
            if (phase is null) errors.Add(at + "phase", "Expected preparation, execution, rollback, acceptance, closed or cancelled.");
            DeploymentOutcome? outcome = null;
            if (e["outcome"] is not null)
            {
                outcome = Enum<DeploymentOutcome>(e["outcome"]);
                if (outcome is null) errors.Add(at + "outcome", "Expected success, success-with-incidents, rolled-back, failed or null.");
            }
            var postponed = e["postponed"] switch
            {
                null => false,
                JsonValue v when v.TryGetValue<bool>(out var b) => b,
                _ => Error(errors, at + "postponed", "Expected true or false."),
            };
            var start = Time(e, "plannedStart", errors, at, required: false);
            var end = Time(e, "plannedEnd", errors, at, required: false);
            var updated = Time(e, "updatedAt", errors, at, required: true);
            var url = Text(e, "url");
            if (url is null || url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                errors.Add(at + "url", "An absolute http(s) URL is expected.");
            var apps = new List<string>();
            if (e["applicationIds"] is JsonArray ids)
                foreach (var node in ids)
                    if (node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length is > 0 and <= Rules.MaxIdLength) apps.Add(s);
                    else errors.Add(at + "applicationIds", "Application ids are strings.");
            else if (e["applicationIds"] is not null) errors.Add(at + "applicationIds", "An array of application ids is expected.");
            var environments = new List<DeploymentEnvironment>();
            if (e["environments"] is JsonArray envs)
                foreach (var node in envs)
                    if (node is JsonObject o && Text(o, "name") is { Length: > 0 and <= Rules.MaxNameLength } name)
                        environments.Add(new DeploymentEnvironment(name, o["production"] is JsonValue p && p.TryGetValue<bool>(out var prod) && prod));
                    else errors.Add(at + "environments", "Each environment is {name, production}.");
            else if (e["environments"] is not null) errors.Add(at + "environments", "An array of environments is expected.");
            if (id is not null && title is not null && phase is { } ph && updated is { } u && url is not null)
                events.Add(new DeploymentEvent(id, title, ph, outcome, postponed, start, end, url, apps.Distinct(StringComparer.Ordinal).ToList(), environments, u));
        }
        return events;
    }

    /// <summary>
    /// Creates or updates each event by its deployment id, per sender (an event older than the stored one changes
    /// nothing). Application ids that are unknown here, or not shared (restricted), are left out and reported. The
    /// changed deployments are queued for the webhooks that receive <c>deployment.updated</c>; all is saved.
    /// </summary>
    public static async Task<DeploymentsReceipt> Upsert(NoosphereDbContext db, DeploymentSender sender, IReadOnlyList<DeploymentEvent> events, DateTimeOffset now,
        CancellationToken cancel = default)
    {
        var shared = (await db.Applications.AsNoTracking().Where(a => a.Visibility == Visibility.Everyone).Select(a => a.Id).ToListAsync(cancel))
            .ToHashSet(StringComparer.Ordinal);
        var ids = events.Select(e => e.DeploymentId).Distinct().ToList();
        var existing = await db.Deployments.Where(d => d.LinkId == sender.LinkId && d.ApiKeyId == sender.ApiKeyId && ids.Contains(d.DeploymentId))
            .ToDictionaryAsync(d => d.DeploymentId, cancel);
        var ignored = new SortedSet<string>(StringComparer.Ordinal);
        var changed = new List<Deployment>();
        foreach (var e in events)
        {
            foreach (var unknown in e.ApplicationIds.Where(a => !shared.Contains(a))) ignored.Add(unknown);
            if (!existing.TryGetValue(e.DeploymentId, out var d))
            {
                d = new Deployment { LinkId = sender.LinkId, ApiKeyId = sender.ApiKeyId, DeploymentId = e.DeploymentId };
                db.Deployments.Add(d);
                existing[e.DeploymentId] = d;
            }
            else if (d.UpdatedAt > e.UpdatedAt) continue;
            if (!changed.Contains(d)) changed.Add(d);
            (d.Title, d.Phase, d.Outcome, d.Postponed, d.PlannedStart, d.PlannedEnd, d.Url) =
                (e.Title, e.Phase, e.Outcome, e.Postponed, e.PlannedStart, e.PlannedEnd, e.Url);
            d.ApplicationIds = [.. e.ApplicationIds.Where(shared.Contains)];
            d.Environments = JsonSerializer.Serialize(e.Environments, EnvironmentJson);
            (d.UpdatedAt, d.ReceivedAt) = (e.UpdatedAt, now);
            d.EndedAt = Ended(e.Phase) ? d.EndedAt ?? e.UpdatedAt : null;
        }
        if (changed.Count > 0)
        {
            var sources = await Sources(db, changed, cancel);
            await Integrations.Webhooks.Enqueue(db, Integrations.Webhooks.DeploymentUpdated,
                new JsonObject { ["deployments"] = new JsonArray([.. changed.Select(d => (JsonNode)Event(d, sources))]) }, now, cancel);
        }
        await db.SaveChangesAsync(cancel);
        return new DeploymentsReceipt(events.Count, [.. ignored]);
    }

    public static bool Ended(DeploymentPhase phase) => phase is DeploymentPhase.Closed or DeploymentPhase.Cancelled;

    /// <summary>
    /// Deployments of an application, or of a flow (those touching either end), split into upcoming and past;
    /// <paramref name="pastLimit"/> keeps the latest past ones only. Guests get none.
    /// </summary>
    public static async Task<DeploymentsDto> Of(NoosphereDbContext db, Access access, RecordKind kind, string id, int? pastLimit = null, CancellationToken cancel = default)
    {
        var apps = kind == RecordKind.Flow && access.Facts.Ends(id) is { } ends ? new[] { ends.FromId, ends.ToId } : [id];
        var rows = await db.Deployments.AsNoTracking().Where(d => d.ApplicationIds.Any(a => apps.Contains(a))).ToListAsync(cancel);
        var links = await Sources(db, rows, cancel);
        return Split(rows, links, access, pastLimit);
    }

    public static async Task<DeploymentSources> Sources(NoosphereDbContext db, IEnumerable<Deployment> rows, CancellationToken cancel = default)
    {
        var list = rows.ToList();
        var links = list.Where(r => r.LinkId is not null).Select(r => r.LinkId!.Value).Distinct().ToList();
        var keys = list.Where(r => r.ApiKeyId is not null).Select(r => r.ApiKeyId!.Value).Distinct().ToList();
        return new DeploymentSources(
            links.Count == 0 ? new Dictionary<Guid, Link>() : await db.Links.AsNoTracking().Where(l => links.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancel),
            keys.Count == 0 ? new Dictionary<Guid, ApiKey>() : await db.ApiKeys.AsNoTracking().Where(k => keys.Contains(k.Id)).ToDictionaryAsync(k => k.Id, cancel));
    }

    /// <summary>A stored deployment in the event shape (as received), with its source: the body of <c>deployment.updated</c>.</summary>
    public static JsonObject Event(Deployment d, DeploymentSources sources)
    {
        var source = Source(d, sources, DateTimeOffset.MaxValue);
        return new JsonObject
        {
            ["deploymentId"] = d.DeploymentId, ["title"] = d.Title, ["phase"] = LinkProtocol.ToNode(d.Phase),
            ["outcome"] = d.Outcome is { } o ? LinkProtocol.ToNode(o) : null, ["postponed"] = d.Postponed,
            ["plannedStart"] = d.PlannedStart is { } s ? LinkProtocol.Time(s) : null, ["plannedEnd"] = d.PlannedEnd is { } e ? LinkProtocol.Time(e) : null,
            ["url"] = d.Url, ["applicationIds"] = new JsonArray([.. d.ApplicationIds.Select(a => (JsonNode)JsonValue.Create(a))]),
            ["environments"] = JsonNode.Parse(d.Environments), ["updatedAt"] = LinkProtocol.Time(d.UpdatedAt),
            ["source"] = new JsonObject { ["kind"] = LinkProtocol.ToNode(source.Kind), ["id"] = source.LinkId.ToString("D"), ["name"] = source.InstanceName },
        };
    }

    public static DeploymentsDto Split(IEnumerable<Deployment> rows, DeploymentSources links, Access access, int? pastLimit = null)
    {
        // Guests do not see deployments: they are for members and administrators.
        if (access.IsGuest) return new DeploymentsDto([], []);
        var list = rows.ToList();
        var upcoming = list.Where(d => !Ended(d.Phase))
            .OrderBy(d => d.PlannedStart ?? DateTimeOffset.MaxValue).ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase);
        var past = list.Where(d => Ended(d.Phase))
            .OrderByDescending(d => d.PlannedEnd ?? d.PlannedStart ?? d.UpdatedAt).ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase)
            .Take(pastLimit ?? int.MaxValue);
        return new DeploymentsDto([.. upcoming.Select(d => Dto(d, links, access))], [.. past.Select(d => Dto(d, links, access))]);
    }

    /// <summary>
    /// The source of a deployment. For an API key, <c>linkId</c> is the key's id, <c>product</c> is <c>api-key</c> and
    /// <c>linkActive</c> says whether the key is still valid.
    /// </summary>
    public static DeploymentSourceDto Source(Deployment d, DeploymentSources sources, DateTimeOffset now)
    {
        if (d.ApiKeyId is { } keyId)
        {
            var key = sources.ApiKeys.GetValueOrDefault(keyId);
            return new DeploymentSourceDto(keyId, ApiKeyProduct, key?.Name ?? "", key is { RevokedAt: null } && (key.ExpiresAt is not { } until || until > now),
                DeploymentSourceKind.ApiKey);
        }
        var link = d.LinkId is { } id ? sources.Links.GetValueOrDefault(id) : null;
        return new DeploymentSourceDto(d.LinkId ?? Guid.Empty, link?.Product ?? "gestion-mep", link?.InstanceName ?? "", link is { State: not LinkState.Cut },
            DeploymentSourceKind.Link);
    }

    /// <summary>The <c>product</c> of a deployment source that is an API key.</summary>
    public const string ApiKeyProduct = "api-key";

    public static DeploymentDto Dto(Deployment d, DeploymentSources sources, Access access)
    {
        var environments = JsonSerializer.Deserialize<List<DeploymentEnvironment>>(d.Environments, EnvironmentJson) ?? [];
        return new DeploymentDto(d.DeploymentId, d.Title, d.Phase, d.Outcome, d.Postponed, d.PlannedStart, d.PlannedEnd, d.Url,
            [.. d.ApplicationIds.Where(a => access.Facts.Exists(RecordKind.Application, a) && !access.Hidden(RecordKind.Application, a))],
            [.. environments.Select(e => new DeploymentEnvironmentDto(e.Name, e.Production))], d.UpdatedAt, d.ReceivedAt,
            Source(d, sources, DateTimeOffset.UtcNow));
    }

    private static string? Text(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static T? Enum<T>(JsonNode? node) where T : struct, Enum
    {
        if (node is not JsonValue v || !v.TryGetValue<string>(out var s)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(s), LinkProtocol.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Time(JsonObject o, string name, FieldErrors errors, string at, bool required)
    {
        var node = o[name];
        if (node is null)
        {
            if (required) errors.Add(at + name, "Required: an ISO 8601 time.");
            return null;
        }
        if (node is JsonValue v && v.TryGetValue<string>(out var s) && LinkProtocol.ParseTime(s) is { } t) return t;
        errors.Add(at + name, "An ISO 8601 time is expected.");
        return null;
    }

    private static bool Error(FieldErrors errors, string field, string message)
    {
        errors.Add(field, message);
        return false;
    }
}
