using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Noosphere.Tests;

/// <summary>Tokens of an agent connected through the whole OAuth flow.</summary>
internal sealed record AgentTokens(string ClientId, string Access, string Refresh, string Scope, string RedirectUri);

/// <summary>OAuth and MCP helpers: register a client, run the authorization code flow with PKCE, call MCP tools.</summary>
internal static class AgentKit
{
    public const string Redirect = "http://127.0.0.1:40123/callback";

    public static string Verifier() => Base64Url(RandomNumberGenerator.GetBytes(48));

    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Dynamic client registration; returns the client id.</summary>
    public static async Task<string> Register(ApiFixture api, string name = "Test agent", string redirect = Redirect)
    {
        var response = await api.Client().PostAsJsonAsync("/oauth/register", new Dictionary<string, object>
        {
            ["client_name"] = name, ["redirect_uris"] = new[] { redirect }, ["token_endpoint_auth_method"] = "none",
        });
        return (await response.Read(HttpStatusCode.Created)).Str("client_id");
    }

    /// <summary>GET /oauth/authorize; returns the Location header.</summary>
    public static async Task<Uri> Authorize(ApiFixture api, string clientId, string challenge, string scope = "docs:read docs:draft",
        string redirect = Redirect, string method = "S256", string? resource = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = clientId, ["redirect_uri"] = redirect, ["code_challenge"] = challenge,
            ["code_challenge_method"] = method, ["scope"] = scope, ["state"] = "st4te", ["resource"] = resource,
        }.Where(p => p.Value is not null);
        var response = await api.Client().SendAsync(new HttpRequestMessage(HttpMethod.Get, QueryHelpers.AddQueryString("/oauth/authorize", query)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return new Uri(new Uri("http://localhost"), response.Headers.Location!);
    }

    public static string? Query(Uri uri, string name) =>
        QueryHelpers.ParseQuery(uri.Query).TryGetValue(name, out var v) ? v.ToString() : null;

    /// <summary>The consent page approves (as <paramref name="person"/>); returns the code.</summary>
    public static async Task<string> Approve(HttpClient person, Uri consent, object? body = null)
    {
        var id = Query(consent, "request")!;
        var result = await person.PostJson($"/api/oauth/requests/{id}/approve", body ?? new { });
        var redirect = new Uri(result.Str("redirectTo"));
        Assert.Equal("st4te", Query(redirect, "state"));
        return Query(redirect, "code")!;
    }

    public static Task<HttpResponseMessage> Exchange(ApiFixture api, string clientId, string code, string verifier, string redirect = Redirect) =>
        api.Client().PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["client_id"] = clientId, ["redirect_uri"] = redirect, ["code_verifier"] = verifier,
        }));

    public static Task<HttpResponseMessage> Refresh(ApiFixture api, string clientId, string refresh) =>
        api.Client().PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = clientId,
        }));

    public static AgentTokens Tokens(JsonElement body, string clientId) =>
        new(clientId, body.Str("access_token"), body.Str("refresh_token"), body.Str("scope"), Redirect);

    /// <summary>Registers a client and connects it for <paramref name="username"/> (password "demo" unless given).</summary>
    public static async Task<AgentTokens> Connect(ApiFixture api, string username, string scope = "docs:read docs:draft", string password = "demo", string name = "Test agent")
    {
        var clientId = await Register(api, name);
        var verifier = Verifier();
        var consent = await Authorize(api, clientId, Challenge(verifier), scope);
        var code = await Approve(await api.SignedIn(username, password), consent);
        return Tokens(await (await Exchange(api, clientId, code, verifier)).Read(), clientId);
    }

    /// <summary>An MCP client on the in-process server, with the bearer token.</summary>
    public static async Task<McpClient> Mcp(ApiFixture api, string accessToken)
    {
        var http = api.Client();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + accessToken },
        }, http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    public static async Task<(bool Error, string Text)> Call(McpClient client, string tool, Dictionary<string, object?>? args = null)
    {
        var result = await client.CallToolAsync(tool, args ?? []);
        return (result.IsError == true, string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text)));
    }

    public static async Task<JsonElement> CallJson(McpClient client, string tool, Dictionary<string, object?>? args = null)
    {
        var (error, text) = await Call(client, tool, args);
        Assert.False(error, text);
        return JsonDocument.Parse(text).RootElement;
    }

    /// <summary>Changes the agent settings as an administrator.</summary>
    public static async Task<JsonElement> Settings(ApiFixture api, object changes)
    {
        var alice = await api.SignedIn("alice");
        var current = await alice.GetJson("/api/settings/agents");
        var body = JsonSerializer.SerializeToNode(changes)!.AsObject();
        body["version"] = current.GetProperty("version").GetInt32();
        return await alice.PutJson("/api/settings/agents", body);
    }

    public static HttpClient Bearer(ApiFixture api, string token)
    {
        var client = api.Client();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
