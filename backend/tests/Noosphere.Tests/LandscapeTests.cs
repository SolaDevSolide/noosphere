using System.Net;
using System.Text.Json;

namespace Noosphere.Tests;

[Collection("Api")]
public class LandscapeTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static JsonElement Find(JsonElement list, string id) => list.EnumerateArray().Single(x => x.Str("id") == id);

    [Fact]
    public async Task Demo_landscape_is_complete()
    {
        var landscape = await (await _api.SignedIn("alice")).GetJson("/api/landscape");
        var stats = landscape.GetProperty("stats");
        Assert.Equal((20, 36, 6), (stats.GetProperty("applications").GetInt32(), stats.GetProperty("flows").GetInt32(), stats.GetProperty("domains").GetInt32()));
        Assert.Equal(5, landscape.GetProperty("dataObjects").GetArrayLength());
        Assert.Equal(11, landscape.GetProperty("teams").GetArrayLength());
        Assert.Equal(["customer", "contracts", "claims", "finance", "platform", "external"], landscape.GetProperty("domains").Ids());
        Assert.True(landscape.TryGetProperty("generatedAt", out _));

        var crm = Find(landscape.GetProperty("applications"), "crm");
        Assert.Equal(["customer"], crm.GetProperty("sourceOf").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(1, crm.GetProperty("completeness").GetDouble());
        // External partner: no tech, no description.
        Assert.Equal(0.6, Find(landscape.GetProperty("applications"), "bank").GetProperty("completeness").GetDouble());

        var flow = Find(landscape.GetProperty("flows"), "crm-to-billing-customers");
        Assert.Equal(("Every night 02:00", "personal", "15,000 lines"), (flow.Str("schedule"), flow.Str("sensitivity"), flow.Str("volume")));
        Assert.Equal("Billing 4.3 on 4 Nov changes the target API to v4", flow.GetProperty("plannedChange").Str("summary"));
        // Sensitivity is computed from the carried objects (Payment is sensitive).
        Assert.Equal("sensitive", Find(landscape.GetProperty("flows"), "payments-acct").Str("sensitivity"));
    }

    [Fact]
    public async Task Restricted_applications_are_hidden_from_non_admins()
    {
        foreach (var name in new[] { "david", "claire" })
        {
            var client = await _api.SignedIn(name);
            var landscape = await client.GetJson("/api/landscape");
            var fraud = Find(landscape.GetProperty("applications"), "fraud");
            Assert.Equal(["id", "restricted", "domainId"], fraud.EnumerateObject().Select(p => p.Name));
            Assert.True(fraud.GetProperty("restricted").GetBoolean());
            var flow = Find(landscape.GetProperty("flows"), "payments-fraud");
            Assert.Equal(["id", "restricted", "fromId", "toId", "type"], flow.EnumerateObject().Select(p => p.Name));
            Assert.False(Find(landscape.GetProperty("applications"), "crm").GetProperty("restricted").GetBoolean());

            Assert.Equal(["id", "restricted", "domainId"], (await client.GetJson("/api/applications/fraud")).EnumerateObject().Select(p => p.Name));
            Assert.True((await client.GetJson("/api/flows/payments-fraud")).GetProperty("restricted").GetBoolean());
            Assert.Empty((await client.GetJson("/api/search?q=fraud")).EnumerateArray());
            Assert.Empty((await client.GetJson("/api/applications?q=scoring")).EnumerateArray());
            Assert.DoesNotContain("Fraud scoring", await client.GetStringAsync("/api/export"), StringComparison.Ordinal);
        }
        var david = await _api.SignedIn("david");
        await david.PutJson("/api/applications/fraud", new { name = "Mine", domainId = "external", version = 1 }, HttpStatusCode.Forbidden);
        await david.PutJson("/api/flows/payments-fraud", new { name = "Mine", fromId = "payments", toId = "fraud", version = 1 }, HttpStatusCode.Forbidden);
        // bob is in Team Payments, on the access list: he sees it in full.
        var bob = await _api.SignedIn("bob");
        Assert.Equal("Fraud scoring", (await bob.GetJson("/api/applications/fraud")).Str("name"));
        Assert.False((await bob.GetJson("/api/flows/payments-fraud")).GetProperty("restricted").GetBoolean());

        var alice = await _api.SignedIn("alice");
        var full = await alice.GetJson("/api/applications/fraud");
        Assert.Equal(("Fraud scoring", "restricted", false), (full.Str("name"), full.Str("visibility"), full.GetProperty("restricted").GetBoolean()));
        Assert.Equal("Fraud scoring", (await alice.GetJson("/api/search?q=fraud"))[0].Str("title"));
    }

    [Fact]
    public async Task Application_filters_keep_matching_applications_and_their_flows()
    {
        var client = await _api.SignedIn("claire");
        var landscape = await client.GetJson("/api/landscape?domainId=claims");
        var flows = landscape.GetProperty("flows").Ids();
        var apps = landscape.GetProperty("applications").Ids();
        Assert.Contains("claims", apps);
        Assert.Contains("expert", apps);
        Assert.Contains("portal", apps); // other end of portal-claims
        Assert.Contains("portal-claims", flows);
        Assert.DoesNotContain("crm-dwh", flows);
        // Every flow comes with both of its ends.
        foreach (var f in landscape.GetProperty("flows").EnumerateArray())
        {
            Assert.Contains(f.Str("fromId"), apps);
            Assert.Contains(f.Str("toId"), apps);
        }
        Assert.Equal(apps.Count, landscape.GetProperty("stats").GetProperty("applications").GetInt32());
    }

    [Fact]
    public async Task Flow_filters_keep_matching_flows_and_their_ends_only()
    {
        var client = await _api.SignedIn("david");
        var landscape = await client.GetJson("/api/landscape?type=batch&dataObjectId=payment");
        Assert.Equal(["bank-payments", "payments-acct", "payments-bank"], landscape.GetProperty("flows").Ids());
        Assert.Equal(["acct", "bank", "payments"], landscape.GetProperty("applications").Ids());
        Assert.Equal(["finance", "external"], landscape.GetProperty("domains").Ids());
        Assert.Equal(2, landscape.GetProperty("stats").GetProperty("domains").GetInt32());
        // Restricted fields never match: the restricted flow is not found through its data object.
        Assert.DoesNotContain("payments-fraud", (await client.GetJson("/api/landscape?type=api&dataObjectId=payment")).GetProperty("flows").Ids());
    }

    [Fact]
    public async Task Search_is_accent_insensitive_and_ranks_name_prefixes_first()
    {
        var client = await _api.SignedIn("claire");
        var results = await client.GetJson("/api/search?q=RESEAU");
        var team = results.EnumerateArray().First();
        Assert.Equal(("team", "reseau-auto", "Réseau Auto+"), (team.Str("kind"), team.Str("id"), team.Str("title")));

        var pay = await client.GetJson("/api/search?q=pay");
        Assert.Equal(("application", "payments"), (pay[0].Str("kind"), pay[0].Str("id")));
        Assert.Contains(pay.EnumerateArray(), r => r.Str("kind") == "dataObject" && r.Str("id") == "payment");
        Assert.Contains(pay.EnumerateArray(), r => r.Str("kind") == "team" && r.Str("id") == "team-payments");
        var flow = (await client.GetJson("/api/search?q=direct%20debits"))[0];
        Assert.Equal(("flow", "Payments → Bank (SEPA)"), (flow.Str("kind"), flow.Str("subtitle")));

        Assert.True((await client.GetJson("/api/search?q=e")).GetArrayLength() == 20);
        Assert.Empty((await client.GetJson("/api/search?q=")).EnumerateArray());
    }
}
