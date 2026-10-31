using System.Security.Cryptography;
using static Noosphere.Api.Links.LinkCrypto;

namespace Noosphere.Api.Links;

/// <summary>The three headers of a signed message: Content-Digest (null without a body), Signature-Input and Signature.</summary>
public sealed record SignatureHeaders(string? ContentDigest, string SignatureInput, string Signature);

/// <summary>
/// Signing and verifying link messages (RFC 9421). Requests cover <c>("@method" "@path" "@query" "content-digest")</c>,
/// responses <c>("@status" "content-digest")</c>; <c>content-digest</c> is left out when there is no body.
/// </summary>
public static class LinkSignatures
{
    public static readonly string[] RequestComponents = ["@method", "@path", "@query", "content-digest"];
    public static readonly string[] ResponseComponents = ["@status", "content-digest"];

    /// <summary>Components covered by a message with or without a body.</summary>
    public static string[] Covered(string[] all, bool body) => body ? all : all[..^1];

    public static SignatureHeaders SignRequest(ECDsa key, string keyId, string method, string path, string? query, byte[]? body, long created, string? nonce = null)
    {
        var hasBody = body is { Length: > 0 };
        var digest = hasBody ? ContentDigest(body) : null;
        var parameters = new SignatureParameters(Covered(RequestComponents, hasBody), created, nonce ?? NewNonce(), keyId, Algorithm, RequestTag);
        var serialized = parameters.Serialize();
        var signature = Sign(key, SignatureBase(RequestValues(method, path, query, digest), serialized));
        return new SignatureHeaders(digest, $"{SignatureLabel}={serialized}", $"{SignatureLabel}=:{Convert.ToBase64String(signature)}:");
    }

    public static SignatureHeaders SignResponse(ECDsa key, string keyId, int status, byte[]? body, long created, string? nonce = null)
    {
        var hasBody = body is { Length: > 0 };
        var digest = hasBody ? ContentDigest(body) : null;
        var parameters = new SignatureParameters(Covered(ResponseComponents, hasBody), created, nonce ?? NewNonce(), keyId, Algorithm, ResponseTag);
        var serialized = parameters.Serialize();
        var signature = Sign(key, SignatureBase(ResponseValues(status, digest), serialized));
        return new SignatureHeaders(digest, $"{SignatureLabel}={serialized}", $"{SignatureLabel}=:{Convert.ToBase64String(signature)}:");
    }

    public static IEnumerable<(string, string)> RequestValues(string method, string path, string? query, string? digest)
    {
        yield return ("@method", method.ToUpperInvariant());
        yield return ("@path", path.Length == 0 ? "/" : path);
        yield return ("@query", QueryComponent(query));
        if (digest is not null) yield return ("content-digest", digest.Trim());
    }

    public static IEnumerable<(string, string)> ResponseValues(int status, string? digest)
    {
        yield return ("@status", status.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
        if (digest is not null) yield return ("content-digest", digest.Trim());
    }

    /// <summary>
    /// Checks a signed response from the peer: the covered components, tag, algorithm, key id, clock, digest and signature.
    /// Returns null when it is valid, or what is wrong.
    /// </summary>
    public static string? CheckResponse(IEnumerable<(ECDsa Key, string KeyId)> peerKeys, int status, string? signatureInput, string? signatureHeader,
        string? contentDigest, byte[] body, DateTimeOffset now)
    {
        var input = ParseSignatureInput(signatureInput);
        var signature = ParseSignature(signatureHeader);
        if (input is null || signature is null) return "the response is not signed";
        var hasBody = body.Length > 0;
        if (!input.Components.SequenceEqual(Covered(ResponseComponents, hasBody)) || input.Tag != ResponseTag || input.Alg != Algorithm)
            return "the response signature does not cover the expected components";
        if (input.Created is not { } created || Math.Abs(now.ToUnixTimeSeconds() - created) > LinkProtocol.ClockSkew.TotalSeconds)
            return "the response signature is too old or in the future";
        if (hasBody && !DigestMatches(contentDigest, body)) return "the response digest does not match its body";
        var key = peerKeys.FirstOrDefault(k => k.KeyId == input.KeyId).Key;
        if (key is null) return "the response is signed with an unknown key";
        var signatureBase = SignatureBase(ResponseValues(status, hasBody ? contentDigest : null), input.Raw);
        return Verify(key, signatureBase, signature) ? null : "the response signature is invalid";
    }
}
