using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noosphere.Api.Cli;
using Noosphere.Api.Data;
using static Noosphere.Tests.ImportTests;

namespace Noosphere.Tests;

/// <summary>Rules of the plan and of its application: owner, expiry, conflicts, rights, limits, audit.</summary>
[Collection("Api")]
public class ImportRulesTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private const string CrmComponent = """
        apiVersion: backstage.io/v1alpha1
        kind: Component
        metadata:
          name: crm
          description: Members and contacts of Mutuelle Alizé.
        spec:
          owner: team-crm
          lifecycle: production
        """;

    private void Write(Action<NoosphereDbContext> change)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
        change(db);
        db.SaveChanges();
    }

    [Fact]
    public async Task A_plan_belongs_to_its_author_and_is_applied_once()
    {
        var alice = await _api.SignedIn("alice");
        await alice.PostJson("/api/users", new { username = "erin", displayName = "Erin", role = "admin", password = "password-1", teamIds = Array.Empty<string>() }, HttpStatusCode.Created);
        var bob = await _api.SignedIn("erin", "password-1");
        var plan = await Plan(alice, Text("catalog-info.yaml", CrmComponent));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/imports/{plan.Str("id")}")).StatusCode);
        await Apply(bob, plan, HttpStatusCode.NotFound);
        Assert.Equal("planned", (await alice.GetJson($"/api/imports/{plan.Str("id")}")).Str("status"));
        await Apply(alice, plan);
        var again = await Apply(alice, plan, HttpStatusCode.Conflict);
        Assert.Contains("applied already", again.Str("detail"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_is_rejected_when_the_catalogue_changed_after_the_dry_run()
    {
        var alice = await _api.SignedIn("alice");
        var description = (await alice.GetJson("/api/applications/crm")).Str("description");
        var plan = await Plan(alice, Text("catalog-info.yaml", CrmComponent));
        var domain = await alice.GetJson("/api/domains/finance");
        await alice.PutJson("/api/domains/finance", new { name = "Finance and accounting", order = 4, version = domain.GetProperty("version").GetInt32() });
        var refused = await Apply(alice, plan, HttpStatusCode.Conflict);
        Assert.Contains("catalogue changed since the dry run", refused.Str("detail"), StringComparison.Ordinal);
        var stored = await alice.GetJson($"/api/imports/{plan.Str("id")}");
        Assert.Equal("rejected", stored.Str("status"));
        Assert.False(stored.GetProperty("canApply").GetBoolean());
        Assert.Equal(description, (await alice.GetJson("/api/applications/crm")).Str("description"));
    }

    [Fact]
    public async Task Entries_can_be_left_out_with_the_entries_that_require_them()
    {
        var alice = await _api.SignedIn("alice");
        var plan = await Plan(alice, File("catalog-info.yaml"));
        var portal = Entry(plan, "application", "portal-v2");
        var coaching = Entry(plan, "application", "coaching");
        var lookup = Entry(plan, "flow", "coaching-crm-crm-api");
        var crm = Entry(plan, "application", "crm");
        var imported = Entry(plan, "domain", "imported");
        int Index(JsonElement e) => e.GetProperty("index").GetInt32();
        int[] Requires(JsonElement e) => [.. e.GetProperty("requires").EnumerateArray().Select(r => r.GetInt32())];
        // The new portal goes to the new "Imported" domain; the lookup flow needs the new coaching application.
        Assert.Contains(Index(imported), Requires(portal));
        Assert.Contains(Index(coaching), Requires(lookup));
        Assert.Empty(Requires(crm));
        Assert.Empty(plan.GetProperty("skipped").EnumerateArray());

        async Task<JsonElement> ApplySkipping(HttpStatusCode expected, params int[] skip) =>
            await alice.PostJson($"/api/imports/{plan.Str("id")}/apply", new { skip }, expected);

        var refused = await ApplySkipping(HttpStatusCode.UnprocessableEntity, Index(coaching));
        Assert.Contains("requires entry " + Index(coaching), refused.Str("detail"), StringComparison.Ordinal);
        await ApplySkipping(HttpStatusCode.UnprocessableEntity, Index(crm) + 1000);
        var unchanged = plan.GetProperty("entries").EnumerateArray().Count(e => e.Str("action") == "unchanged");
        Assert.Equal("planned", (await alice.GetJson($"/api/imports/{plan.Str("id")}")).Str("status"));

        // Leaving out the portal, with what requires it, and the update of the CRM.
        var skip = plan.GetProperty("entries").EnumerateArray()
            .Where(e => Index(e) == Index(portal) || Index(e) == Index(crm) || Requires(e).Contains(Index(portal)))
            .Select(Index).ToArray();
        var description = (await alice.GetJson("/api/applications/crm")).Str("description");
        var applied = await ApplySkipping(HttpStatusCode.OK, skip);
        Assert.Equal("applied", applied.Str("status"));
        Assert.Equal(skip.Order(), applied.GetProperty("skipped").EnumerateArray().Select(i => i.GetInt32()));
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("/api/applications/portal-v2")).StatusCode);
        Assert.Equal(description, (await alice.GetJson("/api/applications/crm")).Str("description"));
        Assert.Equal("coaching", (await alice.GetJson("/api/flows/coaching-crm-crm-api")).Str("fromId"));
        Assert.Equal(0, unchanged);
    }

    [Fact]
    public async Task An_expired_plan_cannot_be_applied()
    {
        var alice = await _api.SignedIn("alice");
        var plan = await Plan(alice, Text("catalog-info.yaml", CrmComponent));
        var id = Guid.Parse(plan.Str("id"));
        Write(db => db.ImportPlans.Single(p => p.Id == id).ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal("expired", (await alice.GetJson($"/api/imports/{id}")).Str("status"));
        await Apply(alice, plan, HttpStatusCode.Gone);
        Assert.Equal("expired", (await alice.GetJson($"/api/imports/{id}")).Str("status"));
    }

    [Fact]
    public async Task Only_administrators_import_but_everyone_signed_in_gets_the_template()
    {
        var alice = await _api.SignedIn("alice");
        var plan = await Plan(alice, Text("catalog-info.yaml", CrmComponent));
        foreach (var name in new[] { "david", "claire" })
        {
            var user = await _api.SignedIn(name);
            await Plan(user, "", HttpStatusCode.Forbidden, Text("catalog-info.yaml", CrmComponent));
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/imports")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"/api/imports/{plan.Str("id")}")).StatusCode);
            await Apply(user, plan, HttpStatusCode.Forbidden);
            Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/imports/template.xlsx")).StatusCode);
        }
        Assert.Equal("planned", (await alice.GetJson($"/api/imports/{plan.Str("id")}")).Str("status"));
        await Apply(alice, plan);
        Assert.Equal("Members and contacts of Mutuelle Alizé.", (await alice.GetJson("/api/applications/crm")).Str("description"));
    }

    [Fact]
    public async Task An_import_never_deletes_nor_clears_and_secrets_are_refused()
    {
        var alice = await _api.SignedIn("alice");
        var before = await alice.GetJson("/api/applications/crm");
        var counts = (Applications: (await alice.GetJson("/api/applications")).GetArrayLength(), Flows: (await alice.GetJson("/api/flows")).GetArrayLength());
        // Only the name is given: the other fields are left as they are.
        var plan = await Plan(alice, Text("crm.yaml", "apiVersion: backstage.io/v1alpha1\nkind: Component\nmetadata: { name: crm, title: CRM }\nspec: {}\n"));
        Assert.Equal((0, 0, 1, 0), Counts(plan));
        await Apply(alice, plan);
        var after = await alice.GetJson("/api/applications/crm");
        Assert.Equal((before.Str("tech"), before.Str("teamId"), before.Str("description")), (after.Str("tech"), after.Str("teamId"), after.Str("description")));
        Assert.Equal(counts, ((await alice.GetJson("/api/applications")).GetArrayLength(), (await alice.GetJson("/api/flows")).GetArrayLength()));

        var token = "ghp_" + new string('a', 36);
        var secret = await Plan(alice, Text("crm.yaml", $"apiVersion: backstage.io/v1alpha1\nkind: Component\nmetadata: {{ name: crm, description: \"token {token}\" }}\nspec: {{}}\n"));
        Assert.Contains("secret", Messages(secret, "errors"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Yaml_aliases_zip_bombs_and_oversized_or_unknown_files_are_refused()
    {
        var alice = await _api.SignedIn("alice");
        var bomb = "apiVersion: backstage.io/v1alpha1\nkind: Component\nmetadata:\n  name: bomb\n  a: &a [x, x, x, x, x, x, x, x]\n  b: &b [*a, *a, *a, *a, *a, *a, *a, *a]\n  c: [*b, *b, *b, *b, *b, *b, *b, *b]\nspec: {}\n";
        var aliases = await Plan(alice, Text("bomb.yaml", bomb));
        Assert.Contains("aliases (*name) are not accepted", Messages(aliases, "errors"), StringComparison.Ordinal);
        Assert.True(aliases.GetProperty("errors")[0].GetProperty("source").GetProperty("line").GetInt32() > 1);

        var zeros = new byte[3 * 1024 * 1024];
        var big = new byte[6 * 1024 * 1024];
        var archive = await Plan(alice, Zip("files.zip", ("compressed.md", zeros), ("big.md", big), ("inner.zip", Zip("x.zip", ("a.md", [1])).Item2)));
        var errors = Messages(archive, "errors");
        Assert.Contains("compressed too much to be genuine", errors, StringComparison.Ordinal);
        Assert.Contains("larger than 5 MB once extracted", errors, StringComparison.Ordinal);
        Assert.Contains("An archive inside an archive is not opened", Messages(archive, "warnings"), StringComparison.Ordinal);

        var unknown = await Plan(alice, Text("notes.txt", "Just some notes."), Text("landscape.yaml", "formatVersion: noosphere/v1\nitems: []\n"),
            Text("legacy.yaml", "swagger: \"2.0\"\n"), ("binary.bin", [0, 159, 146, 150]));
        var messages = Messages(unknown, "errors");
        Assert.Contains("Format not recognised", messages, StringComparison.Ordinal);
        Assert.Contains("exchange import", messages, StringComparison.Ordinal);
        Assert.Contains("Swagger 2.0 is not read", messages, StringComparison.Ordinal);
        Assert.Contains("not a text file", messages, StringComparison.Ordinal);

        var many = new StringBuilder("flowchart LR\n");
        for (var i = 0; i < 5001; i++) many.Append($"  n{i}[Node {i}]\n");
        var tooMany = await Plan(alice, Text("big.mmd", many.ToString()));
        Assert.Contains("at most 5000 in one import", Messages(tooMany, "errors"), StringComparison.Ordinal);
        Assert.Empty(tooMany.GetProperty("entries").EnumerateArray());

        await Plan(alice, "?format=word", HttpStatusCode.BadRequest, Text("a.md", "# A"));
        var option = await Plan(alice, "?domainId=nowhere", HttpStatusCode.Created, Text("a.md", "# A"));
        Assert.Contains("domainId: no domain with the id 'nowhere'", Messages(option, "errors"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_exchange_import_refuses_yaml_aliases_too()
    {
        var alice = await _api.SignedIn("alice");
        var response = await alice.PostAsync("/api/import?dryRun=true", new StringContent(
            "formatVersion: noosphere/v1\nsource: &s { name: x }\nmode: full\nitems: []\nother: *s\n", Encoding.UTF8, "application/yaml"));
        var body = await response.Read(HttpStatusCode.BadRequest);
        Assert.Contains("aliases", body.Str("detail"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Imports_are_listed_as_their_record_and_the_command_line_plans_and_applies()
    {
        var alice = await _api.SignedIn("alice");
        var bob = await _api.SignedIn("bob");
        var plan = await Plan(alice, File("claims-api.yaml"));
        await Apply(alice, plan);
        var list = await alice.GetJson("/api/imports");
        var row = list.EnumerateArray().Single(p => p.Str("id") == plan.Str("id"));
        Assert.Equal(("applied", "alice", 3), (row.Str("status"), row.Str("username"), row.GetProperty("counts").GetProperty("create").GetInt32()));
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/imports")).StatusCode);

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = await CommandLine.Run(["imports", "plan", Fixture("member-events.json"), "--json"], stdout, stderr, ApiFixture.CliConfiguration());
        Assert.True(code == 0, stderr.ToString());
        var cliPlan = JsonDocument.Parse(stdout.ToString()).RootElement.GetProperty("result");
        Assert.Equal("system", cliPlan.Str("username"));
        using var applied = new StringWriter();
        Assert.Equal(0, await CommandLine.Run(["imports", "apply", cliPlan.Str("id")], applied, stderr, ApiFixture.CliConfiguration()));
        Assert.Contains("Import applied: 2 created", applied.ToString(), StringComparison.Ordinal);
        Assert.Equal(CommandLine.ExitCodes.NotFound, await CommandLine.Run(["imports", "apply", plan.Str("id")], applied, stderr, ApiFixture.CliConfiguration()));

        var template = Path.Combine(_api.Directory, "template.xlsx");
        System.IO.Directory.CreateDirectory(_api.Directory);
        Assert.Equal(0, await CommandLine.Run(["imports", "template", "--output", template], applied, stderr, ApiFixture.CliConfiguration()));
        Assert.True(System.IO.File.Exists(template));
        Assert.Equal(1, await CommandLine.Run(["imports", "plan", Fixture("claims-api.yaml"), "--domain", "nowhere"], applied, stderr, ApiFixture.CliConfiguration()));
        Assert.Equal(1, Revisions("crm-bus-crm-member-updated"));
    }

    private int Revisions(string flowId)
    {
        using var scope = _api.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<NoosphereDbContext>().Revisions.Count(r => r.EntityId == flowId && r.AuthorName == "system");
    }
}
