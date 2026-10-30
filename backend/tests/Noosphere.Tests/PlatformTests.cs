using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Noosphere.Api.Data;

namespace Noosphere.Tests;

/// <summary>
/// The OpenAPI document served by the API must equal the committed copy <c>docs/openapi.json</c>, from which the frontend
/// generates its types. After a contract change: <c>UPDATE_OPENAPI=1 dotnet test --filter OpenApi</c>, then
/// <c>npm run api:types</c> in <c>frontend</c>.
/// </summary>
[Collection("Api")]
public class OpenApiTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static readonly JsonSerializerOptions Write = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public async Task Document_matches_the_committed_copy()
    {
        var response = await _api.Client().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.ToJsonString(Write).ReplaceLineEndings("\n") + "\n";

        var file = Path.Combine(RepositoryRoot(), "docs", "openapi.json");
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
            await File.WriteAllTextAsync(file, current);

        Assert.True(File.Exists(file), "docs/openapi.json is missing: run UPDATE_OPENAPI=1 dotnet test --filter OpenApi.");
        Assert.True((await File.ReadAllTextAsync(file)).ReplaceLineEndings("\n") == current,
            "docs/openapi.json is stale: run UPDATE_OPENAPI=1 dotnet test --filter OpenApi, then npm run api:types in frontend.");
    }

    internal static string RepositoryRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "docs")) && Directory.Exists(Path.Combine(d.FullName, "backend")))
                return d.FullName;
        throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}

[Collection("Api")]
public class HealthTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Live_and_ready_answer_without_a_session()
    {
        var client = _api.Client();
        Assert.Equal("live", (await client.GetJson("/health/live")).Str("status"));
        var ready = await client.GetJson("/health/ready");
        Assert.Equal(("ready", "ok"), (ready.Str("status"), ready.Str("database")));
    }

    [Fact]
    public async Task Ready_fails_while_migrations_are_pending()
    {
        using (var scope = _api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<NoosphereDbContext>().Database
                .ExecuteSqlRawAsync("""delete from "__EFMigrationsHistory" """);
        var ready = await _api.Client().GetJson("/health/ready", HttpStatusCode.ServiceUnavailable);
        Assert.Equal(("unavailable", "migrations-pending"), (ready.Str("status"), ready.Str("database")));
        Assert.Equal(HttpStatusCode.OK, (await _api.Client().GetAsync("/health/live")).StatusCode);
    }
}

[Collection("Api")]
public class MigrationBackupTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Backup_writes_every_table_as_json()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
        var file = await DatabaseMigrator.Backup(db, Path.Combine(_api.Directory, "backups"));
        var backup = JsonDocument.Parse(await File.ReadAllTextAsync(file)).RootElement;
        Assert.Equal(20, backup.GetProperty("Applications").GetArrayLength());
        Assert.Equal(36, backup.GetProperty("Flows").GetArrayLength());
        Assert.Equal(4, backup.GetProperty("Users").GetArrayLength());
        Assert.Equal(7, backup.GetProperty("Pages").GetArrayLength());
        Assert.True(backup.TryGetProperty("__EFMigrationsHistory", out _));
    }

    [Fact]
    public async Task Pending_migrations_on_an_existing_database_are_backed_up_first()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
        // Pretend the last migration is not applied yet, with its tables already there.
        var last = (await db.Database.GetAppliedMigrationsAsync()).Last();
        await db.Database.ExecuteSqlAsync($"""delete from "__EFMigrationsHistory" where "MigrationId" = {last}""");
        await db.Database.ExecuteSqlRawAsync("""drop table "ProposalApprovals", "ProposalChanges", "Proposals", "Revisions", "Pages", "TeamMembers", "InstanceSettings" """);
        // An older migration stays applied: the database is not new, so it is backed up.
        await db.Database.ExecuteSqlRawAsync("""insert into "__EFMigrationsHistory" values ('20200101000000_Older', '10.0.0')""");
        var directory = Path.Combine(_api.Directory, "backups");

        Assert.Equal(1, await DatabaseMigrator.Migrate(db, directory, NullLogger.Instance));
        Assert.Single(Directory.GetFiles(directory, "before-migration-*.json"));
        Assert.Equal(0, await DatabaseMigrator.Migrate(db, directory, NullLogger.Instance));

        // The migration backfills one revision per existing record, with its current state.
        Assert.Equal(6 + 11 + 20 + 5 + 36, await db.Revisions.CountAsync());
        var billing = await db.Revisions.SingleAsync(r => r.EntityId == "billing");
        var app = await db.Applications.SingleAsync(a => a.Id == "billing");
        Assert.Equal((app.Version, "system"), (billing.Version, billing.AuthorName));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(Noosphere.Api.Services.Records.Application.Snapshot(app),
            System.Text.Json.Nodes.JsonNode.Parse(billing.Snapshot)), billing.Snapshot);
        var flow = await db.Revisions.SingleAsync(r => r.Kind == Noosphere.Domain.RecordKind.Flow && r.EntityId == "crm-to-billing-customers");
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            Noosphere.Api.Services.Records.Flow.Snapshot(await db.Flows.SingleAsync(f => f.Id == "crm-to-billing-customers")),
            System.Text.Json.Nodes.JsonNode.Parse(flow.Snapshot)), flow.Snapshot);
    }
}

[Collection("Api")]
public class SpaTests : IDisposable
{
    private readonly ApiFixture _api;

    public SpaTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "noosphere-spa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>Noosphere</title>");
        File.WriteAllText(Path.Combine(root, "assets", "app.js"), "console.log(1)");
        WebRoot = root;
        _api = new ApiFixture(host: b => b.UseWebRoot(root));
    }

    private string WebRoot { get; }

    public void Dispose()
    {
        _api.Dispose();
        Directory.Delete(WebRoot, recursive: true);
    }

    [Fact]
    public async Task Client_routes_fall_back_to_index_html_but_api_routes_do_not()
    {
        var client = _api.Client();
        foreach (var path in new[] { "/", "/applications/crm/lens", "/admin/users" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("<title>Noosphere</title>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        Assert.Equal("console.log(1)", await client.GetStringAsync("/assets/app.js"));
        var api = await client.GetAsync("/api/unknown");
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Equal("application/problem+json", api.Content.Headers.ContentType?.MediaType);
    }
}
