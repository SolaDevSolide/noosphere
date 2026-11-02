using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Links;

/// <summary>
/// The directory shared with a link that has <c>directory:read</c>: every team, with its contact and the shared
/// applications it owns, and the members and administrators who belong to at least one team, with their display name
/// and teams. Never roles, never guests, never accounts outside every team, and no e-mail address unless the link's
/// administrator shares them.
/// </summary>
public static partial class LinkDirectory
{
    public static async Task<DirectoryPayload> Build(NoosphereDbContext db, Link link, string instanceName, DateTimeOffset now, CancellationToken cancel = default)
    {
        var teams = await db.Teams.AsNoTracking().OrderBy(t => t.Id).ToListAsync(cancel);
        var owned = (await db.Applications.AsNoTracking().Where(a => a.Visibility == Visibility.Everyone && a.TeamId != null)
                .Select(a => new { a.Id, a.TeamId }).ToListAsync(cancel))
            .GroupBy(a => a.TeamId!).ToDictionary(g => g.Key, g => g.Select(a => a.Id).Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var people = await db.Users.AsNoTracking().Where(u => u.Role != Role.Guest).OrderBy(u => u.Username).Select(u => new { u.Id, u.Username, u.DisplayName })
            .ToListAsync(cancel);
        var memberships = (await db.TeamMembers.AsNoTracking().ToListAsync(cancel))
            .GroupBy(m => m.UserId).ToDictionary(g => g.Key, g => g.Select(m => m.TeamId).Order(StringComparer.Ordinal).ToList());
        return new DirectoryPayload(now, instanceName, link.ShareEmails,
            [.. teams.Select(t => new DirectoryTeam(t.Id, t.Name, Contact(t.Contact, link.ShareEmails), owned.GetValueOrDefault(t.Id) ?? []))],
            [.. people.Where(p => memberships.ContainsKey(p.Id)).Select(p => new DirectoryPerson(p.Username, p.DisplayName, memberships[p.Id]))]);
    }

    /// <summary>What a link would read, for the administrator about to share the directory: counts and three people.</summary>
    public static async Task<Contracts.DirectoryPreviewDto> Preview(NoosphereDbContext db, CancellationToken cancel = default)
    {
        var teams = await db.Teams.AsNoTracking().OrderBy(t => t.Name).Select(t => new { t.Id, t.Name, t.Contact }).ToListAsync(cancel);
        var names = teams.ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);
        var users = await db.Users.AsNoTracking().OrderBy(u => u.DisplayName).Select(u => new { u.Id, u.Username, u.DisplayName, u.Role }).ToListAsync(cancel);
        var memberships = (await db.TeamMembers.AsNoTracking().ToListAsync(cancel))
            .GroupBy(m => m.UserId).ToDictionary(g => g.Key, g => g.Select(m => names.GetValueOrDefault(m.TeamId) ?? m.TeamId).Order(StringComparer.Ordinal).ToList());
        var listed = users.Where(u => u.Role != Role.Guest && memberships.ContainsKey(u.Id)).ToList();
        return new Contracts.DirectoryPreviewDto(teams.Count, listed.Count, users.Count(u => u.Role == Role.Guest),
            users.Count(u => u.Role != Role.Guest && !memberships.ContainsKey(u.Id)), teams.Count(t => t.Contact is { } c && HasEmail(c)),
            [.. listed.Take(3).Select(u => new Contracts.DirectoryPreviewPersonDto(u.Username, u.DisplayName, memberships[u.Id]))]);
    }

    /// <summary>A contact as shared: kept when e-mails are shared or when it holds no e-mail address (a chat channel), null otherwise.</summary>
    public static string? Contact(string? contact, bool shareEmails) =>
        contact is null || shareEmails || !HasEmail(contact) ? contact : null;

    public static bool HasEmail(string text) => EmailPattern().IsMatch(text);

    [GeneratedRegex(@"[^\s@<>()\[\],;:""]+@[^\s@<>()\[\],;:""]+\.[^\s@<>()\[\],;:""]+")]
    private static partial Regex EmailPattern();

    /// <summary>A strong ETag of the content (everything but <c>generatedAt</c>).</summary>
    public static string ETag(DirectoryPayload directory)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(directory with { GeneratedAt = default }, LinkProtocol.Json);
        return "\"" + LinkCrypto.Base64Url(SHA256.HashData(bytes)) + "\"";
    }
}
