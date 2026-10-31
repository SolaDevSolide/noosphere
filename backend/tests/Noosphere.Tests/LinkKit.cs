using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Noosphere.Api.Links;
using static Noosphere.Api.Links.LinkCrypto;

namespace Noosphere.Tests;

/// <summary>How a request of the fake peer is signed, to provoke each refusal.</summary>
internal sealed record SignOptions
{
    public long? CreatedOffset { get; init; }
    public string? Nonce { get; init; }
    /// <summary>Signs with this key instead of the peer's signing key (keeps the key id).</summary>
    public ECDsa? Key { get; init; }
    /// <summary>Key id to announce (default: the peer's signing key).</summary>
    public string? KeyId { get; init; }
    /// <summary>Changes the body after it is signed.</summary>
    public bool TamperBody { get; init; }
    /// <summary>The path covered by the signature, when it differs from the path called.</summary>
    public string? SignedPath { get; init; }
    /// <summary>The query covered by the signature, when it differs from the query called.</summary>
    public string? SignedQuery { get; init; }
    /// <summary>A raw body instead of a JWE of the payload.</summary>
    public byte[]? RawBody { get; init; }
}

/// <summary>An answer of Noosphere to the fake peer.</summary>
internal sealed record PeerReply(HttpStatusCode Status, HttpResponseMessage Response, byte[] Body, JsonObject? Payload, string? ProblemType, string? SignatureProblem)
{
    public string? Reason => ProblemType?[(ProblemType.LastIndexOf('/') + 1)..];
}

/// <summary>
/// Plays gestion-mep against an in-process Noosphere: pairs with a code, signs requests, verifies and decrypts answers,
/// and serves its own link endpoints (status, notices, rekey, unpair) on an in-process server under <c>/mep</c>, which
/// Noosphere's outbound link client reaches.
/// </summary>
internal sealed class FakePeer : IAsyncDisposable
{
    public const string Url = "http://mep.test/mep";

    public ECDsa Signing { get; private set; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public ECDiffieHellman Encryption { get; private set; } = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    public Jwk SigningJwk => Jwk.From(Signing);
    public Jwk EncryptionJwk => Jwk.From(Encryption);
    public Jwk? NoosphereSigning { get; set; }
    public Jwk? NoosphereEncryption { get; set; }
    public string? LinkId { get; private set; }
    public bool Confirmed { get; set; } = true;
    public bool Unpaired { get; private set; }
    public List<JsonObject> Notices { get; } = [];
    public List<string> Rekeys { get; } = [];
    public List<string> Errors { get; } = [];

    private WebApplication? _app;

    public static async Task<FakePeer> Start()
    {
        var peer = new FakePeer();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UsePathBase("/mep");
        app.MapGet("/link/v1/status", (Delegate)(async (HttpContext ctx) => await peer.Serve(ctx, "status")));
        app.MapPost("/link/v1/notices", (Delegate)(async (HttpContext ctx) => await peer.Serve(ctx, "notices")));
        app.MapPost("/link/v1/rekey", (Delegate)(async (HttpContext ctx) => await peer.Serve(ctx, "rekey")));
        app.MapPost("/link/v1/unpair", (Delegate)(async (HttpContext ctx) => await peer.Serve(ctx, "unpair")));
        await app.StartAsync();
        peer._app = app;
        return peer;
    }

    /// <summary>Plugs Noosphere's outbound link client into the fake peer's server.</summary>
    public void Plug(IServiceCollection services) =>
        services.AddHttpClient(LinkClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _app!.GetTestServer().CreateHandler());

    public JsonObject PeerJson(string instanceName = "MEP Alizé") => new()
    {
        ["product"] = "gestion-mep", ["instanceName"] = instanceName, ["url"] = Url, ["version"] = "1.11.0",
        ["signingKey"] = SigningJwk.ToJson(), ["encryptionKey"] = EncryptionJwk.ToJson(),
    };

    /// <summary>POST /link/v1/pair with a code (a wrong MAC when <paramref name="macCode"/> differs).</summary>
    public async Task<HttpResponseMessage> PairRaw(HttpClient client, string code, string? macCode = null)
    {
        var codeId = NormalizeCode(code)[..4];
        var nonce = NewNonce();
        var peer = PeerJson();
        var mac = PairingMac(macCode ?? code, new JsonObject { ["codeId"] = codeId, ["peer"] = peer.DeepClone(), ["nonce"] = nonce });
        var body = new JsonObject { ["codeId"] = codeId, ["peer"] = peer, ["nonce"] = nonce, ["mac"] = mac };
        return await client.PostAsync("/link/v1/pair", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
    }

    /// <summary>Pairs, checks the answer's MAC and keeps Noosphere's keys; returns the answer.</summary>
    public async Task<JsonObject> Pair(HttpClient client, string code)
    {
        var response = await PairRaw(client, code);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        var answer = JsonNode.Parse(text)!.AsObject();
        var expected = PairingMac(code, new JsonObject
        {
            ["linkId"] = answer["linkId"]!.DeepClone(), ["peer"] = answer["peer"]!.DeepClone(),
            ["capabilities"] = answer["capabilities"]!.DeepClone(), ["nonce"] = answer["nonce"]!.DeepClone(),
        });
        Assert.Equal(expected, answer["mac"]!.GetValue<string>());
        LinkId = answer["linkId"]!.GetValue<string>();
        NoosphereSigning = Jwk.FromJson(answer["peer"]!["signingKey"]);
        NoosphereEncryption = Jwk.FromJson(answer["peer"]!["encryptionKey"]);
        return answer;
    }

    public string Fingerprint() => LinkCrypto.Fingerprint([SigningJwk, EncryptionJwk, NoosphereSigning!, NoosphereEncryption!]);

    /// <summary>A signed request to Noosphere; the answer's signature is checked and its JWE body decrypted.</summary>
    public async Task<PeerReply> Call(HttpClient client, HttpMethod method, string path, JsonNode? payload = null, SignOptions? o = null,
        Action<HttpRequestMessage>? headers = null)
    {
        o ??= new SignOptions();
        var body = o.RawBody ?? (payload is null ? null : Encoding.ASCII.GetBytes(EncryptJson(payload, NoosphereEncryption!)));
        var request = new HttpRequestMessage(method, path);
        var uri = new Uri(new Uri("http://localhost"), path);
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (o.CreatedOffset ?? 0);
        var signed = LinkSignatures.SignRequest(o.Key ?? Signing, o.KeyId ?? SigningJwk.Kid(), method.Method, o.SignedPath ?? uri.AbsolutePath, o.SignedQuery ?? uri.Query, body, created, o.Nonce);
        request.Headers.TryAddWithoutValidation("Signature-Input", signed.SignatureInput);
        request.Headers.TryAddWithoutValidation("Signature", signed.Signature);
        if (body is not null)
        {
            if (o.TamperBody) body = [.. body, (byte)'x'];
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(JoseContentType);
            request.Content.Headers.TryAddWithoutValidation("Content-Digest", signed.ContentDigest);
        }
        headers?.Invoke(request);
        var response = await client.SendAsync(request);
        var answer = await response.Content.ReadAsByteArrayAsync();
        string? Header(string name) => response.Headers.TryGetValues(name, out var v) ? string.Join(", ", v)
            : response.Content.Headers.TryGetValues(name, out var c) ? string.Join(", ", c) : null;
        string? problem = NoosphereSigning is null ? "no key"
            : LinkSignatures.CheckResponse([(NoosphereSigning.SigningKey(), NoosphereSigning.Kid())], (int)response.StatusCode,
                Header("Signature-Input"), Header("Signature"), Header("Content-Digest"), answer, DateTimeOffset.UtcNow);
        JsonObject? decrypted = null;
        string? type = null;
        if (response.Content.Headers.ContentType?.MediaType == JoseContentType)
            decrypted = JsonNode.Parse(Decrypt(Encoding.ASCII.GetString(answer), Encryption))!.AsObject();
        else if (answer.Length > 0 && response.Content.Headers.ContentType?.MediaType == "application/problem+json")
            type = JsonNode.Parse(answer)!["type"]!.GetValue<string>();
        return new PeerReply(response.StatusCode, response, answer, decrypted, type, problem);
    }

    /// <summary>Renews the fake peer's keys: <c>POST /link/v1/rekey</c> signed with the old key.</summary>
    public async Task<PeerReply> Rekey(HttpClient client)
    {
        var (signing, encryption) = (ECDsa.Create(ECCurve.NamedCurves.nistP256), ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));
        var reply = await Call(client, HttpMethod.Post, "/link/v1/rekey",
            new JsonObject { ["signingKey"] = Jwk.From(signing).ToJson(), ["encryptionKey"] = Jwk.From(encryption).ToJson() });
        if (reply.Status == HttpStatusCode.OK) (Previous, Signing, Encryption) = (Signing, signing, encryption);
        return reply;
    }

    /// <summary>The signing key before the last renewal.</summary>
    public ECDsa? Previous { get; private set; }

    // ---------- The peer's own endpoints ----------

    private async Task Serve(HttpContext ctx, string operation)
    {
        using var buffer = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(buffer);
        var body = buffer.ToArray();
        var input = ParseSignatureInput(ctx.Request.Headers["Signature-Input"].ToString());
        var signature = ParseSignature(ctx.Request.Headers["Signature"].ToString());
        var path = ctx.Request.PathBase.ToUriComponent() + ctx.Request.Path.ToUriComponent();
        string? error = null;
        if (input is null || signature is null || NoosphereSigning is null) error = "unsigned";
        else if (input.KeyId != NoosphereSigning.Kid() || input.Tag != RequestTag || input.Alg != Algorithm) error = "parameters";
        else if (body.Length > 0 && !DigestMatches(ctx.Request.Headers["Content-Digest"], body)) error = "digest";
        else
        {
            using var key = NoosphereSigning.SigningKey();
            var signatureBase = SignatureBase(LinkSignatures.RequestValues(ctx.Request.Method, path, ctx.Request.QueryString.Value,
                body.Length > 0 ? ctx.Request.Headers["Content-Digest"].ToString() : null), input.Raw);
            if (!LinkCrypto.Verify(key, signatureBase, signature)) error = "signature";
        }
        if (error is not null)
        {
            Errors.Add($"{operation}: {error}");
            ctx.Response.StatusCode = 401;
            return;
        }
        JsonObject? payload = body.Length > 0 ? JsonNode.Parse(Decrypt(Encoding.ASCII.GetString(body), Encryption))!.AsObject() : null;
        switch (operation)
        {
            case "status":
                await Answer(ctx, 200, new JsonObject
                {
                    ["linkId"] = LinkId, ["product"] = "gestion-mep", ["instanceName"] = "MEP Alizé", ["version"] = "1.11.0",
                    ["confirmed"] = Confirmed, ["capabilities"] = new JsonArray(), ["keysRenewedAt"] = null,
                });
                break;
            case "notices":
                lock (Notices) Notices.Add(payload!);
                await Answer(ctx, 202, null);
                break;
            case "rekey":
                await Answer(ctx, 200, null);
                (NoosphereSigning, NoosphereEncryption) = (Jwk.FromJson(payload!["signingKey"]), Jwk.FromJson(payload["encryptionKey"]));
                Rekeys.Add(NoosphereSigning.Kid());
                break;
            case "unpair":
                Unpaired = true;
                await Answer(ctx, 204, null);
                break;
        }
    }

    private async Task Answer(HttpContext ctx, int status, JsonObject? payload)
    {
        var body = payload is null ? null : Encoding.ASCII.GetBytes(EncryptJson(payload, NoosphereEncryption!));
        var signed = LinkSignatures.SignResponse(Signing, SigningJwk.Kid(), status, body, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        ctx.Response.StatusCode = status;
        ctx.Response.Headers["Signature-Input"] = signed.SignatureInput;
        ctx.Response.Headers["Signature"] = signed.Signature;
        if (body is null) return;
        ctx.Response.Headers["Content-Digest"] = signed.ContentDigest;
        ctx.Response.ContentType = JoseContentType;
        await ctx.Response.Body.WriteAsync(body);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        Signing.Dispose();
        Encryption.Dispose();
        Previous?.Dispose();
    }
}
