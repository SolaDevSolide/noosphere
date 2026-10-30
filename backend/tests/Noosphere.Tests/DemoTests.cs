using System.Net;
using System.Net.Http.Json;

namespace Noosphere.Tests;

/// <summary>The demo toolbox (/api/demo/*): only in demo mode, and every scenario does what its notice says.</summary>
[Collection("Api")]
public class DemoTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static void HasNotice(System.Text.Json.JsonElement result, string? link = null)
    {
        var notice = result.GetProperty("notice");
        Assert.NotEmpty(notice.Str("title"));
        Assert.NotEmpty(notice.Str("detail"));
        if (link is not null) Assert.StartsWith(link, notice.Str("link"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task State_lists_personas_with_what_to_try_and_switching_signs_in()
    {
        var anonymous = _api.Client();
        var state = await anonymous.GetJson("/api/demo/state");
        var personas = state.GetProperty("personas").EnumerateArray().ToList();
        Assert.Equal(["alice", "bob", "claire", "david"], personas.Select(p => p.Str("username")).Order(StringComparer.Ordinal));
        Assert.All(personas, p =>
        {
            Assert.NotEmpty(p.GetProperty("whatToTry").Str("en"));
            Assert.NotEmpty(p.GetProperty("whatToTry").Str("fr"));
        });
        Assert.Contains(state.GetProperty("scenarios").EnumerateArray(), s => s.Str("id") == "agent:quota");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, state.GetProperty("current").ValueKind);

        var switched = await anonymous.PostJson("/api/demo/switch", new { username = "claire" });
        Assert.Equal("claire", switched.GetProperty("me").Str("username"));
        HasNotice(switched);
        Assert.Equal("claire", (await anonymous.GetJson("/api/auth/me")).Str("username"));
        Assert.Equal("claire", (await anonymous.GetJson("/api/demo/state")).Str("current"));
        await anonymous.PostJson("/api/demo/switch", new { username = "root" }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_colleague_edit_makes_the_old_version_conflict_and_proposals_outdated()
    {
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");
        var billing = await bob.GetJson("/api/applications/billing");
        var version = billing.GetProperty("version").GetInt32();
        var proposal = await david.PostJson("/api/proposals", new
        {
            title = "Billing: describe it", submit = true,
            changes = new[] { new { kind = "application", targetId = "billing", operation = "update", baseVersion = version,
                after = new { name = "Billing", domainId = "finance", teamId = "team-finance", description = "Invoices premiums." } } },
        }, HttpStatusCode.Created);

        var edit = await bob.PostJson("/api/demo/simulate/edit", new { kind = "application", id = "billing" });
        Assert.Equal(version + 1, edit.GetProperty("version").GetInt32());
        Assert.NotEqual("bob", edit.GetProperty("author").Str("username"));
        Assert.Contains(proposal.Str("id"), edit.GetProperty("outdatedProposals").EnumerateArray().Select(p => p.GetString()));
        HasNotice(edit, "/applications/billing");
        Assert.Contains("_Checked by ", (await bob.GetJson("/api/applications/billing")).Str("description"), StringComparison.Ordinal);

        var stale = await bob.PutAsJsonAsync("/api/applications/billing", new { name = "Billing", domainId = "finance", teamId = "team-finance", description = "Mine.", version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.True((await bob.GetJson($"/api/proposals/{proposal.Str("id")}")).GetProperty("outdated").GetBoolean());
        await bob.PostJson($"/api/proposals/{proposal.Str("id")}/accept", new { }, HttpStatusCode.Conflict);
        await bob.PostJson("/api/demo/simulate/edit", new { kind = "application", id = "nope" }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Proposals_arrive_and_get_decided()
    {
        var bob = await _api.SignedIn("bob");
        var received = await bob.PostJson("/api/demo/simulate/proposal", new { });
        var id = received.Str("proposalId");
        HasNotice(received, "/review/");
        var proposal = await bob.GetJson($"/api/proposals/{id}");
        Assert.Equal(("submitted", true), (proposal.Str("status"), proposal.GetProperty("canDecide").GetBoolean()));
        Assert.NotEqual("bob", proposal.GetProperty("author").Str("username"));
        await (await _api.SignedIn("claire")).PostJson("/api/demo/simulate/proposal", new { }, HttpStatusCode.Conflict);

        // David proposes a change to Billing; the demo validator decides.
        var david = await _api.SignedIn("david");
        var billing = await david.GetJson("/api/applications/billing");
        var mine = await david.PostJson("/api/proposals", new
        {
            title = "Billing: describe it", submit = true,
            changes = new[] { new { kind = "application", targetId = "billing", operation = "update", baseVersion = billing.GetProperty("version").GetInt32(),
                after = new { name = "Billing", domainId = "finance", teamId = "team-finance", description = "Invoices premiums." } } },
        }, HttpStatusCode.Created);
        var refused = await david.PostJson("/api/demo/simulate/decision", new { proposalId = mine.Str("id"), decision = "refuse" });
        Assert.Equal("refused", refused.Str("status"));
        Assert.NotEmpty(refused.Str("refusalReason"));
        HasNotice(refused, "/review/");
        await david.PostJson("/api/demo/simulate/decision", new { proposalId = mine.Str("id"), decision = "accept" }, HttpStatusCode.Conflict);
        await david.PostJson("/api/demo/simulate/decision", new { proposalId = mine.Str("id"), decision = "maybe" }, HttpStatusCode.BadRequest);
        await bob.PostJson("/api/demo/simulate/decision", new { proposalId = mine.Str("id"), decision = "accept" }, HttpStatusCode.NotFound);

        var again = await david.PostJson("/api/proposals", new
        {
            title = "Billing: describe it", submit = true,
            changes = new[] { new { kind = "application", targetId = "billing", operation = "update", baseVersion = billing.GetProperty("version").GetInt32(),
                after = new { name = "Billing", domainId = "finance", teamId = "team-finance", description = "Invoices premiums." } } },
        }, HttpStatusCode.Created);
        Assert.Equal("accepted", (await david.PostJson("/api/demo/simulate/decision", new { proposalId = again.Str("id"), decision = "accept" })).Str("status"));
        Assert.Equal("Invoices premiums.", (await david.GetJson("/api/applications/billing")).Str("description"));
    }

    [Fact]
    public async Task Demo_agents_draft_submit_get_refused_and_hit_their_quota()
    {
        var bob = await _api.SignedIn("bob");
        var draft = await bob.PostJson("/api/demo/simulate/agent", new { scenario = "draft" });
        Assert.Equal(["read", "read", "draft"], draft.GetProperty("activity").EnumerateArray().Select(a => a.Str("kind")));
        var draftId = draft.Str("proposalId");
        HasNotice(draft, "/my-agents");
        Assert.Equal("draft", (await bob.GetJson($"/api/proposals/{draftId}")).Str("status"));
        await (await _api.SignedIn("david")).GetJson($"/api/proposals/{draftId}", HttpStatusCode.NotFound);

        var submit = await bob.PostJson("/api/demo/simulate/agent", new { scenario = "submit" });
        Assert.Equal("proposal", submit.GetProperty("activity").EnumerateArray().Last().Str("kind"));
        var proposal = await bob.GetJson($"/api/proposals/{submit.Str("proposalId")}");
        Assert.Equal(("submitted", "agent", "Demo assistant"), (proposal.Str("status"), proposal.Str("authorKind"), proposal.Str("agentName")));

        var refused = await bob.PostJson("/api/demo/simulate/agent", new { scenario = "refused" });
        var lines = refused.GetProperty("activity").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count(a => a.Str("kind") == "refused"));
        Assert.Contains(lines, a => a.Str("summary").Contains("ceiling", StringComparison.Ordinal));
        Assert.Contains(lines, a => a.Str("summary").Contains("secret", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, a => a.Str("summary").Contains("gh" + "p_", StringComparison.Ordinal));

        var quota = await bob.PostJson("/api/demo/simulate/agent", new { scenario = "quota" });
        Assert.Equal("quota", quota.GetProperty("activity").EnumerateArray().Last().Str("kind"));
        var connection = (await bob.GetJson("/api/me/agents")).GetProperty("connections").EnumerateArray().Single();
        Assert.Equal(("paused", true), (connection.Str("state"), connection.GetProperty("demo").GetBoolean()));
        await bob.PostJson("/api/demo/simulate/agent", new { scenario = "dance" }, HttpStatusCode.BadRequest);

        // A guest's demo agent cannot write drafts: the notice says why.
        var guest = await (await _api.SignedIn("claire")).PostJson("/api/demo/simulate/agent", new { scenario = "draft" });
        Assert.Equal(System.Text.Json.JsonValueKind.Null, guest.GetProperty("proposalId").ValueKind);
        Assert.Contains(guest.GetProperty("activity").EnumerateArray(), a => a.Str("summary").Contains("guest", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_guest_read_is_logged_when_restricted_and_reset_reloads_the_demo()
    {
        var alice = await _api.SignedIn("alice");
        var shared = await alice.PostJson("/api/demo/simulate/guest-read", new { kind = "application", id = "portal" });
        Assert.True(shared.GetProperty("read").GetBoolean());
        var hidden = await alice.PostJson("/api/demo/simulate/guest-read", new { kind = "application", id = "fraud" });
        Assert.Equal((false, 0), (hidden.GetProperty("read").GetBoolean(), hidden.GetProperty("accessLogLines").GetInt32()));
        HasNotice(hidden);

        await alice.PostJson("/api/demo/simulate/agent", new { scenario = "draft" });
        await alice.PostJson("/api/demo/reset", new { confirm = false }, HttpStatusCode.BadRequest);
        var reset = await alice.PostJson("/api/demo/reset", new { confirm = true });
        HasNotice(reset, "/");
        Assert.Empty((await alice.GetJson("/api/agents")).EnumerateArray());
        Assert.DoesNotContain("_Checked by", (await alice.GetJson("/api/applications/billing")).Str("description"), StringComparison.Ordinal);
        // The session survives the reset.
        Assert.Equal("alice", (await alice.GetJson("/api/auth/me")).Str("username"));
    }

    [Fact]
    public async Task Notices_follow_the_language_of_the_person()
    {
        var david = await _api.SignedIn("david");
        await david.PutJson("/api/auth/me/language", new { language = "fr" });
        var result = await david.PostJson("/api/demo/simulate/agent", new { scenario = "refused" });
        Assert.Contains("refusé", result.GetProperty("notice").Str("title"), StringComparison.Ordinal);
    }
}

/// <summary>Outside demo mode the toolbox does not exist.</summary>
[Collection("Api")]
public class DemoOffTests : IDisposable
{
    private readonly ApiFixture _api = new(demo: false);
    public void Dispose() => _api.Dispose();

    [Theory]
    [InlineData("GET", "/api/demo/state")]
    [InlineData("POST", "/api/demo/switch")]
    [InlineData("POST", "/api/demo/simulate/agent")]
    [InlineData("POST", "/api/demo/reset")]
    public async Task Demo_endpoints_are_not_found(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST") request.Content = JsonContent.Create(new { confirm = true });
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Client().SendAsync(request)).StatusCode);
    }
}
