using System.Net;
using System.Net.Http.Json;

namespace Noosphere.Tests;

[Collection("Api")]
public class UsersTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Only_admins_manage_users()
    {
        foreach (var name in new[] { "bob", "claire" })
        {
            var client = await _api.SignedIn(name);
            await client.GetJson("/api/users", HttpStatusCode.Forbidden);
            await client.PostJson("/api/users", new { username = "x", password = "long-enough" }, HttpStatusCode.Forbidden);
        }
        var users = await (await _api.SignedIn("alice")).GetJson("/api/users");
        Assert.Equal(["alice", "bob", "claire", "david"], users.EnumerateArray().Select(u => u.Str("username")));
    }

    [Fact]
    public async Task Admin_creates_updates_and_deletes_a_user()
    {
        var alice = await _api.SignedIn("alice");
        var created = await alice.PostJson("/api/users", new { username = "Dan", displayName = "Dan Roux", role = "member", language = "fr", password = "dan-password" }, HttpStatusCode.Created);
        Assert.Equal(("dan", "member", "fr", 1), (created.Str("username"), created.Str("role"), created.Str("language"), created.GetProperty("version").GetInt32()));
        Assert.False(created.TryGetProperty("passwordHash", out _));
        await alice.PostJson("/api/users", new { username = "dan", password = "dan-password" }, HttpStatusCode.Conflict);
        var invalid = await alice.PostJson("/api/users", new { username = "e e", password = "short" }, HttpStatusCode.BadRequest);
        Assert.True(invalid.GetProperty("errors").TryGetProperty("password", out _));
        Assert.True(invalid.GetProperty("errors").TryGetProperty("username", out _));

        var dan = await _api.SignedIn("dan", "dan-password");
        var updated = await alice.PutJson("/api/users/dan", new { displayName = "Dan R.", role = "guest", version = 1 });
        Assert.Equal(("guest", 2), (updated.Str("role"), updated.GetProperty("version").GetInt32()));
        // A role change closes the user's sessions.
        await dan.GetJson("/api/auth/me", HttpStatusCode.Unauthorized);
        await alice.PutJson("/api/users/dan", new { displayName = "Dan", version = 1 }, HttpStatusCode.Conflict);
        await alice.PutJson("/api/users/dan", new { displayName = "Dan", version = 2, password = "reset-password" });
        await _api.SignedIn("dan", "reset-password");

        Assert.Equal(HttpStatusCode.NoContent, await alice.Delete("/api/users/dan"));
        await alice.GetJson("/api/users/dan", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_last_admin_cannot_be_removed_or_demoted()
    {
        var alice = await _api.SignedIn("alice");
        await alice.PutJson("/api/users/alice", new { displayName = "Alice", role = "member", version = 1 }, HttpStatusCode.Conflict);
        var self = await (await alice.DeleteAsync("/api/users/alice")).Read(HttpStatusCode.Conflict);
        Assert.Contains("own account", self.Str("detail"), StringComparison.Ordinal);

        await alice.PutJson("/api/users/bob", new { displayName = "Bob", role = "admin", version = 1 });
        var bob = await _api.SignedIn("bob");
        Assert.Equal(HttpStatusCode.NoContent, await bob.Delete("/api/users/alice"));
        await bob.PutJson("/api/users/bob", new { displayName = "Bob", role = "member", version = 2 }, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Roles_decide_who_reads_edits_and_deletes()
    {
        var claire = await _api.SignedIn("claire");
        await claire.GetJson("/api/landscape");
        await claire.PostJson("/api/teams", new { name = "Guests" }, HttpStatusCode.Forbidden);
        await claire.PutJson("/api/teams/team-crm", new { name = "CRM", version = 1 }, HttpStatusCode.Forbidden);

        // Domains and teams are changed by administrators only.
        var bob = await _api.SignedIn("bob");
        await bob.PostJson("/api/teams", new { name = "Members" }, HttpStatusCode.Forbidden);
        await bob.PutJson("/api/teams/team-finance", new { name = "Finance!", version = 1 }, HttpStatusCode.Forbidden);
        await bob.PostJson("/api/domains", new { name = "Members" }, HttpStatusCode.Forbidden);

        var alice = await _api.SignedIn("alice");
        await alice.PostJson("/api/teams", new { name = "Members" }, HttpStatusCode.Created);
        await alice.PutJson("/api/teams/members", new { name = "Members!", version = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, await bob.Delete("/api/teams/members"));
        Assert.Equal(HttpStatusCode.NoContent, await alice.Delete("/api/teams/members"));
        _ = (await alice.PostAsJsonAsync("/api/auth/logout", new { })).EnsureSuccessStatusCode();
    }
}
