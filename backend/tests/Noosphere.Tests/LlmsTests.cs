using System.Net;
using static Noosphere.Tests.AgentKit;

namespace Noosphere.Tests;

/// <summary>/llms.txt, /llms-full.txt and the Markdown views: who reads what.</summary>
[Collection("Api")]
public class LlmsTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static async Task<(HttpStatusCode Status, string Text, string? Type)> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Anonymous_readers_need_the_setting_and_see_only_public_records()
    {
        var anonymous = _api.Client();
        var refused = await anonymous.GetAsync("/llms.txt");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Contains("resource_metadata=", refused.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/application/crm.md")).StatusCode);

        await Settings(_api, new { anonymousLlms = true });
        var index = await Get(anonymous, "/llms.txt");
        Assert.Equal((HttpStatusCode.OK, "text/plain"), (index.Status, index.Type));
        Assert.StartsWith("# ", index.Text, StringComparison.Ordinal);
        Assert.Contains("/application/crm.md", index.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("/application/fraud.md", index.Text, StringComparison.Ordinal);
        var full = await Get(anonymous, "/llms-full.txt");
        Assert.Contains("Dynamics 365", full.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("svc-billing-run", full.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fraud scoring", full.Text, StringComparison.Ordinal);

        var crm = await Get(anonymous, "/application/crm.md");
        Assert.Equal((HttpStatusCode.OK, "text/markdown"), (crm.Status, crm.Type));
        Assert.StartsWith("---", crm.Text, StringComparison.Ordinal);
        var billing = await Get(anonymous, "/application/billing.md");
        Assert.Contains("protectedNotes: withheld", billing.Text, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync("/application/fraud.md")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/application/nope.md")).StatusCode);
        foreach (var url in new[] { "/flow/crm-to-billing-customers.md", "/data-object/customer.md", "/team/team-crm.md", "/domain/finance.md", "/page/glossary.md" })
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/robot/crm.md")).StatusCode);
    }

    [Fact]
    public async Task Signed_in_people_read_with_their_rights()
    {
        var bob = await _api.SignedIn("bob");
        var full = await Get(bob, "/llms-full.txt");
        Assert.Contains("Fraud scoring", full.Text, StringComparison.Ordinal);
        Assert.Contains("svc-billing-run", full.Text, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/application/fraud.md")).StatusCode);

        var david = await _api.SignedIn("david");
        Assert.Equal(HttpStatusCode.Forbidden, (await david.GetAsync("/application/fraud.md")).StatusCode);
        Assert.Contains("protectedNotes: withheld", (await Get(david, "/application/billing.md")).Text, StringComparison.Ordinal);

        // A guest reads what is shared with her only.
        var claire = await _api.SignedIn("claire");
        Assert.Equal(HttpStatusCode.OK, (await claire.GetAsync("/application/portal.md")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await claire.GetAsync("/application/billing.md")).StatusCode);
        Assert.DoesNotContain("/application/billing.md", (await Get(claire, "/llms.txt")).Text, StringComparison.Ordinal);

        // Reading a restricted record through its Markdown view is logged.
        var log = await (await _api.SignedIn("alice")).GetJson("/api/access-log?username=david&limit=100");
        Assert.DoesNotContain(log.GetProperty("items").EnumerateArray(), l => l.Str("entityId") == "fraud");
        await bob.GetAsync("/application/fraud.md");
        var forBob = await (await _api.SignedIn("alice")).GetJson("/api/access-log?entityId=fraud&limit=100");
        Assert.NotEmpty(forBob.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Agents_read_within_the_ceiling_and_are_counted()
    {
        var tokens = await Connect(_api, "bob", "docs:read");
        var agent = Bearer(_api, tokens.Access);
        var full = await Get(agent, "/llms-full.txt");
        Assert.Equal(HttpStatusCode.OK, full.Status);
        Assert.DoesNotContain("Fraud scoring", full.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("svc-billing-run", full.Text, StringComparison.Ordinal);
        var fraud = await Get(agent, "/application/fraud.md");
        Assert.Equal(HttpStatusCode.Forbidden, fraud.Status);
        Assert.Contains("ceiling", fraud.Text, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync("/application/nope.md")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/application/crm.md")).StatusCode);

        var tools = (await (await _api.SignedIn("bob")).GetJson("/api/agents/activity")).GetProperty("items").EnumerateArray().Select(a => a.Str("tool")).ToList();
        Assert.Contains("llms-full.txt", tools);
        Assert.Contains("markdown", tools);

        var invalid = await Bearer(_api, "nsa_not-a-token").GetAsync("/llms.txt");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Contains("invalid_token", invalid.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }
}
