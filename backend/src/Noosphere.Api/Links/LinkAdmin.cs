using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;
using static Noosphere.Api.Links.LinkCrypto;

namespace Noosphere.Api.Links;

/// <summary>
/// What administrators do with links, shared by <c>/api/links</c> and the <c>noosphere link</c> command: pairing codes,
/// confirmation, capabilities, key renewal and cut. Calls to the peer go through <see cref="LinkClient"/>.
/// </summary>
public sealed class LinkAdmin(NoosphereDbContext db, LinkKeys keys, LinkClient client, NoticeBuckets buckets, TimeProvider clock)
{
    /// <summary>Crockford base32: digits and letters without I, L, O and U.</summary>
    public const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    /// <summary>A generated code: 12 characters, shown as <c>XXXX-XXXX-XXXX</c>.</summary>
    public const int CodeLength = 12;

    public static string Version =>
        typeof(LinkAdmin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0.0";

    // ---------- Codes ----------

    /// <summary>Capabilities from user input: known values, deduplicated, in protocol order; null means all three.</summary>
    public static List<string> Capabilities(IEnumerable<string>? values, FieldErrors errors, string field = "capabilities")
    {
        if (values is null) return [.. LinkProtocol.AllCapabilities];
        var wanted = values.Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (var unknown in wanted.Where(v => !LinkProtocol.AllCapabilities.Contains(v)))
            errors.Add(field, $"Unknown capability '{unknown}'. Expected {string.Join(", ", LinkProtocol.AllCapabilities)}.");
        return [.. LinkProtocol.AllCapabilities.Where(wanted.Contains)];
    }

    public static string Hash(string normalizedCode) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedCode)));

    /// <summary>Creates a single-use pairing code; returns it with its value (<c>XXXX-XXXX-XXXX</c>, no prefix), shown once.</summary>
    public async Task<(LinkCode Code, string Value)> CreateCode(string createdBy, TimeSpan lifetime, IReadOnlyList<string> capabilities, CancellationToken cancel = default)
    {
        var now = clock.GetUtcNow();
        var active = (await db.LinkCodes.Where(c => c.UsedAt == null && c.BurntAt == null && c.ExpiresAt > now).Select(c => c.CodeId).ToListAsync(cancel))
            .ToHashSet(StringComparer.Ordinal);
        string raw;
        do raw = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
        while (active.Contains(raw[..4]));
        var value = string.Join('-', Enumerable.Range(0, CodeLength / 4).Select(i => raw.Substring(i * 4, 4)));
        var code = Add(raw, createdBy, now, now + lifetime, capabilities, bootstrap: false);
        await db.SaveChangesAsync(cancel);
        return (code, value);
    }

    private LinkCode Add(string normalized, string createdBy, DateTimeOffset now, DateTimeOffset expires, IReadOnlyList<string> capabilities, bool bootstrap)
    {
        var code = new LinkCode
        {
            CodeId = normalized[..4], ProtectedCode = keys.Protect(normalized), CodeHash = Hash(normalized), Capabilities = [.. capabilities],
            CreatedAt = now, ExpiresAt = expires, CreatedBy = createdBy, Bootstrap = bootstrap,
        };
        db.LinkCodes.Add(code);
        return code;
    }

    /// <summary>
    /// At startup: the code of NOOSPHERE_LINK_BOOTSTRAP_CODE (at least 16 characters once dashes and spaces are removed)
    /// becomes a single-use pairing code valid 24 hours, once. Returns a message for the log (refused: an error), or null
    /// when nothing changed.
    /// </summary>
    public async Task<(bool Refused, string Message)?> LoadBootstrapCode(string? configured, CancellationToken cancel = default)
    {
        if (configured is null) return null;
        var normalized = NormalizeCode(configured);
        if (normalized.Length < LinkProtocol.MinBootstrapCodeLength || !normalized.All(char.IsAsciiLetterOrDigit))
            return (true, $"NOOSPHERE_LINK_BOOTSTRAP_CODE is refused: at least {LinkProtocol.MinBootstrapCodeLength} letters or digits (dashes and spaces aside).");
        var hash = Hash(normalized);
        if (await db.LinkCodes.AnyAsync(c => c.CodeHash == hash, cancel)) return null;
        var now = clock.GetUtcNow();
        var code = Add(normalized, "bootstrap", now, now + LinkProtocol.BootstrapCodeLifetime, LinkProtocol.AllCapabilities, bootstrap: true);
        await db.SaveChangesAsync(cancel);
        return (false, $"Bootstrap pairing code {code.CodeId}… loaded: gestion-mep can pair with it until {LinkProtocol.Time(code.ExpiresAt)}.");
    }

    public static LinkCodeState State(LinkCode c, DateTimeOffset now) =>
        c.BurntAt is not null ? LinkCodeState.Burnt : c.UsedAt is not null ? LinkCodeState.Used : c.ExpiresAt <= now ? LinkCodeState.Expired : LinkCodeState.Active;

    public static LinkCodeDto Dto(LinkCode c, DateTimeOffset now) =>
        new(c.Id, c.CodeId, State(c, now), c.Capabilities, c.CreatedAt, c.ExpiresAt, c.CreatedBy, c.UsedAt, c.LinkId, c.FailedAttempts, c.Bootstrap);

    // ---------- Pairing ----------

    /// <summary>An answer to <c>POST /link/v1/pair</c>: a status, and either the JSON body or a problem (reason, detail).</summary>
    public sealed record PairOutcome(int Status, JsonObject? Body, string? Reason, string Detail, Link? Link = null);

    /// <summary>
    /// Pairs with a peer: finds the code by <c>codeId</c>, checks the MAC (keyed with the code) over the canonical
    /// <c>{codeId, peer, nonce}</c>, creates the pending link with new keys, and answers with this side's keys and a MAC
    /// over the canonical <c>{linkId, peer, capabilities, nonce}</c>.
    /// </summary>
    public async Task<PairOutcome> Pair(JsonObject request, string instanceName, string publicUrl, CancellationToken cancel = default)
    {
        var now = clock.GetUtcNow();
        if (Text(request["codeId"]) is not { Length: > 0 } codeId || request["peer"] is not JsonObject peer
            || Text(request["nonce"]) is not { Length: > 0 and <= 100 } nonce || Text(request["mac"]) is not { Length: > 0 } mac)
            return new PairOutcome(400, null, LinkProtocol.Reasons.BadBody, "Expected {codeId, peer, nonce, mac}.");

        var key = codeId.ToUpperInvariant();
        var candidates = await db.LinkCodes.Where(c => c.CodeId == key).OrderByDescending(c => c.CreatedAt).ToListAsync(cancel);
        var code = candidates.FirstOrDefault(c => State(c, now) == LinkCodeState.Active) ?? candidates.FirstOrDefault();
        switch (code is null ? LinkCodeState.Expired : State(code, now))
        {
            case LinkCodeState.Burnt: return new PairOutcome(410, null, "code-burnt", "This pairing code is burnt after too many wrong attempts: create another one.");
            case LinkCodeState.Used: return new PairOutcome(409, null, "code-used", "This pairing code was already used: create another one.");
            case LinkCodeState.Expired: return new PairOutcome(404, null, "code-unknown", "Unknown or expired pairing code.");
        }
        var secret = keys.Unprotect(code!.ProtectedCode);
        var expected = PairingMac(secret, new JsonObject { ["codeId"] = codeId, ["peer"] = peer.DeepClone(), ["nonce"] = nonce });
        if (!MacMatches(mac, expected))
        {
            await db.LinkCodes.Where(c => c.Id == code.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1), cancel);
            var failures = await db.LinkCodes.Where(c => c.Id == code.Id).Select(c => c.FailedAttempts).FirstAsync(cancel);
            if (failures >= LinkProtocol.MaxCodeFailures)
            {
                await db.LinkCodes.Where(c => c.Id == code.Id && c.BurntAt == null).ExecuteUpdateAsync(s => s.SetProperty(c => c.BurntAt, now), cancel);
                return new PairOutcome(410, null, "code-burnt", $"Wrong MAC: after {LinkProtocol.MaxCodeFailures} failures the code is burnt.");
            }
            return new PairOutcome(401, null, LinkProtocol.Reasons.BadMac, $"Wrong MAC: check the code ({LinkProtocol.MaxCodeFailures - failures} attempt(s) left).");
        }

        var errors = new FieldErrors();
        var product = Text(peer["product"])?.Trim();
        if (product is not { Length: > 0 and <= 50 }) errors.Add("peer.product", "Required, at most 50 characters.");
        var name = Text(peer["instanceName"])?.Trim();
        if (name is not { Length: > 0 and <= Rules.MaxNameLength }) errors.Add("peer.instanceName", $"Required, at most {Rules.MaxNameLength} characters.");
        var url = Text(peer["url"])?.Trim().TrimEnd('/');
        if (url is null || url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Query.Length > 0)
            errors.Add("peer.url", "An absolute http(s) URL without query is expected.");
        var version = Text(peer["version"])?.Trim() is { Length: > 0 } v ? v[..Math.Min(v.Length, 50)] : null;
        var signing = PeerKey(peer["signingKey"], "peer.signingKey", errors);
        var encryption = PeerKey(peer["encryptionKey"], "peer.encryptionKey", errors);
        if (signing is not null && encryption is not null && signing.Kid() == encryption.Kid()) errors.Add("peer.encryptionKey", "The two keys must differ.");
        if (errors.Any)
            return new PairOutcome(400, null, LinkProtocol.Reasons.BadBody, string.Join(" ", errors.All.Select(e => $"{e.Field}: {e.Message}")));

        var (ownSigning, ownEncryption) = (keys.NewSigningKey(), keys.NewEncryptionKey());
        var link = new Link
        {
            Product = product!, InstanceName = name!, Url = url!, PeerVersion = version, State = LinkState.Pending,
            Capabilities = [.. code.Capabilities],
            OwnSigningKey = ownSigning.Protected, OwnSigningKid = ownSigning.Kid, OwnEncryptionKey = ownEncryption.Protected, OwnEncryptionKid = ownEncryption.Kid,
            PeerSigningKey = signing!.Serialize(), PeerSigningKid = signing.Kid(), PeerEncryptionKey = encryption!.Serialize(), PeerEncryptionKid = encryption.Kid(),
            Fingerprint = LinkCrypto.Fingerprint([ownSigning.Public, ownEncryption.Public, signing, encryption]),
            PairedAt = now, PairedBy = code.CreatedBy, Version = 1,
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancel);
        db.Links.Add(link);
        await db.SaveChangesAsync(cancel);
        var claimed = await db.LinkCodes.Where(c => c.Id == code.Id && c.UsedAt == null && c.BurntAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now).SetProperty(c => c.LinkId, link.Id), cancel);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancel);
            db.ChangeTracker.Clear();
            return new PairOutcome(409, null, "code-used", "This pairing code was already used: create another one.");
        }
        await transaction.CommitAsync(cancel);

        var self = new JsonObject
        {
            ["product"] = LinkProtocol.Product, ["instanceName"] = instanceName, ["url"] = publicUrl.TrimEnd('/'), ["version"] = Version,
            ["signingKey"] = ownSigning.Public.ToJson(), ["encryptionKey"] = ownEncryption.Public.ToJson(),
        };
        var capabilities = new JsonArray([.. link.Capabilities.Select(c => (JsonNode)JsonValue.Create(c))]);
        var linkId = link.Id.ToString("D");
        var body = new JsonObject { ["linkId"] = linkId, ["peer"] = self, ["capabilities"] = capabilities, ["nonce"] = nonce };
        body["mac"] = PairingMac(secret, new JsonObject
        {
            ["linkId"] = linkId, ["peer"] = self.DeepClone(), ["capabilities"] = capabilities.DeepClone(), ["nonce"] = nonce,
        });
        return new PairOutcome(200, body, null, $"Paired with {name} ({product}); waiting for confirmation on both sides.", link);
    }

    private static Jwk? PeerKey(JsonNode? node, string field, FieldErrors errors)
    {
        try
        {
            var jwk = Jwk.FromJson(node);
            jwk.Parameters();
            return jwk;
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            errors.Add(field, "A P-256 public key as JWK {kty:\"EC\", crv:\"P-256\", x, y} is expected.");
            return null;
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // ---------- Links ----------

    public static LinkDto Dto(Link l) => new(l.Id, l.Product, l.InstanceName, l.Url, l.PeerVersion, l.State, l.ConfirmedHere, l.ConfirmedAt, l.ConfirmedBy,
        l.PeerConfirmed, l.PeerCheckedAt, l.Capabilities, l.Fingerprint, l.PairedAt, l.PairedBy, l.KeysRenewedAt, l.LastExchangeAt, l.CutAt, l.CutBy, l.Demo,
        new LinkKeyIdsDto(l.OwnSigningKid, l.OwnEncryptionKid, l.PeerSigningKid, l.PeerEncryptionKid), l.Version);

    public async Task<Link> Find(Guid id, CancellationToken cancel = default) =>
        await db.Links.FirstOrDefaultAsync(l => l.Id == id, cancel) ?? throw ApiProblem.NotFound($"No link with the id '{id}'.");

    private static void RequireNotCut(Link link)
    {
        if (link.State == LinkState.Cut) throw ApiProblem.Conflict("This link is cut: pair again with a new code.");
    }

    /// <summary>Confirms the fingerprint on this side (422 when it differs). The link becomes active and exchanges data.</summary>
    public async Task<Link> Confirm(Link link, string? fingerprint, string by, CancellationToken cancel = default)
    {
        RequireNotCut(link);
        if (NormalizeFingerprint(fingerprint) != NormalizeFingerprint(link.Fingerprint))
            throw ApiProblem.Unprocessable("The fingerprint does not match: do not confirm. Compare the four words shown on both sides; if they differ, cut the link and pair again.");
        if (!link.ConfirmedHere)
        {
            (link.ConfirmedHere, link.ConfirmedAt, link.ConfirmedBy, link.State) = (true, clock.GetUtcNow(), by, LinkState.Active);
            link.Version++;
            await db.SaveChangesAsync(cancel);
            await RefreshStatus(link, cancel);
        }
        return link;
    }

    /// <summary>Reads the peer's status (best effort): its confirmation, name and version.</summary>
    public async Task<LinkCallResult?> RefreshStatus(Link link, CancellationToken cancel = default)
    {
        if (link.State == LinkState.Cut) return null;
        var result = await client.Send(db, link, "status", HttpMethod.Get, null, cancel);
        var now = clock.GetUtcNow();
        if (result.Result == LinkExchangeResult.Ok && result.Payload is { } status)
        {
            link.PeerConfirmed = status["confirmed"] is JsonValue c && c.TryGetValue<bool>(out var confirmed) && confirmed;
            if (Text(status["instanceName"]) is { Length: > 0 and <= Rules.MaxNameLength } name) link.InstanceName = name;
            if (Text(status["version"]) is { Length: > 0 and <= 50 } version) link.PeerVersion = version;
            link.PeerCheckedAt = now;
        }
        else if (result.Reason == LinkProtocol.Reasons.NotConfirmed)
            (link.PeerConfirmed, link.PeerCheckedAt) = (false, now);
        await db.SaveChangesAsync(cancel);
        return result;
    }

    public async Task<Link> SetCapabilities(Link link, IReadOnlyList<string> capabilities, CancellationToken cancel = default)
    {
        RequireNotCut(link);
        link.Capabilities = [.. capabilities];
        link.Version++;
        if (!capabilities.Contains(LinkProtocol.NoticesSend)) buckets.Drop(link.Id);
        await db.SaveChangesAsync(cancel);
        return link;
    }

    /// <summary>
    /// Renews this side's keys: <c>POST /link/v1/rekey</c> signed with the old signing key, the new public keys encrypted
    /// to the peer; once the peer answers 200, the new keys are used (the old encryption key still decrypts for 5 minutes).
    /// </summary>
    public async Task<Link> RenewKeys(Link link, CancellationToken cancel = default)
    {
        RequireNotCut(link);
        var (signing, encryption) = (keys.NewSigningKey(), keys.NewEncryptionKey());
        var payload = new JsonObject { ["signingKey"] = signing.Public.ToJson(), ["encryptionKey"] = encryption.Public.ToJson() };
        var result = await client.Send(db, link, "rekey", HttpMethod.Post, payload, cancel);
        if (!result.Ok)
            throw new ApiProblem(502, $"{link.InstanceName} did not accept the new keys ({result.Detail}). The current keys stay in use.");
        var now = clock.GetUtcNow();
        (link.OwnPreviousEncryptionKey, link.OwnPreviousEncryptionKid, link.OwnPreviousUntil) = (link.OwnEncryptionKey, link.OwnEncryptionKid, now + LinkProtocol.RekeyGrace);
        (link.OwnSigningKey, link.OwnSigningKid) = (signing.Protected, signing.Kid);
        (link.OwnEncryptionKey, link.OwnEncryptionKid) = (encryption.Protected, encryption.Kid);
        link.Fingerprint = keys.Fingerprint(link);
        link.KeysRenewedAt = now;
        link.Version++;
        await db.SaveChangesAsync(cancel);
        return link;
    }

    /// <summary>Cuts a link: tells the peer (best effort, <c>POST /link/v1/unpair</c>), then erases the keys. Deployments stay as history.</summary>
    public async Task<(Link Link, LinkCallResult? Notified)> Cut(Link link, string by, CancellationToken cancel = default)
    {
        if (link.State == LinkState.Cut) return (link, null);
        var notified = await client.Send(db, link, "unpair", HttpMethod.Post, null, cancel);
        Erase(link, by, clock.GetUtcNow());
        buckets.Drop(link.Id);
        await db.SaveChangesAsync(cancel);
        return (link, notified);
    }

    public static void Erase(Link link, string by, DateTimeOffset now)
    {
        LinkKeys.Erase(link);
        (link.State, link.CutAt, link.CutBy) = (LinkState.Cut, now, by);
        link.Version++;
    }
}
