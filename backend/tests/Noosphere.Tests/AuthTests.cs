using System.Net;
using System.Net.Http.Json;

namespace Noosphere.Tests;

[Collection("Api")]
public class AuthTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Login_sets_an_http_only_lax_cookie_and_returns_the_user()
    {
        var response = await _api.Client().PostAsJsonAsync("/api/auth/login", new { username = "Alice", password = "demo" });
        var me = await response.Read();
        Assert.Equal(("alice", "Alice Martin", "admin", "en"), (me.Str("username"), me.Str("displayName"), me.Str("role"), me.Str("language")));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("noosphere.auth=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        // The test server speaks plain HTTP: Secure is only set on HTTPS requests.
        Assert.DoesNotContain("secure", cookie.Replace("samesite", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cookie_is_secure_behind_https()
    {
        var client = _api.Client();
        client.BaseAddress = new Uri("https://localhost/");
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "bob", password = "demo" });
        await response.Read();
        Assert.Contains("secure", Assert.Single(response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("alice", "wrong")]
    [InlineData("nobody", "demo")]
    [InlineData("", "")]
    public async Task Wrong_credentials_return_401(string username, string password)
    {
        var body = await (await _api.Client().PostAsJsonAsync("/api/auth/login", new { username, password })).Read(HttpStatusCode.Unauthorized);
        Assert.Equal("Wrong username or password.", body.Str("detail"));
    }

    [Fact]
    public async Task Everything_but_login_and_health_requires_a_session()
    {
        var anonymous = _api.Client();
        foreach (var url in new[] { "/api/auth/me", "/api/landscape", "/api/applications", "/api/search?q=crm", "/api/export", "/api/users" })
        {
            var problem = await anonymous.GetJson(url, HttpStatusCode.Unauthorized);
            Assert.Equal(401, problem.GetProperty("status").GetInt32());
        }
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Problems_use_the_problem_json_content_type()
    {
        var response = await _api.Client().GetAsync("/api/landscape");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Logout_closes_the_session()
    {
        var client = await _api.SignedIn("bob");
        await client.GetJson("/api/auth/me");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        await client.GetJson("/api/auth/me", HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task State_changing_requests_need_the_anti_csrf_header()
    {
        var forged = _api.ClientWithoutHeader();
        var body = await (await forged.PostAsJsonAsync("/api/auth/login", new { username = "alice", password = "demo" })).Read(HttpStatusCode.Forbidden);
        Assert.Contains("X-Requested-With", body.Str("detail"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Language_is_saved_on_the_account()
    {
        var client = await _api.SignedIn("claire");
        var me = await client.PutJson("/api/auth/me/language", new { language = "fr" });
        Assert.Equal("fr", me.Str("language"));
        Assert.Equal("fr", (await (await _api.SignedIn("claire")).GetJson("/api/auth/me")).Str("language"));
        var problem = await client.PutJson("/api/auth/me/language", new { language = "de" }, HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("language", out _));
    }

    [Fact]
    public async Task Changing_the_password_keeps_this_session_and_closes_the_others()
    {
        var here = await _api.SignedIn("bob");
        var elsewhere = await _api.SignedIn("bob");
        await here.PutJson("/api/auth/me/password", new { currentPassword = "wrong", newPassword = "a-new-password" }, HttpStatusCode.BadRequest);
        await here.PutJson("/api/auth/me/password", new { currentPassword = "demo", newPassword = "short" }, HttpStatusCode.BadRequest);
        var response = await here.PutAsJsonAsync("/api/auth/me/password", new { currentPassword = "demo", newPassword = "a-new-password" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await here.GetJson("/api/auth/me");
        await elsewhere.GetJson("/api/auth/me", HttpStatusCode.Unauthorized);
        await _api.SignedIn("bob", "a-new-password");
    }

    [Fact]
    public async Task Security_headers_are_sent()
    {
        var response = await _api.Client().GetAsync("/health/live");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }
}

[Collection("Api")]
public class LoginRateLimitTests : IDisposable
{
    private readonly ApiFixture _api = new(settings: new Dictionary<string, string> { ["LOGIN_RATE_LIMIT"] = "3" });
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Too_many_attempts_from_one_address_return_429()
    {
        var client = _api.Client();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { username = "alice", password = "bad" })).StatusCode);
        var blocked = await (await client.PostAsJsonAsync("/api/auth/login", new { username = "alice", password = "demo" })).Read(HttpStatusCode.TooManyRequests);
        Assert.Equal(429, blocked.GetProperty("status").GetInt32());
    }
}

[Collection("Api")]
public class FirstRunTests
{
    [Fact]
    public async Task Without_demo_an_admin_is_created_from_the_configured_password()
    {
        using var api = new ApiFixture(demo: false, settings: new Dictionary<string, string> { ["ADMIN_PASSWORD"] = "first-run-secret" });
        var admin = await api.SignedIn("admin", "first-run-secret");
        Assert.Equal("admin", (await admin.GetJson("/api/auth/me")).Str("role"));
        Assert.Equal(0, (await admin.GetJson("/api/landscape")).GetProperty("stats").GetProperty("applications").GetInt32());
        Assert.Equal(1, (await admin.GetJson("/api/users")).GetArrayLength());
    }

    [Fact]
    public async Task Without_a_configured_password_a_random_one_is_generated()
    {
        using var api = new ApiFixture(demo: false);
        var users = await api.Services.GetRequiredDb(db => db.Users.Select(u => u.Username).ToList());
        Assert.Equal(["admin"], users);
        var response = await api.Client().PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "demo" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
