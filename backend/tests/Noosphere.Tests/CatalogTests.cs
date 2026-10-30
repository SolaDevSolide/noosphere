using System.Net;

namespace Noosphere.Tests;

[Collection("Api")]
public class CatalogTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Create_derives_the_id_from_the_name_and_keeps_it_unique()
    {
        var bob = await _api.SignedIn("bob");
        var first = await bob.PostJson("/api/domains", new { name = "Human resources", order = 7 }, HttpStatusCode.Created);
        var second = await bob.PostJson("/api/domains", new { name = "Human Resources" }, HttpStatusCode.Created);
        Assert.Equal(("human-resources", "human-resources-2", 1), (first.Str("id"), second.Str("id"), first.GetProperty("version").GetInt32()));
        await bob.PostJson("/api/domains", new { id = "human-resources", name = "Again" }, HttpStatusCode.Conflict);
        var bad = await bob.PostJson("/api/domains", new { id = "Bad Id", name = "" }, HttpStatusCode.BadRequest);
        Assert.Equal(["id", "name"], bad.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order());
        await bob.GetJson("/api/domains/human-resources");
    }

    [Fact]
    public async Task Update_needs_the_current_version_and_bumps_it()
    {
        var bob = await _api.SignedIn("bob");
        var crm = await bob.GetJson("/api/applications/crm");
        Assert.Equal(1, crm.GetProperty("version").GetInt32());
        var body = new { name = "CRM 2", domainId = "customer", teamId = "team-crm", kind = "saas", tech = "Dynamics 365", lifecycle = "retiring", visibility = "everyone", version = 1 };
        var updated = await bob.PutJson("/api/applications/crm", body);
        Assert.Equal(("CRM 2", "saas", "retiring", 2), (updated.Str("name"), updated.Str("kind"), updated.Str("lifecycle"), updated.GetProperty("version").GetInt32()));
        Assert.True(updated.GetProperty("updatedAt").GetDateTimeOffset() > updated.GetProperty("createdAt").GetDateTimeOffset());

        var stale = await bob.PutJson("/api/applications/crm", body, HttpStatusCode.Conflict);
        Assert.Equal("Someone changed this record. Reload it and try again.", stale.Str("detail"));
        var missing = await bob.PutJson("/api/applications/crm", new { name = "CRM", domainId = "customer" }, HttpStatusCode.BadRequest);
        Assert.True(missing.GetProperty("errors").TryGetProperty("version", out _));
        await bob.PutJson("/api/applications/nope", body, HttpStatusCode.NotFound);
        await bob.PutJson("/api/applications/crm", new { id = "other", name = "CRM", domainId = "customer", version = 2 }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_enum_values_and_references_are_refused()
    {
        var bob = await _api.SignedIn("bob");
        await bob.PostJson("/api/applications", new { name = "X", domainId = "customer", kind = "mainframe" }, HttpStatusCode.BadRequest);
        await bob.PostJson("/api/applications", new { name = "X", domainId = "customer", kind = 2 }, HttpStatusCode.BadRequest);
        var refs = await bob.PostJson("/api/applications", new { name = "X", domainId = "nowhere", teamId = "nobody" }, HttpStatusCode.BadRequest);
        Assert.Equal(["domainId", "teamId"], refs.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order());
        var loop = await bob.PostJson("/api/flows", new { name = "Loop", fromId = "crm", toId = "crm" }, HttpStatusCode.BadRequest);
        Assert.True(loop.GetProperty("errors").TryGetProperty("toId", out _));
    }

    [Fact]
    public async Task A_flow_gets_computed_sensitivity_and_completeness()
    {
        var bob = await _api.SignedIn("bob");
        var flow = await bob.PostJson("/api/flows", new
        {
            name = "Claims export", fromId = "claims", toId = "dwh", type = "batch", schedule = "Daily",
            dataObjectIds = new[] { "invoice", "claim", "invoice" }, plannedChange = new { summary = "Moves to events", date = "2027-01-15" },
        }, HttpStatusCode.Created);
        Assert.Equal("claims-export", flow.Str("id"));
        Assert.Equal("sensitive", flow.Str("sensitivity"));
        Assert.Equal(["invoice", "claim"], flow.GetProperty("dataObjectIds").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(0.5, flow.GetProperty("completeness").GetDouble());
        Assert.Equal("2027-01-15", flow.GetProperty("plannedChange").Str("date"));
        Assert.Equal("live", flow.Str("status"));
    }

    [Fact]
    public async Task Records_in_use_cannot_be_deleted()
    {
        var alice = await _api.SignedIn("alice");
        foreach (var url in new[] { "/api/domains/finance", "/api/teams/team-crm", "/api/applications/crm", "/api/data-objects/customer" })
            Assert.Equal(HttpStatusCode.Conflict, await alice.Delete(url));

        // Once nothing uses them any more, they go.
        foreach (var flow in new[] { "acct-legacy", "billing-acct", "payments-acct" })
            Assert.Equal(HttpStatusCode.NoContent, await alice.Delete($"/api/flows/{flow}"));
        Assert.Equal(HttpStatusCode.NoContent, await alice.Delete("/api/applications/acct"));
        Assert.Equal(HttpStatusCode.NotFound, await alice.Delete("/api/applications/acct"));
    }

    [Fact]
    public async Task List_endpoints_filter()
    {
        var claire = await _api.SignedIn("claire");
        Assert.Equal(["acct", "billing", "payments"], (await claire.GetJson("/api/applications?domainId=finance")).Ids());
        Assert.Equal(["policies"], (await claire.GetJson("/api/applications?q=ORACLE")).Ids());
        Assert.Equal(["acct-legacy"], (await claire.GetJson("/api/flows?status=deprecated")).Ids());
        Assert.Equal(13, (await claire.GetJson("/api/flows?type=batch")).GetArrayLength());
        Assert.Equal(["claims-expert", "mobile-claims", "portal-claims"],
            (await claire.GetJson("/api/flows?type=api&dataObjectId=claim&teamId=team-claims")).Ids());
        Assert.Equal(["team-crm"], (await claire.GetJson("/api/teams?q=crm")).Ids());
        Assert.Equal(["payment"], (await claire.GetJson("/api/data-objects?q=paym")).Ids());
        Assert.Equal(["platform"], (await claire.GetJson("/api/domains?q=PLATFORM")).Ids());
        var bad = await claire.GetJson("/api/flows?type=soap", HttpStatusCode.BadRequest);
        Assert.True(bad.GetProperty("errors").TryGetProperty("type", out _));
    }
}
