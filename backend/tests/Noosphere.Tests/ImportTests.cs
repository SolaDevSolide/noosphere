using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noosphere.Api.Data;
using Noosphere.Api.Importing;

namespace Noosphere.Tests;

/// <summary>Imports from other formats, one test per format, with the fixtures of Fixtures/Imports (fictional Mutuelle Alizé data).</summary>
[Collection("Api")]
public class ImportTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    internal static string Fixture(string name) =>
        Path.Combine(OpenApiTests.RepositoryRoot(), "backend", "tests", "Noosphere.Tests", "Fixtures", "Imports", name);

    internal static (string, byte[]) File(string name) => (name, System.IO.File.ReadAllBytes(Fixture(name)));

    internal static (string, byte[]) Text(string name, string text) => (name, Encoding.UTF8.GetBytes(text));

    internal static (string, byte[]) Zip(string name, params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (entryName, content) in entries)
            {
                using var s = zip.CreateEntry(entryName, CompressionLevel.SmallestSize).Open();
                s.Write(content);
            }
        return (name, stream.ToArray());
    }

    internal static async Task<JsonElement> Plan(HttpClient client, string query, HttpStatusCode expected, params (string Name, byte[] Content)[] files)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, content) in files)
        {
            var part = new ByteArrayContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, "files", name);
        }
        return await (await client.PostAsync("/api/imports" + query, form)).Read(expected);
    }

    internal static Task<JsonElement> Plan(HttpClient client, params (string Name, byte[] Content)[] files) => Plan(client, "", HttpStatusCode.Created, files);

    internal static async Task<JsonElement> Apply(HttpClient client, JsonElement plan, HttpStatusCode expected = HttpStatusCode.OK) =>
        await (await client.PostAsync($"/api/imports/{plan.Str("id")}/apply", null)).Read(expected);

    internal static (int Create, int Update, int Unchanged, int Errors) Counts(JsonElement plan)
    {
        var c = plan.GetProperty("counts");
        return (c.GetProperty("create").GetInt32(), c.GetProperty("update").GetInt32(), c.GetProperty("unchanged").GetInt32(), c.GetProperty("errors").GetInt32());
    }

    internal static JsonElement Entry(JsonElement plan, string kind, string id) =>
        plan.GetProperty("entries").EnumerateArray().Single(e => e.Str("kind") == kind && e.Str("id") == id);

    internal static string Messages(JsonElement plan, string list) =>
        string.Join("\n", plan.GetProperty(list).EnumerateArray().Select(m => m.Str("message")));

    private static string Errors(JsonElement plan) => Messages(plan, "errors");

    private T Db<T>(Func<NoosphereDbContext, T> read)
    {
        using var scope = _api.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<NoosphereDbContext>());
    }

    [Fact]
    public async Task Backstage_catalog_is_previewed_applied_and_then_unchanged()
    {
        var alice = await _api.SignedIn("alice");
        var plan = await Plan(alice, File("catalog-info.yaml"));
        Assert.True(plan.GetProperty("canApply").GetBoolean(), Errors(plan));
        Assert.Equal("planned", plan.Str("status"));
        Assert.Equal(["backstage"], plan.GetProperty("formats").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal((10, 1, 0), (Counts(plan).Create, Counts(plan).Update, Counts(plan).Errors));
        // The new portal has no system, hence no domain: it goes to the "Imported" domain.
        Assert.Equal("create", Entry(plan, "domain", "imported").Str("action"));

        var crm = Entry(plan, "application", "crm");
        Assert.Equal(("update", "identifier"), (crm.Str("action"), crm.Str("matchedBy")));
        Assert.Equal(["description"], crm.GetProperty("changes").EnumerateArray().Select(c => c.Str("field")));
        var portal = Entry(plan, "application", "portal-v2");
        Assert.Equal("create", portal.Str("action"));
        Assert.Contains(portal.GetProperty("possibleMatches").EnumerateArray(), m => m.Str("id") == "portal");
        Assert.Equal("catalog-info.yaml", portal.GetProperty("source").Str("file"));
        Assert.Equal("documents[5]", portal.GetProperty("source").Str("place"));
        Assert.True(portal.GetProperty("source").GetProperty("line").GetInt32() > 1);
        Assert.Contains("jdupont", Messages(plan, "warnings"), StringComparison.Ordinal);

        var applied = await Apply(alice, plan);
        Assert.Equal("applied", applied.Str("status"));
        var coaching = await alice.GetJson("/api/applications/coaching");
        Assert.Equal(("prevention", "team-prevention", "planned"), (coaching.Str("domainId"), coaching.Str("teamId"), coaching.Str("lifecycle")));
        Assert.Contains("[Runbook](https://wiki.alize.example/coaching)", coaching.Str("description"), StringComparison.Ordinal);
        Assert.Equal("data", (await alice.GetJson("/api/applications/coaching-db")).Str("kind"));
        Assert.Equal("prevention@alize.example", (await alice.GetJson("/api/teams/team-prevention")).Str("contact"));
        var lookup = await alice.GetJson("/api/flows/coaching-crm-crm-api");
        Assert.Equal(("coaching", "crm", "api", "team-crm", "live"), (lookup.Str("fromId"), lookup.Str("toId"), lookup.Str("type"), lookup.Str("ownerTeamId"), lookup.Str("status")));
        Assert.Equal(("portal-v2", "coaching"), ((await alice.GetJson("/api/flows/portal-v2-coaching-coaching-api")).Str("fromId"), "coaching"));
        Assert.Equal("Depends on Coaching database", (await alice.GetJson("/api/flows/coaching-coaching-db")).Str("name"));

        var revision = Db(db => db.Revisions.Single(r => r.EntityId == "coaching" && r.Kind == Domain.RecordKind.Application));
        Assert.Equal("alice", revision.AuthorName);
        Assert.StartsWith("Imported (backstage, import " + plan.Str("id")[..8], revision.Summary, StringComparison.Ordinal);

        var again = await Plan(alice, File("catalog-info.yaml"));
        Assert.Equal((0, 0, 10, 0), Counts(again));
    }

    [Fact]
    public async Task OpenApi_becomes_a_page_on_its_application_and_flows_from_its_consumers()
    {
        var alice = await _api.SignedIn("alice");
        var plan = await Plan(alice, File("claims-api.yaml"));
        Assert.True(plan.GetProperty("canApply").GetBoolean(), Errors(plan));
        Assert.Equal("openapi", plan.GetProperty("formats")[0].GetString());
        var page = Entry(plan, "page", "claims-api");
        Assert.Equal(("create", "Claims API"), (page.Str("action"), page.Str("name")));
        Assert.Equal("identifier", Entry(plan, "application", "claims").Str("matchedBy"));
        await Apply(alice, plan);

        var body = (await alice.GetJson("/api/pages/claims-api")).Str("body");
        Assert.Contains("**OpenAPI 3.0.3** · version 2.4.0", body, StringComparison.Ordinal);
        Assert.Contains("| https://api.alize.example/claims/v2 | Production |", body, StringComparison.Ordinal);
        Assert.Contains("| POST | `/claims` | Declare a claim | `declareClaim` |", body, StringComparison.Ordinal);
        Assert.Contains("| POST | `/claims/{id}/photos` | Add a photo to a claim. |", body, StringComparison.Ordinal);
        Assert.Contains("`Claim`, `Photo`", body, StringComparison.Ordinal);
        var raw = await alice.GetJson("/api/pages/claims-api");
        Assert.Equal("claims", raw.GetProperty("attachedTo").Str("id"));
        // "Mobile app" is found by its name.
        Assert.Equal(("mobile", "claims", "api"), ((await alice.GetJson("/api/flows/mobile-claims-api")).Str("fromId"), "claims", "api"));
        Assert.Equal("portal", (await alice.GetJson("/api/flows/portal-claims-api")).Str("fromId"));
    }

    [Fact]
    public async Task AsyncApi_2_maps_channels_to_event_flows_through_the_bus_and_messages_to_data_objects()
    {
        var alice = await _api.SignedIn("alice");
        var plan = await Plan(alice, File("billing-events.yaml"));
        Assert.True(plan.GetProperty("canApply").GetBoolean(), Errors(plan));
        // The existing flows are found by their ends and their name; they keep the data objects they carried.
        var sent = Entry(plan, "flow", "billing-bus");
        Assert.Equal(("update", "name"), (sent.Str("action"), sent.Str("matchedBy")));
        Assert.Equal("name", Entry(plan, "flow", "bus-billing").Str("matchedBy"));
        var issued = Entry(plan, "dataObject", "invoiceissued");
        Assert.Equal(("create", "Invoice issued"), (issued.Str("action"), issued.Str("name")));
        Assert.Contains(issued.GetProperty("possibleMatches").EnumerateArray(), m => m.Str("id") == "invoice");
        await Apply(alice, plan);

        var flow = await alice.GetJson("/api/flows/billing-bus");
        Assert.Equal(["invoice", "invoiceissued"], flow.GetProperty("dataObjectIds").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("billing", (await alice.GetJson("/api/data-objects/invoiceissued")).Str("sourceApplicationId"));
        Assert.Equal(JsonValueKind.Null, (await alice.GetJson("/api/data-objects/paymentreceived")).GetProperty("sourceApplicationId").ValueKind);
        Assert.Equal(Db(db => db.Applications.Count()), (await alice.GetJson("/api/applications")).GetArrayLength());
    }

    [Fact]
    public async Task AsyncApi_3_in_json_finds_the_broker_by_its_server_title()
    {
        var alice = await _api.SignedIn("alice");
        var plan = await Plan(alice, File("member-events.json"));
        Assert.True(plan.GetProperty("canApply").GetBoolean(), Errors(plan));
        Assert.Equal(("unchanged", "name"), (Entry(plan, "application", "bus").Str("action"), Entry(plan, "application", "bus").Str("matchedBy")));
        await Apply(alice, plan);
        var flow = await alice.GetJson("/api/flows/crm-bus-crm-member-updated");
        Assert.Equal(("crm", "bus", "event", "MemberUpdated"), (flow.Str("fromId"), flow.Str("toId"), flow.Str("type"), flow.Str("name")));
        Assert.Equal("A member changed their contact details.", flow.Str("description"));
        var member = (await alice.GetJson("/api/data-objects/memberupdated"));
        Assert.Equal(("personal", "crm", "Member updated"), (member.Str("sensitivity"), member.Str("sourceApplicationId"), member.Str("name")));
    }

    [Fact]
    public async Task Adr_zip_becomes_decision_record_pages_linked_to_the_applications_they_mention()
    {
        var bob = await _api.SignedIn("bob");
        var importer = await _api.SignedIn("alice");
        var zip = Zip("adr.zip", File("0001-record-architecture-decisions.md"), File("0002-invoice-run-on-postgresql.md"),
            File("0003-claims-photos-storage.md"), ("__MACOSX/._0001.md", [0, 1]));
        // Pages owned by one of the teams, read by its members.
        var plan = await Plan(importer, "?teamId=team-finance", HttpStatusCode.Created, zip);
        Assert.True(plan.GetProperty("canApply").GetBoolean(), Errors(plan));
        Assert.Equal((3, 0, 0), (Counts(plan).Create, Counts(plan).Update, Counts(plan).Errors));
        // The landscape keeps its decision records under "Architecture decisions" (the top-level page tagged adr): they go there.
        Assert.Equal(("unchanged", "tag"), (Entry(plan, "page", "architecture").Str("action"), Entry(plan, "page", "architecture").Str("matchedBy")));
        Assert.Equal("adr.zip/0002-invoice-run-on-postgresql.md", Entry(plan, "page", "adr-0002-invoice-run-on-postgresql").GetProperty("source").Str("file"));
        await Apply(importer, plan);

        var first = await bob.GetJson("/api/pages/adr-0001-record-architecture-decisions");
        Assert.Equal(("ADR 0001: Record architecture decisions", "architecture", 1), (first.Str("title"), first.Str("parentId"), first.GetProperty("order").GetInt32()));
        Assert.StartsWith("**Status:** Accepted · **Date:** 2026-03-02", first.Str("body"), StringComparison.Ordinal);
        Assert.Equal(["adr", "accepted"], first.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal("team-finance", first.Str("ownerTeamId"));

        var second = await bob.GetJson("/api/pages/adr-0002-invoice-run-on-postgresql");
        Assert.Equal("ADR 0002: Run the invoice batch on PostgreSQL", second.Str("title"));
        Assert.Equal("billing", second.GetProperty("attachedTo").Str("id"));
        Assert.Contains("## Records mentioned\n\n- [[application:billing]]", second.Str("body"), StringComparison.Ordinal);
        Assert.DoesNotContain("deciders", second.Str("body"), StringComparison.Ordinal);

        var third = await bob.GetJson("/api/pages/adr-0003-claims-photos-storage");
        Assert.Equal(["adr", "proposed"], third.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains("- [[application:claims]]\n- [[application:expert]]", third.Str("body"), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, third.GetProperty("attachedTo").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync("/api/pages/decision-records")).StatusCode);

        Assert.Equal((0, 0, 4, 0), Counts(await Plan(importer, "?teamId=team-finance", HttpStatusCode.Created, zip)));

        // Without such a page, a "Decision records" page is created.
        var alice = await _api.SignedIn("alice");
        var tree = await alice.GetJson("/api/pages/architecture");
        await alice.PutJson("/api/pages/architecture", new { title = tree.Str("title"), body = tree.Str("body"), tags = new[] { "reference" }, version = tree.GetProperty("version").GetInt32() });
        var fresh = await Plan(alice, File("0003-claims-photos-storage.md"));
        Assert.Equal("create", Entry(fresh, "page", "decision-records").Str("action"));
    }

    [Fact]
    public async Task Mermaid_c4_and_flowchart_become_applications_and_flows()
    {
        var alice = await _api.SignedIn("alice");
        var plan = await Plan(alice, File("prevention-c4.mmd"), File("finance-flows.mmd"));
        Assert.True(plan.GetProperty("canApply").GetBoolean(), Errors(plan));
        Assert.Equal("unchanged", Entry(plan, "application", "portal").Str("action"));
        Assert.Equal(("unchanged", "identifier"), (Entry(plan, "flow", "billing-acct").Str("action"), Entry(plan, "flow", "billing-acct").Str("matchedBy")));
        Assert.Equal("create", Entry(plan, "domain", "imported").Str("action"));
        var warnings = Messages(plan, "warnings");
        Assert.Contains("1 person(s) and 1 relation(s) with them left out", warnings, StringComparison.Ordinal);
        Assert.Contains("domain 'Imported'", warnings, StringComparison.Ordinal);
        await Apply(alice, plan);

        var booking = await alice.GetJson("/api/applications/booking");
        Assert.Equal(("customer", "Java", "app"), (booking.Str("domainId"), booking.Str("tech"), booking.Str("kind")));
        Assert.Equal("data", (await alice.GetJson("/api/applications/bookingdb")).Str("kind"));
        var calendar = await alice.GetJson("/api/applications/calendar");
        Assert.Equal(("external", "imported"), (calendar.Str("kind"), calendar.Str("domainId")));
        Assert.Equal("event", (await alice.GetJson("/api/flows/booking-calendar")).Str("type"));
        Assert.Equal(("api", "Books sessions"), ((await alice.GetJson("/api/flows/portal-booking")).Str("type"), (await alice.GetJson("/api/flows/portal-booking")).Str("name")));
        Assert.Equal("Ledger entries", (await alice.GetJson("/api/flows/billing-acct")).Str("name"));
        Assert.Equal(("check", "acct"), ((await alice.GetJson("/api/flows/check-acct")).Str("fromId"), "acct"));
        Assert.Equal("Payment check", (await alice.GetJson("/api/applications/check")).Str("name"));
    }

    [Fact]
    public async Task Excel_workbook_with_shared_strings_resolves_references_by_id_and_by_name()
    {
        var alice = await _api.SignedIn("alice");
        var plan = await Plan(alice, File("landscape.xlsx"));
        Assert.True(plan.GetProperty("canApply").GetBoolean(), Errors(plan));
        var billing = Entry(plan, "application", "billing");
        Assert.Equal(("update", "name"), (billing.Str("action"), billing.Str("matchedBy")));
        Assert.Equal(["tech"], billing.GetProperty("changes").EnumerateArray().Select(c => c.Str("field")));
        Assert.Equal("Applications!A3", billing.GetProperty("source").Str("place"));
        await Apply(alice, plan);

        var coaching = await alice.GetJson("/api/applications/coaching");
        Assert.Equal(("prevention", "team-prevention", "Kotlin", "planned"), (coaching.Str("domainId"), coaching.Str("teamId"), coaching.Str("tech"), coaching.Str("lifecycle")));
        var flow = await alice.GetJson("/api/flows/coaching-crm");
        Assert.Equal(["customer", "session"], flow.GetProperty("dataObjectIds").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(("crm", "planned", "team-prevention"), (flow.Str("toId"), flow.Str("status"), flow.Str("ownerTeamId")));
        Assert.Equal(".NET 9, PostgreSQL", (await alice.GetJson("/api/applications/billing")).Str("tech"));
        Assert.Equal(7, (await alice.GetJson("/api/domains/prevention")).GetProperty("order").GetInt32());
    }

    [Fact]
    public async Task Template_is_a_workbook_with_one_sheet_per_kind_and_errors_point_to_the_cell()
    {
        var bob = await _api.SignedIn("bob");
        var response = await bob.GetAsync("/api/imports/template.xlsx");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExcelReader.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("noosphere-import-template.xlsx", response.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        var sheets = Xlsx.Read(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(["Read me", "Domains", "Teams", "Applications", "Data objects", "Flows"], sheets.Select(s => s.Name));
        var header = sheets.Single(s => s.Name == "Applications").Rows[0].Cells;
        Assert.Equal(["id", "name", "domainId", "teamId", "kind", "tech", "lifecycle", "description"], header.OrderBy(c => c.Key).Select(c => c.Value));

        var alice = await _api.SignedIn("alice");
        var filled = Xlsx.Write([new Xlsx.SheetToWrite("Applications", [[.. header.OrderBy(c => c.Key).Select(c => c.Value)],
            ["", "Invoice archive", "finance", "team-finance", "mainframe", "", "", ""]], [])]);
        var plan = await Plan(alice, ("finance.xlsx", filled));
        Assert.False(plan.GetProperty("canApply").GetBoolean());
        var error = plan.GetProperty("errors").EnumerateArray().Single();
        Assert.Contains("'mainframe' is not one of app, bus, saas, data, file, external", error.Str("message"), StringComparison.Ordinal);
        Assert.Equal(("finance.xlsx", "Applications!E2"), (error.GetProperty("source").Str("file"), error.GetProperty("source").Str("place")));
        await Apply(alice, plan, HttpStatusCode.UnprocessableEntity);
    }
}
