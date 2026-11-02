using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Links;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Integrations;

/// <summary>
/// API keys of third-party tools: <c>nsk_</c>, 8 characters (the prefix, shown in lists), <c>_</c> and 40 characters.
/// Only the SHA-256 of the whole key is stored. A key is accepted only by the integration endpoints, as
/// <c>Authorization: Bearer</c>; it never opens a session.
/// </summary>
public static class ApiKeys
{
    public const string CatalogRead = "catalog:read";
    public const string DeploymentsWrite = "deployments:write";
    public static readonly IReadOnlyList<string> AllScopes = [CatalogRead, DeploymentsWrite];

    public const string KeyPrefix = "nsk_";
    /// <summary>Lower case letters and digits without the look-alikes (l, o, 0, 1).</summary>
    public const string Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
    public const int PrefixLength = 8;
    public const int SecretLength = 40;
    /// <summary>A key always expires: after one of these lifetimes, in days.</summary>
    public static readonly int[] Lifetimes = [90, 180, 365];
    /// <summary>A rotated key keeps working this long, so the tool's secret can be updated without a gap.</summary>
    public static readonly TimeSpan RotationGrace = TimeSpan.FromHours(24);
    public const string RateLimit = "integration";
    public const int RequestsPerMinute = 120;

    /// <summary>A new key: its value (shown once) and the prefix and hash to store.</summary>
    public static (string Value, string Prefix, string Hash) New()
    {
        var prefix = KeyPrefix + RandomNumberGenerator.GetString(Alphabet, PrefixLength);
        var value = prefix + "_" + RandomNumberGenerator.GetString(Alphabet, SecretLength);
        return (value, prefix, Hash(value));
    }

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>The prefix of a value shaped like a key, or null.</summary>
    public static string? PrefixOf(string? value) =>
        value is { Length: > 0 } v && v.StartsWith(KeyPrefix, StringComparison.Ordinal) && v.Length == KeyPrefix.Length + PrefixLength + 1 + SecretLength
            && v[KeyPrefix.Length + PrefixLength] == '_' ? v[..(KeyPrefix.Length + PrefixLength)] : null;

    public static ApiKeyState State(ApiKey key, DateTimeOffset now) =>
        key.RevokedAt is not null ? ApiKeyState.Revoked : key.ExpiresAt is { } until && until <= now ? ApiKeyState.Expired : ApiKeyState.Active;

    public static ApiKeyDto Dto(ApiKey k, DateTimeOffset now) =>
        new(k.Id, k.Name, k.Prefix, k.Scopes, State(k, now), k.CreatedAt, k.CreatedBy, k.ExpiresAt, k.LastUsedAt, k.RevokedAt, k.RevokedBy, k.ReplacedById);

    /// <summary>Scopes from user input: known values, deduplicated, in order; at least one.</summary>
    public static List<string> Scopes(IEnumerable<string>? values, FieldErrors errors, string field = "scopes")
    {
        var wanted = (values ?? []).Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (var unknown in wanted.Where(v => !AllScopes.Contains(v)))
            errors.Add(field, $"Unknown scope '{unknown}'. Expected {string.Join(", ", AllScopes)}.");
        if (wanted.Count == 0) errors.Add(field, $"At least one scope: {string.Join(", ", AllScopes)}.");
        return [.. AllScopes.Where(wanted.Contains)];
    }

    /// <summary>Rate limit partition: the key's prefix, or the client address without a key.</summary>
    public static RateLimitPartition<string> Partition(HttpContext ctx) => RateLimitPartition.GetFixedWindowLimiter(
        PrefixOf(Agents.AgentBearerHandler.TokenOf(ctx.Request)) is { } prefix ? "key:" + prefix : "ip:" + ctx.Connection.RemoteIpAddress,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = RequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    /// <summary>
    /// Checks the bearer key of an integration call and its scope. A refusal is a problem
    /// (<c>https://noosphere.dev/problems/invalid-api-key</c>, <c>api-key-revoked</c>, <c>api-key-expired</c>: 401;
    /// <c>scope-missing</c>: 403) written to the exchange log. On success, the key's last use is dated (saved with the
    /// caller's next save).
    /// </summary>
    public static async Task<(ApiKey? Key, IResult? Refusal)> Authenticate(HttpContext ctx, NoosphereDbContext db, string scope, string operation, DateTimeOffset now)
    {
        var value = Agents.AgentBearerHandler.TokenOf(ctx.Request);
        ApiKey? key = null;
        if (PrefixOf(value) is { } prefix)
        {
            key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix, ctx.RequestAborted);
            if (key is not null && !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(key.SecretHash), Encoding.ASCII.GetBytes(Hash(value!))))
                key = null;
        }
        (int Status, string Reason, string Detail)? problem = key switch
        {
            null => (401, "invalid-api-key", value is null
                ? "An API key is required: send it as Authorization: Bearer <key>. Administrators create keys under Links."
                : "Unknown API key."),
            { RevokedAt: not null } => (401, "api-key-revoked", "This API key was revoked."),
            _ when State(key, now) == ApiKeyState.Expired => (401, "api-key-expired", "This API key expired: ask an administrator for a new one."),
            _ when !key.Scopes.Contains(scope) => (403, "scope-missing", $"This API key does not have the {scope} scope."),
            _ => null,
        };
        if (problem is { } p)
        {
            Log(db, key, operation, LinkExchangeResult.Refused, p.Status, now, p.Detail, p.Reason);
            await db.SaveChangesAsync(ctx.RequestAborted);
            if (p.Status == 401) ctx.Response.Headers.WWWAuthenticate = $"Bearer error=\"invalid_token\", error_description=\"{p.Detail.Replace("\"", "'", StringComparison.Ordinal)}\"";
            else ctx.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", scope=\"{scope}\"";
            return (null, Problem(p.Status, p.Reason, p.Detail));
        }
        key!.LastUsedAt = now;
        return (key, null);
    }

    /// <summary>A problem with a <c>https://noosphere.dev/problems/{reason}</c> type.</summary>
    public static IResult Problem(int status, string reason, string detail, IReadOnlyDictionary<string, string[]>? errors = null) =>
        Results.Json(new ProblemDto(LinkProtocol.ProblemType(reason), Problems.Title(status), status, detail, errors), contentType: Problems.ContentType, statusCode: status);

    /// <summary>An exchange line of an API key call (shared with the exchange log of links).</summary>
    public static void Log(NoosphereDbContext db, ApiKey? key, string operation, LinkExchangeResult result, int status, DateTimeOffset now, string? detail = null, string? reason = null)
    {
        var line = LinkLog.Add(db, null, LinkDirection.In, operation, result, status, now, detail, reason);
        line.ApiKeyId = key?.Id;
    }
}
