using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noosphere.Api.Data;
using Noosphere.Api.Links;
using Noosphere.Domain;

namespace Noosphere.Tests;

/// <summary>
/// The link protocol end to end: an in-process Noosphere (demo data) and a fake gestion-mep played by
/// <see cref="FakePeer"/>, which pairs, signs, encrypts and serves its own link endpoints.
/// </summary>
[Collection("Api")]
public sealed class LinkTests : IAsyncLifetime, IDisposable
{
    private FakePeer _peer = null!;
    private ApiFixture _api = null!;

    public async Task InitializeAsync()
    {
        _peer = await FakePeer.Start();
        _api = new ApiFixture(host: b => b.ConfigureTestServices(_peer.Plug));
    }

    public async Task DisposeAsync() => await _peer.DisposeAsync();

    public void Dispose() => _api.Dispose();

    private Task<HttpClient> Alice() => _api.SignedIn("alice");

    private async Task<string> NewCode(object? body = null)
    {
        var created = await (await Alice()).PostJson("/api/links/codes", body ?? new { }, HttpStatusCode.Created);
        return created.Str("code");
    }

    private async Task<Guid> Paired(bool confirm = true)
    {
        var answer = await _peer.Pair(_api.Client(), await NewCode());
        var id = Guid.Parse(answer["linkId"]!.GetValue<string>());
        if (confirm) await (await Alice()).PostJson($"/api/links/{id}/confirm", new { fingerprint = _peer.Fingerprint() });
        return id;
    }

    private T Db<T>(Func<NoosphereDbContext, T> read)
    {
        using var scope = _api.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<NoosphereDbContext>());
    }

    private async Task<List<JsonElement>> Exchanges(Guid? id = null) =>
        [.. (await (await Alice()).GetJson(id is null ? "/api/links/exchanges?limit=200" : $"/api/links/{id}/exchanges?limit=200")).GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task Pairing_shows_the_same_fingerprint_on_both_sides_and_needs_confirmation()
    {
        var alice = await Alice();
        var created = await alice.PostJson("/api/links/codes", new { expiresInMinutes = 10, capabilities = new[] { "deployments:receive", "catalog:read" } }, HttpStatusCode.Created);
        var code = created.Str("code");
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", code);
        Assert.Equal(code[..4], created.Str("codeId"));
        Assert.Equal(["catalog:read", "deployments:receive"], created.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()));
        var codes = await alice.GetJson("/api/links/codes");
        Assert.Contains(codes.EnumerateArray(), c => c.Str("codeId") == code[..4] && c.Str("state") == "active");
        Assert.DoesNotContain("\"code\"", codes.GetRawText(), StringComparison.Ordinal);
        await (await _api.SignedIn("bob")).PostJson("/api/links/codes", new { }, HttpStatusCode.Forbidden);

        // gestion-mep may type the code in lower case with spaces.
        var answer = await _peer.Pair(_api.Client(), code.ToLowerInvariant().Replace('-', ' '));
        Assert.Equal("noosphere", answer["peer"]!["product"]!.GetValue<string>());
        var id = Guid.Parse(answer["linkId"]!.GetValue<string>());
        var link = await alice.GetJson($"/api/links/{id}");
        Assert.Equal(("pending", false, "MEP Alizé", FakePeer.Url), (link.Str("state"), link.GetProperty("confirmedHere").GetBoolean(), link.Str("instanceName"), link.Str("url")));
        Assert.Equal(_peer.Fingerprint(), link.Str("fingerprint"));
        Assert.Equal(_peer.SigningJwk.Kid(), link.GetProperty("keys").Str("peerSigning"));

        // Before confirmation: the status answers (confirmed: false), data is refused with 403.
        var status = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/status");
        Assert.Equal(HttpStatusCode.OK, status.Status);
        Assert.Null(status.SignatureProblem);
        Assert.False(status.Payload!["confirmed"]!.GetValue<bool>());
        Assert.Equal(id.ToString(), status.Payload["linkId"]!.GetValue<string>());
        var early = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/catalog");
        Assert.Equal((HttpStatusCode.Forbidden, "link-not-confirmed"), (early.Status, early.Reason));
        Assert.Null(early.SignatureProblem);

        // A wrong fingerprint is refused; the right one activates the link, and Noosphere reads the peer's status.
        var wrong = await alice.PostJson($"/api/links/{id}/confirm", new { fingerprint = "abbey abbey 0000 abbey" }, (HttpStatusCode)422);
        Assert.Contains("does not match", wrong.Str("detail"), StringComparison.Ordinal);
        var confirmed = await alice.PostJson($"/api/links/{id}/confirm", new { fingerprint = "  " + _peer.Fingerprint().ToUpperInvariant() });
        Assert.Equal(("active", true, true), (confirmed.Str("state"), confirmed.GetProperty("confirmedHere").GetBoolean(), confirmed.GetProperty("peerConfirmed").GetBoolean()));
        Assert.Equal(HttpStatusCode.OK, (await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/catalog")).Status);
        // Capabilities chosen with the code.
        var notices = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/status");
        Assert.Equal(["catalog:read", "deployments:receive"], notices.Payload!["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Empty(_peer.Errors);

        var log = await Exchanges(id);
        Assert.Contains(log, e => e.Str("operation") == "pair" && e.Str("direction") == "in" && e.Str("result") == "ok");
        Assert.Contains(log, e => e.Str("operation") == "status" && e.Str("direction") == "out" && e.Str("result") == "ok");
        Assert.Contains(log, e => e.Str("operation") == "catalog" && e.Str("reason") == "link-not-confirmed");
        Assert.Contains((await alice.GetJson("/api/links")).EnumerateArray(), l => l.Str("id") == id.ToString());
    }

    [Fact]
    public async Task Pairing_codes_are_single_use_and_burn_after_five_wrong_macs()
    {
        var client = _api.Client();
        var code = await NewCode();
        await _peer.Pair(client, code);
        Assert.Equal(HttpStatusCode.Conflict, (await _peer.PairRaw(client, code)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _peer.PairRaw(client, "ZZZZ-ZZZZ-ZZZZ")).StatusCode);

        var other = await NewCode();
        for (var i = 1; i <= 4; i++)
        {
            var response = await _peer.PairRaw(client, other, macCode: other[..^1] + (other[^1] == '0' ? '1' : '0'));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.EndsWith("/bad-mac", JsonNode.Parse(await response.Content.ReadAsStringAsync())!["type"]!.GetValue<string>(), StringComparison.Ordinal);
        }
        Assert.Equal(HttpStatusCode.Gone, (await _peer.PairRaw(client, other, macCode: "WRONG-CODE-0000")).StatusCode);
        // Burnt: even the right MAC is refused.
        Assert.Equal(HttpStatusCode.Gone, (await _peer.PairRaw(client, other)).StatusCode);
        var codes = await (await Alice()).GetJson("/api/links/codes");
        var burnt = codes.EnumerateArray().Single(c => c.Str("codeId") == other[..4]);
        Assert.Equal(("burnt", 5), (burnt.Str("state"), burnt.GetProperty("failedAttempts").GetInt32()));

        // Expired after its lifetime.
        var expiring = await NewCode(new { expiresInMinutes = 1 });
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
            await db.LinkCodes.Where(c => c.CodeId == expiring.Substring(0, 4)).ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await _peer.PairRaw(client, expiring)).StatusCode);
        await (await Alice()).PostJson("/api/links/codes", new { expiresInMinutes = 0, capabilities = new[] { "everything" } }, HttpStatusCode.BadRequest);

        var refusals = await Exchanges();
        Assert.Contains(refusals, e => e.Str("operation") == "pair" && e.Str("reason") == "bad-mac" && e.GetProperty("linkId").ValueKind == JsonValueKind.Null);
        Assert.Contains(refusals, e => e.Str("operation") == "pair" && e.GetProperty("status").GetInt32() == 410);
    }

    [Fact]
    public async Task Each_refusal_reason_is_answered_and_logged()
    {
        var id = await Paired();
        var client = _api.Client();
        async Task Refused(PeerReply reply, string reason, HttpStatusCode status = HttpStatusCode.Unauthorized, bool signed = true)
        {
            Assert.Equal((status, reason), (reply.Status, reply.Reason));
            Assert.Equal("application/problem+json", reply.Response.Content.Headers.ContentType?.MediaType);
            if (signed) Assert.Null(reply.SignatureProblem);
            await Task.CompletedTask;
        }

        Assert.Equal(HttpStatusCode.OK, (await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog")).Status);

        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await Refused(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { Key = stranger, KeyId = Jwk(stranger) }), "unknown-key", signed: false);
        var unsigned = await client.GetAsync("/link/v1/catalog");
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);

        await Refused(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { CreatedOffset = -400 }), "clock-skew");
        await Refused(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { CreatedOffset = 310 }), "clock-skew");

        var nonce = LinkCrypto.NewNonce();
        Assert.Equal(HttpStatusCode.OK, (await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { Nonce = nonce })).Status);
        await Refused(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { Nonce = nonce }), "replay");

        var events = new JsonObject { ["events"] = new JsonArray() };
        await Refused(await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", events, new SignOptions { TamperBody = true }), "bad-digest");
        await Refused(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { Key = stranger }), "bad-signature");
        await Refused(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { SignedPath = "/link/v1/status" }), "bad-signature");
        await Refused(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog?x=1", o: new SignOptions { SignedQuery = "?x=2" }), "bad-signature");
        // The query is covered: signed as called, it passes.
        Assert.Equal(HttpStatusCode.OK, (await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog?x=1")).Status);

        await (await Alice()).PostJson($"/api/links/{id}/cut", new { confirm = true });
        await Refused(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog"), "link-cut", signed: false);

        var log = await Exchanges(id);
        foreach (var reason in new[] { "clock-skew", "replay", "bad-digest", "bad-signature", "link-cut", "link-not-confirmed" }.Where(r => r != "link-not-confirmed"))
            Assert.Contains(log, e => e.Str("reason") == reason && e.Str("result") == "refused" && e.Str("direction") == "in");
        Assert.Contains(await Exchanges(), e => e.Str("reason") == "unknown-key" && e.GetProperty("linkId").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Troubleshooting_tables_give_the_statuses_the_server_answers()
    {
        // The interop test with gestion-mep found link-cut documented as 410 and bad-digest as 400, while both are
        // answered 401 as the contract says. The tables in both languages must give the status actually answered.
        var id = await Paired();
        var client = _api.Client();
        var answered = new Dictionary<string, int>();
        void Seen(PeerReply reply) => answered[reply.Reason!] = (int)reply.Status;

        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Seen(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { Key = stranger, KeyId = Jwk(stranger) }));
        Seen(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { CreatedOffset = -400 }));
        Seen(await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", new JsonObject { ["events"] = new JsonArray() },
            new SignOptions { TamperBody = true }));
        Seen(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { Key = stranger }));
        var nonce = LinkCrypto.NewNonce();
        await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { Nonce = nonce });
        Seen(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { Nonce = nonce }));
        await (await Alice()).PostJson($"/api/links/{id}/cut", new { confirm = true });
        Seen(await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog"));
        Assert.Equal(["bad-digest", "bad-signature", "clock-skew", "link-cut", "replay", "unknown-key"], answered.Keys.Order(StringComparer.Ordinal));

        foreach (var doc in new[] { Path.Combine("en", "links.md"), Path.Combine("fr", "liaisons.md") })
        {
            var rows = File.ReadAllLines(Path.Combine(OpenApiTests.RepositoryRoot(), "docs", doc))
                .Select(line => System.Text.RegularExpressions.Regex.Match(line, @"^\| `([a-z-]+)` \| (\d{3}) \|"))
                .Where(m => m.Success).ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            foreach (var (reason, status) in answered)
                Assert.True(rows.TryGetValue(reason, out var documented) && documented == status,
                    $"{doc}: `{reason}` is answered {status}, documented {(rows.TryGetValue(reason, out var d) ? d : "nowhere")}.");
        }
    }

    private static string Jwk(ECDsa key) => LinkCrypto.Jwk.From(key).Kid();

    [Fact]
    public async Task Catalogue_holds_only_what_everyone_sees()
    {
        var id = await Paired();
        var reply = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/catalog");
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Null(reply.SignatureProblem);
        var catalog = reply.Payload!;
        var apps = catalog["applications"]!.AsArray().Select(a => a!["id"]!.GetValue<string>()).ToList();
        var flows = catalog["flows"]!.AsArray().Select(f => f!.AsObject()).ToList();
        Assert.Contains("billing", apps);
        // Fraud scoring is restricted: neither it nor its flows are sent.
        Assert.DoesNotContain("fraud", apps);
        Assert.All(flows, f => Assert.True(apps.Contains(f["fromId"]!.GetValue<string>()) && apps.Contains(f["toId"]!.GetValue<string>())));
        var allFlows = Db(db => db.Flows.Count());
        Assert.True(flows.Count < allFlows);
        var text = catalog.ToJsonString();
        Assert.DoesNotContain("protectedNotes", text, StringComparison.Ordinal);
        Assert.DoesNotContain("svc-billing-run", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dataObject", text, StringComparison.Ordinal);
        var billing = catalog["applications"]!.AsArray().Single(a => a!["id"]!.GetValue<string>() == "billing")!;
        Assert.Equal(("finance", "team-finance", "app", "live"), (billing["domainId"]!.GetValue<string>(), billing["teamId"]!.GetValue<string>(),
            billing["kind"]!.GetValue<string>(), billing["lifecycle"]!.GetValue<string>()));
        Assert.Equal("http://localhost/applications/billing", billing["url"]!.GetValue<string>());
        Assert.Equal(["applications", "baseUrl", "domains", "flows", "generatedAt", "instanceName", "teams"], catalog.Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.EndsWith("Z", catalog["generatedAt"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.NotEmpty(catalog["teams"]!.AsArray());

        // The same catalogue again: 304 with the ETag, signed.
        var etag = reply.Response.Headers.ETag!.Tag;
        var again = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/catalog", headers: r => r.Headers.TryAddWithoutValidation("If-None-Match", etag));
        Assert.Equal(HttpStatusCode.NotModified, again.Status);
        Assert.Null(again.SignatureProblem);

        // Restricting the CRM takes it and its flows out.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
            await db.Applications.Where(a => a.Id == "crm").ExecuteUpdateAsync(s => s.SetProperty(a => a.Visibility, Visibility.Restricted));
        }
        var after = (await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/catalog", headers: r => r.Headers.TryAddWithoutValidation("If-None-Match", etag))).Payload!;
        Assert.DoesNotContain(after["applications"]!.AsArray(), a => a!["id"]!.GetValue<string>() == "crm");
        Assert.DoesNotContain(after["flows"]!.AsArray(), f => f!["fromId"]!.GetValue<string>() == "crm" || f!["toId"]!.GetValue<string>() == "crm");

        // Without catalog:read, refused.
        await (await Alice()).PutJson($"/api/links/{id}/capabilities", new { capabilities = new[] { "notices:send" } });
        var refused = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/catalog");
        Assert.Equal((HttpStatusCode.Forbidden, "capability-missing"), (refused.Status, refused.Reason));
    }

    private static JsonObject Event(string id, string phase, string updatedAt, string[] apps, string? start = "2026-11-12T20:00:00Z", string? outcome = null) => new()
    {
        ["deploymentId"] = id, ["title"] = "Billing 4.3", ["phase"] = phase, ["outcome"] = outcome, ["postponed"] = false,
        ["plannedStart"] = start, ["plannedEnd"] = null, ["url"] = $"{FakePeer.Url}/meps/{id}",
        ["applicationIds"] = new JsonArray([.. apps.Select(a => (JsonNode)JsonValue.Create(a))]),
        ["environments"] = new JsonArray(new JsonObject { ["name"] = "Production", ["production"] = true }), ["updatedAt"] = updatedAt,
    };

    [Fact]
    public async Task Deployments_are_upserted_shown_on_records_and_kept_a_year_after_they_end()
    {
        var id = await Paired();
        var client = _api.Client();
        var mep = Guid.NewGuid().ToString();
        var reply = await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", new JsonObject
        {
            ["events"] = new JsonArray(Event(mep, "preparation", "2026-10-31T10:00:00Z", ["billing", "nope", "fraud"])),
        });
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Null(reply.SignatureProblem);
        Assert.Equal(1, reply.Payload!["accepted"]!.GetValue<int>());
        Assert.Equal(["fraud", "nope"], reply.Payload["ignoredApplicationIds"]!.AsArray().Select(a => a!.GetValue<string>()));

        var bob = await _api.SignedIn("bob");
        var billing = await bob.GetJson("/api/applications/billing");
        var upcoming = billing.GetProperty("deployments").GetProperty("upcoming").EnumerateArray().ToList();
        var mine = upcoming.Single(d => d.Str("deploymentId") == mep);
        Assert.Equal(("preparation", "MEP Alizé", true), (mine.Str("phase"), mine.GetProperty("source").Str("instanceName"), mine.GetProperty("source").GetProperty("linkActive").GetBoolean()));
        Assert.Equal(["billing"], mine.GetProperty("applicationIds").EnumerateArray().Select(a => a.GetString()));
        // A flow shows the deployments of its ends.
        var flow = await bob.GetJson("/api/flows/crm-to-billing-customers/deployments");
        Assert.Contains(flow.GetProperty("upcoming").EnumerateArray(), d => d.Str("deploymentId") == mep);

        // Same id: updated; an older event changes nothing.
        await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", new JsonObject
        {
            ["events"] = new JsonArray(Event(mep, "closed", "2026-11-13T01:00:00Z", ["billing"], outcome: "success-with-incidents"),
                Event(mep, "execution", "2026-11-12T21:00:00Z", ["billing"])),
        });
        Assert.Equal(1, Db(db => db.Deployments.Count(d => d.DeploymentId == mep)));
        var list = await bob.GetJson("/api/applications/billing/deployments");
        var past = list.GetProperty("past").EnumerateArray().Single(d => d.Str("deploymentId") == mep);
        Assert.Equal(("closed", "success-with-incidents"), (past.Str("phase"), past.Str("outcome")));

        // The application detail keeps the last three past deployments; the demo has one more on Billing.
        for (var i = 0; i < 3; i++)
            await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", new JsonObject
            {
                ["events"] = new JsonArray(Event(Guid.NewGuid().ToString(), "cancelled", "2026-09-01T10:00:00Z", ["billing"], start: $"2026-09-0{i + 1}T20:00:00Z")),
            });
        Assert.Equal(3, (await bob.GetJson("/api/applications/billing")).GetProperty("deployments").GetProperty("past").GetArrayLength());
        Assert.True((await bob.GetJson("/api/applications/billing/deployments")).GetProperty("past").GetArrayLength() > 3);

        // Invalid events are refused as a whole.
        var invalid = await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", new JsonObject { ["events"] = new JsonArray(new JsonObject { ["phase"] = "launch" }) });
        Assert.Equal((HttpStatusCode.BadRequest, "bad-body"), (invalid.Status, invalid.Reason));
        var plain = await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", o: new SignOptions { RawBody = "{\"events\":[]}"u8.ToArray() });
        Assert.Equal((HttpStatusCode.BadRequest, "bad-body"), (plain.Status, plain.Reason));

        // Restricted records: not readable by those who cannot see them.
        await (await _api.SignedIn("david")).GetJson("/api/applications/fraud/deployments", HttpStatusCode.Forbidden);
        await bob.GetJson("/api/applications/nope/deployments", HttpStatusCode.NotFound);

        // Retention: 365 days after the end.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
            await db.Deployments.Where(d => d.DeploymentId == mep).ExecuteUpdateAsync(s => s.SetProperty(d => d.EndedAt, DateTimeOffset.UtcNow.AddDays(-366)));
            var open = await db.Deployments.CountAsync(d => d.EndedAt == null);
            await LinkLog.Purge(db, DateTimeOffset.UtcNow);
            Assert.False(await db.Deployments.AnyAsync(d => d.DeploymentId == mep));
            Assert.Equal(open, await db.Deployments.CountAsync(d => d.EndedAt == null));
            Assert.True(await db.Deployments.AnyAsync(d => d.EndedAt != null));
        }

        // Without deployments:receive, refused.
        await (await Alice()).PutJson($"/api/links/{id}/capabilities", new { capabilities = new[] { "catalog:read" } });
        var refused = await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", new JsonObject { ["events"] = new JsonArray() });
        Assert.Equal((HttpStatusCode.Forbidden, "capability-missing"), (refused.Status, refused.Reason));
    }

    [Fact]
    public async Task Peer_key_renewal_keeps_the_old_signing_key_for_five_minutes()
    {
        var id = await Paired();
        var client = _api.Client();
        var before = (await (await Alice()).GetJson($"/api/links/{id}")).Str("fingerprint");
        var reply = await _peer.Rekey(client);
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Null(reply.SignatureProblem);

        var link = await (await Alice()).GetJson($"/api/links/{id}?refresh=false");
        Assert.NotEqual(before, link.Str("fingerprint"));
        Assert.Equal(_peer.Fingerprint(), link.Str("fingerprint"));
        Assert.NotEqual(JsonValueKind.Null, link.GetProperty("keysRenewedAt").ValueKind);
        // The new keys work; answers are encrypted to the new encryption key.
        Assert.Equal(HttpStatusCode.OK, (await _peer.Call(client, HttpMethod.Get, "/link/v1/catalog")).Status);
        // The old signing key still works during the grace period...
        var oldKid = Jwk(_peer.Previous!);
        Assert.Equal(HttpStatusCode.OK, (await _peer.Call(client, HttpMethod.Get, "/link/v1/status", o: new SignOptions { Key = _peer.Previous, KeyId = oldKid })).Status);
        // ...but not for another key renewal...
        var rekeyWithOld = await _peer.Call(client, HttpMethod.Post, "/link/v1/rekey",
            new JsonObject { ["signingKey"] = _peer.SigningJwk.ToJson(), ["encryptionKey"] = _peer.EncryptionJwk.ToJson() }, new SignOptions { Key = _peer.Previous, KeyId = oldKid });
        Assert.Equal((HttpStatusCode.Unauthorized, "bad-signature"), (rekeyWithOld.Status, rekeyWithOld.Reason));
        // ...and not after 5 minutes.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
            await db.Links.Where(l => l.Id == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.PeerPreviousUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        var late = await _peer.Call(client, HttpMethod.Get, "/link/v1/status", o: new SignOptions { Key = _peer.Previous, KeyId = oldKid });
        Assert.Equal((HttpStatusCode.Unauthorized, "unknown-key"), (late.Status, late.Reason));
    }

    [Fact]
    public async Task Noosphere_key_renewal_tells_the_peer_and_keeps_the_old_encryption_key_for_five_minutes()
    {
        var id = await Paired();
        var client = _api.Client();
        var oldEncryption = _peer.NoosphereEncryption!;
        var renewed = await (await Alice()).PostJson($"/api/links/{id}/renew-keys", new { });
        Assert.Single(_peer.Rekeys);
        Assert.Empty(_peer.Errors);
        Assert.Equal(_peer.Fingerprint(), renewed.Str("fingerprint"));
        Assert.Equal(_peer.NoosphereSigning!.Kid(), renewed.GetProperty("keys").Str("signing"));
        // Answers are now signed with the new key (the fake peer checks them with what it received).
        Assert.Null((await _peer.Call(client, HttpMethod.Get, "/link/v1/status")).SignatureProblem);

        // A body still encrypted to the old key decrypts during the grace period.
        var current = _peer.NoosphereEncryption;
        _peer.NoosphereEncryption = oldEncryption;
        Assert.Equal(HttpStatusCode.OK, (await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", new JsonObject { ["events"] = new JsonArray() })).Status);
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
            await db.Links.Where(l => l.Id == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.OwnPreviousUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", new JsonObject { ["events"] = new JsonArray() })).Status);
        _peer.NoosphereEncryption = current;
        Assert.Equal(HttpStatusCode.OK, (await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", new JsonObject { ["events"] = new JsonArray() })).Status);
        Assert.Contains(await Exchanges(id), e => e.Str("operation") == "rekey" && e.Str("direction") == "out" && e.Str("result") == "ok");
    }

    [Fact]
    public async Task Unpair_from_the_peer_cuts_the_link_and_keeps_the_deployments()
    {
        var id = await Paired();
        var client = _api.Client();
        var mep = Guid.NewGuid().ToString();
        await _peer.Call(client, HttpMethod.Post, "/link/v1/deployments", new JsonObject { ["events"] = new JsonArray(Event(mep, "preparation", "2026-10-31T10:00:00Z", ["billing"])) });

        var reply = await _peer.Call(client, HttpMethod.Post, "/link/v1/unpair");
        Assert.Equal(HttpStatusCode.NoContent, reply.Status);
        Assert.Null(reply.SignatureProblem);
        var link = Db(db => db.Links.Single(l => l.Id == id));
        Assert.Equal((LinkState.Cut, "peer"), (link.State, link.CutBy));
        Assert.Null(link.OwnSigningKey);
        Assert.Null(link.OwnEncryptionKey);
        Assert.Null(link.PeerSigningKey);
        Assert.Null(link.PeerEncryptionKey);

        var deployment = (await (await _api.SignedIn("bob")).GetJson("/api/applications/billing/deployments")).GetProperty("upcoming").EnumerateArray()
            .Single(d => d.Str("deploymentId") == mep);
        Assert.False(deployment.GetProperty("source").GetProperty("linkActive").GetBoolean());
        Assert.Equal($"{FakePeer.Url}/meps/{mep}", deployment.Str("url"));
        Assert.Equal("link-cut", (await _peer.Call(client, HttpMethod.Get, "/link/v1/status")).Reason);
        await (await Alice()).PostJson($"/api/links/{id}/confirm", new { fingerprint = link.Fingerprint }, HttpStatusCode.Conflict);
        Assert.Contains(await Exchanges(id), e => e.Str("operation") == "unpair" && e.Str("result") == "ok");
    }

    [Fact]
    public async Task Cutting_here_tells_the_peer()
    {
        var id = await Paired();
        await (await Alice()).PostJson($"/api/links/{id}/cut", new { confirm = false }, HttpStatusCode.BadRequest);
        var cut = await (await Alice()).PostJson($"/api/links/{id}/cut", new { confirm = true });
        Assert.Equal(("cut", "alice"), (cut.Str("state"), cut.Str("cutBy")));
        Assert.True(_peer.Unpaired);
        Assert.Empty(_peer.Errors);
    }

    [Fact]
    public async Task Changes_to_shared_records_are_noticed_to_the_peer()
    {
        await Paired();
        await RestrictingThePortalIsNoticedInFull();
        // The demo link is never called: its notices are logged as "demo".
        var demo = Db(db => db.Links.Single(l => l.Demo).Id);
        Assert.Contains(await Exchanges(demo), e => e.Str("operation") == "notices" && e.Str("result") == "demo");
    }

    [Fact]
    public async Task Writes_committed_before_the_confirmation_are_neither_noticed_nor_mixed_with_later_ones()
    {
        var id = await Paired();
        // Writes committed before the link was confirmed (the demo landscape loaded at startup, say) and not resolved
        // yet: the peer reads them with the catalogue. Here, the portal and its flows created, and billing.
        var confirmed = Db(db => db.Links.Single(l => l.Id == id).ConfirmedAt!.Value);
        var earlier = confirmed.AddSeconds(-1);
        var portalFlows = Db(db => db.Flows.Where(f => f.FromId == "portal" || f.ToId == "portal").Select(f => f.Id).ToList());
        _api.Services.GetRequiredService<LinkChangeFeed>().Enqueue(new ChangeBatch(earlier,
        [
            new ChangeCandidate(RecordKind.Application, "portal", true, earlier),
            new ChangeCandidate(RecordKind.Application, "billing", true, earlier),
            .. portalFlows.Select(f => new ChangeCandidate(RecordKind.Flow, f, true, earlier)),
        ]));
        await RestrictingThePortalIsNoticedInFull();
    }

    /// <summary>Restricts the portal: the first notice has a delete for it and for each of its shared flows, and nothing else.</summary>
    private async Task RestrictingThePortalIsNoticedInFull()
    {
        var alice = await Alice();
        var portal = await alice.GetJson("/api/applications/portal");
        // Restricting the portal: a delete for it and for its flows, in one notice.
        await alice.PutJson("/api/applications/portal", new
        {
            name = portal.Str("name"), domainId = portal.Str("domainId"), teamId = portal.Str("teamId"), kind = portal.Str("kind"),
            tech = portal.GetProperty("tech").GetString(), description = portal.GetProperty("description").GetString(), lifecycle = portal.Str("lifecycle"),
            visibility = "restricted", version = portal.GetProperty("version").GetInt32(),
        });
        var notice = await WaitForNotice();
        var changes = notice["changes"]!.AsArray().Select(c => c!.AsObject()).ToList();
        Assert.Contains(changes, c => c["kind"]!.GetValue<string>() == "application" && c["id"]!.GetValue<string>() == "portal" && c["operation"]!.GetValue<string>() == "delete");
        var portalFlows = Db(db => db.Flows.Where(f => (f.FromId == "portal" || f.ToId == "portal") && f.FromId != "fraud" && f.ToId != "fraud").Select(f => f.Id).ToList());
        Assert.NotEmpty(portalFlows);
        foreach (var f in portalFlows)
            Assert.Contains(changes, c => c["kind"]!.GetValue<string>() == "flow" && c["id"]!.GetValue<string>() == f && c["operation"]!.GetValue<string>() == "delete");
        Assert.DoesNotContain(changes, c => c["operation"]!.GetValue<string>() == "upsert");
        Assert.All(changes, c => Assert.EndsWith("Z", c["at"]!.GetValue<string>(), StringComparison.Ordinal));
        Assert.Empty(_peer.Errors);
    }

    [Fact]
    public async Task Changes_saved_in_a_transaction_are_released_once_it_commits()
    {
        var feed = new LinkChangeFeed();
        var options = new DbContextOptionsBuilder<NoosphereDbContext>()
            .UseNpgsql($"{ApiFixture.Server};Database={_api.DatabaseName}")
            .AddInterceptors(new LinkChangeInterceptor(feed, TimeProvider.System)).Options;
        async Task Rename(NoosphereDbContext db, string name)
        {
            (await db.Teams.FirstAsync(t => t.Id == "team-finance")).Name = name;
            await db.SaveChangesAsync();
        }

        // Without a transaction: released by the save.
        await using (var db = new NoosphereDbContext(options))
            await Rename(db, "Finance 1");
        Assert.Equal("team-finance", Assert.Single(Assert.Single(feed.Drain()).Candidates).Id);

        // In a transaction: every save of it, in one batch, once it commits.
        await using (var db = new NoosphereDbContext(options))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await Rename(db, "Finance 2");
            (await db.Applications.FirstAsync(a => a.Id == "billing")).Name = "Billing 2";
            await db.SaveChangesAsync();
            Assert.Empty(feed.Drain());
            await transaction.CommitAsync();
        }
        Assert.Equal([(RecordKind.Team, "team-finance"), (RecordKind.Application, "billing")],
            Assert.Single(feed.Drain()).Candidates.Select(c => (c.Kind, c.Id)));

        // Rolled back, or never committed: nothing.
        await using (var db = new NoosphereDbContext(options))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await Rename(db, "Finance 3");
            await transaction.RollbackAsync();
        }
        await using (var db = new NoosphereDbContext(options))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await Rename(db, "Finance 4");
        }
        Assert.Empty(feed.Drain());
        Assert.Equal("Finance 2", Db(db => db.Teams.Single(t => t.Id == "team-finance").Name));
    }

    private async Task<JsonObject> WaitForNotice()
    {
        for (var i = 0; i < 100; i++)
        {
            lock (_peer.Notices)
                if (_peer.Notices.Count > 0) return _peer.Notices[0];
            await Task.Delay(100);
        }
        throw new TimeoutException("No change notice received.");
    }

    [Fact]
    public async Task Notice_resolver_tells_upserts_from_deletes()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
        var now = DateTimeOffset.UtcNow;
        // Fraud scoring is restricted: its changes are never noticed; a flow of it neither.
        var fraudFlow = await db.Flows.FirstAsync(f => f.FromId == "fraud" || f.ToId == "fraud");
        var changes = await NoticeResolver.Resolve(db,
        [
            new ChangeCandidate(RecordKind.Application, "billing", false, now, Visibility.Everyone),
            new ChangeCandidate(RecordKind.Application, "fraud", false, now, Visibility.Restricted),
            new ChangeCandidate(RecordKind.Flow, fraudFlow.Id, false, now, null, false, fraudFlow.FromId, fraudFlow.ToId),
            new ChangeCandidate(RecordKind.Application, "gone", false, now, Visibility.Everyone),
            new ChangeCandidate(RecordKind.Application, "gone-restricted", false, now, Visibility.Restricted),
            new ChangeCandidate(RecordKind.Domain, "finance", false, now),
            new ChangeCandidate(RecordKind.Team, "team-gone", false, now),
        ]);
        Assert.Equal([("application", "gone", "delete"), ("team", "team-gone", "delete"), ("application", "billing", "upsert"), ("domain", "finance", "upsert")],
            changes.Select(c => (c.Kind, c.Id, c.Operation)).OrderBy(c => c.Operation, StringComparer.Ordinal).ThenBy(c => c.Kind, StringComparer.Ordinal));
        // Fraud scoring made visible to everyone: an upsert for it and its flows to shared applications.
        await db.Applications.Where(a => a.Id == "fraud").ExecuteUpdateAsync(s => s.SetProperty(a => a.Visibility, Visibility.Everyone));
        var opened = await NoticeResolver.Resolve(db, [new ChangeCandidate(RecordKind.Application, "fraud", false, now, Visibility.Restricted, true)]);
        Assert.Contains(opened, c => c.Kind == "flow" && c.Id == fraudFlow.Id && c.Operation == "upsert");
    }

    [Fact]
    public async Task Demo_link_and_deployments_are_seeded()
    {
        var alice = await Alice();
        var demo = (await alice.GetJson("/api/links")).EnumerateArray().Single(l => l.GetProperty("demo").GetBoolean());
        Assert.Equal(("active", "MEP Alizé", "gestion-mep"), (demo.Str("state"), demo.Str("instanceName"), demo.Str("product")));
        Assert.Matches("^[a-z]+ [a-z]+ [0-9]{4} [a-z]+$", demo.Str("fingerprint"));
        Assert.NotEmpty((await alice.GetJson($"/api/links/{demo.Str("id")}/exchanges")).GetProperty("items").EnumerateArray());
        var billing = (await alice.GetJson("/api/applications/billing")).GetProperty("deployments");
        Assert.Contains(billing.GetProperty("upcoming").EnumerateArray(), d => d.Str("title") == "Billing 4.2");
        Assert.Contains(billing.GetProperty("past").EnumerateArray(), d => d.Str("title") == "Billing 4.1");
        // Renewing the demo link's keys calls nobody: logged as "demo".
        var renewed = await alice.PostJson($"/api/links/{demo.Str("id")}/renew-keys", new { });
        Assert.NotEqual(demo.Str("fingerprint"), renewed.Str("fingerprint"));
        Assert.Contains((await alice.GetJson($"/api/links/{demo.Str("id")}/exchanges")).GetProperty("items").EnumerateArray(),
            e => e.Str("operation") == "rekey" && e.Str("result") == "demo");
    }

    [Fact]
    public async Task Get_record_markdown_lists_deployments()
    {
        var tokens = await AgentKit.Connect(_api, "bob");
        await using var mcp = await AgentKit.Mcp(_api, tokens.Access);
        var (error, text) = await AgentKit.Call(mcp, "get_record", new() { ["kind"] = "application", ["id"] = "billing" });
        Assert.False(error, text);
        Assert.Contains("## Deployments", text, StringComparison.Ordinal);
        Assert.Contains("Billing 4.2", text, StringComparison.Ordinal);
        Assert.Contains("From MEP Alizé (gestion-mep).", text, StringComparison.Ordinal);
        Assert.Contains("nextDeployment: ", text, StringComparison.Ordinal);
        var flow = await AgentKit.Call(mcp, "get_record", new() { ["kind"] = "flow", ["id"] = "crm-to-billing-customers" });
        Assert.Contains("## Deployments of its applications", flow.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Command_line_creates_codes_lists_confirms_and_cuts()
    {
        static async Task<(int Code, string Out, string Err)> Run(params string[] args)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var code = await Noosphere.Api.Cli.CommandLine.Run(args, stdout, stderr, ApiFixture.CliConfiguration());
            return (code, stdout.ToString(), stderr.ToString());
        }
        var created = await Run("link", "code", "--expires", "2h", "--capabilities", "catalog:read,deployments:receive", "--json");
        Assert.Equal(0, created.Code);
        var result = JsonDocument.Parse(created.Out).RootElement.GetProperty("result");
        var code = result.Str("code");
        Assert.Equal(["catalog:read", "deployments:receive"], result.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(2, (await Run("link", "code", "--expires", "2d")).Code);

        var answer = await _peer.Pair(_api.Client(), code);
        var id = answer["linkId"]!.GetValue<string>();
        var list = await Run("link", "list");
        Assert.Contains(id, list.Out, StringComparison.Ordinal);
        Assert.Contains(_peer.Fingerprint(), list.Out, StringComparison.Ordinal);
        Assert.Contains("MEP Alizé", JsonDocument.Parse((await Run("link", "list", "--json")).Out).RootElement.GetRawText(), StringComparison.Ordinal);

        Assert.Equal(1, (await Run("link", "confirm", id[..8], "--fingerprint", "abbey abbey 0000 abbey")).Code);
        Assert.Equal(2, (await Run("link", "confirm", id)).Code);
        Assert.Equal(5, (await Run("link", "confirm", Guid.NewGuid().ToString(), "--fingerprint", "x")).Code);
        Assert.Equal(0, (await Run("link", "confirm", id[..8], "--fingerprint", _peer.Fingerprint())).Code);
        Assert.Equal("active", Db(db => db.Links.Single(l => l.Id == Guid.Parse(id)).State.ToString().ToLowerInvariant()));

        Assert.Equal(4, (await Run("link", "cut", id)).Code);
        // The command line has no route to the fake peer: the cut is done here anyway, and says the peer was not told.
        var cut = await Run("link", "cut", id, "--yes");
        Assert.Equal(0, cut.Code);
        Assert.Contains("could not be told", cut.Out, StringComparison.Ordinal);
        Assert.Equal(LinkState.Cut, Db(db => db.Links.Single(l => l.Id == Guid.Parse(id)).State));
        Assert.Contains("link code", (await Run("help")).Out, StringComparison.Ordinal);
    }
}

/// <summary>NOOSPHERE_LINK_BOOTSTRAP_CODE, and a Noosphere behind a reverse proxy with a path prefix (PUBLIC_URL).</summary>
[Collection("Api")]
public sealed class LinkBootstrapTests : IAsyncLifetime, IDisposable
{
    private static readonly string Code = "MEPA" + "-LIZE-2026-BOOT";
    private FakePeer _peer = null!;
    private ApiFixture _api = null!;

    public async Task InitializeAsync()
    {
        _peer = await FakePeer.Start();
        _api = new ApiFixture(demo: false, settings: new Dictionary<string, string>
        {
            ["LINK_BOOTSTRAP_CODE"] = Code, ["PUBLIC_URL"] = "https://docs.example/noosphere", ["ADMIN_PASSWORD"] = "admin-password-1",
        }, host: b => b.ConfigureTestServices(_peer.Plug));
    }

    public async Task DisposeAsync() => await _peer.DisposeAsync();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Bootstrap_code_pairs_once_and_the_proxy_prefix_is_signed()
    {
        var answer = await _peer.Pair(_api.Client(), Code);
        Assert.Equal("https://docs.example/noosphere", answer["peer"]!["url"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict, (await _peer.PairRaw(_api.Client(), Code)).StatusCode);
        var admin = await _api.SignedIn("admin", "admin-password-1");
        var id = answer["linkId"]!.GetValue<string>();
        var link = await admin.GetJson($"/api/links/{id}");
        Assert.Equal("bootstrap", link.Str("pairedBy"));
        // Admins still confirm the fingerprint.
        Assert.Equal("pending", link.Str("state"));
        await admin.PostJson($"/api/links/{id}/confirm", new { fingerprint = _peer.Fingerprint() });

        // gestion-mep calls https://docs.example/noosphere/link/v1/catalog: it signs the full path; the proxy strips the prefix.
        var reply = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { SignedPath = "/noosphere/link/v1/catalog" });
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal("https://docs.example/noosphere", reply.Payload!["baseUrl"]!.GetValue<string>());
        // The path without the prefix is accepted too.
        Assert.Equal(HttpStatusCode.OK, (await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/catalog")).Status);
        Assert.Equal("bad-signature", (await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/catalog", o: new SignOptions { SignedPath = "/other/link/v1/catalog" })).Reason);
    }
}
