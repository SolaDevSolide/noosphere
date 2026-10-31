using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Noosphere.Api.Data;
using Noosphere.Domain;
using static Noosphere.Api.Links.LinkCrypto;

namespace Noosphere.Api.Links;

/// <summary>Outcome of a request sent to the peer.</summary>
/// <param name="Payload">The decrypted JWE body of a successful answer, when there is one.</param>
/// <param name="Reason">The problem type's last segment of a refusal (<c>link-not-confirmed</c>…), when the peer gave one.</param>
public sealed record LinkCallResult(LinkExchangeResult Result, int? Status, JsonObject? Payload, string? Reason, string Detail)
{
    public bool Ok => Result is LinkExchangeResult.Ok or LinkExchangeResult.Demo;
}

/// <summary>
/// Sends signed requests to the peer (<c>{url}/link/v1/...</c>): status, notices, rekey and unpair. Bodies are JWE to the
/// peer's encryption key; answers must be signed with the peer's signing key. A demo link is never called: the exchange
/// is logged as <c>demo</c>. Every call writes an exchange line and saves.
/// </summary>
public sealed class LinkClient(IHttpClientFactory http, LinkKeys keys, TimeProvider clock, ILogger<LinkClient> logger)
{
    public const string HttpClientName = "noosphere-link";

    public async Task<LinkCallResult> Send(NoosphereDbContext db, Link link, string operation, HttpMethod method, JsonNode? payload,
        CancellationToken cancel = default)
    {
        var now = clock.GetUtcNow();
        if (link.Demo)
        {
            LinkLog.Add(db, link, LinkDirection.Out, operation, LinkExchangeResult.Demo, null, now, "Demo link: nothing was sent.");
            await db.SaveChangesAsync(cancel);
            return new LinkCallResult(LinkExchangeResult.Demo, null, null, null, "Demo link: nothing was sent.");
        }
        var result = await Call(link, operation, method, payload, now, link.OwnSigningKey, link.OwnSigningKid, cancel);
        LinkLog.Add(db, link, LinkDirection.Out, operation, result.Result, result.Status, clock.GetUtcNow(), result.Detail, result.Reason);
        await db.SaveChangesAsync(cancel);
        if (!result.Ok) logger.LogWarning("Link {Operation} to {Url} failed: {Detail}", operation, link.Url, result.Detail);
        return result;
    }

    private async Task<LinkCallResult> Call(Link link, string operation, HttpMethod method, JsonNode? payload, DateTimeOffset now,
        string? signingKey, string signingKid, CancellationToken cancel)
    {
        if (signingKey is null || link.PeerSigningKey is null || link.PeerEncryptionKey is null)
            return new LinkCallResult(LinkExchangeResult.Failed, null, null, null, "The link has no keys (cut).");
        var uri = new Uri(link.Url.TrimEnd('/') + LinkProtocol.BasePath + "/" + operation);
        var body = payload is null ? null : Encoding.ASCII.GetBytes(EncryptJson(payload, Jwk.Parse(link.PeerEncryptionKey)));
        using var request = new HttpRequestMessage(method, uri);
        using (var key = keys.Signing(signingKey))
        {
            var headers = LinkSignatures.SignRequest(key, signingKid, method.Method, uri.AbsolutePath, uri.Query, body, now.ToUnixTimeSeconds());
            request.Headers.TryAddWithoutValidation("Signature-Input", headers.SignatureInput);
            request.Headers.TryAddWithoutValidation("Signature", headers.Signature);
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(JoseContentType);
                request.Content.Headers.TryAddWithoutValidation("Content-Digest", headers.ContentDigest);
            }
        }
        HttpResponseMessage response;
        byte[] answer;
        try
        {
            response = await http.CreateClient(HttpClientName).SendAsync(request, cancel);
            answer = await response.Content.ReadAsByteArrayAsync(cancel);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            return new LinkCallResult(LinkExchangeResult.Failed, null, null, null, $"{uri.GetLeftPart(UriPartial.Path)} unreachable: {e.Message}");
        }
        using (response)
        {
            var status = (int)response.StatusCode;
            string? Header(string name) =>
                response.Headers.TryGetValues(name, out var v) ? string.Join(", ", v)
                : response.Content.Headers.TryGetValues(name, out var c) ? string.Join(", ", c) : null;
            var reason = ProblemReason(response, answer);
            var peerKeys = new List<(ECDsa, string)> { (Jwk.Parse(link.PeerSigningKey).SigningKey(), link.PeerSigningKid) };
            if (link.PeerPreviousSigningKey is { } previous && link.PeerPreviousUntil > now) peerKeys.Add((Jwk.Parse(previous).SigningKey(), link.PeerPreviousSigningKid!));
            var problem = LinkSignatures.CheckResponse(peerKeys, status, Header("Signature-Input"), Header("Signature"), Header("Content-Digest"), answer, clock.GetUtcNow());
            foreach (var (k, _) in peerKeys) k.Dispose();
            if (!response.IsSuccessStatusCode)
            {
                var detail = $"HTTP {status}" + (reason is null ? "" : $" ({reason})") + (problem is null ? "" : $"; {problem}");
                return new LinkCallResult(status is >= 400 and < 500 ? LinkExchangeResult.Refused : LinkExchangeResult.Failed, status, null, reason, detail);
            }
            if (problem is not null) return new LinkCallResult(LinkExchangeResult.Failed, status, null, null, $"HTTP {status}, but {problem}.");
            JsonObject? decrypted = null;
            if (answer.Length > 0 && response.Content.Headers.ContentType?.MediaType == JoseContentType)
            {
                using var own = keys.Encryption(link.OwnEncryptionKey!);
                try
                {
                    decrypted = JsonNode.Parse(Decrypt(Encoding.ASCII.GetString(answer), own)) as JsonObject;
                }
                catch (Exception e) when (e is CryptographicException or JsonException)
                {
                    return new LinkCallResult(LinkExchangeResult.Failed, status, null, null, $"HTTP {status}, but the body cannot be decrypted: {e.Message}");
                }
            }
            return new LinkCallResult(LinkExchangeResult.Ok, status, decrypted, null, $"HTTP {status}");
        }
    }

    /// <summary>The last segment of a problem+json <c>type</c> (<c>link-not-confirmed</c>).</summary>
    private static string? ProblemReason(HttpResponseMessage response, byte[] body)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/problem+json" or "application/json") || body.Length == 0) return null;
        try
        {
            return JsonNode.Parse(body)?["type"]?.GetValue<string>() is { } type && type.LastIndexOf('/') is var slash and >= 0 ? type[(slash + 1)..] : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
