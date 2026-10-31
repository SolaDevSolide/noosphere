using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;
using static Noosphere.Api.Links.LinkCrypto;

namespace Noosphere.Api.Links;

/// <summary>
/// A link response. When this side's signing key of the link is known, the response is signed (<c>@status</c> and
/// <c>content-digest</c>); requests whose key is unknown, and requests on a cut link, get an unsigned answer.
/// </summary>
public sealed class LinkResponse(int status, byte[]? body = null, string? contentType = null, ECDsa? signer = null, string? keyId = null) : IResult
{
    public int Status { get; } = status;
    public Dictionary<string, string> Headers { get; } = [];

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var ctx = httpContext;
        var response = ctx.Response;
        response.StatusCode = Status;
        foreach (var (name, value) in Headers) response.Headers[name] = value;
        if (signer is not null && keyId is not null)
        {
            var clock = ctx.RequestServices.GetRequiredService<TimeProvider>();
            var headers = LinkSignatures.SignResponse(signer, keyId, Status, body, clock.GetUtcNow().ToUnixTimeSeconds());
            if (headers.ContentDigest is { } digest) response.Headers["Content-Digest"] = digest;
            response.Headers["Signature-Input"] = headers.SignatureInput;
            response.Headers["Signature"] = headers.Signature;
            signer.Dispose();
        }
        if (body is { Length: > 0 })
        {
            response.ContentType = contentType;
            response.ContentLength = body.Length;
            await response.Body.WriteAsync(body, ctx.RequestAborted);
        }
    }

    public static byte[] ProblemBody(int status, string reason, string detail) =>
        JsonSerializer.SerializeToUtf8Bytes(new ProblemDto(LinkProtocol.ProblemType(reason), Problems.Title(status), status, detail), LinkProtocol.Json);
}

/// <summary>A verified link request: its link, raw body and signature parameters.</summary>
public sealed record LinkRequest(Link Link, byte[] Body, ParsedSignatureInput Signature)
{
    /// <summary>The responder's signing key, to sign the answer.</summary>
    public required ECDsa? Signer { get; init; }
}

/// <summary>Checks signed link requests and answers them: refusals are problem+json, logged in the exchange log.</summary>
public sealed class LinkInbound(NoosphereDbContext db, LinkKeys keys, Settings settings, TimeProvider clock, ILogger<LinkInbound> logger)
{
    public DateTimeOffset Now { get; } = clock.GetUtcNow();

    /// <summary>
    /// Verifies the signature of a request. Order of the checks: key id (<c>unknown-key</c>), link state (<c>link-cut</c>),
    /// <c>created</c> (<c>clock-skew</c>), digest (<c>bad-digest</c>), signature (<c>bad-signature</c>), nonce (<c>replay</c>),
    /// then this side's confirmation (<c>link-not-confirmed</c>, 403) unless <paramref name="beforeConfirmation"/>.
    /// </summary>
    public async Task<(LinkRequest? Request, LinkResponse? Refusal)> Verify(HttpContext ctx, string operation, bool beforeConfirmation = false)
    {
        var request = ctx.Request;
        byte[] body;
        using (var buffer = new MemoryStream())
        {
            await request.Body.CopyToAsync(buffer, ctx.RequestAborted);
            body = buffer.ToArray();
        }
        var input = ParseSignatureInput(request.Headers["Signature-Input"].ToString());
        var signature = ParseSignature(request.Headers["Signature"].ToString());
        var keyId = input?.KeyId;
        var link = keyId is null ? null : await db.Links
            .Where(l => l.PeerSigningKid == keyId || (l.PeerPreviousSigningKid == keyId && l.PeerPreviousUntil > Now))
            .OrderByDescending(l => l.PairedAt).FirstOrDefaultAsync(ctx.RequestAborted);
        if (link is null)
            return (null, await Refuse(null, operation, 401, LinkProtocol.Reasons.UnknownKey,
                input is null ? "The request is not signed (Signature-Input \"link\" is missing or malformed)." : "No link uses this signing key."));
        if (link.State == LinkState.Cut || link.PeerSigningKey is null)
            return (null, await Refuse(link, operation, 401, LinkProtocol.Reasons.LinkCut, "This link was cut: pair again."));

        var hasBody = body.Length > 0;
        var created = input!.Created;
        if (created is null || Math.Abs(Now.ToUnixTimeSeconds() - created.Value) > LinkProtocol.ClockSkew.TotalSeconds)
            return (null, await Refuse(link, operation, 401, LinkProtocol.Reasons.ClockSkew,
                $"The signature was created more than {LinkProtocol.ClockSkew.TotalSeconds:0} s away from this server's clock ({LinkProtocol.Time(Now)}). Check the clocks (NTP)."));
        var digest = request.Headers["Content-Digest"].ToString();
        if (hasBody && !DigestMatches(digest, body))
            return (null, await Refuse(link, operation, 401, LinkProtocol.Reasons.BadDigest, "Content-Digest is missing or does not match the body as received."));
        if (signature is null || input.Alg != Algorithm || input.Tag != RequestTag
            || !input.Components.SequenceEqual(LinkSignatures.Covered(LinkSignatures.RequestComponents, hasBody)) || input.Nonce is not { Length: > 0 and <= 100 })
            return (null, await Refuse(link, operation, 401, LinkProtocol.Reasons.BadSignature,
                "The signature parameters are not those of the link protocol (components, alg, tag or nonce)."));

        var peerKey = keyId == link.PeerSigningKid ? link.PeerSigningKey : link.PeerPreviousSigningKey!;
        using (var key = Jwk.Parse(peerKey).SigningKey())
        {
            var query = request.QueryString.Value;
            var verified = SignedPaths(request).Any(path =>
                LinkCrypto.Verify(key, SignatureBase(LinkSignatures.RequestValues(request.Method, path, query, hasBody ? digest : null), input.Raw), signature));
            if (!verified)
                return (null, await Refuse(link, operation, 401, LinkProtocol.Reasons.BadSignature,
                    "The signature does not verify: check that @path is the full path of the URL called and @query the raw query."));
        }

        db.LinkNonces.Add(new LinkNonce { LinkId = link.Id, Nonce = input.Nonce, SeenAt = Now });
        try
        {
            await db.SaveChangesAsync(ctx.RequestAborted);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            link = await db.Links.FirstAsync(l => l.Id == link.Id, ctx.RequestAborted);
            return (null, await Refuse(link, operation, 401, LinkProtocol.Reasons.Replay, "This nonce was already used in the last 10 minutes."));
        }

        if (!beforeConfirmation && !link.ConfirmedHere)
            return (null, await Refuse(link, operation, 403, LinkProtocol.Reasons.NotConfirmed,
                "The fingerprint is not confirmed yet on Noosphere: an administrator confirms it in Links."));
        return (new LinkRequest(link, body, input) { Signer = keys.OwnSigning(link) }, null);
    }

    /// <summary>
    /// The paths the sender may have signed: the full path of the URL it called. Behind a reverse proxy that strips a
    /// prefix, the path of PUBLIC_URL comes first; then PathBase + Path; then Path alone.
    /// </summary>
    private IEnumerable<string> SignedPaths(HttpRequest request)
    {
        var path = request.Path.ToUriComponent();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (settings.PublicUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var prefix = uri.AbsolutePath.TrimEnd('/');
            if (seen.Add(prefix + path)) yield return prefix + path;
        }
        var withBase = request.PathBase.ToUriComponent() + path;
        if (seen.Add(withBase)) yield return withBase;
        if (seen.Add(path)) yield return path;
    }

    /// <summary>A problem response, logged; signed when this side still has its key for the link.</summary>
    public async Task<LinkResponse> Refuse(Link? link, string operation, int status, string reason, string detail)
    {
        LinkLog.Add(db, link, LinkDirection.In, operation, LinkExchangeResult.Refused, status, Now, detail, reason);
        await db.SaveChangesAsync();
        logger.LogWarning("Link request {Operation} refused ({Reason}): {Detail}", operation, reason, detail);
        return Problem(link, status, reason, detail);
    }

    public LinkResponse Problem(Link? link, int status, string reason, string detail) =>
        new(status, LinkResponse.ProblemBody(status, reason, detail), Problems.ContentType,
            link is null ? null : keys.OwnSigning(link), link?.OwnSigningKid);

    /// <summary>A successful answer, logged: a JWE of <paramref name="payload"/> to the peer's encryption key, or no body.</summary>
    public async Task<LinkResponse> Answer(LinkRequest r, string operation, int status, JsonNode? payload, string? detail = null)
    {
        var body = payload is null ? null : Encoding.ASCII.GetBytes(EncryptJson(payload, Jwk.Parse(r.Link.PeerEncryptionKey!)));
        LinkLog.Add(db, r.Link, LinkDirection.In, operation, LinkExchangeResult.Ok, status, Now, detail);
        await db.SaveChangesAsync();
        return new LinkResponse(status, body, payload is null ? null : JoseContentType, r.Signer, r.Link.OwnSigningKid);
    }

    /// <summary>Decrypts the JWE body of a request to this side's encryption key (the previous one during a renewal).</summary>
    public JsonObject? DecryptBody(LinkRequest r)
    {
        var compact = Encoding.ASCII.GetString(r.Body);
        using var key = keys.DecryptionKey(r.Link, JweKid(compact), Now);
        if (key is null) return null;
        try
        {
            return JsonNode.Parse(Decrypt(compact, key)) as JsonObject;
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            return null;
        }
    }
}
