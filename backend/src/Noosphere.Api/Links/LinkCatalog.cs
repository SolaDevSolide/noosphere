using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Links;

/// <summary>
/// The catalogue shared with a linked gestion-mep: domains, teams, applications visible to everyone, and the flows both
/// ends of which are shared. No restricted record, no protected note, no data object.
/// </summary>
public static class LinkCatalog
{
    public static async Task<CatalogPayload> Build(NoosphereDbContext db, string instanceName, string baseUrl, DateTimeOffset now, CancellationToken cancel = default)
    {
        var apps = await db.Applications.AsNoTracking().Where(a => a.Visibility == Visibility.Everyone).OrderBy(a => a.Id).ToListAsync(cancel);
        var shared = apps.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var flows = (await db.Flows.AsNoTracking().OrderBy(f => f.Id).ToListAsync(cancel))
            .Where(f => shared.Contains(f.FromId) && shared.Contains(f.ToId)).ToList();
        var domains = await db.Domains.AsNoTracking().OrderBy(d => d.Order).ThenBy(d => d.Id).ToListAsync(cancel);
        var teams = await db.Teams.AsNoTracking().OrderBy(t => t.Id).ToListAsync(cancel);
        var root = baseUrl.TrimEnd('/');
        return new CatalogPayload(now, instanceName, root,
            [.. domains.Select(d => new CatalogDomain(d.Id, d.Name))],
            [.. teams.Select(t => new CatalogTeam(t.Id, t.Name, t.Contact))],
            [.. apps.Select(a => new CatalogApplication(a.Id, a.Name, a.Description, a.DomainId, a.TeamId, Lower(a.Kind), Lower(a.Lifecycle), a.Tech,
                $"{root}/applications/{Uri.EscapeDataString(a.Id)}"))],
            [.. flows.Select(f => new CatalogFlow(f.Id, f.Name, f.FromId, f.ToId, Lower(f.Type), Lower(f.Status), f.Schedule,
                $"{root}/flows/{Uri.EscapeDataString(f.Id)}"))]);
    }

    /// <summary>A strong ETag of the content (everything but <c>generatedAt</c>).</summary>
    public static string ETag(CatalogPayload catalog)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog with { GeneratedAt = default }, LinkProtocol.Json);
        return "\"" + LinkCrypto.Base64Url(SHA256.HashData(bytes)) + "\"";
    }

    /// <summary>True when <c>If-None-Match</c> names this ETag (or is <c>*</c>).</summary>
    public static bool NotModified(string? ifNoneMatch, string etag) =>
        !string.IsNullOrWhiteSpace(ifNoneMatch)
        && ifNoneMatch.Split(',').Select(t => t.Trim()).Any(t => t == "*" || t == etag || t == "W/" + etag);

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
}
