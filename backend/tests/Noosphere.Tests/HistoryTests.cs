using System.Net;
using System.Net.Http.Json;

namespace Noosphere.Tests;

/// <summary>Revisions, compare, restore and the trash. Demo: Billing has 4 versions.</summary>
[Collection("Api")]
public class HistoryTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task History_lists_revisions_newest_first_with_snapshots_and_compare()
    {
        // A guest reads the history of what is shared with her only.
        var guest = await _api.SignedIn("claire");
        await guest.GetJson("/api/applications/billing/history", HttpStatusCode.Forbidden);
        Assert.NotEqual(0, (await guest.GetJson("/api/applications/portal/history")).GetArrayLength());
        var claire = await _api.SignedIn("david");
        var history = await claire.GetJson("/api/applications/billing/history");
        Assert.Equal([4, 3, 2, 1], history.EnumerateArray().Select(h => h.GetProperty("version").GetInt32()));
        Assert.Equal(("create", "Imported"), (history[3].Str("operation"), history[3].Str("summary")));
        Assert.Equal(("alice", "Mention the invoice run"), (history[1].GetProperty("author").Str("username"), history[1].Str("summary")));

        var v2 = await claire.GetJson("/api/applications/billing/history/2");
        Assert.Equal((".NET 8, PostgreSQL 16", "Invoices premiums and sends them by email and post."),
            (v2.GetProperty("snapshot").Str("tech"), v2.GetProperty("snapshot").Str("description")));
        Assert.False(v2.GetProperty("snapshot").TryGetProperty("version", out _));

        var compare = await claire.GetJson("/api/applications/billing/history/compare?from=1&to=2");
        var entry = compare.GetProperty("diff").EnumerateArray().Single();
        Assert.Equal(("tech", ".NET, PostgreSQL", ".NET 8, PostgreSQL 16"), (entry.Str("field"), entry.Str("before"), entry.Str("after")));
        Assert.Equal(["description", "tech"], (await claire.GetJson("/api/applications/billing/history/compare?from=4&to=1"))
            .GetProperty("diff").EnumerateArray().Select(d => d.Str("field")).Order(StringComparer.Ordinal));

        await claire.GetJson("/api/applications/billing/history/9", HttpStatusCode.NotFound);
        await claire.GetJson("/api/applications/nope/history", HttpStatusCode.NotFound);
        await claire.GetJson("/api/applications/billing/history/compare?from=1", HttpStatusCode.BadRequest);
        // Every kind has a history.
        Assert.Equal(1, (await claire.GetJson("/api/teams/team-crm/history")).GetArrayLength());
        Assert.Equal(1, (await claire.GetJson("/api/pages/glossary/history")).GetArrayLength());
        Assert.Equal(1, (await claire.GetJson("/api/data-objects/invoice/history")).GetArrayLength());
        // Restricted records keep their history to administrators.
        await claire.GetJson("/api/applications/fraud/history", HttpStatusCode.Forbidden);
        await (await _api.SignedIn("alice")).GetJson("/api/applications/fraud/history");
    }

    [Fact]
    public async Task Owners_restore_a_version_and_others_propose_the_restore()
    {
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");
        var claire = await _api.SignedIn("claire");

        var published = await bob.PostJson("/api/applications/billing/history/1/restore", new { });
        Assert.Equal(("published", 5), (published.Str("outcome"), published.GetProperty("version").GetInt32()));
        var billing = await bob.GetJson("/api/applications/billing");
        Assert.Equal((".NET, PostgreSQL", "Invoices premiums and sends them by email and post."), (billing.Str("tech"), billing.Str("description")));
        var latest = (await bob.GetJson("/api/applications/billing/history"))[0];
        Assert.Equal(("restore", "Restored version 1"), (latest.Str("operation"), latest.Str("summary")));
        await bob.PostJson("/api/applications/billing/history/5/restore", new { }, HttpStatusCode.Conflict);

        var proposed = await david.PostJson("/api/applications/billing/history/4/restore", new { });
        Assert.Equal("proposed", proposed.Str("outcome"));
        Assert.Equal(5, (await bob.GetJson("/api/applications/billing")).GetProperty("version").GetInt32());
        var proposal = await bob.GetJson($"/api/proposals/{proposed.Str("proposalId")}");
        Assert.Equal(("submitted", "Restore Billing to version 4"), (proposal.Str("status"), proposal.Str("title")));
        Assert.Equal(["tech", "description"], proposal.GetProperty("changes")[0].GetProperty("diff").EnumerateArray().Select(d => d.Str("field")));
        await bob.PostJson($"/api/proposals/{proposed.Str("proposalId")}/accept", new { });
        Assert.Equal(".NET 8, PostgreSQL 16", (await bob.GetJson("/api/applications/billing")).Str("tech"));

        var guest = await claire.PostAsJsonAsync("/api/applications/billing/history/1/restore", new { });
        Assert.Equal(HttpStatusCode.Forbidden, guest.StatusCode);
    }

    [Fact]
    public async Task Deleted_records_keep_their_history_and_admins_restore_them_from_the_trash()
    {
        var bob = await _api.SignedIn("bob");
        var alice = await _api.SignedIn("alice");
        Assert.Equal(0, (await alice.GetJson("/api/trash")).GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, await bob.Delete("/api/flows/billing-mail"));
        var history = await bob.GetJson("/api/flows/billing-mail/history");
        Assert.Equal(("delete", 2), (history[0].Str("operation"), history[0].GetProperty("version").GetInt32()));
        await bob.PostJson("/api/flows/billing-mail/history/1/restore", new { }, HttpStatusCode.Conflict);

        await bob.GetJson("/api/trash", HttpStatusCode.Forbidden);
        var trash = await alice.GetJson("/api/trash");
        var item = trash.EnumerateArray().Single();
        Assert.Equal(("flow", "billing-mail", "bob"), (item.Str("kind"), item.Str("entityId"), item.GetProperty("deletedBy").Str("username")));
        Assert.DoesNotContain("billing-mail", (await bob.GetJson("/api/flows")).Ids());

        await bob.PostJson("/api/trash/flow/billing-mail/restore", new { }, HttpStatusCode.Forbidden);
        var restored = await alice.PostJson("/api/trash/flow/billing-mail/restore", new { });
        Assert.Equal(3, restored.GetProperty("version").GetInt32());
        var flow = await bob.GetJson("/api/flows/billing-mail");
        Assert.Equal(("billing", "mail", 3), (flow.Str("fromId"), flow.Str("toId"), flow.GetProperty("version").GetInt32()));
        Assert.Equal(0, (await alice.GetJson("/api/trash")).GetArrayLength());
        await alice.PostJson("/api/trash/flow/billing-mail/restore", new { }, HttpStatusCode.NotFound);

        // A record created again with the same id continues the history.
        Assert.Equal(HttpStatusCode.NoContent, await bob.Delete("/api/flows/billing-mail"));
        var again = await bob.PostJson("/api/flows", new { id = "billing-mail", name = "Mails", fromId = "billing", toId = "mail" }, HttpStatusCode.Created);
        Assert.Equal(5, again.GetProperty("version").GetInt32());
    }
}

[Collection("Api")]
public class SettingsTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Admins_set_the_instance_and_organisation_names()
    {
        var anonymous = await _api.Client().GetJson("/api/settings");
        Assert.Equal(("Noosphere", "", "en", 0), (anonymous.Str("instanceName"), anonymous.Str("organisationName"), anonymous.Str("defaultLanguage"),
            anonymous.GetProperty("version").GetInt32()));

        var bob = await _api.SignedIn("bob");
        await bob.PutJson("/api/settings", new { instanceName = "Mine", version = 0 }, HttpStatusCode.Forbidden);
        var alice = await _api.SignedIn("alice");
        var bad = await alice.PutJson("/api/settings", new { instanceName = " ", defaultLanguage = "de" }, HttpStatusCode.BadRequest);
        Assert.Equal(["defaultLanguage", "instanceName", "version"], bad.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal));
        var saved = await alice.PutJson("/api/settings", new { instanceName = " Alizé map ", organisationName = "Alizé Insurance", defaultLanguage = "fr", version = 0 });
        Assert.Equal(("Alizé map", "Alizé Insurance", "fr", 1), (saved.Str("instanceName"), saved.Str("organisationName"), saved.Str("defaultLanguage"),
            saved.GetProperty("version").GetInt32()));
        await alice.PutJson("/api/settings", new { instanceName = "Again", version = 0 }, HttpStatusCode.Conflict);
        Assert.Equal("Alizé map", (await bob.GetJson("/api/settings")).Str("instanceName"));

        // The export names its source after the instance, and new accounts get the default language.
        var export = await bob.GetJson("/api/export");
        Assert.Equal("Alizé map", export.GetProperty("source").Str("name"));
        var created = await alice.PostJson("/api/users", new { username = "erin", password = "erin-password" }, HttpStatusCode.Created);
        Assert.Equal("fr", created.Str("language"));
    }
}

[Collection("Api")]
public class MembershipTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Admins_manage_team_members_from_users_and_teams()
    {
        var alice = await _api.SignedIn("alice");
        var bob = await _api.SignedIn("bob");
        Assert.Equal(["team-finance", "team-payments"], (await bob.GetJson("/api/auth/me")).GetProperty("teams").EnumerateArray().Select(t => t.Str("id")));
        Assert.Equal(["bob"], (await bob.GetJson("/api/teams/team-finance/members")).EnumerateArray().Select(m => m.Str("username")));
        Assert.Equal(["bob"], (await bob.GetJson("/api/teams/team-finance")).GetProperty("members").EnumerateArray().Select(m => m.Str("username")));

        var claire = await alice.GetJson("/api/users/claire");
        var updated = await alice.PutJson("/api/users/claire", new { displayName = "Claire Petit", role = "member", version = claire.GetProperty("version").GetInt32(),
            teamIds = new[] { "team-finance", "team-data" } });
        Assert.Equal(["team-data", "team-finance"], updated.GetProperty("teamIds").EnumerateArray().Select(t => t.GetString()));
        await alice.PutJson("/api/users/claire", new { displayName = "Claire Petit", version = 2, teamIds = new[] { "nowhere" } }, HttpStatusCode.BadRequest);
        // Leaving teamIds out keeps them.
        var kept = await alice.PutJson("/api/users/claire", new { displayName = "Claire P.", version = 2 });
        Assert.Equal(2, kept.GetProperty("teamIds").GetArrayLength());

        await bob.PutJson("/api/teams/team-finance/members", new { usernames = new[] { "bob" } }, HttpStatusCode.Forbidden);
        var members = await alice.PutJson("/api/teams/team-finance/members", new { usernames = new[] { "alice", "Bob" } });
        Assert.Equal(["alice", "bob"], members.EnumerateArray().Select(m => m.Str("username")));
        await alice.PutJson("/api/teams/team-finance/members", new { usernames = new[] { "ghost" } }, HttpStatusCode.BadRequest);
        Assert.Equal(["team-data"], (await alice.GetJson("/api/users/claire")).GetProperty("teamIds").EnumerateArray().Select(t => t.GetString()));

        // Membership decides who publishes: claire, now in Team Data, publishes the glossary (owned by Team Data).
        var claireClient = await _api.SignedIn("claire");
        var glossary = await claireClient.GetJson("/api/pages/glossary");
        Assert.True(glossary.GetProperty("permissions").GetProperty("publish").GetBoolean());
    }
}
