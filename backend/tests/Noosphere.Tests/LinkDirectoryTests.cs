using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noosphere.Api.Data;
using Noosphere.Api.Links;
using Noosphere.Domain;

namespace Noosphere.Tests;

/// <summary>The shared directory (<c>directory:read</c>): off by default, teams and people, e-mails only when shared, person notices.</summary>
[Collection("Api")]
public sealed class LinkDirectoryTests : IAsyncLifetime, IDisposable
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

    private async Task<Guid> Paired(object? code = null)
    {
        var alice = await Alice();
        var created = await alice.PostJson("/api/links/codes", code ?? new { }, HttpStatusCode.Created);
        var answer = await _peer.Pair(_api.Client(), created.Str("code"));
        var id = Guid.Parse(answer["linkId"]!.GetValue<string>());
        await alice.PostJson($"/api/links/{id}/confirm", new { fingerprint = _peer.Fingerprint() });
        return id;
    }

    private async Task Run(Func<NoosphereDbContext, Task> write)
    {
        using var scope = _api.Services.CreateScope();
        await write(scope.ServiceProvider.GetRequiredService<NoosphereDbContext>());
    }

    [Fact]
    public async Task The_directory_is_off_by_default_and_refused_with_capability_missing()
    {
        var id = await Paired();
        var link = await (await Alice()).GetJson($"/api/links/{id}");
        Assert.Equal(["catalog:read", "notices:send", "deployments:receive"], link.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()));
        Assert.False(link.GetProperty("shareEmails").GetBoolean());

        var refused = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/directory");
        Assert.Equal((HttpStatusCode.Forbidden, "capability-missing"), (refused.Status, refused.Reason));
        Assert.Null(refused.SignatureProblem);
        var log = (await (await Alice()).GetJson($"/api/links/{id}/exchanges")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(log, e => e.Str("operation") == "directory" && e.Str("result") == "refused" && e.Str("reason") == "capability-missing");

        // Unsigned or badly signed: the usual refusals.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client().GetAsync("/link/v1/directory")).StatusCode);
        var tampered = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/directory", o: new SignOptions { SignedPath = "/link/v1/catalog" });
        Assert.Equal("bad-signature", tampered.Reason);
    }

    [Fact]
    public async Task The_directory_lists_teams_and_people_without_guests_roles_or_emails_unless_shared()
    {
        var id = await Paired(new { capabilities = new[] { "catalog:read", "directory:read" } });
        await Run(async db =>
        {
            await db.Teams.Where(t => t.Id == "team-finance").ExecuteUpdateAsync(s => s.SetProperty(t => t.Contact, "finance-ops@alize.example"));
        });

        var reply = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/directory");
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Null(reply.SignatureProblem);
        Assert.Equal("application/jose", reply.Response.Content.Headers.ContentType!.MediaType);
        var directory = reply.Payload!;
        Assert.False(directory["emailsShared"]!.GetValue<bool>());
        var teams = directory["teams"]!.AsArray().Select(t => t!.AsObject()).ToDictionary(t => t["id"]!.GetValue<string>());
        Assert.Null(teams["team-finance"]["contact"]);
        Assert.Equal("#team-crm", teams["team-crm"]["contact"]!.GetValue<string>());
        Assert.Contains("billing", teams["team-finance"]["applicationIds"]!.AsArray().Select(a => a!.GetValue<string>()));
        // The restricted Fraud scoring application is not in the shared catalogue.
        Assert.DoesNotContain(teams.Values.SelectMany(t => t["applicationIds"]!.AsArray()), a => a!.GetValue<string>() == "fraud");

        var people = directory["people"]!.AsArray().Select(p => p!.AsObject()).ToDictionary(p => p["username"]!.GetValue<string>());
        // Members and administrators in at least one team: Alice, in no team, is not listed.
        Assert.Equal(["bob", "david"], people.Keys.Order());
        Assert.Equal(["team-finance", "team-payments"], people["bob"]["teamIds"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.All(people.Values, p => Assert.Equal(["displayName", "teamIds", "username"], p.Select(kv => kv.Key).Order()));
        var text = JsonSerializer.Serialize(directory);
        Assert.DoesNotContain("claire", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
        Assert.DoesNotContain("role", text, StringComparison.OrdinalIgnoreCase);

        // Unchanged: 304 with the same ETag.
        var etag = reply.Response.Headers.ETag!.Tag;
        var again = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/directory", headers: r => r.Headers.TryAddWithoutValidation("If-None-Match", etag));
        Assert.Equal(HttpStatusCode.NotModified, again.Status);
        Assert.Null(again.SignatureProblem);

        // The administrator shares e-mails: the team contact comes through, and the ETag changes.
        var link = await (await Alice()).PutJson($"/api/links/{id}/capabilities", new { capabilities = new[] { "catalog:read", "directory:read" }, shareEmails = true });
        Assert.True(link.GetProperty("shareEmails").GetBoolean());
        var shared = await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/directory", headers: r => r.Headers.TryAddWithoutValidation("If-None-Match", etag));
        Assert.Equal(HttpStatusCode.OK, shared.Status);
        Assert.True(shared.Payload!["emailsShared"]!.GetValue<bool>());
        Assert.Contains(shared.Payload["teams"]!.AsArray(), t => t!["contact"]?.GetValue<string>() == "finance-ops@alize.example");

        // Leaving shareEmails out keeps the choice; removing directory:read refuses the directory again.
        link = await (await Alice()).PutJson($"/api/links/{id}/capabilities", new { capabilities = new[] { "catalog:read" } });
        Assert.True(link.GetProperty("shareEmails").GetBoolean());
        Assert.Equal("capability-missing", (await _peer.Call(_api.Client(), HttpMethod.Get, "/link/v1/directory")).Reason);

        var log = (await (await Alice()).GetJson($"/api/links/{id}/exchanges")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(log, e => e.Str("operation") == "directory" && e.Str("result") == "ok" && e.Str("detail").Contains("withheld", StringComparison.Ordinal));
        Assert.Contains(log, e => e.Str("operation") == "directory" && e.Str("result") == "ok" && e.Str("detail").Contains("shared", StringComparison.Ordinal));
        Assert.Empty(_peer.Errors);
    }

    [Fact]
    public void Contacts_hide_email_addresses_unless_shared()
    {
        Assert.Null(LinkDirectory.Contact("ops@alize.example", shareEmails: false));
        Assert.Null(LinkDirectory.Contact("Slack #ops, or ops@alize.example", shareEmails: false));
        Assert.Equal("ops@alize.example", LinkDirectory.Contact("ops@alize.example", shareEmails: true));
        Assert.Equal("#team-ops", LinkDirectory.Contact("#team-ops", shareEmails: false));
        Assert.Null(LinkDirectory.Contact(null, shareEmails: true));
    }

    [Fact]
    public async Task People_changes_are_noticed_only_to_links_that_read_the_directory()
    {
        await Paired(new { capabilities = new[] { "catalog:read", "notices:send", "directory:read" } });
        var alice = await Alice();
        await alice.PutJson("/api/users/david", new { displayName = "David R.", version = (await alice.GetJson("/api/users")).EnumerateArray().Single(u => u.Str("username") == "david").GetProperty("version").GetInt32() });
        var notice = await WaitFor(n => n.Any(c => c["kind"]!.GetValue<string>() == "person"));
        Assert.Contains(notice, c => c["kind"]!.GetValue<string>() == "person" && c["id"]!.GetValue<string>() == "david" && c["operation"]!.GetValue<string>() == "upsert");
    }

    [Fact]
    public async Task People_changes_are_not_noticed_without_the_directory()
    {
        await Paired();
        var alice = await Alice();
        await alice.PutJson("/api/users/david", new { displayName = "David R.", version = (await alice.GetJson("/api/users")).EnumerateArray().Single(u => u.Str("username") == "david").GetProperty("version").GetInt32() });
        await Run(async db =>
        {
            (await db.Teams.FirstAsync(t => t.Id == "team-crm")).Name = "CRM";
            await db.SaveChangesAsync();
        });
        var notice = await WaitFor(n => n.Any(c => c["kind"]!.GetValue<string>() == "team"));
        Assert.DoesNotContain(notice, c => c["kind"]!.GetValue<string>() == "person");
        lock (_peer.Notices)
            Assert.DoesNotContain(_peer.Notices.SelectMany(n => n["changes"]!.AsArray()), c => c!["kind"]!.GetValue<string>() == "person");
    }

    [Fact]
    public async Task People_resolver_tells_upserts_from_deletes()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Username);
        var now = DateTimeOffset.UtcNow;
        var changes = await NoticeResolver.ResolvePeople(db,
        [
            new PersonCandidate(users["bob"].Id, false, now, Membership: true),
            // A guest is never in the directory, nor was a member who became one listed as a guest.
            new PersonCandidate(users["claire"].Id, false, now, "claire", Role.Guest),
            // Deleted (or made a guest): it leaves the directory.
            new PersonCandidate(Guid.NewGuid(), false, now, "gone", Role.Member),
            new PersonCandidate(Guid.NewGuid(), false, now, "guest-gone", Role.Guest),
            new PersonCandidate(Guid.NewGuid(), true, now),
        ]);
        Assert.Equal([("person", "bob", "upsert"), ("person", "gone", "delete")], changes.Select(c => (c.Kind, c.Id, c.Operation)).Order());
    }

    [Fact]
    public async Task People_leaving_their_last_team_leave_the_directory()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
        var david = await db.Users.AsNoTracking().SingleAsync(u => u.Username == "david");
        await db.TeamMembers.Where(m => m.UserId == david.Id).ExecuteDeleteAsync();
        var changes = await NoticeResolver.ResolvePeople(db, [new PersonCandidate(david.Id, false, DateTimeOffset.UtcNow, Membership: true)]);
        Assert.Equal([("person", "david", "delete")], changes.Select(c => (c.Kind, c.Id, c.Operation)));
    }

    [Fact]
    public async Task Administrators_preview_the_directory_before_sharing_it()
    {
        await (await _api.SignedIn("bob")).GetJson("/api/links/directory-preview", HttpStatusCode.Forbidden);
        await Run(async db => await db.Teams.Where(t => t.Id == "team-finance").ExecuteUpdateAsync(s => s.SetProperty(t => t.Contact, "finance-ops@alize.example")));
        var preview = await (await Alice()).GetJson("/api/links/directory-preview");
        Assert.Equal((2, 1, 1, 1), (preview.GetProperty("people").GetInt32(), preview.GetProperty("guests").GetInt32(), preview.GetProperty("withoutTeam").GetInt32(),
            preview.GetProperty("contactEmails").GetInt32()));
        Assert.True(preview.GetProperty("teams").GetInt32() > 2);
        var sample = preview.GetProperty("sample").EnumerateArray().ToList();
        Assert.Equal(["bob", "david"], sample.Select(p => p.Str("username")).Order());
        Assert.Contains(sample, p => p.GetProperty("teams").EnumerateArray().Any(t => t.GetString() == "Team Finance"));
    }

    private async Task<List<JsonObject>> WaitFor(Func<List<JsonObject>, bool> match)
    {
        for (var i = 0; i < 150; i++)
        {
            lock (_peer.Notices)
                foreach (var notice in _peer.Notices)
                    if (notice["changes"]!.AsArray().Select(c => c!.AsObject()).ToList() is var changes && match(changes)) return changes;
            await Task.Delay(100);
        }
        throw new TimeoutException("No matching change notice received.");
    }
}
