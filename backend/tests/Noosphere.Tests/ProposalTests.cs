using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Noosphere.Domain;

namespace Noosphere.Tests;

/// <summary>Ownership, proposals and their validation. Demo: bob is in Team Finance and Team Payments, david in Team CRM.</summary>
[Collection("Api")]
public class ProposalTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static object Billing(int version, string description, string tech = ".NET 8, PostgreSQL 16") => new
    {
        name = "Billing", domainId = "finance", teamId = "team-finance", kind = "app", tech, description,
        lifecycle = "live", visibility = "everyone", version,
    };

    private static async Task<JsonElement> Propose(HttpClient client, string title, object[] changes, bool submit = true,
        HttpStatusCode expected = HttpStatusCode.Created) =>
        await client.PostJson("/api/proposals", new { title, changes, submit }, expected);

    private static object Update(string kind, string id, int baseVersion, object after) =>
        new { kind, targetId = id, operation = "update", baseVersion, after };

    private static string Id(JsonElement proposal) => proposal.Str("id");

    [Fact]
    public async Task Owners_publish_directly_others_propose_and_guests_only_read()
    {
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");
        var claire = await _api.SignedIn("claire");

        var billing = await david.GetJson("/api/applications/billing");
        Assert.Equal("team-finance", billing.Str("owningTeamId"));
        Assert.Equal((false, true), (billing.GetProperty("permissions").GetProperty("publish").GetBoolean(), billing.GetProperty("permissions").GetProperty("propose").GetBoolean()));
        var forClaire = (await claire.GetJson("/api/applications/billing")).GetProperty("permissions");
        Assert.Equal((false, false), (forClaire.GetProperty("publish").GetBoolean(), forClaire.GetProperty("propose").GetBoolean()));
        Assert.True((await bob.GetJson("/api/applications/billing")).GetProperty("permissions").GetProperty("publish").GetBoolean());

        // A member of the owning team saves directly: a new revision, by them.
        var saved = await bob.PutJson("/api/applications/billing", Billing(4, "Invoices premiums."));
        Assert.Equal(5, saved.GetProperty("version").GetInt32());
        var history = await bob.GetJson("/api/applications/billing/history");
        Assert.Equal((5, "bob"), (history[0].GetProperty("version").GetInt32(), history[0].GetProperty("author").Str("username")));

        // Anyone else is refused and told to propose.
        var refused = await david.PutJson("/api/applications/billing", Billing(5, "Mine."), HttpStatusCode.Forbidden);
        Assert.Contains("proposal", refused.Str("detail"), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, await david.Delete("/api/flows/billing-mail"));
        // The owner of a new flow is its owner team, or else the team of its source application.
        await david.PostJson("/api/flows", new { name = "Status", fromId = "billing", toId = "crm" }, HttpStatusCode.Forbidden);
        await david.PostJson("/api/flows", new { name = "Status", fromId = "billing", toId = "crm", ownerTeamId = "team-crm" }, HttpStatusCode.Created);
        // A data object belongs to the team of its source application.
        Assert.Equal("team-finance", (await claire.GetJson("/api/data-objects/invoice")).Str("owningTeamId"));

        var proposal = await Propose(david, "Better description", [Update("application", "billing", 5, Billing(5, "Invoices premiums, monthly."))]);
        Assert.Equal(("submitted", "david", false), (proposal.Str("status"), proposal.GetProperty("author").Str("username"), proposal.GetProperty("canDecide").GetBoolean()));
        Assert.Equal(["team-finance"], proposal.GetProperty("ownerTeams").EnumerateArray().Select(t => t.Str("id")));

        // Guests cannot propose; owners may propose instead of saving.
        await Propose(claire, "Guest", [Update("application", "billing", 5, Billing(5, "x"))], expected: HttpStatusCode.Forbidden);
        await Propose(bob, "Review first, please", [Update("application", "acct", 1, new { name = "Accounting", domainId = "finance", teamId = "team-finance", tech = "SAP" })]);
        // Domains and teams are not proposed.
        var invalid = await Propose(david, "Domain", [Update("domain", "finance", 1, new { name = "Money" })], expected: HttpStatusCode.BadRequest);
        Assert.True(invalid.GetProperty("errors").TryGetProperty("changes[0].kind", out _));
    }

    [Fact]
    public async Task A_proposal_is_validated_with_a_field_diff_then_accepted()
    {
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");
        Assert.Equal(2, (await bob.GetJson("/api/me/inbox")).GetProperty("toValidate").GetInt32());
        var waiting = await bob.GetJson("/api/proposals?toValidate=true");
        var described = waiting.EnumerateArray().Single(p => p.Str("title") == "Clarify Billing's description");

        var detail = await bob.GetJson($"/api/proposals/{Id(described)}");
        Assert.True(detail.GetProperty("canDecide").GetBoolean());
        var change = detail.GetProperty("changes")[0];
        Assert.Equal(("billing", "Billing", false), (change.Str("targetId"), change.Str("targetTitle"), change.GetProperty("outdated").GetBoolean()));
        var diff = change.GetProperty("diff").EnumerateArray().ToList();
        Assert.Equal(("description", "Description", "markdown"), (diff.Single().Str("field"), diff[0].Str("label"), diff[0].Str("kind")));
        Assert.StartsWith("Invoices premiums, issues credit notes", diff[0].Str("before"), StringComparison.Ordinal);
        Assert.Contains("a late import delays", diff[0].Str("after"), StringComparison.Ordinal);

        // The author can neither accept nor refuse their own proposal.
        await david.PostJson($"/api/proposals/{Id(described)}/accept", new { }, HttpStatusCode.Forbidden);
        var accepted = await bob.PostJson($"/api/proposals/{Id(described)}/accept", new { });
        Assert.Equal(("accepted", "bob"), (accepted.Str("status"), accepted.GetProperty("decidedBy").Str("username")));
        Assert.Contains("a late import delays", (await bob.GetJson("/api/applications/billing")).Str("description"), StringComparison.Ordinal);
        var revision = (await bob.GetJson("/api/applications/billing/history"))[0];
        Assert.Equal(("david", Id(described)), (revision.GetProperty("author").Str("username"), revision.Str("proposalId")));
        await bob.PostJson($"/api/proposals/{Id(described)}/accept", new { }, HttpStatusCode.Conflict);

        // A creation gets its id and revision 1 on acceptance.
        var created = waiting.EnumerateArray().Single(p => p.Str("title").StartsWith("Document the invoice status", StringComparison.Ordinal));
        var createDiff = (await bob.GetJson($"/api/proposals/{Id(created)}")).GetProperty("changes")[0].GetProperty("diff");
        Assert.Contains(createDiff.EnumerateArray(), d => d.Str("field") == "fromId" && d.Str("afterTitle") == "Billing");
        await bob.PostJson($"/api/proposals/{Id(created)}/accept", new { });
        var flow = await bob.GetJson("/api/flows/billing-to-crm-invoice-status");
        Assert.Equal(("planned", 1), (flow.Str("status"), flow.GetProperty("version").GetInt32()));
        Assert.Equal(new InboxPair(0, 0), await Inbox(bob));
        Assert.Equal(new InboxPair(0, 0), await Inbox(david));
    }

    private sealed record InboxPair(int ToValidate, int MineOpen);

    private static async Task<InboxPair> Inbox(HttpClient client)
    {
        var inbox = await client.GetJson("/api/me/inbox");
        return new(inbox.GetProperty("toValidate").GetInt32(), inbox.GetProperty("mineOpen").GetInt32());
    }

    [Fact]
    public async Task Refusing_needs_a_reason_that_the_author_sees()
    {
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");
        var claire = await _api.SignedIn("claire");
        var proposal = (await bob.GetJson("/api/proposals?toValidate=true"))[0];
        var url = $"/api/proposals/{Id(proposal)}";

        foreach (var reason in new[] { "", "no", new string('x', 501) })
        {
            var bad = await bob.PostJson($"{url}/refuse", new { reason }, HttpStatusCode.BadRequest);
            Assert.True(bad.GetProperty("errors").TryGetProperty("reason", out _));
        }
        await claire.PostJson($"{url}/refuse", new { reason = "Not mine to say" }, HttpStatusCode.Forbidden);
        await bob.PostJson($"{url}/refuse", new { reason = "  The CRM does not consume these events yet.  " });

        var seen = await david.GetJson(url);
        Assert.Equal(("refused", "The CRM does not consume these events yet.", "bob"),
            (seen.Str("status"), seen.Str("refusalReason"), seen.GetProperty("decidedBy").Str("username")));
        var refused = await david.GetJson("/api/proposals?mine=true&status=refused");
        Assert.Equal(2, refused.GetArrayLength());
        Assert.Contains(refused.EnumerateArray(), p => p.Str("refusalReason").StartsWith("Accounting stays", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Drafts_are_private_edited_submitted_and_withdrawn_by_their_author()
    {
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");
        var draft = await Propose(david, "Draft", [Update("application", "billing", 4, Billing(4, "Draft text"))], submit: false);
        var url = $"/api/proposals/{Id(draft)}";
        Assert.Equal("draft", draft.Str("status"));
        await bob.GetJson(url, HttpStatusCode.NotFound);
        Assert.Equal(new InboxPair(0, 3), await Inbox(david));

        await bob.PutJson(url, new { title = "Mine now", changes = Array.Empty<object>(), version = 1 }, HttpStatusCode.NotFound);
        await david.PutJson(url, new { title = "Edited", changes = new[] { Update("application", "billing", 4, Billing(4, "Edited text")) }, version = 2 },
            HttpStatusCode.Conflict);
        var edited = await david.PutJson(url, new { title = "Edited", changes = new[] { Update("application", "billing", 4, Billing(4, "Edited text")) }, version = 1 });
        Assert.Equal(("Edited", "Edited text"), (edited.Str("title"), edited.GetProperty("changes")[0].GetProperty("after").Str("description")));
        var submitted = await david.PostJson($"{url}/submit", new { });
        Assert.Equal("submitted", submitted.Str("status"));
        Assert.Equal(3, (await bob.GetJson("/api/me/inbox")).GetProperty("toValidate").GetInt32());
        await bob.PostJson($"{url}/withdraw", new { }, HttpStatusCode.Forbidden);
        Assert.Equal("withdrawn", (await david.PostJson($"{url}/withdraw", new { })).Str("status"));
        await david.PostJson($"{url}/submit", new { }, HttpStatusCode.Conflict);

        // Invalid changes are reported per change.
        var invalid = await Propose(david, "Bad", [Update("application", "billing", 4, new { name = "", domainId = "nowhere" }),
            new { kind = "flow", operation = "update", targetId = "nope", baseVersion = 1, after = new { } }], expected: HttpStatusCode.BadRequest);
        Assert.Equal(["changes[0].after.domainId", "changes[0].after.name", "changes[1].targetId"],
            invalid.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_outdated_proposal_returns_409_until_rebased_on_the_latest_version()
    {
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");
        var proposal = await Propose(david, "Wording", [Update("application", "billing", 4, Billing(4, "David's description"))]);
        var url = $"/api/proposals/{Id(proposal)}";

        // Meanwhile, the owner changes another field.
        await bob.PutJson("/api/applications/billing", Billing(4, "Invoices premiums, issues credit notes and sends them by email and post. The invoice run starts every night at 05:00.", ".NET 9"));
        Assert.True((await bob.GetJson(url)).GetProperty("outdated").GetBoolean());
        var conflict = await bob.PostJson($"{url}/accept", new { }, HttpStatusCode.Conflict);
        Assert.True(conflict.GetProperty("outdated").GetBoolean());

        await bob.PostJson($"{url}/rebase", new { }, HttpStatusCode.Forbidden);
        var rebased = await david.PostJson($"{url}/rebase", new { });
        var change = rebased.GetProperty("changes")[0];
        Assert.Equal((5, false, 0), (change.GetProperty("baseVersion").GetInt32(), rebased.GetProperty("outdated").GetBoolean(), change.GetProperty("conflicts").GetArrayLength()));
        Assert.Equal(("David's description", ".NET 9"), (change.GetProperty("after").Str("description"), change.GetProperty("after").Str("tech")));
        Assert.Equal(["description"], change.GetProperty("diff").EnumerateArray().Select(d => d.Str("field")));

        await bob.PostJson($"{url}/accept", new { });
        var billing = await bob.GetJson("/api/applications/billing");
        Assert.Equal(("David's description", ".NET 9", 6), (billing.Str("description"), billing.Str("tech"), billing.GetProperty("version").GetInt32()));
    }

    [Fact]
    public async Task A_rebase_flags_fields_changed_on_both_sides()
    {
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");
        var proposal = await Propose(david, "Wording", [Update("application", "billing", 4, Billing(4, "David's description"))]);
        await bob.PutJson("/api/applications/billing", Billing(4, "Bob's description"));
        var rebased = await david.PostJson($"/api/proposals/{Id(proposal)}/rebase", new { });
        var change = rebased.GetProperty("changes")[0];
        Assert.Equal(["description"], change.GetProperty("conflicts").EnumerateArray().Select(c => c.GetString()));
        // The author's value is kept; the diff is against the latest version.
        Assert.Equal(("Bob's description", "David's description"),
            (change.GetProperty("diff")[0].Str("before"), change.GetProperty("diff")[0].Str("after")));
    }

    [Fact]
    public async Task A_proposal_spanning_several_owners_needs_one_validator_per_team_or_an_admin()
    {
        var alice = await _api.SignedIn("alice");
        await alice.PostJson("/api/users", new { username = "erin", displayName = "Erin Blanc", role = "member", password = "erin-password", teamIds = new[] { "team-contracts" } },
            HttpStatusCode.Created);
        var erin = await _api.SignedIn("erin", "erin-password");
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");

        var quotes = await david.GetJson("/api/applications/quotes");
        object Quotes(string description) => new
        {
            name = "Quotes", domainId = quotes.Str("domainId"), teamId = "team-contracts", kind = quotes.Str("kind"), tech = quotes.Str("tech"),
            description, lifecycle = "live", visibility = "everyone",
        };
        var both = await Propose(david, "Explain the quote to invoice path", [
            Update("application", "billing", 4, Billing(4, "Invoices accepted quotes.")),
            Update("application", "quotes", quotes.GetProperty("version").GetInt32(), Quotes("Accepted quotes become policies, then invoices.")),
        ]);
        var url = $"/api/proposals/{Id(both)}";
        Assert.Equal(["team-contracts", "team-finance"], both.GetProperty("ownerTeams").EnumerateArray().Select(t => t.Str("id")));

        var half = await bob.PostJson($"{url}/accept", new { });
        Assert.Equal("submitted", half.Str("status"));
        Assert.Equal("bob", half.GetProperty("ownerTeams").EnumerateArray().Single(t => t.Str("id") == "team-finance").GetProperty("approvedBy").Str("username"));
        Assert.False(half.GetProperty("canDecide").GetBoolean());
        Assert.Equal("Invoices premiums, issues credit notes and sends them by email and post. The invoice run starts every night at 05:00.",
            (await bob.GetJson("/api/applications/billing")).Str("description"));
        Assert.Equal(1, (await erin.GetJson("/api/me/inbox")).GetProperty("toValidate").GetInt32());

        var done = await erin.PostJson($"{url}/accept", new { });
        Assert.Equal("accepted", done.Str("status"));
        Assert.Equal("Invoices accepted quotes.", (await bob.GetJson("/api/applications/billing")).Str("description"));
        Assert.Equal("Accepted quotes become policies, then invoices.", (await bob.GetJson("/api/applications/quotes")).Str("description"));

        // An administrator accepts for every team at once.
        var again = await Propose(david, "Again", [
            Update("application", "billing", 5, Billing(5, "Again.")),
            Update("application", "quotes", quotes.GetProperty("version").GetInt32() + 1, Quotes("Again.")),
        ]);
        Assert.Equal("accepted", (await alice.PostJson($"/api/proposals/{Id(again)}/accept", new { })).Str("status"));
    }

    [Fact]
    public async Task Creations_and_deletions_are_proposed_too()
    {
        var bob = await _api.SignedIn("bob");
        var david = await _api.SignedIn("david");
        var proposal = await Propose(david, "Retire the mail flow and add a page", [
            new { kind = "flow", targetId = "billing-mail", operation = "delete", baseVersion = 1 },
            new { kind = "page", operation = "create", after = new { title = "Billing mail retirement", body = "See [[application:billing]].", ownerTeamId = "team-finance" } },
        ]);
        Assert.Equal("billing-mail-retirement", proposal.GetProperty("changes")[1].Str("targetId"));
        await bob.PostJson($"/api/proposals/{Id(proposal)}/accept", new { });
        await bob.GetJson("/api/flows/billing-mail", HttpStatusCode.NotFound);
        Assert.Equal("Billing mail retirement", (await bob.GetJson("/api/pages/billing-mail-retirement")).Str("title"));
        var count = await _api.Services.GetRequiredDb(db => db.Revisions.Count(r => r.ProposalId == Guid.Parse(Id(proposal))));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Restricted_records_are_not_proposed_by_non_admins()
    {
        var david = await _api.SignedIn("david");
        await Propose(david, "Fraud", [Update("application", "fraud", 1, new { name = "Fraud", domainId = "external" })], expected: HttpStatusCode.Forbidden);
        await Propose(david, "Hide it", [Update("application", "crm", 1, new { name = "CRM", domainId = "customer", teamId = "team-crm", visibility = "restricted" })],
            expected: HttpStatusCode.Forbidden);
        var states = await _api.Services.GetRequiredDb(db => db.Proposals.AsNoTracking().Select(p => p.Status).ToList());
        Assert.Equal(3, states.Count);
        Assert.DoesNotContain(ProposalStatus.Draft, states);
    }
}
