using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Agents;

/// <summary>OAuth scopes of the MCP server.</summary>
public static class Scopes
{
    public const string Read = "docs:read";
    /// <summary>Write drafts and submit them as proposals.</summary>
    public const string Draft = "docs:draft";
    /// <summary>Read restricted records the person sees; granted only under the <c>asUser</c> ceiling.</summary>
    public const string ReadRestricted = "docs:read:restricted";

    public static readonly IReadOnlyList<string> All = [Read, Draft, ReadRestricted];

    /// <summary>Asked when the client names none.</summary>
    public static readonly IReadOnlyList<string> Default = [Read, Draft];

    /// <summary>Space-separated scopes; null when one is unknown.</summary>
    public static List<string>? Parse(string? text)
    {
        var list = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
        if (list.Any(s => !All.Contains(s))) return null;
        return list.Count == 0 ? [.. Default] : [.. All.Where(list.Contains)];
    }

    /// <summary>Scopes a person may grant under the settings: drafts need a member or administrator, restricted records the <c>asUser</c> ceiling.</summary>
    public static bool Grantable(string scope, Role role, AgentSettings settings) => scope switch
    {
        Read => true,
        Draft => role is Role.Admin or Role.Member,
        ReadRestricted => settings.Ceiling == AgentCeiling.AsUser,
        _ => false,
    };

    public static LocalizedTextDto Describe(string scope) => scope switch
    {
        Read => new("Read the records you can see, within the agent ceiling.", "Lire les fiches que vous voyez, dans la limite du plafond des agents."),
        Draft => new("Write drafts and submit them as change proposals. Owners still validate.", "Écrire des brouillons et les soumettre comme propositions. Les propriétaires valident toujours."),
        _ => new("Read the restricted records you can see.", "Lire les fiches restreintes que vous voyez."),
    };
}

/// <summary>Opaque tokens and codes: random, and stored as SHA-256 hashes only.</summary>
public static class OpaqueTokens
{
    public const string AccessPrefix = "nsa_";
    public const string RefreshPrefix = "nsr_";
    public const string CodePrefix = "nsc_";

    public static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);

    public static string New(string prefix) => prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>PKCE S256: BASE64URL(SHA256(verifier)) equals the challenge. The verifier has 43 to 128 unreserved characters.</summary>
    public static bool VerifyPkce(string? verifier, string challenge)
    {
        if (verifier is not { Length: >= 43 and <= 128 } || !verifier.All(IsUnreserved)) return false;
        var computed = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    /// <summary>A code challenge is 43 base64url characters (a SHA-256 hash).</summary>
    public static bool IsChallenge(string? challenge) =>
        challenge is { Length: 43 } && challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsUnreserved(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';
}

/// <summary>Agent settings: one row, created on the first save.</summary>
public static class AgentSettingsStore
{
    public const int MinQuota = 1;
    public const int MaxQuota = 100_000;

    public static async Task<AgentSettings> Get(NoosphereDbContext db, CancellationToken cancel = default) =>
        await db.AgentSettings.FirstOrDefaultAsync(s => s.Id == 1, cancel) ?? new AgentSettings { Version = 0 };

    public static AgentSettingsDto Dto(AgentSettings s, string baseUrl) =>
        new(s.Enabled, s.Ceiling, s.QuotaPerHour, s.WhoConnects, s.DynamicRegistration, s.AnonymousLlms, McpUrl(baseUrl), s.Version,
            s.Version == 0 ? null : s.UpdatedAt);

    public static string McpUrl(string baseUrl) => baseUrl + "/mcp";
}

/// <summary>The public address of the instance: PUBLIC_URL, or the address of the request.</summary>
public static class PublicAddress
{
    public static string Of(HttpRequest request, Settings settings) =>
        settings.PublicUrl ?? $"{request.Scheme}://{request.Host}{request.PathBase}";
}
