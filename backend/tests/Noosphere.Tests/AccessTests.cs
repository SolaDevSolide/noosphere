using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Tests;

/// <summary>
/// Restricted records, guests, protected notes and the access log. Demo: Fraud scoring is restricted to Team Payments
/// (bob), the customer portal is shared with claire (guest), Billing and the payments-bank flow have protected notes.
/// The tests add: payroll (restricted, owned by Team Finance, listed: Team CRM, erin, gail), hr (restricted, owned by
/// Team Data, listed: erin) and the flow payroll-hr. erin and frank are members without a team, henri is in Team Data,
/// gail is a guest.
/// </summary>
[Collection("Api")]
public class AccessTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private const string Payroll = "/api/applications/payroll";

    private async Task<HttpClient> Setup()
    {
        var alice = await _api.SignedIn("alice");
        foreach (var (name, role, teams) in new[] { ("erin", "member", Array.Empty<string>()), ("frank", "member", []), ("henri", "member", ["team-data"]), ("gail", "guest", []) })
            await alice.PostJson("/api/users", new { username = name, displayName = name.ToUpperInvariant(), role, password = "password-1", teamIds = teams }, HttpStatusCode.Created);
        await alice.PostJson("/api/applications", new
        {
            id = "payroll", name = "Payroll", domainId = "finance", teamId = "team-finance", tech = "SAP HCM", description = "Salaries.",
            visibility = "restricted", access = new { teamIds = new[] { "team-crm" }, usernames = new[] { "erin", "gail" } },
            protectedNotes = "Payroll exports are encrypted with the key in vault `hr/payroll`.",
        }, HttpStatusCode.Created);
        await alice.PostJson("/api/applications", new
        {
            id = "hr", name = "HR core", domainId = "platform", teamId = "team-data", visibility = "restricted",
            access = new { teamIds = Array.Empty<string>(), usernames = new[] { "erin" } },
        }, HttpStatusCode.Created);
        await alice.PostJson("/api/flows", new { id = "payroll-hr", name = "Employees", fromId = "hr", toId = "payroll", type = "batch", ownerTeamId = "team-data",
            protectedNotes = "SFTP account in vault `hr/sftp`." }, HttpStatusCode.Created);
        return alice;
    }

    private Task<HttpClient> As(string name) => _api.SignedIn(name, name is "erin" or "frank" or "henri" or "gail" ? "password-1" : "demo");

    private static bool IsStub(JsonElement e) => e.GetProperty("restricted").GetBoolean();

    [Fact]
    public async Task Restricted_records_follow_the_visibility_matrix()
    {
        await Setup();
        // admin, owner, listed team, listed user, listed guest: full; other member, other guest: stub.
        foreach (var (name, sees) in new[] { ("alice", true), ("bob", true), ("david", true), ("erin", true), ("gail", true), ("frank", false), ("claire", false), ("henri", false) })
        {
            var client = await As(name);
            var app = await client.GetJson(Payroll);
            Assert.True(sees != IsStub(app), name);
            if (!sees) Assert.Equal(["id", "restricted", "domainId"], app.EnumerateObject().Select(p => p.Name));
            var listed = (await client.GetJson("/api/applications?domainId=finance")).EnumerateArray().Single(a => a.Str("id") == "payroll");
            Assert.True(sees != IsStub(listed), name);
            Assert.Equal(sees, (await client.GetJson("/api/search?q=payroll")).EnumerateArray().Any(r => r.Str("id") == "payroll"));
            Assert.Equal(sees, (await client.GetStringAsync("/api/export")).Contains("\"payroll\"", StringComparison.Ordinal));
            await client.GetJson($"{Payroll}/history", sees ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
            await client.GetJson($"{Payroll}/access", sees ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        }

        // Owners publish a restricted record; listed members propose; others are refused.
        var bob = await As("bob");
        var detail = await bob.GetJson(Payroll);
        Assert.Equal((true, true), (detail.GetProperty("permissions").GetProperty("publish").GetBoolean(), detail.GetProperty("permissions").GetProperty("propose").GetBoolean()));
        var david = (await (await As("david")).GetJson(Payroll)).GetProperty("permissions");
        Assert.Equal((false, true), (david.GetProperty("publish").GetBoolean(), david.GetProperty("propose").GetBoolean()));
        var body = new { name = "Payroll", domainId = "finance", teamId = "team-finance", visibility = "restricted", version = detail.GetProperty("version").GetInt32() };
        await (await As("frank")).PutJson(Payroll, body, HttpStatusCode.Forbidden);
        var saved = await bob.PutJson(Payroll, body);
        // Leaving out the access list and the protected notes keeps them.
        Assert.Equal(["team-crm"], saved.GetProperty("access").GetProperty("teamIds").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("Payroll exports are encrypted with the key in vault `hr/payroll`.", (await bob.GetJson(Payroll)).Str("protectedNotes"));
        // An owner may not lock themselves out; nobody makes a record hide from themselves.
        var hide = new { name = "Payroll", domainId = "finance", teamId = "team-data", visibility = "restricted", version = saved.GetProperty("version").GetInt32(),
            access = new { teamIds = Array.Empty<string>(), usernames = Array.Empty<string>() } };
        await bob.PutJson(Payroll, hide, HttpStatusCode.Forbidden);

        // Data objects stay visible: one sourced by a restricted application keeps its source id.
        await (await As("alice")).PostJson("/api/data-objects", new { id = "salary", name = "Salary", sensitivity = "sensitive", sourceApplicationId = "payroll" }, HttpStatusCode.Created);
        Assert.Equal("payroll", (await (await As("frank")).GetJson("/api/data-objects/salary")).Str("sourceApplicationId"));
    }

    [Fact]
    public async Task A_flow_needs_every_restricted_end()
    {
        await Setup();
        // payroll-hr: payroll (Team Finance, Team CRM, erin, gail) and hr (Team Data, erin).
        foreach (var (name, sees) in new[] { ("alice", true), ("erin", true), ("bob", false), ("david", false), ("henri", false), ("frank", false), ("gail", false) })
        {
            var client = await As(name);
            var flow = await client.GetJson("/api/flows/payroll-hr");
            Assert.True(sees != IsStub(flow), name);
            if (!sees) Assert.Equal(["id", "restricted", "fromId", "toId", "type"], flow.EnumerateObject().Select(p => p.Name));
            var landscape = (await client.GetJson("/api/landscape")).GetProperty("flows").EnumerateArray().Single(f => f.Str("id") == "payroll-hr");
            Assert.True(sees != IsStub(landscape), name);
        }
        // Who can see it: no team reads both ends; erin by name.
        var access = await (await As("alice")).GetJson("/api/flows/payroll-hr/access");
        Assert.Equal(("restricted", false), (access.Str("visibility"), access.GetProperty("readers").GetProperty("everyoneExceptGuests").GetBoolean()));
        Assert.Empty(access.GetProperty("readers").GetProperty("teams").EnumerateArray());
        Assert.Equal(["erin"], access.GetProperty("readers").GetProperty("users").EnumerateArray().Select(u => u.Str("username")));
        Assert.Equal("team-data", access.GetProperty("owningTeam").Str("id"));
        Assert.False(access.GetProperty("canManage").GetBoolean());
    }

    [Fact]
    public async Task The_access_window_explains_who_sees_and_edits()
    {
        await Setup();
        var bob = await As("bob");
        var access = await bob.GetJson($"{Payroll}/access");
        Assert.Equal("restricted", access.Str("visibility"));
        Assert.Equal(("team-finance", "Team Finance", 1), (access.GetProperty("owningTeam").Str("id"), access.GetProperty("owningTeam").Str("name"),
            access.GetProperty("owningTeam").GetProperty("memberCount").GetInt32()));
        var readers = access.GetProperty("readers");
        Assert.Equal(["team-finance", "team-crm"], readers.GetProperty("teams").EnumerateArray().Select(t => t.Str("id")));
        Assert.Equal(["erin", "gail"], readers.GetProperty("users").EnumerateArray().Select(u => u.Str("username")));
        Assert.Equal(["team-finance"], access.GetProperty("publishers").GetProperty("teams").EnumerateArray().Select(t => t.Str("id")));
        Assert.True(access.GetProperty("publishers").GetProperty("admins").GetBoolean());
        Assert.Equal("members", access.Str("proposers"));
        Assert.Equal((true, true, true), (access.GetProperty("protectedNotes").GetProperty("exists").GetBoolean(),
            access.GetProperty("protectedNotes").GetProperty("readable").GetBoolean(), access.GetProperty("canManage").GetBoolean()));
        Assert.False((await (await As("david")).GetJson($"{Payroll}/access")).GetProperty("canManage").GetBoolean());

        // A record visible to everyone: every member reads it, and the guests it is shared with.
        var portal = await bob.GetJson("/api/applications/portal/access");
        Assert.Equal(("everyone", true), (portal.Str("visibility"), portal.GetProperty("readers").GetProperty("everyoneExceptGuests").GetBoolean()));
        Assert.Equal(["claire"], portal.GetProperty("readers").GetProperty("users").EnumerateArray().Select(u => u.Str("username")));
        var domain = await bob.GetJson("/api/domains/finance/access");
        Assert.Equal(("none", false), (domain.Str("proposers"), domain.GetProperty("canManage").GetBoolean()));
        await bob.GetJson("/api/applications/nope/access", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Guests_see_the_outline_and_what_is_shared_with_them()
    {
        var claire = await As("claire");
        var billing = await claire.GetJson("/api/applications/billing");
        Assert.Equal(["id", "restricted", "shared", "name", "domainId", "kind"], billing.EnumerateObject().Select(p => p.Name));
        Assert.Equal(("Billing", false, false), (billing.Str("name"), billing.GetProperty("restricted").GetBoolean(), billing.GetProperty("shared").GetBoolean()));
        var portal = await claire.GetJson("/api/applications/portal");
        Assert.Equal("Customer portal", portal.Str("name"));
        Assert.True(portal.TryGetProperty("description", out _));
        Assert.Equal(["claire"], portal.GetProperty("access").GetProperty("usernames").EnumerateArray().Select(u => u.GetString()));
        // Flows of a shared application are shared too; the others are an outline.
        Assert.True((await claire.GetJson("/api/flows/portal-billing")).TryGetProperty("description", out _));
        Assert.Equal(["id", "restricted", "shared", "name", "fromId", "toId", "type"],
            (await claire.GetJson("/api/flows/billing-mail")).EnumerateObject().Select(p => p.Name));
        // Pages: titles only, unless shared.
        Assert.Equal(["id", "restricted", "shared", "title", "parentId", "order"],
            (await claire.GetJson("/api/pages/glossary")).EnumerateObject().Select(p => p.Name));
        // Names match a search, descriptions do not; data objects, domains and teams stay readable.
        Assert.Contains((await claire.GetJson("/api/search?q=billing")).EnumerateArray(), r => r.Str("id") == "billing");
        Assert.DoesNotContain((await claire.GetJson("/api/search?q=credit%20notes")).EnumerateArray(), r => r.Str("id") == "billing");
        Assert.True((await claire.GetJson("/api/data-objects/invoice")).TryGetProperty("description", out _));
        // Her export holds what she sees in full only, and stays a valid file.
        var export = await claire.GetStringAsync("/api/export");
        ExchangeTests.AssertValid(export);
        var ids = JsonDocument.Parse(export).RootElement.GetProperty("items").EnumerateArray().Where(i => i.Str("kind") == "Application").Select(i => i.Str("id")).ToList();
        Assert.Contains("portal", ids);
        Assert.DoesNotContain("billing", ids);

        // Shared on a page too.
        var alice = await As("alice");
        var glossary = await alice.GetJson("/api/pages/glossary");
        await alice.PutJson("/api/pages/glossary", new { title = glossary.Str("title"), body = glossary.Str("body"), ownerTeamId = "team-data",
            access = new { usernames = new[] { "claire" } }, version = glossary.GetProperty("version").GetInt32() });
        Assert.True((await claire.GetJson("/api/pages/glossary")).TryGetProperty("body", out _));
    }

    [Fact]
    public async Task Protected_notes_are_read_by_the_owners_the_access_list_and_admins_only()
    {
        await Setup();
        var bob = await As("bob");
        var billing = await bob.GetJson("/api/applications/billing");
        Assert.StartsWith("**Break-glass access.**", billing.Str("protectedNotes"), StringComparison.Ordinal);
        Assert.False(billing.GetProperty("protectedNotesHidden").GetBoolean());
        var forDavid = await (await As("david")).GetJson("/api/applications/billing");
        Assert.Equal((JsonValueKind.Null, true), (forDavid.GetProperty("protectedNotes").ValueKind, forDavid.GetProperty("protectedNotesHidden").GetBoolean()));
        Assert.False((await bob.GetJson("/api/applications/crm")).GetProperty("protectedNotesHidden").GetBoolean());
        // A flow: its owner team (Team Payments) reads them.
        Assert.Contains("EBICS", (await bob.GetJson("/api/flows/payments-bank")).Str("protectedNotes"), StringComparison.Ordinal);
        Assert.True((await (await As("david")).GetJson("/api/flows/payments-bank")).GetProperty("protectedNotesHidden").GetBoolean());
        // The access list reads them (members); a guest on it sees the record but never its notes.
        Assert.Contains("hr/payroll", (await (await As("erin")).GetJson(Payroll)).Str("protectedNotes"), StringComparison.Ordinal);
        Assert.Contains("hr/payroll", (await (await As("david")).GetJson(Payroll)).Str("protectedNotes"), StringComparison.Ordinal);
        var gail = await (await As("gail")).GetJson(Payroll);
        Assert.Equal((JsonValueKind.Null, true), (gail.GetProperty("protectedNotes").ValueKind, gail.GetProperty("protectedNotesHidden").GetBoolean()));
        Assert.Contains("hr/sftp", (await (await As("erin")).GetJson("/api/flows/payroll-hr")).Str("protectedNotes"), StringComparison.Ordinal);

        // Search: notes match for their readers only.
        Assert.Contains((await bob.GetJson("/api/search?q=svc-billing-run")).EnumerateArray(), r => r.Str("id") == "billing");
        Assert.Empty((await (await As("david")).GetJson("/api/search?q=svc-billing-run")).EnumerateArray());
        // Export: notes only where readable.
        Assert.Contains("svc-billing-run", await bob.GetStringAsync("/api/export"), StringComparison.Ordinal);
        Assert.DoesNotContain("svc-billing-run", await (await As("david")).GetStringAsync("/api/export"), StringComparison.Ordinal);

        // History and compare count them, masked for others.
        var version = billing.GetProperty("version").GetInt32();
        await bob.PutJson("/api/applications/billing", new { name = "Billing", domainId = "finance", teamId = "team-finance", tech = ".NET 8, PostgreSQL 16",
            description = billing.Str("description"), protectedNotes = "Rotated: see vault `finance/billing/run-2026`.", version });
        var david = await As("david");
        var snapshot = (await david.GetJson($"/api/applications/billing/history/{version + 1}")).GetProperty("snapshot");
        Assert.Equal(Rules.ProtectedMask, snapshot.Str("protectedNotes"));
        var compare = (await david.GetJson($"/api/applications/billing/history/compare?from={version}&to={version + 1}")).GetProperty("diff");
        var notes = compare.EnumerateArray().Single(d => d.Str("field") == "protectedNotes");
        Assert.Equal((Rules.ProtectedMask, Rules.ProtectedMask), (notes.Str("before"), notes.Str("after")));
        Assert.DoesNotContain("run-2026", compare.ToString(), StringComparison.Ordinal);
        var forBob = (await bob.GetJson($"/api/applications/billing/history/compare?from={version}&to={version + 1}")).GetProperty("diff");
        Assert.Equal("Rotated: see vault `finance/billing/run-2026`.", forBob.EnumerateArray().Single(d => d.Str("field") == "protectedNotes").Str("after"));
    }

    [Fact]
    public async Task Proposals_may_set_protected_notes_without_reading_them()
    {
        var david = await As("david");
        var bob = await As("bob");
        var billing = await bob.GetJson("/api/applications/billing");
        var version = billing.GetProperty("version").GetInt32();
        object Body(string? notes) => notes is null
            ? new { name = "Billing", domainId = "finance", teamId = "team-finance", description = "Invoices premiums." }
            : new { name = "Billing", domainId = "finance", teamId = "team-finance", description = "Invoices premiums.", protectedNotes = notes };

        // Left out, the notes are kept; the author never sees them.
        var keep = await david.PostJson("/api/proposals", new { title = "Shorter", submit = true,
            changes = new[] { new { kind = "application", targetId = "billing", operation = "update", baseVersion = version, after = Body(null) } } }, HttpStatusCode.Created);
        var change = keep.GetProperty("changes")[0];
        Assert.Equal(Rules.ProtectedMask, change.GetProperty("before").Str("protectedNotes"));
        Assert.Equal(Rules.ProtectedMask, change.GetProperty("after").Str("protectedNotes"));
        Assert.DoesNotContain(change.GetProperty("diff").EnumerateArray(), d => d.Str("field") == "protectedNotes");
        Assert.DoesNotContain("svc-billing-run", keep.ToString(), StringComparison.Ordinal);

        // Written, they replace the notes: the author sees what they wrote, the validators see both sides, others neither.
        var set = await david.PostJson("/api/proposals", new { title = "Notes", submit = true,
            changes = new[] { new { kind = "application", targetId = "billing", operation = "update", baseVersion = version, after = Body("Ask Team CRM for the extract account.") } } }, HttpStatusCode.Created);
        var mine = set.GetProperty("changes")[0];
        Assert.Equal("Ask Team CRM for the extract account.", mine.GetProperty("after").Str("protectedNotes"));
        Assert.Equal(Rules.ProtectedMask, mine.GetProperty("diff").EnumerateArray().Single(d => d.Str("field") == "protectedNotes").Str("before"));
        var validator = (await bob.GetJson($"/api/proposals/{set.Str("id")}")).GetProperty("changes")[0];
        Assert.StartsWith("**Break-glass", validator.GetProperty("before").Str("protectedNotes"), StringComparison.Ordinal);

        await bob.PostJson($"/api/proposals/{keep.Str("id")}/accept", new { });
        Assert.StartsWith("**Break-glass", (await bob.GetJson("/api/applications/billing")).Str("protectedNotes"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_outside_the_owning_team_are_logged()
    {
        await Setup();
        var alice = await As("alice");
        var bob = await As("bob");
        var david = await As("david");
        await bob.GetJson("/api/applications/fraud");          // listed team, restricted: logged
        await bob.GetJson("/api/applications/billing");        // owning team: not logged
        await david.GetJson("/api/applications/billing");      // notes hidden: not logged
        await david.GetJson(Payroll);                          // listed team: record and notes
        await (await As("erin")).GetJson(Payroll);             // listed user
        await alice.GetJson("/api/applications/billing");      // admin: notes, flagged
        await (await As("frank")).GetJson(Payroll);            // stub: not logged
        await david.GetJson($"{Payroll}/history/1");           // history snapshot with notes

        var log = (await alice.GetJson("/api/access-log")).GetProperty("items").EnumerateArray().ToList();
        string Row(JsonElement l) => $"{l.GetProperty("user").Str("username")} {l.Str("what")} {l.Str("entityId")} {l.Str("why")}"
            + (l.GetProperty("whyTeam").ValueKind == JsonValueKind.Object ? " " + l.GetProperty("whyTeam").Str("id") : "")
            + (l.GetProperty("admin").GetBoolean() ? " admin" : "");
        Assert.Equal([
            "david protectedNotes payroll listedTeam team-crm",
            "david record payroll listedTeam team-crm",
            "alice protectedNotes billing admin admin",
            "erin protectedNotes payroll listedUser",
            "erin record payroll listedUser",
            "david protectedNotes payroll listedTeam team-crm",
            "david record payroll listedTeam team-crm",
            "bob record fraud listedTeam team-payments",
        ], log.Select(Row).ToList());
        Assert.Equal("history v1", log[0].Str("detail"));
        Assert.Equal(("Payroll", "application", "Team Finance"), (log[1].Str("title"), log[1].Str("kind"), log[1].GetProperty("owningTeam").Str("name")));
        Assert.Equal("ERIN", log[3].GetProperty("user").Str("displayName"));

        // Filters and pages, newest first.
        Assert.Equal(2, (await alice.GetJson("/api/access-log?username=erin")).GetProperty("items").GetArrayLength());
        Assert.Single((await alice.GetJson("/api/access-log?kind=application&entityId=fraud")).GetProperty("items").EnumerateArray());
        Assert.Single((await alice.GetJson("/api/access-log?what=protectedNotes&username=alice")).GetProperty("items").EnumerateArray());
        var first = await alice.GetJson("/api/access-log?limit=3");
        Assert.Equal(3, first.GetProperty("items").GetArrayLength());
        var next = await alice.GetJson($"/api/access-log?limit=3&cursor={first.Str("nextCursor")}");
        Assert.Equal(log[3].GetProperty("id").GetInt64(), next.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Empty((await alice.GetJson("/api/access-log?to=2000-01-01")).GetProperty("items").EnumerateArray());
        Assert.Equal(8, (await alice.GetJson($"/api/access-log?from={DateTime.UtcNow:yyyy-MM-dd}")).GetProperty("items").GetArrayLength());
        await alice.GetJson("/api/access-log?from=yesterday", HttpStatusCode.BadRequest);
        await bob.GetJson("/api/access-log", HttpStatusCode.Forbidden);

        // Old lines are purged.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
            db.AccessLogs.Add(new AccessLog { At = DateTimeOffset.UtcNow.AddDays(-400), Username = "old", What = AccessLogWhat.Record });
            await db.SaveChangesAsync();
            Assert.Equal(1, await AccessLogs.Purge(db, 365, DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task A_full_export_is_for_admins_and_logged_with_its_reason()
    {
        var alice = await As("alice");
        var bob = await As("bob");
        await bob.GetJson("/api/export?scope=full", HttpStatusCode.Forbidden);
        await alice.GetJson("/api/export?scope=everything", HttpStatusCode.BadRequest);
        await alice.GetJson($"/api/export?scope=full&reason={new string('x', 201)}", HttpStatusCode.BadRequest);
        var full = await alice.GetJson("/api/export?scope=full&reason=Yearly%20audit");
        Assert.Equal("full", full.Str("scope"));
        Assert.Contains(full.GetProperty("items").EnumerateArray(), i => i.Str("id") == "fraud");
        var line = (await alice.GetJson("/api/access-log?what=export")).GetProperty("items")[0];
        Assert.Equal(("alice", "Yearly audit", "admin", true), (line.GetProperty("user").Str("username"), line.Str("detail"), line.Str("why"), line.GetProperty("admin").GetBoolean()));
        Assert.Equal("visible", (await bob.GetJson("/api/export")).Str("scope"));
    }

    [Fact]
    public async Task Secrets_are_refused_on_every_write_path()
    {
        const string token = "ghp_16C7e42F292c6912E7710c838347Ae178B4a";
        var bob = await As("bob");
        var david = await As("david");
        var alice = await As("alice");
        var billing = await bob.GetJson("/api/applications/billing");
        var version = billing.GetProperty("version").GetInt32();

        async Task Refused(HttpResponseMessage response, string field, string kind)
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.DoesNotContain(token, text, StringComparison.Ordinal);
            var problem = JsonDocument.Parse(text).RootElement;
            Assert.Equal($"This looks like a secret ({kind}). Noosphere is documentation, not a vault: store it in your secret manager and write where it lives instead.",
                problem.GetProperty("errors").GetProperty(field)[0].GetString());
        }

        await Refused(await bob.PutAsJsonAsync("/api/applications/billing", new { name = "Billing", domainId = "finance", teamId = "team-finance",
            description = $"Deploy with {token}", version }), "description", "GitHub token");
        await Refused(await bob.PutAsJsonAsync("/api/applications/billing", new { name = "Billing", domainId = "finance", teamId = "team-finance",
            protectedNotes = "Server=db;Password=hunter22;", version }), "protectedNotes", "password");
        await Refused(await david.PostAsJsonAsync("/api/proposals", new { title = "Tech", submit = true, changes = new[] { new { kind = "application", targetId = "billing",
            operation = "update", baseVersion = version, after = new { name = "Billing", domainId = "finance", tech = "postgres://app:S3cret@db/billing" } } } }),
            "changes[0].after.tech", "password in a URL");
        await Refused(await david.PostAsJsonAsync("/api/proposals", new { title = $"Token {token}", changes = new[] { new { kind = "application", targetId = "billing",
            operation = "update", baseVersion = version, after = new { name = "Billing", domainId = "finance" } } } }), "title", "GitHub token");
        await Refused(await bob.PostAsJsonAsync("/api/pages", new { title = "Keys", body = "-----BEGIN PRIVATE KEY-----\nMIIE", ownerTeamId = "team-finance" }), "body", "private key");
        await Refused(await alice.PutAsJsonAsync("/api/settings", new { instanceName = $"Noosphere {token}", version = 0 }), "instanceName", "GitHub token");
        // Placeholders pass.
        await bob.PutJson("/api/applications/billing", new { name = "Billing", domainId = "finance", teamId = "team-finance",
            description = "Connection: Server=db;Password=${BILLING_DB_PASSWORD};", version });

        // Import: reported per item, nothing written.
        var file = $$$"""
            {"formatVersion":"noosphere/v1","source":{"name":"t"},"mode":"full","items":[
              {"kind":"Application","id":"leaky","metadata":{"name":"Leaky","description":"key {{{token}}}"},"spec":{"domainId":"finance"}}]}
            """;
        var import = await alice.PostAsync("/api/import", new StringContent(file, Encoding.UTF8, "application/json"));
        var result = await import.Read(HttpStatusCode.UnprocessableEntity);
        var error = result.GetProperty("errors")[0];
        Assert.Equal("items[0].metadata.description", error.Str("path"));
        Assert.Contains("GitHub token", error.Str("message"), StringComparison.Ordinal);
        Assert.DoesNotContain(token, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Applications_get_a_computed_sensitivity_with_reasons()
    {
        var david = await As("david");
        var payments = await david.GetJson("/api/applications/payments");
        Assert.Equal("sensitive", payments.Str("sensitivity"));
        var reasons = payments.GetProperty("sensitivityReasons").EnumerateArray().ToList();
        Assert.Equal(5, reasons.Count);
        Assert.Equal(("dataObject", "payment", "Payment", "sensitive"), (reasons[0].Str("kind"), reasons[0].Str("id"), reasons[0].Str("name"), reasons[0].Str("sensitivity")));
        Assert.All(reasons.Zip(reasons.Skip(1)), p => Assert.True(Rank(p.First) >= Rank(p.Second)));
        // A hidden flow is not a reason (nor counted) for those who cannot see it.
        Assert.DoesNotContain(reasons, r => r.Str("id") == "payments-fraud");
        Assert.Equal("personal", (await david.GetJson("/api/applications/idp")).Str("sensitivity"));

        var sensitive = (await david.GetJson("/api/applications?sensitivity=sensitive")).Ids();
        Assert.Contains("payments", sensitive);
        Assert.Contains("claims", sensitive);
        Assert.DoesNotContain("idp", sensitive);
        Assert.DoesNotContain("fraud", sensitive);
        Assert.Contains("idp", (await david.GetJson("/api/applications?sensitivity=personal")).Ids());
        var landscape = await david.GetJson("/api/landscape?sensitivity=sensitive");
        Assert.DoesNotContain("idp", landscape.GetProperty("applications").EnumerateArray().Where(a => !a.GetProperty("restricted").GetBoolean()
            && !a.TryGetProperty("shared", out _) && a.Str("sensitivity") != "sensitive").Select(a => a.Str("id")).Intersect(sensitive));
        Assert.Contains(landscape.GetProperty("applications").EnumerateArray(), a => a.Str("id") == "payments" && a.Str("sensitivity") == "sensitive");
        await david.GetJson("/api/landscape?sensitivity=secret", HttpStatusCode.BadRequest);
        // Search subtitles carry it.
        var hit = (await david.GetJson("/api/search?q=payments")).EnumerateArray().First(r => r.Str("id") == "payments");
        Assert.Equal("sensitive", hit.Str("sensitivity"));
        Assert.EndsWith("· sensitive", hit.Str("subtitle"), StringComparison.Ordinal);
    }

    private static int Rank(JsonElement reason) => Array.IndexOf(["public", "internal", "personal", "sensitive"], reason.Str("sensitivity"));

    [Fact]
    public async Task Exchange_carries_access_lists_and_protected_notes()
    {
        await Setup();
        var alice = await As("alice");
        var export = await alice.GetStringAsync("/api/export");
        ExchangeTests.AssertValid(export);
        var items = JsonDocument.Parse(export).RootElement.GetProperty("items").EnumerateArray().ToList();
        var payroll = items.Single(i => i.Str("id") == "payroll").GetProperty("spec");
        Assert.Equal(["erin", "gail"], payroll.GetProperty("access").GetProperty("usernames").EnumerateArray().Select(u => u.GetString()));
        Assert.Contains("hr/payroll", payroll.Str("protectedNotes"), StringComparison.Ordinal);

        // A member's export has no notes they may not read; imported back, it keeps the notes in place.
        var davidExport = await (await As("david")).GetStringAsync("/api/export");
        Assert.DoesNotContain("svc-billing-run", davidExport, StringComparison.Ordinal);
        var result = await (await alice.PostAsync("/api/import", new StringContent(davidExport, Encoding.UTF8, "application/json"))).Read();
        Assert.Equal(0, result.GetProperty("updated").GetInt32());
        Assert.Contains("svc-billing-run", (await alice.GetJson("/api/applications/billing")).Str("protectedNotes"), StringComparison.Ordinal);

        // A malformed access list is reported first; unknown teams and users once the file is well formed.
        async Task<List<string>> Errors(string file) => [.. (await (await alice.PostAsync("/api/import?dryRun=true",
            new StringContent(file, Encoding.UTF8, "application/json"))).Read()).GetProperty("errors").EnumerateArray().Select(e => $"{e.Str("path")}: {e.Str("message")}")];
        Assert.Equal(["items[0].spec.access.teamIds: Expected an array of texts."], await Errors("""
            {"formatVersion":"noosphere/v1","source":{"name":"t"},"mode":"full","items":[
              {"kind":"Page","id":"secret-page","metadata":{"name":"P"},"spec":{"body":"x","access":{"teamIds":"team-crm"}}}]}
            """));
        var errors = await Errors("""
            {"formatVersion":"noosphere/v1","source":{"name":"t"},"mode":"full","scope":"visible","items":[
              {"kind":"Application","id":"vault","metadata":{"name":"Vault"},"spec":{"domainId":"platform","visibility":"restricted",
               "access":{"teamIds":["team-nowhere"],"usernames":["nobody"]}}}]}
            """);
        Assert.Contains("items[0].spec.access.teamIds: Unknown team 'team-nowhere'.", errors);
        Assert.Contains("items[0].spec.access.usernames: No user named 'nobody'.", errors);
    }

    [Fact]
    public async Task Pages_have_their_own_access_list()
    {
        await Setup();
        var bob = await As("bob");
        await bob.PostJson("/api/pages", new { id = "payroll-runbook", title = "Payroll runbook", body = "Steps.", attachedTo = new { kind = "application", id = "payroll" },
            visibility = "restricted", access = new { usernames = new[] { "frank" } } }, HttpStatusCode.Created);
        Assert.True((await (await As("frank")).GetJson("/api/pages/payroll-runbook")).TryGetProperty("body", out _));
        Assert.True((await (await As("henri")).GetJson("/api/pages/payroll-runbook")).GetProperty("restricted").GetBoolean());
        // A public page attached to a restricted record is flagged, for the editor's warning.
        await bob.PostJson("/api/pages", new { id = "payroll-faq", title = "Payroll FAQ", body = "Ask HR.", attachedTo = new { kind = "application", id = "payroll" } }, HttpStatusCode.Created);
        var faq = await (await As("henri")).GetJson("/api/pages/payroll-faq");
        Assert.True(faq.GetProperty("attachedRestricted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, faq.GetProperty("attachedTitle").ValueKind);
    }

    [Fact]
    public async Task Deleted_users_leave_access_lists_and_teams_on_them_cannot_be_deleted()
    {
        var alice = await Setup();
        Assert.Equal(HttpStatusCode.NoContent, await alice.Delete("/api/users/erin"));
        Assert.Equal(["gail"], (await alice.GetJson(Payroll)).GetProperty("access").GetProperty("usernames").EnumerateArray().Select(u => u.GetString()));
        await alice.PostJson("/api/teams", new { id = "team-audit", name = "Audit" }, HttpStatusCode.Created);
        var payroll = await alice.GetJson(Payroll);
        await alice.PutJson(Payroll, new { name = "Payroll", domainId = "finance", teamId = "team-finance", visibility = "restricted",
            access = new { teamIds = new[] { "team-audit", "team-unknown" } }, version = payroll.GetProperty("version").GetInt32() }, HttpStatusCode.BadRequest);
        await alice.PutJson(Payroll, new { name = "Payroll", domainId = "finance", teamId = "team-finance", visibility = "restricted",
            access = new { teamIds = new[] { "team-audit" } }, version = payroll.GetProperty("version").GetInt32() });
        Assert.Contains("access list", (await (await alice.DeleteAsync("/api/teams/team-audit")).Read(HttpStatusCode.Conflict)).Str("detail"), StringComparison.Ordinal);
    }
}
