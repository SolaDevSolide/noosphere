using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Noosphere.Api.Data;

namespace Noosphere.Tests;

[Collection("Api")]
public class ExchangeTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static readonly Lazy<JsonSchema> LoadedSchema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(OpenApiTests.RepositoryRoot(), "docs", "format", "noosphere-v1.schema.json"))));

    internal static JsonSchema Schema() => LoadedSchema.Value;

    internal static void AssertValid(string json)
    {
        var result = Schema().Evaluate(JsonDocument.Parse(json).RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(result.IsValid, string.Join("\n", (result.Details ?? []).Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))));
    }

    private static StringContent Body(string text, string type) => new(text, Encoding.UTF8, type);

    [Fact]
    public void Demo_file_follows_the_schema() => AssertValid(Bootstrap.DemoLandscape());

    [Fact]
    public async Task Json_export_follows_the_schema_and_holds_the_whole_landscape()
    {
        var response = await (await _api.SignedIn("alice")).GetAsync("/api/export?format=json");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString() ?? response.Headers.GetValues("Content-Disposition").Single(), StringComparison.Ordinal);
        var text = await response.Content.ReadAsStringAsync();
        AssertValid(text);
        var envelope = JsonDocument.Parse(text).RootElement;
        Assert.Equal(("noosphere/v1", "full"), (envelope.Str("formatVersion"), envelope.Str("mode")));
        Assert.Equal("Noosphere", envelope.GetProperty("source").Str("name"));
        var kinds = envelope.GetProperty("items").EnumerateArray().GroupBy(i => i.Str("kind")).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int> { ["Domain"] = 6, ["Team"] = 11, ["Application"] = 20, ["DataObject"] = 5, ["Flow"] = 36, ["Page"] = 7 }, kinds);
    }

    [Fact]
    public async Task Yaml_export_reimports_unchanged()
    {
        var alice = await _api.SignedIn("alice");
        var response = await alice.GetAsync("/api/export?format=yaml");
        Assert.Equal("application/yaml", response.Content.Headers.ContentType?.MediaType);
        var yaml = await response.Content.ReadAsStringAsync();
        Assert.Contains("formatVersion: noosphere/v1", yaml, StringComparison.Ordinal);
        var result = await (await alice.PostAsync("/api/import", Body(yaml, "application/yaml"))).Read();
        Assert.Equal((0, 0, 85, true), (result.GetProperty("created").GetInt32(), result.GetProperty("updated").GetInt32(),
            result.GetProperty("unchanged").GetInt32(), result.GetProperty("applied").GetBoolean()));
    }

    [Fact]
    public async Task Import_creates_and_updates_by_id_and_dry_run_writes_nothing()
    {
        var alice = await _api.SignedIn("alice");
        var file = """
            formatVersion: noosphere/v1
            source: { name: Elsewhere }
            mode: full
            items:
              - kind: Domain
                id: hr
                metadata: { name: Human resources }
                spec: { order: 9 }
              - kind: Application
                id: payroll
                metadata: { name: Payroll, description: "Pays **everyone**." }
                spec: { domainId: hr, kind: saas, tech: "123" }
              - kind: Flow
                id: payroll-to-acct
                metadata: { name: Salaries }
                spec: { fromId: payroll, toId: acct, type: batch, dataObjectIds: [payment], plannedChange: { summary: Monthly, date: 2027-02-01 } }
              - kind: Team
                id: team-crm
                metadata: { name: Team CRM, description: Renamed by import }
                spec: { contact: "#team-crm" }
            """;
        var dry = await (await alice.PostAsync("/api/import?dryRun=true", Body(file, "text/plain"))).Read();
        Assert.Equal((3, 1, 0, true, false), (dry.GetProperty("created").GetInt32(), dry.GetProperty("updated").GetInt32(),
            dry.GetProperty("unchanged").GetInt32(), dry.GetProperty("dryRun").GetBoolean(), dry.GetProperty("applied").GetBoolean()));
        await alice.GetJson("/api/applications/payroll", HttpStatusCode.NotFound);
        Assert.Equal(1, (await alice.GetJson("/api/teams/team-crm")).GetProperty("version").GetInt32());

        var done = await (await alice.PostAsync("/api/import", Body(file, "application/yaml"))).Read();
        Assert.True(done.GetProperty("applied").GetBoolean());
        var payroll = await alice.GetJson("/api/applications/payroll");
        Assert.Equal(("hr", "saas", "123", "Pays **everyone**."), (payroll.Str("domainId"), payroll.Str("kind"), payroll.Str("tech"), payroll.Str("description")));
        var team = await alice.GetJson("/api/teams/team-crm");
        Assert.Equal(("Renamed by import", 2), (team.Str("description"), team.GetProperty("version").GetInt32()));
        Assert.Equal("2027-02-01", (await alice.GetJson("/api/flows/payroll-to-acct")).GetProperty("plannedChange").Str("date"));

        var again = await (await alice.PostAsync("/api/import", Body(file, "application/yaml"))).Read();
        Assert.Equal(4, again.GetProperty("unchanged").GetInt32());
    }

    [Fact]
    public async Task Invalid_files_are_refused_whole_with_every_error()
    {
        var alice = await _api.SignedIn("alice");
        var file = new
        {
            formatVersion = "noosphere/v1", source = new { name = "x" }, mode = "full",
            items = new object[]
            {
                new { kind = "Application", id = "new-app", metadata = new { name = "New" }, spec = new { domainId = "finance" } },
                new { kind = "Flow", id = "bad", metadata = new { name = "Bad" }, spec = new { fromId = "crm", toId = "ghost", dataObjectIds = new[] { "nothing" } } },
                new { kind = "Flow", id = "bad", metadata = new { name = "Bad again" }, spec = new { fromId = "crm", toId = "billing" } },
            },
        };
        var result = await (await alice.PostAsync("/api/import", Body(JsonSerializer.Serialize(file), "application/json"))).Read(HttpStatusCode.UnprocessableEntity);
        var errors = result.GetProperty("errors").EnumerateArray().Select(e => e.Str("path")).ToList();
        Assert.Equal(["items[1].spec.toId", "items[1].spec.dataObjectIds", "items[2].id"], errors);
        Assert.False(result.GetProperty("applied").GetBoolean());
        await alice.GetJson("/api/applications/new-app", HttpStatusCode.NotFound);

        var structure = await (await alice.PostAsync("/api/import", Body("""
            {"formatVersion":"noosphere/v2","mode":"full","items":[]}
            """, "application/json"))).Read(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("formatVersion", structure.GetProperty("errors")[0].Str("path"));
        var unknown = await (await alice.PostAsync("/api/import?dryRun=true", Body("""
            {"formatVersion":"noosphere/v1","mode":"full","items":[{"kind":"Robot","id":"Bad Id","metadata":{"name":"x"},"spec":{},"extra":1}]}
            """, "application/json"))).Read();
        Assert.Equal(["items[0].extra", "items[0].kind", "items[0].id"], unknown.GetProperty("errors").EnumerateArray().Select(e => e.Str("path")));

        var garbage = await (await alice.PostAsync("/api/import", Body("{ not json", "application/json"))).Read(HttpStatusCode.BadRequest);
        Assert.StartsWith("The file is not valid JSON", garbage.Str("detail"), StringComparison.Ordinal);
        await (await alice.PostAsync("/api/import", Body("a: [unclosed", "application/yaml"))).Read(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_admins_import_but_everyone_exports()
    {
        var bob = await _api.SignedIn("bob");
        var export = await bob.GetStringAsync("/api/export?format=json");
        await (await bob.PostAsync("/api/import", Body(export, "application/json"))).Read(HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.OK, (await (await _api.SignedIn("claire")).GetAsync("/api/export?format=yaml")).StatusCode);
        await (await bob.GetAsync("/api/export?format=xml")).Read(HttpStatusCode.BadRequest);
    }
}
