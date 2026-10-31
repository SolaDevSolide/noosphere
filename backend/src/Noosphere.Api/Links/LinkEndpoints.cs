using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Noosphere.Api.Agents;
using Noosphere.Api.Data;
using Noosphere.Api.Endpoints;
using Noosphere.Api.Services;
using Noosphere.Domain;
using static Noosphere.Api.Links.LinkCrypto;

namespace Noosphere.Api.Links;

/// <summary>
/// The link protocol served by Noosphere, outside <c>/api</c> (no cookie, no CSRF header): pairing, status, catalogue,
/// deployments, key renewal and cut. Every request but the pairing is signed (RFC 9421); every answer to an identified
/// link is signed; data bodies are JWE. 120 requests per minute per link.
/// </summary>
public static class LinkEndpoints
{
    public const string RateLimit = "link";

    public static IServiceCollection AddNoosphereLinks(this IServiceCollection services)
    {
        services.AddSingleton<LinkKeys>();
        services.AddSingleton<NoticeBuckets>();
        services.AddScoped<LinkInbound>();
        services.AddScoped<LinkClient>();
        services.AddScoped<LinkAdmin>();
        services.AddHttpClient(LinkClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        return services;
    }

    /// <summary>Rate limit partition: the key id of the signature, or the client address for the (unsigned) pairing.</summary>
    public static RateLimitPartition<string> Partition(HttpContext ctx) => RateLimitPartition.GetFixedWindowLimiter(
        ParseSignatureInput(ctx.Request.Headers["Signature-Input"].ToString())?.KeyId is { } kid ? "key:" + kid : "ip:" + ctx.Connection.RemoteIpAddress,
        _ => new FixedWindowRateLimiterOptions { PermitLimit = LinkProtocol.RequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    public static void MapLinks(this WebApplication app, Settings settings)
    {
        var link = app.MapGroup(LinkProtocol.BasePath).AllowAnonymous().ExcludeFromDescription().RequireRateLimiting(RateLimit);

        link.MapPost("/pair", async (HttpContext ctx, NoosphereDbContext db, LinkAdmin admin, TimeProvider clock, ILoggerFactory logs) =>
        {
            JsonObject? request;
            try
            {
                request = await JsonNode.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted) as JsonObject;
            }
            catch (JsonException)
            {
                request = null;
            }
            var outcome = request is null
                ? new LinkAdmin.PairOutcome(400, null, LinkProtocol.Reasons.BadBody, "A JSON object is expected.")
                : await admin.Pair(request, (await InstanceSettingsStore.Get(db, settings)).InstanceName, PublicAddress.Of(ctx.Request, settings), ctx.RequestAborted);
            LinkLog.Add(db, outcome.Link, LinkDirection.In, "pair", outcome.Body is null ? LinkExchangeResult.Refused : LinkExchangeResult.Ok,
                outcome.Status, clock.GetUtcNow(), outcome.Detail, outcome.Reason);
            await db.SaveChangesAsync();
            if (outcome.Body is null)
            {
                logs.CreateLogger("Noosphere.Links").LogWarning("Pairing refused ({Reason}): {Detail}", outcome.Reason, outcome.Detail);
                return new LinkResponse(outcome.Status, LinkResponse.ProblemBody(outcome.Status, outcome.Reason!, outcome.Detail), Problems.ContentType);
            }
            return new LinkResponse(200, Encoding.UTF8.GetBytes(outcome.Body.ToJsonString(LinkProtocol.Json)), "application/json");
        });

        link.MapGet("/status", async (HttpContext ctx, LinkInbound inbound, NoosphereDbContext db) =>
        {
            var (r, refusal) = await inbound.Verify(ctx, "status", beforeConfirmation: true);
            if (r is null) return refusal!;
            var l = r.Link;
            var status = new LinkStatusPayload(l.Id, LinkProtocol.Product, (await InstanceSettingsStore.Get(db, settings)).InstanceName, LinkAdmin.Version,
                l.ConfirmedHere, l.Capabilities, l.KeysRenewedAt);
            return await inbound.Answer(r, "status", 200, LinkProtocol.ToNode(status));
        });

        link.MapGet("/catalog", async (HttpContext ctx, LinkInbound inbound, NoosphereDbContext db) =>
        {
            var (r, refusal) = await inbound.Verify(ctx, "catalog");
            if (r is null) return refusal!;
            Seen(r.Link);
            if (!r.Link.Capabilities.Contains(LinkProtocol.CatalogRead))
                return await inbound.Refuse(r.Link, "catalog", 403, LinkProtocol.Reasons.CapabilityMissing, "This link does not have the catalog:read capability.");
            var catalog = await LinkCatalog.Build(db, (await InstanceSettingsStore.Get(db, settings)).InstanceName, PublicAddress.Of(ctx.Request, settings),
                inbound.Now, ctx.RequestAborted);
            var etag = LinkCatalog.ETag(catalog);
            LinkResponse answer;
            if (LinkCatalog.NotModified(ctx.Request.Headers.IfNoneMatch.ToString(), etag))
                answer = await inbound.Answer(r, "catalog", 304, null, "Not modified.");
            else
                answer = await inbound.Answer(r, "catalog", 200, LinkProtocol.ToNode(catalog),
                    $"{catalog.Applications.Count} application(s), {catalog.Flows.Count} flow(s), {catalog.Domains.Count} domain(s), {catalog.Teams.Count} team(s).");
            answer.Headers["ETag"] = etag;
            return answer;
        });

        link.MapPost("/deployments", async (HttpContext ctx, LinkInbound inbound, NoosphereDbContext db) =>
        {
            var (r, refusal) = await inbound.Verify(ctx, "deployments");
            if (r is null) return refusal!;
            Seen(r.Link);
            if (!r.Link.Capabilities.Contains(LinkProtocol.DeploymentsReceive))
                return await inbound.Refuse(r.Link, "deployments", 403, LinkProtocol.Reasons.CapabilityMissing, "This link does not have the deployments:receive capability.");
            if (inbound.DecryptBody(r) is not { } body)
                return await inbound.Refuse(r.Link, "deployments", 400, LinkProtocol.Reasons.BadBody, "The body is not a JWE (ECDH-ES, A256GCM) for this link's encryption key.");
            var errors = new FieldErrors();
            var events = Deployments.Parse(body, errors);
            if (errors.Any)
                return await inbound.Refuse(r.Link, "deployments", 400, LinkProtocol.Reasons.BadBody,
                    "Invalid events: " + string.Join("; ", errors.All.Take(5).Select(e => $"{e.Field}: {e.Message}")));
            var receipt = await Deployments.Upsert(db, r.Link, events, inbound.Now, ctx.RequestAborted);
            return await inbound.Answer(r, "deployments", 200, LinkProtocol.ToNode(receipt),
                $"{receipt.Accepted} event(s) accepted" + (receipt.IgnoredApplicationIds.Count > 0 ? $", unknown applications ignored: {string.Join(", ", receipt.IgnoredApplicationIds)}." : "."));
        });

        link.MapPost("/rekey", async (HttpContext ctx, LinkInbound inbound, NoosphereDbContext db, LinkKeys keys) =>
        {
            var (r, refusal) = await inbound.Verify(ctx, "rekey");
            if (r is null) return refusal!;
            var l = r.Link;
            if (r.Signature.KeyId != l.PeerSigningKid)
                return await inbound.Refuse(l, "rekey", 401, LinkProtocol.Reasons.BadSignature, "A key renewal is signed with the current signing key.");
            Jwk signing, encryption;
            try
            {
                var body = inbound.DecryptBody(r) ?? throw new FormatException("not a JWE for this link");
                (signing, encryption) = (Jwk.FromJson(body["signingKey"]), Jwk.FromJson(body["encryptionKey"]));
                signing.Parameters();
                encryption.Parameters();
            }
            catch (Exception e) when (e is FormatException or System.Security.Cryptography.CryptographicException)
            {
                return await inbound.Refuse(l, "rekey", 400, LinkProtocol.Reasons.BadBody, "Expected a JWE of {signingKey, encryptionKey}, two P-256 public keys.");
            }
            var now = inbound.Now;
            (l.PeerPreviousSigningKey, l.PeerPreviousSigningKid, l.PeerPreviousUntil) = (l.PeerSigningKey, l.PeerSigningKid, now + LinkProtocol.RekeyGrace);
            (l.PeerSigningKey, l.PeerSigningKid) = (signing.Serialize(), signing.Kid());
            (l.PeerEncryptionKey, l.PeerEncryptionKid) = (encryption.Serialize(), encryption.Kid());
            l.Fingerprint = keys.Fingerprint(l);
            l.KeysRenewedAt = now;
            l.Version++;
            return await inbound.Answer(r, "rekey", 200, null, "The peer renewed its keys; its old signing key is accepted for 5 more minutes.");
        });

        link.MapPost("/unpair", async (HttpContext ctx, LinkInbound inbound) =>
        {
            var (r, refusal) = await inbound.Verify(ctx, "unpair", beforeConfirmation: true);
            if (r is null) return refusal!;
            LinkAdmin.Erase(r.Link, "peer", inbound.Now);
            // The answer is still signed: the signing key was read before the keys were erased.
            return await inbound.Answer(r, "unpair", 204, null, "The peer cut the link; its keys are erased.");
        });
    }

    /// <summary>A data request from the peer: its administrator has confirmed the link.</summary>
    private static void Seen(Link link) => link.PeerConfirmed = true;
}
