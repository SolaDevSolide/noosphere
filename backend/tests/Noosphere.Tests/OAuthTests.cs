using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Noosphere.Api.Agents;
using static Noosphere.Tests.AgentKit;

namespace Noosphere.Tests;

/// <summary>The OAuth 2.1 authorization server of the MCP endpoint: discovery, PKCE, codes, refresh rotation, revocation, clients.</summary>
[Collection("Api")]
public class OAuthTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Discovery_documents_describe_the_resource_and_the_server()
    {
        var client = _api.Client();
        var resource = await client.GetJson("/.well-known/oauth-protected-resource/mcp");
        Assert.Equal("http://localhost/mcp", resource.Str("resource"));
        Assert.Equal(["http://localhost"], resource.GetProperty("authorization_servers").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("docs:read:restricted", resource.GetProperty("scopes_supported").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(resource.ToString(), (await client.GetJson("/.well-known/oauth-protected-resource")).ToString());

        var server = await client.GetJson("/.well-known/oauth-authorization-server");
        Assert.Equal("http://localhost", server.Str("issuer"));
        Assert.Equal(["S256"], server.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString()));
        Assert.True(server.GetProperty("client_id_metadata_document_supported").GetBoolean());
        Assert.Equal("http://localhost/oauth/register", server.Str("registration_endpoint"));

        // Without a token, /mcp answers 401 with the metadata address.
        var mcp = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);
        Assert.Equal("Bearer resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"", mcp.Headers.WwwAuthenticate.ToString());
        var bad = await Bearer(_api, "nsa_unknown").PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Contains("error=\"invalid_token\"", bad.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorization_code_with_pkce_gives_tokens_once()
    {
        var clientId = await Register(_api);
        var verifier = Verifier();
        var consent = await Authorize(_api, clientId, Challenge(verifier), resource: "http://localhost/mcp");
        Assert.Equal("/oauth/consent", consent.AbsolutePath);

        var bob = await _api.SignedIn("bob");
        var details = await bob.GetJson($"/api/oauth/requests/{Query(consent, "request")}");
        Assert.Equal(("Test agent", "registered", "bob"), (details.Str("clientName"), details.Str("clientKind"), details.GetProperty("person").Str("username")));
        Assert.Equal("public", details.Str("ceiling"));
        Assert.Contains(details.GetProperty("capabilities").EnumerateArray(), c => c.Str("key") == "restricted" && !c.GetProperty("allowed").GetBoolean());
        // The consent API needs the anti-CSRF header, like every state-changing /api request.
        var forged = await _api.ClientWithoutHeader().PostAsJsonAsync($"/api/oauth/requests/{Query(consent, "request")}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);

        var code = await Approve(bob, consent);
        Assert.StartsWith("nsc_", code, StringComparison.Ordinal);
        await bob.PostJson($"/api/oauth/requests/{Query(consent, "request")}/approve", new { }, HttpStatusCode.Conflict);

        // Wrong verifier, wrong redirect: refused, and the code is still unused.
        Assert.Equal("invalid_grant", (await (await Exchange(_api, clientId, code, Verifier())).Read(HttpStatusCode.BadRequest)).Str("error"));
        Assert.Equal("invalid_grant", (await (await Exchange(_api, clientId, code, verifier, "http://127.0.0.1:40123/other")).Read(HttpStatusCode.BadRequest)).Str("error"));
        var response = await Exchange(_api, clientId, code, verifier);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var tokens = Tokens(await response.Read(), clientId);
        Assert.StartsWith("nsa_", tokens.Access, StringComparison.Ordinal);
        Assert.Equal("docs:read docs:draft", tokens.Scope);

        // Only hashes are stored.
        var stored = await _api.Services.GetRequiredDb(db => db.AgentTokens.Select(t => t.Hash).ToList());
        Assert.DoesNotContain(tokens.Access, stored);
        Assert.Contains(OpaqueTokens.Hash(tokens.Access), stored);

        var mcp = await Mcp(_api, tokens.Access);
        Assert.Equal(10, (await mcp.ListToolsAsync()).Count);

        // A code used twice: refused, and the tokens it gave are revoked.
        Assert.Equal("invalid_grant", (await (await Exchange(_api, clientId, code, verifier)).Read(HttpStatusCode.BadRequest)).Str("error"));
        var after = await Bearer(_api, tokens.Access).PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Authorize_refuses_bad_requests()
    {
        var clientId = await Register(_api);
        var challenge = Challenge(Verifier());
        // Client and redirect URI errors stay on Noosphere (the consent page shows them).
        var unknown = await Authorize(_api, "nsclient_unknown", challenge);
        Assert.Equal(("/oauth/consent", "invalid_client"), (unknown.AbsolutePath, Query(unknown, "error")));
        var wrongRedirect = await Authorize(_api, clientId, challenge, redirect: "https://evil.example/callback");
        Assert.Equal(("/oauth/consent", "invalid_request"), (wrongRedirect.AbsolutePath, Query(wrongRedirect, "error")));
        // Other errors go back to the client, with the state.
        var plain = await Authorize(_api, clientId, challenge, method: "plain");
        Assert.Equal(("127.0.0.1", "invalid_request", "st4te"), (plain.Host, Query(plain, "error"), Query(plain, "state")));
        var noChallenge = await Authorize(_api, clientId, "short");
        Assert.Equal("invalid_request", Query(noChallenge, "error"));
        Assert.Equal("invalid_scope", Query(await Authorize(_api, clientId, challenge, scope: "docs:write"), "error"));
        Assert.Equal("invalid_target", Query(await Authorize(_api, clientId, challenge, resource: "https://elsewhere.example/mcp"), "error"));
        // A loopback redirect URI may use another port (native clients).
        var otherPort = await Authorize(_api, clientId, challenge, redirect: "http://127.0.0.1:51234/callback");
        Assert.Equal("/oauth/consent", otherPort.AbsolutePath);
        Assert.NotNull(Query(otherPort, "request"));

        // Refusing sends access_denied back to the client.
        var consent = await Authorize(_api, clientId, challenge);
        var denied = await (await _api.SignedIn("bob")).PostJson($"/api/oauth/requests/{Query(consent, "request")}/deny", new { });
        Assert.Equal("access_denied", Query(new Uri(denied.Str("redirectTo")), "error"));

        // Turned off: no new connection.
        await Settings(_api, new { enabled = false });
        Assert.Equal("access_denied", Query(await Authorize(_api, clientId, challenge), "error"));
    }

    [Fact]
    public async Task Refresh_tokens_rotate_and_reuse_revokes_the_connection()
    {
        var first = await Connect(_api, "bob");
        var second = Tokens(await (await Refresh(_api, first.ClientId, first.Refresh)).Read(), first.ClientId);
        Assert.NotEqual(first.Access, second.Access);
        Assert.NotEqual(first.Refresh, second.Refresh);
        // The old access token stays valid until it expires; the new one works.
        await (await Mcp(_api, second.Access)).ListToolsAsync();
        var third = Tokens(await (await Refresh(_api, first.ClientId, second.Refresh)).Read(), first.ClientId);

        // The first refresh token, used again: every token of the connection is revoked.
        Assert.Equal("invalid_grant", (await (await Refresh(_api, first.ClientId, first.Refresh)).Read(HttpStatusCode.BadRequest)).Str("error"));
        Assert.Equal("invalid_grant", (await (await Refresh(_api, first.ClientId, third.Refresh)).Read(HttpStatusCode.BadRequest)).Str("error"));
        var mcp = await Bearer(_api, third.Access).PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);
        var lines = await _api.Services.GetRequiredDb(db => db.AgentActivities.Where(a => a.Kind == Domain.AgentActivityKind.Revoked).Select(a => a.Summary).ToList());
        Assert.Contains(lines, l => l.Contains("used twice", StringComparison.Ordinal));

        // Wrong client id, unknown token, unknown grant.
        Assert.Equal("invalid_grant", (await (await Refresh(_api, "nsclient_other", third.Refresh)).Read(HttpStatusCode.BadRequest)).Str("error"));
        var grant = await _api.Client().PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "password" }));
        Assert.Equal("unsupported_grant_type", (await grant.Read(HttpStatusCode.BadRequest)).Str("error"));
    }

    [Fact]
    public async Task Revocation_ends_access_and_refresh()
    {
        var tokens = await Connect(_api, "bob");
        var revoke = await _api.Client().PostAsync("/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = tokens.Access, ["client_id"] = tokens.ClientId }));
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer(_api, tokens.Access).PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" })).StatusCode);
        // The refresh token still works after revoking the access token alone...
        var next = Tokens(await (await Refresh(_api, tokens.ClientId, tokens.Refresh)).Read(), tokens.ClientId);
        // ...and revoking a refresh token ends the whole connection's tokens.
        await _api.Client().PostAsync("/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = next.Refresh }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer(_api, next.Access).PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" })).StatusCode);
        Assert.Equal("invalid_grant", (await (await Refresh(_api, tokens.ClientId, next.Refresh)).Read(HttpStatusCode.BadRequest)).Str("error"));
        // Unknown tokens are accepted silently (RFC 7009).
        Assert.Equal(HttpStatusCode.OK, (await _api.Client().PostAsync("/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "nope" }))).StatusCode);
    }

    [Fact]
    public async Task Dynamic_registration_checks_redirects_and_can_be_turned_off()
    {
        var client = _api.Client();
        var bad = await client.PostAsJsonAsync("/oauth/register", new Dictionary<string, object> { ["client_name"] = "X", ["redirect_uris"] = new[] { "http://evil.example/cb" } });
        Assert.Equal("invalid_redirect_uri", (await bad.Read(HttpStatusCode.BadRequest)).Str("error"));
        var none = await client.PostAsJsonAsync("/oauth/register", new Dictionary<string, object> { ["client_name"] = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        var ok = await (await client.PostAsJsonAsync("/oauth/register", new Dictionary<string, object>
        {
            ["client_name"] = "Desktop", ["redirect_uris"] = new[] { "https://app.example/callback", "http://localhost:1234/cb", "com.example.app:/oauth" },
            ["token_endpoint_auth_method"] = "client_secret_post",
        })).Read(HttpStatusCode.Created);
        Assert.Equal("none", ok.Str("token_endpoint_auth_method"));
        Assert.False(ok.TryGetProperty("client_secret", out _));

        var settings = await Settings(_api, new { dynamicRegistration = false });
        Assert.False(settings.GetProperty("dynamicRegistration").GetBoolean());
        var off = await client.PostAsJsonAsync("/oauth/register", new Dictionary<string, object> { ["client_name"] = "X", ["redirect_uris"] = new[] { Redirect } });
        Assert.Equal(HttpStatusCode.Forbidden, off.StatusCode);
        Assert.False((await client.GetJson("/.well-known/oauth-authorization-server")).TryGetProperty("registration_endpoint", out _));
    }

    [Fact]
    public async Task Approval_required_connections_wait_for_an_administrator()
    {
        await Settings(_api, new { whoConnects = "approvalRequired" });
        var tokens = await Connect(_api, "david");
        var mcp = await Mcp(_api, tokens.Access);
        var (error, text) = await Call(mcp, "search", new() { ["query"] = "billing" });
        Assert.True(error);
        Assert.Contains("approval", text, StringComparison.Ordinal);

        var alice = await _api.SignedIn("alice");
        var pending = (await alice.GetJson("/api/agents")).EnumerateArray().Single();
        Assert.Equal("pending", pending.Str("state"));
        await (await _api.SignedIn("david")).PostJson($"/api/agents/{pending.Str("id")}/approve", new { }, HttpStatusCode.Forbidden);
        Assert.Equal("active", (await alice.PostJson($"/api/agents/{pending.Str("id")}/approve", new { })).Str("state"));
        Assert.False((await Call(mcp, "search", new() { ["query"] = "billing" })).Error);
        await alice.PostJson($"/api/agents/{pending.Str("id")}/approve", new { }, HttpStatusCode.Conflict);
    }
}

/// <summary>Client ID Metadata Documents through the authorization flow, with a fake fetcher.</summary>
[Collection("Api")]
public class ClientMetadataFlowTests : IDisposable
{
    private sealed class FakeFetcher : IClientMetadataFetcher
    {
        public Dictionary<string, string> Documents { get; } = [];
        public Task<string> Fetch(Uri url, CancellationToken cancel = default) =>
            Documents.TryGetValue(url.ToString(), out var doc) ? Task.FromResult(doc) : throw new ClientMetadataException("The client metadata document answered 404.");
    }

    private readonly FakeFetcher _fetcher = new();
    private readonly ApiFixture _api;

    public ClientMetadataFlowTests() =>
        _api = new ApiFixture(host: b => b.ConfigureTestServices(s => s.AddSingleton<IClientMetadataFetcher>(_fetcher)));

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task A_metadata_document_url_is_a_client_id()
    {
        const string id = "https://agent.example/oauth/client.json";
        _fetcher.Documents[id] = $$"""{"client_id":"{{id}}","client_name":"Example agent","client_uri":"https://agent.example","redirect_uris":["{{AgentKit.Redirect}}"]}""";
        var verifier = AgentKit.Verifier();
        var consent = await AgentKit.Authorize(_api, id, AgentKit.Challenge(verifier));
        var bob = await _api.SignedIn("bob");
        var details = await bob.GetJson($"/api/oauth/requests/{AgentKit.Query(consent, "request")}");
        Assert.Equal(("metadataDocument", "Example agent", "https://agent.example"), (details.Str("clientKind"), details.Str("clientName"), details.Str("clientUri")));
        var code = await AgentKit.Approve(bob, consent);
        var tokens = AgentKit.Tokens(await (await AgentKit.Exchange(_api, id, code, verifier)).Read(), id);
        var mine = (await bob.GetJson("/api/me/agents")).GetProperty("connections").EnumerateArray().Single();
        Assert.Equal((id, "Example agent", "active"), (mine.Str("clientId"), mine.Str("clientName"), mine.Str("state")));
        Assert.False((await AgentKit.Call(await AgentKit.Mcp(_api, tokens.Access), "search", new() { ["query"] = "crm" })).Error);

        // Invalid documents and URLs are refused on the consent page.
        _fetcher.Documents["https://agent.example/other.json"] = $$"""{"client_id":"{{id}}","client_name":"Spoof","redirect_uris":["{{AgentKit.Redirect}}"]}""";
        foreach (var (clientId, expected) in new[]
        {
            ("https://agent.example/other.json", "must equal its URL"),
            ("https://agent.example/missing.json", "404"),
            ("https://127.0.0.1/client.json", "local address"),
            ("https://10.0.0.8/client.json", "private network"),
            ("https://localhost/client.json", "local address"),
            ("https://agent.example/", "must have a path"),
        })
        {
            var refused = await AgentKit.Authorize(_api, clientId, AgentKit.Challenge(verifier));
            Assert.Equal("invalid_client", AgentKit.Query(refused, "error"));
            Assert.Contains(expected, AgentKit.Query(refused, "error_description"), StringComparison.Ordinal);
        }
    }
}
