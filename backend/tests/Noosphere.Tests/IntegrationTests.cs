using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Noosphere.Api.Data;
using Noosphere.Api.Integrations;
using Noosphere.Api.Links;
using Noosphere.Domain;

namespace Noosphere.Tests;

/// <summary>Resolves the test host names: hooks.test is public, internal.test private, anything else unknown.</summary>
internal sealed class FakeResolver : IWebhookResolver
{
    public Dictionary<string, IPAddress[]> Hosts { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hooks.test"] = [IPAddress.Parse("203.0.113.10")],
        ["internal.test"] = [IPAddress.Parse("10.0.0.5")],
        ["mixed.test"] = [IPAddress.Parse("203.0.113.11"), IPAddress.Parse("169.254.169.254")],
    };

    public Task<IPAddress[]> Resolve(string host, CancellationToken cancel) =>
        Hosts.TryGetValue(host, out var a) ? Task.FromResult(a) : throw new SocketException((int)SocketError.HostNotFound);
}

/// <summary>A webhook receiver on an in-process server: records every delivery and answers <see cref="Status"/>.</summary>
internal sealed class FakeReceiver : IAsyncDisposable
{
    public sealed record Received(string Path, Dictionary<string, string> Headers, byte[] Body)
    {
        public JsonObject Json => JsonNode.Parse(Body)!.AsObject();
    }

    public List<Received> Deliveries { get; } = [];
    public int Status { get; set; } = 200;
    private WebApplication? _app;

    public static async Task<FakeReceiver> Start()
    {
        var receiver = new FakeReceiver();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapPost("/{**path}", (Delegate)(async (HttpContext ctx) =>
        {
            using var buffer = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(buffer);
            lock (receiver.Deliveries)
                receiver.Deliveries.Add(new Received(ctx.Request.Path, ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase), buffer.ToArray()));
            ctx.Response.StatusCode = receiver.Status;
        }));
        await app.StartAsync();
        receiver._app = app;
        return receiver;
    }

    public void Plug(IServiceCollection services, FakeResolver resolver)
    {
        services.AddSingleton<IWebhookResolver>(resolver);
        services.AddHttpClient(Webhooks.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _app!.GetTestServer().CreateHandler());
    }

    public List<Received> Of(string type)
    {
        lock (Deliveries) return [.. Deliveries.Where(d => d.Headers.GetValueOrDefault(Webhooks.EventHeader) == type)];
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }
}

/// <summary>The simple connection for third-party tools: API keys, the integration endpoints and outgoing webhooks.</summary>
[Collection("Api")]
public sealed class IntegrationTests : IAsyncLifetime, IDisposable
{
    private FakeReceiver _receiver = null!;
    private readonly FakeResolver _resolver = new();
    private ApiFixture _api = null!;

    public async Task InitializeAsync()
    {
        _receiver = await FakeReceiver.Start();
        _api = new ApiFixture(host: b => b.ConfigureTestServices(s => _receiver.Plug(s, _resolver)));
    }

    public async Task DisposeAsync() => await _receiver.DisposeAsync();

    public void Dispose() => _api.Dispose();

    private Task<HttpClient> Alice() => _api.SignedIn("alice");

    private T Db<T>(Func<NoosphereDbContext, T> read)
    {
        using var scope = _api.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<NoosphereDbContext>());
    }

    private async Task<(string Id, string Key)> NewKey(params string[] scopes)
    {
        var created = await (await Alice()).PostJson("/api/integrations/keys", new { name = "Release pipeline", scopes, expiresInDays = 90 }, HttpStatusCode.Created);
        return (created.GetProperty("apiKey").Str("id"), created.Str("key"));
    }

    /// <summary>A client with no session and no CSRF header, as a CI job is.</summary>
    private HttpClient Tool(string? key)
    {
        var client = _api.ClientWithoutHeader();
        if (key is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpClient client, HttpMethod method, string url, object? body = null, string? idempotency = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (idempotency is not null) request.Headers.Add("Idempotency-Key", idempotency);
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static object Events(string id, string updatedAt, string phase = "preparation", params string[] apps) => new
    {
        events = new[]
        {
            new
            {
                deploymentId = id, title = "Billing 4.2", phase, outcome = (string?)null, postponed = false, plannedStart = "2026-11-12T20:00:00Z",
                plannedEnd = (string?)null, url = $"https://ci.example/runs/{id}", applicationIds = apps.Length == 0 ? new[] { "billing" } : apps,
                environments = new[] { new { name = "Production", production = true } }, updatedAt,
            },
        },
    };

    [Fact]
    public async Task Api_keys_are_shown_once_stored_hashed_scoped_and_revocable()
    {
        await (await _api.SignedIn("bob")).PostJson("/api/integrations/keys", new { name = "x", scopes = new[] { "catalog:read" }, expiresInDays = 90 }, HttpStatusCode.Forbidden);
        var alice = await Alice();
        var invalid = await alice.PostJson("/api/integrations/keys", new { name = "", scopes = new[] { "everything" }, expiresInDays = 0 }, HttpStatusCode.BadRequest);
        Assert.True(invalid.GetProperty("errors").TryGetProperty("scopes", out _));
        Assert.True(invalid.GetProperty("errors").TryGetProperty("expiresInDays", out _));
        // A key always expires, after 90, 180 or 365 days.
        foreach (var days in new int?[] { null, 30, 3650 })
        {
            var refused = await alice.PostJson("/api/integrations/keys", new { name = "x", scopes = new[] { "catalog:read" }, expiresInDays = days }, HttpStatusCode.BadRequest);
            Assert.True(refused.GetProperty("errors").TryGetProperty("expiresInDays", out _));
        }

        var (id, key) = await NewKey("catalog:read");
        Assert.Matches("^nsk_[a-z2-9]{8}_[a-z2-9]{40}$", key);
        var stored = Db(db => db.ApiKeys.Single(k => k.Id == Guid.Parse(id)));
        Assert.Equal(key[..12], stored.Prefix);
        Assert.Equal(ApiKeys.Hash(key), stored.SecretHash);
        Assert.DoesNotContain(key[13..], JsonSerializer.Serialize(stored), StringComparison.Ordinal);
        var list = await alice.GetJson("/api/integrations/keys");
        Assert.DoesNotContain(key, list.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(list.EnumerateArray(), k => k.Str("id") == id && k.Str("state") == "active" && k.GetProperty("lastUsedAt").ValueKind == JsonValueKind.Null);

        // No key, a wrong key, a session cookie: refused.
        var none = await Tool(null).GetAsync("/api/integrations/catalog");
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.StartsWith("Bearer", none.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.Equal("https://noosphere.dev/problems/invalid-api-key", JsonNode.Parse(await none.Content.ReadAsStringAsync())!["type"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Tool(key[..^1] + (key[^1] == 'a' ? 'b' : 'a')).GetAsync("/api/integrations/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/integrations/catalog")).StatusCode);
        // A key never opens the rest of the API.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Tool(key).GetAsync("/api/applications")).StatusCode);

        var (status, catalog) = await Send(Tool(key), HttpMethod.Get, "/api/integrations/catalog");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEmpty(catalog.GetProperty("applications").EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, (await alice.GetJson("/api/integrations/keys")).EnumerateArray().Single(k => k.Str("id") == id).GetProperty("lastUsedAt").ValueKind);

        // Missing scope: 403.
        var (forbidden, problem) = await Send(Tool(key), HttpMethod.Post, "/api/integrations/deployments", Events("run-1", "2026-11-02T10:00:00Z"));
        Assert.Equal((HttpStatusCode.Forbidden, "https://noosphere.dev/problems/scope-missing"), (forbidden, problem.Str("type")));

        // Expired, then revoked.
        using (var scope = _api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<NoosphereDbContext>().ApiKeys.Where(k => k.Id == Guid.Parse(id))
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal("https://noosphere.dev/problems/api-key-expired", (await Send(Tool(key), HttpMethod.Get, "/api/integrations/catalog")).Body.Str("type"));
        var revoked = await alice.PostJson($"/api/integrations/keys/{id}/revoke", new { });
        Assert.Equal(("revoked", "alice"), (revoked.Str("state"), revoked.Str("revokedBy")));
        Assert.Equal("https://noosphere.dev/problems/api-key-revoked", (await Send(Tool(key), HttpMethod.Get, "/api/integrations/catalog")).Body.Str("type"));

        // The exchange log is shared with links.
        var log = (await alice.GetJson($"/api/integrations/keys/{id}/exchanges")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(log, e => e.Str("operation") == "catalog" && e.Str("result") == "ok");
        Assert.Contains(log, e => e.Str("reason") == "scope-missing");
        Assert.Contains(log, e => e.Str("reason") == "api-key-revoked");
        var all = (await alice.GetJson("/api/links/exchanges?limit=200")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(all, e => e.Str("reason") == "invalid-api-key" && e.GetProperty("apiKeyId").ValueKind == JsonValueKind.Null);
        Assert.Contains(all, e => e.GetProperty("apiKeyId").ValueKind == JsonValueKind.String && e.Str("apiKeyId") == id);
    }

    [Fact]
    public async Task Catalog_pages_cover_the_shared_catalogue_once()
    {
        var (_, key) = await NewKey("catalog:read");
        var client = Tool(key);
        var apps = new List<string>();
        var flows = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var (status, page) = await Send(client, HttpMethod.Get, "/api/integrations/catalog?limit=6" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.NotEmpty(page.GetProperty("domains").EnumerateArray());
            Assert.NotEmpty(page.GetProperty("teams").EnumerateArray());
            apps.AddRange(page.GetProperty("applications").Ids());
            flows.AddRange(page.GetProperty("flows").Ids());
            cursor = page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null && pages < 20);
        Assert.True(pages > 1);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
        var catalog = await LinkCatalog.Build(db, "x", "http://localhost", DateTimeOffset.UtcNow);
        Assert.Equal(catalog.Applications.Select(a => a.Id), apps);
        Assert.Equal(catalog.Flows.Select(f => f.Id).Order(StringComparer.Ordinal), flows.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("fraud", apps);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, HttpMethod.Get, "/api/integrations/catalog?limit=0")).Status);
    }

    [Fact]
    public async Task Deployments_sent_with_a_key_are_validated_idempotent_and_shown_on_records()
    {
        var (id, key) = await NewKey("deployments:write");
        var client = Tool(key);

        var (bad, problem) = await Send(client, HttpMethod.Post, "/api/integrations/deployments", new { events = new[] { new { deploymentId = "", phase = "launch" } } });
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        Assert.True(problem.GetProperty("errors").TryGetProperty("events[0].phase", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(client, HttpMethod.Post, "/api/integrations/deployments", "not json")).Status);

        // No CSRF header and no session: accepted with the key.
        var (ok, receipt) = await Send(client, HttpMethod.Post, "/api/integrations/deployments", Events("run-1", "2026-11-02T10:00:00Z", "preparation", "billing", "nowhere"), "req-1");
        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.Equal(1, receipt.GetProperty("accepted").GetInt32());
        Assert.Equal(["nowhere"], receipt.GetProperty("ignoredApplicationIds").EnumerateArray().Select(a => a.GetString()));

        // The same key and body: the same answer, nothing done twice. Another body with the same key: refused.
        using (var replay = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/deployments"))
        {
            replay.Content = new StringContent(JsonSerializer.Serialize(Events("run-1", "2026-11-02T10:00:00Z", "preparation", "billing", "nowhere")), Encoding.UTF8, "application/json");
            replay.Headers.Add("Idempotency-Key", "req-1");
            var response = await client.SendAsync(replay);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("true", response.Headers.GetValues("Idempotent-Replayed").Single());
            Assert.Equal(1, JsonNode.Parse(await response.Content.ReadAsStringAsync())!["accepted"]!.GetValue<int>());
        }
        var (reused, reusedProblem) = await Send(client, HttpMethod.Post, "/api/integrations/deployments", Events("run-1", "2026-11-02T11:00:00Z", "closed"), "req-1");
        Assert.Equal(((HttpStatusCode)422, "https://noosphere.dev/problems/idempotency-key-reused"), (reused, reusedProblem.Str("type")));

        var deployment = (await (await _api.SignedIn("bob")).GetJson("/api/applications/billing/deployments")).GetProperty("upcoming").EnumerateArray()
            .Single(d => d.Str("deploymentId") == "run-1");
        var source = deployment.GetProperty("source");
        Assert.Equal(("apiKey", "api-key", "Release pipeline", id, true), (source.Str("kind"), source.Str("product"), source.Str("instanceName"), source.Str("linkId"), source.GetProperty("linkActive").GetBoolean()));
        Assert.Equal(1, Db(db => db.Deployments.Count(d => d.ApiKeyId == Guid.Parse(id))));

        // Upserted by deployment id: a later event closes it; an older one changes nothing.
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, "/api/integrations/deployments", Events("run-1", "2026-11-02T12:00:00Z", "closed"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, "/api/integrations/deployments", Events("run-1", "2026-11-02T09:00:00Z", "execution"))).Status);
        Assert.Equal(DeploymentPhase.Closed, Db(db => db.Deployments.Single(d => d.ApiKeyId == Guid.Parse(id)).Phase));

        // Revoked: the deployment stays, shown as from a revoked key.
        await (await Alice()).PostJson($"/api/integrations/keys/{id}/revoke", new { });
        var past = (await (await _api.SignedIn("bob")).GetJson("/api/applications/billing/deployments")).GetProperty("past").EnumerateArray().Single(d => d.Str("deploymentId") == "run-1");
        Assert.False(past.GetProperty("source").GetProperty("linkActive").GetBoolean());
    }

    [Fact]
    public void Private_link_local_and_loopback_addresses_are_recognised()
    {
        foreach (var a in new[] { "127.0.0.1", "10.1.2.3", "172.16.0.1", "172.31.255.255", "192.168.1.1", "169.254.169.254", "100.64.0.1", "0.0.0.0", "224.0.0.1",
                     "::1", "::", "fe80::1", "fd00::1", "fc00::1", "ff02::1", "::ffff:127.0.0.1", "::ffff:10.0.0.1" })
            Assert.True(WebhookAddresses.IsPrivate(IPAddress.Parse(a)), a);
        foreach (var a in new[] { "203.0.113.10", "8.8.8.8", "172.32.0.1", "2001:4860:4860::8888", "::ffff:8.8.8.8" })
            Assert.False(WebhookAddresses.IsPrivate(IPAddress.Parse(a)), a);
    }

    [Fact]
    public async Task Webhook_urls_refuse_private_addresses_unless_an_administrator_allows_them()
    {
        var alice = await Alice();
        await (await _api.SignedIn("bob")).PostJson("/api/integrations/webhooks", new { name = "x", url = "https://hooks.test/in", events = new[] { "catalog.changed" } }, HttpStatusCode.Forbidden);
        foreach (var url in new[] { "https://127.0.0.1/in", "https://localhost/in", "https://[::1]/in", "https://10.0.0.8/in", "https://169.254.169.254/latest",
                     "https://internal.test/in", "https://mixed.test/in", "http://hooks.test/in", "https://user:pw@hooks.test/in", "https://unknown.test/in", "ftp://hooks.test/in" })
        {
            var refused = await alice.PostJson("/api/integrations/webhooks", new { name = "Hook", url, events = new[] { "catalog.changed" } }, HttpStatusCode.BadRequest);
            Assert.True(refused.GetProperty("errors").TryGetProperty("url", out _), url);
        }
        var noEvents = await alice.PostJson("/api/integrations/webhooks", new { name = "Hook", url = "https://hooks.test/in", events = new[] { "everything" } }, HttpStatusCode.BadRequest);
        Assert.True(noEvents.GetProperty("errors").TryGetProperty("events", out _));

        var settings = await alice.GetJson("/api/integrations/settings");
        Assert.False(settings.GetProperty("allowPrivateAddresses").GetBoolean());
        settings = await alice.PutJson("/api/integrations/settings", new { allowPrivateAddresses = true });
        Assert.Equal((true, "alice"), (settings.GetProperty("allowPrivateAddresses").GetBoolean(), settings.Str("updatedBy")));
        await alice.PostJson("/api/integrations/webhooks", new { name = "Internal", url = "http://internal.test/in", events = new[] { "catalog.changed" } }, HttpStatusCode.Created);
        await alice.PutJson("/api/integrations/settings", new { allowPrivateAddresses = false });

        // Checked again at delivery: an internal webhook is not called once private addresses are refused.
        var hook = Db(db => db.Webhooks.Single(w => w.Name == "Internal"));
        var test = await alice.PostJson($"/api/integrations/webhooks/{hook.Id}/test", new { });
        Assert.Equal("failed", test.Str("state"));
        Assert.Contains("private", test.Str("error"), StringComparison.Ordinal);
        Assert.Empty(_receiver.Deliveries);
    }

    [Fact]
    public async Task Webhooks_receive_signed_events_and_retry_with_backoff()
    {
        var alice = await Alice();
        var created = await alice.PostJson("/api/integrations/webhooks",
            new { name = "ITSM", url = "https://hooks.test/noosphere", events = new[] { "catalog.changed", "deployment.updated" } }, HttpStatusCode.Created);
        var secret = created.Str("secret");
        var id = created.GetProperty("webhook").Str("id");
        Assert.StartsWith("whsec_", secret, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, (await alice.GetJson("/api/integrations/webhooks")).GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, Db(db => db.Webhooks.Single(w => w.Id == Guid.Parse(id)).ProtectedSecret), StringComparison.Ordinal);

        // The test button: a signed ping, delivered at once.
        var test = await alice.PostJson($"/api/integrations/webhooks/{id}/test", new { });
        Assert.Equal(("delivered", 200, true), (test.Str("state"), test.GetProperty("responseStatus").GetInt32(), test.GetProperty("test").GetBoolean()));
        var ping = Assert.Single(_receiver.Of("ping"));
        Assert.Equal("/noosphere", ping.Path);
        AssertSigned(ping, secret);
        Assert.Equal(test.Str("eventId"), ping.Headers[Webhooks.DeliveryHeader]);
        Assert.Equal(("ping", test.Str("eventId")), (ping.Json["type"]!.GetValue<string>(), ping.Json["id"]!.GetValue<string>()));

        // A catalogue change: a catalog.changed event with the changes.
        var team = await alice.GetJson("/api/teams/team-crm");
        await alice.PutJson("/api/teams/team-crm", new { name = "CRM squad", contact = team.GetProperty("contact").GetString(), description = team.GetProperty("description").GetString(), version = team.GetProperty("version").GetInt32() });
        var changed = await WaitFor("catalog.changed");
        AssertSigned(changed, secret);
        Assert.Contains(changed.Json["data"]!["changes"]!.AsArray(), c => c!["kind"]!.GetValue<string>() == "team" && c["id"]!.GetValue<string>() == "team-crm");

        // A deployment received with a key: deployment.updated. The receiver fails: retried after 1 min, then delivered.
        _receiver.Status = 500;
        var (_, key) = await NewKey("deployments:write");
        Assert.Equal(HttpStatusCode.OK, (await Send(Tool(key), HttpMethod.Post, "/api/integrations/deployments", Events("run-9", "2026-11-02T10:00:00Z"))).Status);
        var first = await WaitFor("deployment.updated");
        var delivery = await WaitForDelivery(id, d => d.Str("eventType") == "deployment.updated" && d.GetProperty("attempts").GetInt32() == 1);
        Assert.Equal(("pending", 500), (delivery.Str("state"), delivery.GetProperty("responseStatus").GetInt32()));
        var next = delivery.GetProperty("nextAttemptAt").GetDateTimeOffset() - delivery.GetProperty("lastAttemptAt").GetDateTimeOffset();
        Assert.InRange(next.TotalSeconds, 59, 61);
        var data = first.Json["data"]!["deployments"]!.AsArray().Single()!;
        Assert.Equal(("run-9", "apiKey", "Release pipeline"), (data["deploymentId"]!.GetValue<string>(), data["source"]!["kind"]!.GetValue<string>(), data["source"]!["name"]!.GetValue<string>()));

        _receiver.Status = 204;
        using (var scope = _api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<NoosphereDbContext>().WebhookDeliveries.Where(d => d.Id == delivery.GetProperty("id").GetInt64())
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.NextAttemptAt, DateTimeOffset.UtcNow));
        var retried = await WaitForDelivery(id, d => d.GetProperty("id").GetInt64() == delivery.GetProperty("id").GetInt64() && d.Str("state") == "delivered");
        Assert.Equal(2, retried.GetProperty("attempts").GetInt32());
        var attempts = _receiver.Of("deployment.updated");
        Assert.Equal(2, attempts.Count);
        Assert.Equal(attempts[0].Headers[Webhooks.DeliveryHeader], attempts[1].Headers[Webhooks.DeliveryHeader]);

        // The exchange log has every attempt.
        var log = (await alice.GetJson("/api/links/exchanges?limit=200")).GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("webhookId").ValueKind == JsonValueKind.String && e.Str("webhookId") == id).ToList();
        Assert.Contains(log, e => e.Str("operation") == "webhook" && e.Str("result") == "failed" && e.GetProperty("status").GetInt32() == 500);
        Assert.Contains(log, e => e.Str("operation") == "webhook" && e.Str("result") == "ok");

        // Disabled: nothing more is queued. Deleted: gone.
        var hook = await alice.GetJson($"/api/integrations/webhooks/{id}");
        await alice.PutJson($"/api/integrations/webhooks/{id}", new { name = "ITSM", url = "https://hooks.test/noosphere", events = new[] { "catalog.changed" }, enabled = false, version = hook.GetProperty("version").GetInt32() });
        await alice.PutJson($"/api/integrations/webhooks/{id}", new { name = "ITSM", url = "https://hooks.test/noosphere", events = new[] { "catalog.changed" }, version = 1 }, HttpStatusCode.Conflict);
        using (var scope = _api.Services.CreateScope())
            Assert.Equal(0, await Webhooks.Enqueue(scope.ServiceProvider.GetRequiredService<NoosphereDbContext>(), Webhooks.CatalogChanged, new JsonObject(), DateTimeOffset.UtcNow));
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/integrations/webhooks/{id}")).StatusCode);
        await alice.GetJson($"/api/integrations/webhooks/{id}", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rotating_a_key_shows_a_new_one_once_and_keeps_the_old_one_for_24_hours()
    {
        var alice = await Alice();
        var (id, key) = await NewKey("deployments:write");
        Assert.Equal(HttpStatusCode.OK, (await Send(Tool(key), HttpMethod.Post, "/api/integrations/deployments", Events("run-r", "2026-11-02T10:00:00Z"))).Status);

        var rotated = await alice.PostJson($"/api/integrations/keys/{id}/rotate", new { }, HttpStatusCode.Created);
        var newKey = rotated.Str("key");
        var newId = rotated.GetProperty("apiKey").Str("id");
        Assert.NotEqual(key, newKey);
        Assert.Equal(("Release pipeline", "deployments:write"), (rotated.GetProperty("apiKey").Str("name"), rotated.GetProperty("apiKey").GetProperty("scopes")[0].GetString()));
        Assert.InRange((rotated.GetProperty("apiKey").GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalDays, 89, 90.1);

        var old = (await alice.GetJson("/api/integrations/keys")).EnumerateArray().Single(k => k.Str("id") == id);
        Assert.Equal(("active", newId), (old.Str("state"), old.Str("replacedById")));
        Assert.InRange((old.GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalHours, 23.9, 24.1);

        // Both keys work during the grace period, and the tool's deployments stay one list under the new key.
        Assert.Equal(HttpStatusCode.OK, (await Send(Tool(key), HttpMethod.Post, "/api/integrations/deployments", Events("run-r", "2026-11-02T11:00:00Z", "execution"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(Tool(newKey), HttpMethod.Post, "/api/integrations/deployments", Events("run-r", "2026-11-02T12:00:00Z", "closed"))).Status);
        Assert.Equal((0, 1, DeploymentPhase.Closed), Db(db => (db.Deployments.Count(d => d.ApiKeyId == Guid.Parse(id)), db.Deployments.Count(d => d.ApiKeyId == Guid.Parse(newId)),
            db.Deployments.Single(d => d.ApiKeyId == Guid.Parse(newId)).Phase)));

        // Rotated once: the old key cannot be rotated again; a revoked key neither.
        await alice.PostJson($"/api/integrations/keys/{id}/rotate", new { }, HttpStatusCode.Conflict);
        await alice.PostJson($"/api/integrations/keys/{newId}/revoke", new { });
        await alice.PostJson($"/api/integrations/keys/{newId}/rotate", new { }, HttpStatusCode.Conflict);
        await alice.PostJson($"/api/integrations/keys/{Guid.NewGuid()}/rotate", new { }, HttpStatusCode.NotFound);

        // The exchanges of a tool: its keys and webhooks together.
        var log = (await alice.GetJson($"/api/integrations/exchanges?apiKeyId={id}&apiKeyId={newId}")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, log.Count(e => e.Str("operation") == "deployments"));
        Assert.Contains(log, e => e.Str("apiKeyId") == newId);
        await alice.GetJson("/api/integrations/exchanges", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhooks_regenerate_their_secret_retry_on_demand_and_pause_after_a_day_of_failures()
    {
        var alice = await Alice();
        var created = await alice.PostJson("/api/integrations/webhooks", new { name = "ITSM", url = "https://hooks.test/n", events = new[] { "catalog.changed" } }, HttpStatusCode.Created);
        var id = created.GetProperty("webhook").Str("id");
        var regenerated = await alice.PostJson($"/api/integrations/webhooks/{id}/secret", new { });
        var secret = regenerated.Str("secret");
        Assert.NotEqual(created.Str("secret"), secret);
        await alice.PostJson($"/api/integrations/webhooks/{id}/test", new { });
        AssertSigned(Assert.Single(_receiver.Of("ping")), secret);

        // A failing receiver: the delivery waits; "retry now" makes one more attempt at once.
        _receiver.Status = 503;
        long deliveryId;
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
            var delivery = new WebhookDelivery
            {
                WebhookId = Guid.Parse(id), EventType = Webhooks.CatalogChanged, Payload = "{}", CreatedAt = DateTimeOffset.UtcNow, State = WebhookDeliveryState.Pending,
                Attempts = 2, NextAttemptAt = DateTimeOffset.UtcNow.AddHours(1),
            };
            db.WebhookDeliveries.Add(delivery);
            await db.SaveChangesAsync();
            deliveryId = delivery.Id;
        }
        var retried = await alice.PostJson($"/api/integrations/webhooks/{id}/deliveries/{deliveryId}/retry", new { });
        Assert.Equal(("pending", 3, 503), (retried.Str("state"), retried.GetProperty("attempts").GetInt32(), retried.GetProperty("responseStatus").GetInt32()));
        var hook = await alice.GetJson($"/api/integrations/webhooks/{id}");
        Assert.NotEqual(JsonValueKind.Null, hook.GetProperty("failingSince").ValueKind);
        Assert.Equal(JsonValueKind.Null, hook.GetProperty("pausedAt").ValueKind);

        // Failing for more than 24 hours: the next failure pauses it, and its waiting deliveries stop.
        using (var scope = _api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<NoosphereDbContext>().Webhooks.Where(w => w.Id == Guid.Parse(id))
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.FailingSince, DateTimeOffset.UtcNow.AddHours(-25)));
        retried = await alice.PostJson($"/api/integrations/webhooks/{id}/deliveries/{deliveryId}/retry", new { });
        Assert.Equal("failed", retried.Str("state"));
        hook = await alice.GetJson($"/api/integrations/webhooks/{id}");
        Assert.NotEqual(JsonValueKind.Null, hook.GetProperty("pausedAt").ValueKind);
        Assert.Equal(0, hook.GetProperty("pending").GetInt32());
        using (var scope = _api.Services.CreateScope())
            Assert.Equal(0, await Webhooks.Enqueue(scope.ServiceProvider.GetRequiredService<NoosphereDbContext>(), Webhooks.CatalogChanged, new JsonObject(), DateTimeOffset.UtcNow));
        await alice.PostJson($"/api/integrations/webhooks/{id}/deliveries/{deliveryId}/retry", new { }, HttpStatusCode.Conflict);
        var log = (await alice.GetJson($"/api/integrations/exchanges?webhookId={id}")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(log, e => e.Str("reason") == "webhook-paused");

        // Resumed by switching it on again: a fresh start.
        hook = await alice.PutJson($"/api/integrations/webhooks/{id}", new { name = "ITSM", url = "https://hooks.test/n", events = new[] { "catalog.changed" }, enabled = true, version = hook.GetProperty("version").GetInt32() });
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (hook.GetProperty("pausedAt").ValueKind, hook.GetProperty("failingSince").ValueKind));
        _receiver.Status = 200;
        Assert.Equal("delivered", (await alice.PostJson($"/api/integrations/webhooks/{id}/deliveries/{deliveryId}/retry", new { })).Str("state"));
        await alice.PostJson($"/api/integrations/webhooks/{id}/deliveries/{deliveryId}/retry", new { }, HttpStatusCode.Conflict);
    }

    [Fact]
    public void Signatures_follow_the_documented_format()
    {
        var body = Encoding.UTF8.GetBytes("{\"id\":\"1\"}");
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("s3cr" + "et"), Encoding.UTF8.GetBytes("1700000000.{\"id\":\"1\"}")));
        Assert.Equal($"t=1700000000,v1={expected}", Webhooks.Signature("s3cr" + "et", 1700000000, body));
    }

    /// <summary>Verifies a delivery as a receiver does: v1 = HMAC-SHA256(secret, "t.body"), t within 5 minutes.</summary>
    private static void AssertSigned(FakeReceiver.Received delivery, string secret)
    {
        var parts = delivery.Headers[Webhooks.SignatureHeader].Split(',').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var t = long.Parse(parts["t"], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - t, -5, 300);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), (byte[])[.. Encoding.UTF8.GetBytes(parts["t"] + "."), .. delivery.Body]);
        Assert.True(CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(parts["v1"])));
        Assert.StartsWith("application/json", delivery.Headers["Content-Type"], StringComparison.Ordinal);
    }

    private async Task<FakeReceiver.Received> WaitFor(string type)
    {
        for (var i = 0; i < 150; i++)
        {
            if (_receiver.Of(type) is [var first, ..]) return first;
            await Task.Delay(100);
        }
        throw new TimeoutException($"No {type} delivery received.");
    }

    private async Task<JsonElement> WaitForDelivery(string webhookId, Func<JsonElement, bool> match)
    {
        var alice = await Alice();
        for (var i = 0; i < 100; i++)
        {
            var page = await alice.GetJson($"/api/integrations/webhooks/{webhookId}/deliveries");
            if (page.GetProperty("items").EnumerateArray().FirstOrDefault(match) is { ValueKind: JsonValueKind.Object } found) return found;
            await Task.Delay(150);
        }
        throw new TimeoutException("No matching delivery.");
    }
}
