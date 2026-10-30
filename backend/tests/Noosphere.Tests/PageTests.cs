using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Noosphere.Domain;

namespace Noosphere.Tests;

public class WikiLinkTests
{
    [Fact]
    public void Links_are_extracted_with_labels_once_per_target()
    {
        var links = Rules.WikiLinks("""
            See [[application:billing|the billing app]] and [[flow:crm-to-billing-customers]].
            Again [[application:billing]], a [[dataObject:customer]], [[page:glossary| ]] and [[team:team-crm]].
            Not links: [[Application:billing]], [[app:billing]], [[page:Bad Id]], [application:billing], `[[page:]]`.
            """);
        Assert.Equal(
            [(RecordKind.Application, "billing", "the billing app"), (RecordKind.Flow, "crm-to-billing-customers", null),
                (RecordKind.DataObject, "customer", null), (RecordKind.Page, "glossary", null), (RecordKind.Team, "team-crm", null)],
            links.Select(l => (l.Kind, l.Id, l.Label)));
        Assert.Equal("dataObject:customer", links[2].Key);
        Assert.Empty(Rules.WikiLinks(null));
    }
}

/// <summary>Documentation pages. Demo: 7 pages, the runbook is attached to crm-to-billing-customers (owned by Team CRM).</summary>
[Collection("Api")]
public class PageTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task A_page_resolves_its_links_and_backlinks_and_flags_broken_ones()
    {
        var claire = await _api.SignedIn("claire");
        var page = await claire.GetJson("/api/pages/replay-nightly-import");
        Assert.Equal(("Replay the nightly customer import", "flow", "crm-to-billing-customers", "Customers"),
            (page.Str("title"), page.GetProperty("attachedTo").Str("kind"), page.GetProperty("attachedTo").Str("id"), page.Str("attachedTitle")));
        Assert.Equal("team-crm", page.Str("owningTeamId"));
        Assert.Equal(["operations"], page.GetProperty("ancestors").EnumerateArray().Select(a => a.Str("id")));

        var links = page.GetProperty("links").EnumerateArray().ToList();
        var billing = links.Single(l => l.Str("id") == "billing");
        Assert.Equal(("application", "Billing", true, "Billing"), (billing.Str("kind"), billing.Str("title"), billing.GetProperty("exists").GetBoolean(), billing.Str("label")));
        var broken = links.Single(l => !l.GetProperty("exists").GetBoolean());
        Assert.Equal(("page", "billing-api-v4-cutover"), (broken.Str("kind"), broken.Str("id")));
        Assert.Equal(JsonValueKind.Null, broken.GetProperty("title").ValueKind);
        Assert.Equal(1, (await claire.GetJson("/api/pages")).EnumerateArray().Single(p => p.Str("id") == "replay-nightly-import").GetProperty("brokenLinks").GetInt32());

        var adr = await claire.GetJson("/api/pages/adr-nightly-file");
        Assert.Equal(["architecture", "replay-nightly-import"], adr.GetProperty("backlinks").EnumerateArray().Select(b => b.Str("id")));

        // Records list their attached pages and the pages mentioning them.
        var flow = await claire.GetJson("/api/flows/crm-to-billing-customers");
        Assert.Equal(["replay-nightly-import"], flow.GetProperty("pages").Ids());
        Assert.Equal(["adr-nightly-file", "finance-overview", "replay-nightly-import"], flow.GetProperty("mentionedIn").Ids().Order(StringComparer.Ordinal));
        var finance = await claire.GetJson("/api/domains/finance");
        Assert.Equal(["finance-overview"], finance.GetProperty("pages").Ids());
        Assert.Contains("glossary", (await claire.GetJson("/api/data-objects/customer")).GetProperty("mentionedIn").Ids());
        Assert.Contains("finance-overview", (await claire.GetJson("/api/teams/team-payments")).GetProperty("mentionedIn").Ids());
    }

    [Fact]
    public async Task Saving_a_page_extracts_its_links_and_follows_the_ownership_rules()
    {
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");
        var page = await david.GetJson("/api/pages/replay-nightly-import");
        object Body(string body, int version) => new
        {
            title = page.Str("title"), body, parentId = "operations", order = 1, attachedTo = new { kind = "flow", id = "crm-to-billing-customers" },
            tags = new[] { "runbook", "runbook", " crm " }, version,
        };
        // Owned through the flow it is attached to: Team CRM.
        await bob.PutJson("/api/pages/replay-nightly-import", Body("x", 1), HttpStatusCode.Forbidden);
        var saved = await david.PutJson("/api/pages/replay-nightly-import", Body("Now see [[page:glossary]] and [[application:payments]].", 1));
        Assert.Equal(2, saved.GetProperty("version").GetInt32());
        Assert.Equal(["runbook", "crm"], saved.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains("replay-nightly-import", (await bob.GetJson("/api/pages/glossary")).GetProperty("backlinks").Ids());
        Assert.DoesNotContain("replay-nightly-import", (await bob.GetJson("/api/pages/adr-nightly-file")).GetProperty("backlinks").Ids());
        Assert.Contains("replay-nightly-import", (await bob.GetJson("/api/applications/payments")).GetProperty("mentionedIn").Ids());

        // A free-standing page without owner team belongs to administrators.
        await bob.PostJson("/api/pages", new { title = "Loose notes" }, HttpStatusCode.Forbidden);
        var created = await bob.PostJson("/api/pages", new { title = "Finance FAQ", body = "", parentId = "finance-overview", ownerTeamId = "team-finance" }, HttpStatusCode.Created);
        Assert.Equal(("finance-faq", 1), (created.Str("id"), created.GetProperty("version").GetInt32()));
        var bad = await bob.PostJson("/api/pages", new { title = "", parentId = "nowhere", attachedTo = new { kind = "page", id = "glossary" }, ownerTeamId = "team-finance" },
            HttpStatusCode.BadRequest);
        Assert.Equal(["attachedTo.kind", "parentId", "title"], bad.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Pages_form_a_tree_that_refuses_cycles()
    {
        var alice = await _api.SignedIn("alice");
        var tree = await alice.GetJson("/api/pages/tree");
        Assert.Equal(["operations", "architecture", "finance-overview", "glossary"], tree.Ids());
        Assert.Equal(["replay-nightly-import", "month-end-closing"], tree[0].GetProperty("children").Ids());

        var moved = await alice.PostJson("/api/pages/month-end-closing/move", new { parentId = "finance-overview", order = 0 });
        Assert.Equal(("finance-overview", 2), (moved.Str("parentId"), moved.GetProperty("version").GetInt32()));
        Assert.Equal("Moved", (await alice.GetJson("/api/pages/month-end-closing/history"))[0].Str("summary"));
        var cycle = await alice.PostJson("/api/pages/operations/move", new { parentId = "replay-nightly-import" }, HttpStatusCode.BadRequest);
        Assert.True(cycle.GetProperty("errors").TryGetProperty("parentId", out _));
        await alice.PostJson("/api/pages/operations/move", new { parentId = "operations" }, HttpStatusCode.BadRequest);
        await alice.PostJson("/api/pages/glossary/move", new { order = 0, version = 7 }, HttpStatusCode.Conflict);
        var top = await alice.PostJson("/api/pages/month-end-closing/move", new { order = 9 });
        Assert.Equal(JsonValueKind.Null, top.GetProperty("parentId").ValueKind);

        // A page with sub-pages is not deleted.
        Assert.Equal(HttpStatusCode.Conflict, await alice.Delete("/api/pages/operations"));
        Assert.Equal(HttpStatusCode.NoContent, await alice.Delete("/api/pages/replay-nightly-import"));
        Assert.Equal(HttpStatusCode.NoContent, await alice.Delete("/api/pages/operations"));
    }

    [Fact]
    public async Task Raw_markdown_has_a_front_matter()
    {
        var claire = await _api.SignedIn("claire");
        var response = await claire.GetAsync("/api/pages/replay-nightly-import/raw");
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("---\nid: replay-nightly-import\ntitle: \"Replay the nightly customer import\"\nparent: operations\n", raw, StringComparison.Ordinal);
        Assert.Contains("attachedTo: flow:crm-to-billing-customers\nowner: team-crm\n", raw, StringComparison.Ordinal);
        Assert.Contains("  - page:billing-api-v4-cutover  # broken\n", raw, StringComparison.Ordinal);
        Assert.Contains("\n---\n\nUse this runbook when [[flow:crm-to-billing-customers]]", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restricted_pages_are_stubs_for_non_admins()
    {
        var alice = await _api.SignedIn("alice");
        await alice.PostJson("/api/pages", new { id = "incident-2026-10", title = "Incident review", body = "Fraud scores leaked: [[application:fraud]].", parentId = "operations",
            visibility = "restricted" }, HttpStatusCode.Created);
        await alice.PostJson("/api/pages", new { title = "Fraud overview", body = "Uses [[application:fraud]].", ownerTeamId = "team-data" }, HttpStatusCode.Created);
        var bob = await _api.SignedIn("bob");

        var stub = await bob.GetJson("/api/pages/incident-2026-10");
        Assert.Equal(["id", "restricted", "parentId", "order"], stub.EnumerateObject().Select(p => p.Name));
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/pages/incident-2026-10/raw")).StatusCode);
        var node = (await bob.GetJson("/api/pages/tree")).EnumerateArray().Single(n => n.Str("id") == "operations").GetProperty("children").EnumerateArray().Single(c => c.Str("id") == "incident-2026-10");
        Assert.Equal((true, JsonValueKind.Null), (node.GetProperty("restricted").GetBoolean(), node.GetProperty("title").ValueKind));
        Assert.DoesNotContain((await bob.GetJson("/api/search?q=incident")).EnumerateArray(), r => r.Str("kind") == "page");
        Assert.DoesNotContain("incident-2026-10", (await bob.GetJson("/api/pages/operations")).GetProperty("children").Ids());

        // A link to a restricted record does not reveal its name.
        var link = (await bob.GetJson("/api/pages/fraud-overview")).GetProperty("links")[0];
        Assert.Equal((true, true, JsonValueKind.Null), (link.GetProperty("exists").GetBoolean(), link.GetProperty("restricted").GetBoolean(), link.GetProperty("title").ValueKind));
        Assert.Equal("Fraud scoring", (await alice.GetJson("/api/pages/fraud-overview")).GetProperty("links")[0].Str("title"));
        var export = await bob.GetJson("/api/export");
        Assert.DoesNotContain(export.GetProperty("items").EnumerateArray(), i => i.Str("id") == "incident-2026-10");
    }

    [Fact]
    public async Task Search_finds_pages_by_title_then_body_with_an_excerpt()
    {
        var claire = await _api.SignedIn("claire");
        var byTitle = await claire.GetJson("/api/search?q=replay");
        Assert.Equal(("page", "replay-nightly-import"), (byTitle[0].Str("kind"), byTitle[0].Str("id")));
        var byBody = (await claire.GetJson("/api/search?q=rate-limited")).EnumerateArray().Single();
        Assert.Equal(("page", "adr-nightly-file"), (byBody.Str("kind"), byBody.Str("id")));
        Assert.Contains("its API is rate-limited", byBody.Str("subtitle"), StringComparison.Ordinal);
        Assert.DoesNotContain("[[", byBody.Str("subtitle"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_round_trip_through_export_and_import()
    {
        var alice = await _api.SignedIn("alice");
        var text = await alice.GetStringAsync("/api/export?format=json");
        ExchangeTests.AssertValid(text);
        var envelope = JsonNode.Parse(text)!;
        var items = envelope["items"]!.AsArray();
        var pages = items.Where(i => i!["kind"]!.GetValue<string>() == "Page").ToList();
        Assert.Equal(7, pages.Count);
        // Parents come before their children.
        Assert.True(pages.FindIndex(p => p!["id"]!.GetValue<string>() == "operations") < pages.FindIndex(p => p!["id"]!.GetValue<string>() == "replay-nightly-import"));
        var replay = pages.Single(p => p!["id"]!.GetValue<string>() == "replay-nightly-import")!;
        Assert.Equal("crm-to-billing-customers", replay["spec"]!["attachedTo"]!["id"]!.GetValue<string>());

        replay["metadata"]!["name"] = "Replay the customer import";
        items.Add(JsonNode.Parse("""
            { "kind": "Page", "id": "child-of-new", "metadata": { "name": "Child" }, "spec": { "body": "[[page:new-root]]", "parentId": "new-root" } }
            """));
        items.Add(JsonNode.Parse("""
            { "kind": "Page", "id": "new-root", "metadata": { "name": "New root" }, "spec": { "body": "", "tags": ["imported"], "ownerTeamId": "team-data" } }
            """));
        var result = await (await alice.PostAsync("/api/import", new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json"))).Read();
        Assert.Equal((2, 1, 84), (result.GetProperty("created").GetInt32(), result.GetProperty("updated").GetInt32(), result.GetProperty("unchanged").GetInt32()));
        Assert.Equal("Replay the customer import", (await alice.GetJson("/api/pages/replay-nightly-import")).Str("title"));
        Assert.Equal(["child-of-new"], (await alice.GetJson("/api/pages/new-root")).GetProperty("backlinks").Ids());
        var history = await alice.GetJson("/api/pages/replay-nightly-import/history");
        Assert.Equal(("alice", "Imported", 2), (history[0].GetProperty("author").Str("username"), history[0].Str("summary"), history[0].GetProperty("version").GetInt32()));

        var bad = """
            { "formatVersion": "noosphere/v1", "source": { "name": "x" }, "mode": "full", "items": [
              { "kind": "Page", "id": "a", "metadata": { "name": "A", "description": "no" }, "spec": { "parentId": "b" } },
              { "kind": "Page", "id": "b", "metadata": { "name": "B" }, "spec": { "parentId": "a", "attachedTo": { "kind": "flow", "id": "nope" }, "tags": "x" } }
            ] }
            """;
        var refused = await (await alice.PostAsync("/api/import", new StringContent(bad, Encoding.UTF8, "application/json"))).Read(HttpStatusCode.UnprocessableEntity);
        Assert.Equal(["items[0].metadata.description", "items[1].spec.tags"],
            refused.GetProperty("errors").EnumerateArray().Select(e => e.Str("path")).Order(StringComparer.Ordinal));
        var cycle = """
            { "formatVersion": "noosphere/v1", "source": { "name": "x" }, "mode": "full", "items": [
              { "kind": "Page", "id": "a", "metadata": { "name": "A" }, "spec": { "parentId": "b" } },
              { "kind": "Page", "id": "b", "metadata": { "name": "B" }, "spec": { "parentId": "a", "attachedTo": { "kind": "flow", "id": "nope" } } }
            ] }
            """;
        var cycleErrors = await (await alice.PostAsync("/api/import?dryRun=true", new StringContent(cycle, Encoding.UTF8, "application/json"))).Read();
        Assert.Equal(["items[0].spec.parentId", "items[1].spec.attachedTo.id", "items[1].spec.parentId"],
            cycleErrors.GetProperty("errors").EnumerateArray().Select(e => e.Str("path")).Order(StringComparer.Ordinal));
    }
}
