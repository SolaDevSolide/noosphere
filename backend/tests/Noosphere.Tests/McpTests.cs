using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using Noosphere.Api.Agents;
using Noosphere.Api.Data;
using Noosphere.Domain;
using static Noosphere.Tests.AgentKit;

namespace Noosphere.Tests;

/// <summary>
/// The MCP tools through an in-process MCP client, the agent ceiling, quota, emergency stop, agents' drafts and the console.
/// Demo: Fraud scoring is restricted to Team Payments (bob); Billing has protected notes readable by Team Finance (bob).
/// </summary>
[Collection("Api")]
public class McpTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static readonly string[] ToolNames =
        ["data_lineage", "draft_change", "get_page", "get_proposal_status", "get_record", "list_landscape", "list_my_drafts", "search", "submit_draft", "who_owns"];

    [Fact]
    public async Task Reading_tools_answer_within_the_person_rights()
    {
        var mcp = await Mcp(_api, (await Connect(_api, "bob")).Access);
        Assert.Equal(ToolNames, (await mcp.ListToolsAsync()).Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.NotNull(mcp.ServerInstructions);

        var search = await CallJson(mcp, "search", new() { ["query"] = "billing" });
        Assert.Contains(search.GetProperty("results").EnumerateArray(), r => r.Str("kind") == "application" && r.Str("id") == "billing");
        var pages = await CallJson(mcp, "search", new() { ["query"] = "nightly", ["kinds"] = new[] { "page" } });
        Assert.All(pages.GetProperty("results").EnumerateArray(), r => Assert.Equal("page", r.Str("kind")));
        Assert.Contains("Unknown kind", (await Call(mcp, "search", new() { ["query"] = "x", ["kinds"] = new[] { "robot" } })).Text, StringComparison.Ordinal);
        Assert.True((await Call(mcp, "search", new() { ["query"] = " " })).Error);

        var (error, crm) = await Call(mcp, "get_record", new() { ["kind"] = "application", ["id"] = "crm" });
        Assert.False(error, crm);
        Assert.StartsWith("---", crm, StringComparison.Ordinal);
        Assert.Contains("Dynamics 365", crm, StringComparison.Ordinal);
        var missing = await Call(mcp, "get_record", new() { ["kind"] = "application", ["id"] = "nope" });
        Assert.True(missing.Error);
        Assert.Contains("No application with the id 'nope'", missing.Text, StringComparison.Ordinal);

        var page = await Call(mcp, "get_page", new() { ["id"] = "replay-nightly-import" });
        Assert.False(page.Error, page.Text);
        Assert.Contains("[[flow:crm-to-billing-customers]]", page.Text, StringComparison.Ordinal);

        var landscape = await CallJson(mcp, "list_landscape", new() { ["domain"] = "finance" });
        Assert.Contains(landscape.GetProperty("applications").EnumerateArray(), a => a.Str("id") == "billing");
        // Fraud scoring (restricted) is left out under the default ceiling, and said to be.
        var all = await CallJson(mcp, "list_landscape");
        Assert.DoesNotContain(all.GetProperty("applications").EnumerateArray(), a => a.Str("id") == "fraud");
        Assert.Contains("left out", all.Str("notVisible"), StringComparison.Ordinal);

        var lineage = await CallJson(mcp, "data_lineage", new() { ["dataObjectId"] = "customer" });
        Assert.Equal("crm", lineage.GetProperty("source").Str("id"));
        Assert.NotEmpty(lineage.GetProperty("flows").EnumerateArray());

        var owner = await CallJson(mcp, "who_owns", new() { ["kind"] = "application", ["id"] = "billing" });
        Assert.Equal("team-finance", owner.GetProperty("owningTeam").Str("id"));
        Assert.Contains(owner.GetProperty("owningTeam").GetProperty("members").EnumerateArray(), m => m.Str("username") == "bob");
        Assert.Contains("administrators only", (await CallJson(mcp, "who_owns", new() { ["kind"] = "domain", ["id"] = "finance" })).Str("howToChange"), StringComparison.OrdinalIgnoreCase);

        Assert.True((await Call(mcp, "get_record", new() { ["kind"] = "application", ["id"] = "fraud" })).Error);

        // Every call is one line of activity, under the person's name.
        var activity = (await (await _api.SignedIn("bob")).GetJson("/api/agents/activity?limit=100")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(activity, a => a.Str("tool") == "search" && a.Str("summary").Contains("\"billing\"", StringComparison.Ordinal));
        Assert.Contains(activity, a => a.Str("tool") == "get_record" && a.Str("recordId") == "crm" && a.Str("recordTitle") == "CRM");
        Assert.Contains(activity, a => a.Str("tool") == "get_record" && a.Str("kind") == "refused");
        Assert.All(activity, a => Assert.Equal("bob", a.GetProperty("person").Str("username")));
        Assert.Empty((await (await _api.SignedIn("david")).GetJson("/api/agents/activity")).GetProperty("items").EnumerateArray());
    }

    /// <summary>Restricted record and protected notes, for bob (who sees both), under each ceiling.</summary>
    [Theory]
    [InlineData("public", false, false, false)]
    [InlineData("public+protected", false, false, true)]
    [InlineData("asUser", false, false, true)]
    [InlineData("asUser", true, true, true)]
    public async Task The_ceiling_limits_what_agents_read(string ceiling, bool restrictedScope, bool readsFraud, bool readsNotes)
    {
        await Settings(_api, new { ceiling });
        var scope = "docs:read" + (restrictedScope ? " docs:read:restricted" : "");
        var tokens = await Connect(_api, "bob", scope);
        Assert.Equal(scope, tokens.Scope);
        var mcp = await Mcp(_api, tokens.Access);

        var fraud = await Call(mcp, "get_record", new() { ["kind"] = "application", ["id"] = "fraud" });
        Assert.Equal(!readsFraud, fraud.Error);
        if (!readsFraud) Assert.Contains("Refused (ceiling)", fraud.Text, StringComparison.Ordinal);
        var found = await CallJson(mcp, "search", new() { ["query"] = "fraud" });
        Assert.Equal(readsFraud, found.GetProperty("results").EnumerateArray().Any(r => r.Str("id") == "fraud"));

        var (error, billing) = await Call(mcp, "get_record", new() { ["kind"] = "application", ["id"] = "billing" });
        Assert.False(error, billing);
        Assert.Equal(readsNotes, billing.Contains("svc-billing-run", StringComparison.Ordinal));
        Assert.Equal(!readsNotes, billing.Contains("protectedNotes: withheld", StringComparison.Ordinal));

        // A person's own limits stay: david is not on Fraud scoring's access list, nor in Team Finance.
        var david = await Mcp(_api, (await Connect(_api, "david", scope)).Access);
        var forDavid = await Call(david, "get_record", new() { ["kind"] = "application", ["id"] = "fraud" });
        Assert.True(forDavid.Error);
        Assert.Contains("Refused (visibility)", forDavid.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("svc-billing-run", (await Call(david, "get_record", new() { ["kind"] = "application", ["id"] = "billing" })).Text, StringComparison.Ordinal);

        // Reads of restricted records and protected notes are in the access log, under the person, with the agent.
        var log = await (await _api.SignedIn("alice")).GetJson("/api/access-log?limit=100");
        var lines = log.GetProperty("items").EnumerateArray().Where(l => l.TryGetProperty("detail", out var d) && d.GetString()?.StartsWith("AI agent", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(readsFraud, lines.Any(l => l.Str("entityId") == "fraud"));
        // Bob's own team owns Billing: reading its notes is not logged.
        Assert.DoesNotContain(lines, l => l.Str("entityId") == "billing");
    }

    [Fact]
    public async Task Restricted_scope_is_not_granted_below_the_as_user_ceiling()
    {
        var tokens = await Connect(_api, "bob", "docs:read docs:read:restricted");
        Assert.Equal("docs:read", tokens.Scope);
    }

    [Fact]
    public async Task Agents_draft_and_submit_but_never_publish()
    {
        var tokens = await Connect(_api, "bob", name: "Doc helper");
        var mcp = await Mcp(_api, tokens.Access);
        Assert.Equal(0, (await CallJson(mcp, "list_my_drafts")).GetProperty("count").GetInt32());

        var draft = await CallJson(mcp, "draft_change", new()
        {
            ["kind"] = "application", ["id"] = "billing", ["operation"] = "update",
            ["fields"] = new Dictionary<string, object> { ["description"] = "Invoices premiums and credit notes." }, ["comment"] = "Credit notes were missing.",
        });
        Assert.True(draft.GetProperty("saved").GetBoolean());
        var id = draft.Str("draftId");
        Assert.Equal("description", draft.GetProperty("changedFields").EnumerateArray().Single().Str("field"));
        // A second call on the same record updates the same draft.
        var again = await CallJson(mcp, "draft_change", new()
        {
            ["kind"] = "application", ["id"] = "billing", ["operation"] = "update",
            ["fields"] = new Dictionary<string, object> { ["description"] = "Invoices premiums, credit notes and refunds." },
        });
        Assert.Equal(id, again.Str("draftId"));
        Assert.Equal(1, (await CallJson(mcp, "list_my_drafts")).GetProperty("count").GetInt32());

        // Refusals: unknown fields, secrets, a guest's record kind... and nothing is saved.
        Assert.Contains("Unknown field 'colour'", (await Call(mcp, "draft_change", new() { ["kind"] = "application", ["id"] = "crm", ["operation"] = "update", ["fields"] = new Dictionary<string, object> { ["colour"] = "red" } })).Text, StringComparison.Ordinal);
        var secret = await Call(mcp, "draft_change", new()
        {
            ["kind"] = "application", ["id"] = "crm", ["operation"] = "update",
            ["fields"] = new Dictionary<string, object> { ["description"] = "Token: " + "gh" + "p_" + new string('a', 36) },
        });
        Assert.True(secret.Error);
        Assert.Contains("Refused (secret)", secret.Text, StringComparison.Ordinal);
        Assert.Contains("Refused (protected notes)", (await Call(mcp, "draft_change", new() { ["kind"] = "application", ["id"] = "billing", ["operation"] = "update", ["fields"] = new Dictionary<string, object> { ["protectedNotes"] = "x" } })).Text, StringComparison.Ordinal);
        Assert.Contains("unchangeable kind", (await Call(mcp, "draft_change", new() { ["kind"] = "team", ["id"] = "team-crm", ["operation"] = "update", ["fields"] = new Dictionary<string, object> { ["name"] = "x" } })).Text, StringComparison.Ordinal);
        var created = await CallJson(mcp, "draft_change", new()
        {
            ["kind"] = "page", ["id"] = null, ["operation"] = "create",
            ["fields"] = new Dictionary<string, object> { ["title"] = "Invoice run checks", ["body"] = "Checks before [[application:billing]] invoices." },
        });
        Assert.Equal("invoice-run-checks", created.Str("targetId"));

        // The draft is the person's (and administrators'); nobody else sees it.
        var bob = await _api.SignedIn("bob");
        var alice = await _api.SignedIn("alice");
        var forBob = await bob.GetJson($"/api/proposals/{id}");
        Assert.Equal(("agent", "draft", "Doc helper", "bob"), (forBob.Str("authorKind"), forBob.Str("status"), forBob.Str("agentName"), forBob.GetProperty("onBehalfOf").Str("username")));
        Assert.Equal("draft", (await alice.GetJson($"/api/proposals/{id}")).Str("status"));
        await (await _api.SignedIn("david")).GetJson($"/api/proposals/{id}", HttpStatusCode.NotFound);
        Assert.DoesNotContain(id, (await alice.GetJson("/api/proposals")).Ids());
        var mine = await bob.GetJson("/api/me/agents");
        Assert.Equal(2, mine.GetProperty("drafts").GetArrayLength());
        Assert.Equal(2, mine.GetProperty("connections").EnumerateArray().Single().GetProperty("openDrafts").GetInt32());

        var submitted = await CallJson(mcp, "submit_draft", new() { ["draftId"] = id, ["title"] = "Billing: credit notes and refunds" });
        Assert.Equal("submitted", submitted.Str("status"));
        Assert.Contains("Team Finance", submitted.GetProperty("validators").EnumerateArray().Select(v => v.GetString()));
        Assert.Contains("already submitted", (await Call(mcp, "submit_draft", new() { ["draftId"] = id, ["title"] = "Again" })).Text, StringComparison.Ordinal);
        Assert.Equal("submitted", (await CallJson(mcp, "get_proposal_status", new() { ["id"] = id })).Str("status"));
        // Submitted, it is visible to everyone who sees the record; the owning team decides.
        var forDavid = await (await _api.SignedIn("david")).GetJson($"/api/proposals/{id}");
        Assert.Equal(("Doc helper", "bob"), (forDavid.Str("agentName"), forDavid.GetProperty("onBehalfOf").Str("username")));
        await alice.PostJson($"/api/proposals/{id}/refuse", new { reason = "Refunds are in Payments." });
        var status = await CallJson(mcp, "get_proposal_status", new() { ["id"] = id });
        Assert.Equal(("refused", "Refunds are in Payments."), (status.Str("status"), status.Str("refusalReason")));
        Assert.Contains("No proposal", (await Call(mcp, "get_proposal_status", new() { ["id"] = Guid.NewGuid().ToString() })).Text, StringComparison.Ordinal);

        // Billing was not changed by the agent.
        Assert.DoesNotContain("refunds", (await bob.GetJson("/api/applications/billing")).Str("description"), StringComparison.Ordinal);
        var kinds = (await bob.GetJson("/api/agents/activity?limit=100")).GetProperty("items").EnumerateArray().Select(a => a.Str("kind")).ToHashSet();
        Assert.Superset(new HashSet<string> { "read", "draft", "proposal", "refused" }, kinds);
    }

    [Fact]
    public async Task Read_only_and_guest_agents_cannot_draft()
    {
        var reader = await Mcp(_api, (await Connect(_api, "bob", "docs:read")).Access);
        Assert.Contains("docs:draft", (await Call(reader, "list_my_drafts")).Text, StringComparison.Ordinal);
        // A guest cannot grant docs:draft, and reads only what is shared with her.
        var tokens = await Connect(_api, "claire");
        Assert.Equal("docs:read", tokens.Scope);
        var guest = await Mcp(_api, tokens.Access);
        Assert.False((await Call(guest, "get_record", new() { ["kind"] = "application", ["id"] = "portal" })).Error);
        Assert.Contains("guest", (await Call(guest, "get_record", new() { ["kind"] = "application", ["id"] = "billing" })).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_hourly_quota_pauses_the_agent()
    {
        await Settings(_api, new { quotaPerHour = 3 });
        var tokens = await Connect(_api, "bob");
        var mcp = await Mcp(_api, tokens.Access);
        for (var i = 0; i < 3; i++) Assert.False((await Call(mcp, "search", new() { ["query"] = "crm" })).Error);
        var e = await Assert.ThrowsAsync<McpProtocolException>(() => Call(mcp, "search", new() { ["query"] = "crm" }));
        Assert.Equal(-32029, (int)e.ErrorCode);
        Assert.Contains("quota", e.Message, StringComparison.OrdinalIgnoreCase);
        var connection = (await (await _api.SignedIn("alice")).GetJson("/api/agents")).EnumerateArray().Single();
        Assert.Equal("paused", connection.Str("state"));
        Assert.Equal(3, connection.GetProperty("callsLastHour").GetInt32());
        Assert.Contains((await (await _api.SignedIn("bob")).GetJson("/api/agents/activity")).GetProperty("items").EnumerateArray(), a => a.Str("kind") == "quota");
        // Still paused even when the quota is raised, until the next hour.
        await Settings(_api, new { quotaPerHour = 600 });
        await Assert.ThrowsAsync<McpProtocolException>(() => Call(mcp, "search", new() { ["query"] = "crm" }));
    }

    [Fact]
    public async Task Turning_agents_off_or_the_emergency_stop_ends_every_connection()
    {
        var bobTokens = await Connect(_api, "bob");
        var davidTokens = await Connect(_api, "david");
        var bob = await Mcp(_api, bobTokens.Access);
        Assert.False((await Call(bob, "search", new() { ["query"] = "crm" })).Error);

        await Settings(_api, new { enabled = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await McpPost(davidTokens.Access)).StatusCode);
        await Settings(_api, new { enabled = true });
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await McpPost(davidTokens.Access)).StatusCode);

        var alice = await _api.SignedIn("alice");
        await (await _api.SignedIn("bob")).PostJson("/api/agents/stop-all", new { confirm = true }, HttpStatusCode.Forbidden);
        await alice.PostJson("/api/agents/stop-all", new { confirm = false }, HttpStatusCode.BadRequest);
        var stop = await alice.PostJson("/api/agents/stop-all", new { confirm = true });
        Assert.Equal(2, stop.GetProperty("revokedConnections").GetInt32());
        Assert.True(stop.GetProperty("revokedTokens").GetInt32() >= 4);

        Assert.Equal(HttpStatusCode.Unauthorized, (await McpPost(bobTokens.Access)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await McpPost(davidTokens.Access)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(_api, bobTokens.ClientId, bobTokens.Refresh)).StatusCode);
        Assert.All((await alice.GetJson("/api/agents")).EnumerateArray(), c => Assert.Equal("revoked", c.Str("state")));
        Assert.Equal(2, (await alice.GetJson("/api/agents/activity?kind=revoked")).GetProperty("items").GetArrayLength());
        // A new connection works again.
        Assert.False((await Call(await Mcp(_api, (await Connect(_api, "bob")).Access), "search", new() { ["query"] = "crm" })).Error);
    }

    private Task<HttpResponseMessage> McpPost(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return _api.Client().SendAsync(request);
    }

    [Fact]
    public async Task People_see_and_revoke_their_agents_and_administrators_every_agent()
    {
        var bobTokens = await Connect(_api, "bob", name: "Bob's assistant");
        await Connect(_api, "david", name: "David's assistant");
        var bob = await _api.SignedIn("bob");
        var alice = await _api.SignedIn("alice");
        var david = await _api.SignedIn("david");

        var mine = await bob.GetJson("/api/me/agents");
        var connection = mine.GetProperty("connections").EnumerateArray().Single();
        Assert.Equal(("Bob's assistant", "active", "bob"), (connection.Str("clientName"), connection.Str("state"), connection.GetProperty("person").Str("username")));
        Assert.Equal("public", mine.GetProperty("settings").Str("ceiling"));
        Assert.EndsWith("/mcp", mine.GetProperty("settings").Str("mcpUrl"), StringComparison.Ordinal);
        Assert.Equal(2, (await alice.GetJson("/api/agents")).GetArrayLength());
        await bob.GetJson("/api/agents", HttpStatusCode.Forbidden);

        var id = connection.Str("id");
        var capabilities = await bob.GetJson($"/api/agents/{id}/capabilities");
        var items = capabilities.GetProperty("items").EnumerateArray().ToDictionary(c => c.Str("key"), c => c.GetProperty("allowed").GetBoolean());
        Assert.Equal((true, true, false, false), (items["read"], items["draft"], items["publish"], items["restricted"]));
        Assert.NotEmpty(capabilities.GetProperty("items")[0].GetProperty("text").Str("fr"));
        await david.GetJson($"/api/agents/{id}/capabilities", HttpStatusCode.NotFound);
        await david.PostJson($"/api/agents/{id}/revoke", new { }, HttpStatusCode.NotFound);
        await david.GetJson($"/api/agents/activity?connectionId={id}", HttpStatusCode.NotFound);

        Assert.Equal("revoked", (await bob.PostJson($"/api/agents/{id}/revoke", new { })).Str("state"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await McpPost(bobTokens.Access)).StatusCode);
        Assert.Contains((await bob.GetJson($"/api/agents/activity?connectionId={id}")).GetProperty("items").EnumerateArray(), a => a.Str("kind") == "revoked");
    }

    [Fact]
    public async Task Settings_are_read_by_everyone_and_changed_by_administrators()
    {
        var bob = await _api.SignedIn("bob");
        var settings = await bob.GetJson("/api/settings/agents");
        Assert.Equal((true, "public", 600, "anyMember"), (settings.GetProperty("enabled").GetBoolean(), settings.Str("ceiling"), settings.GetProperty("quotaPerHour").GetInt32(), settings.Str("whoConnects")));
        await bob.PutJson("/api/settings/agents", new { ceiling = "asUser", version = settings.GetProperty("version").GetInt32() }, HttpStatusCode.Forbidden);
        var changed = await Settings(_api, new { ceiling = "public+protected", quotaPerHour = 100 });
        Assert.Equal(("public+protected", 100), (changed.Str("ceiling"), changed.GetProperty("quotaPerHour").GetInt32()));
        var alice = await _api.SignedIn("alice");
        await alice.PutJson("/api/settings/agents", new { quotaPerHour = 0, version = changed.GetProperty("version").GetInt32() }, HttpStatusCode.BadRequest);
        await alice.PutJson("/api/settings/agents", new { quotaPerHour = 50, version = 0 }, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Old_activity_and_expired_tokens_are_purged()
    {
        var tokens = await Connect(_api, "bob");
        await Call(await Mcp(_api, tokens.Access), "search", new() { ["query"] = "crm" });
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
        var old = DateTimeOffset.UtcNow.AddDays(-91);
        db.AgentActivities.Add(new AgentActivity { At = old, ConnectionId = db.AgentConnections.Single().Id, Tool = "search", Kind = AgentActivityKind.Read, Summary = "Old." });
        foreach (var t in db.AgentTokens) t.ExpiresAt = old;
        await db.SaveChangesAsync();
        await AgentRuntime.Purge(db, 90, DateTimeOffset.UtcNow);
        Assert.DoesNotContain(db.AgentActivities, a => a.Summary == "Old.");
        Assert.NotEmpty(db.AgentActivities);
        Assert.Empty(db.AgentTokens);
    }
}
