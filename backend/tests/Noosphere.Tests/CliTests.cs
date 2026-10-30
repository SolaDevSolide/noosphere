using System.Text.Json;
using Noosphere.Api.Cli;

namespace Noosphere.Tests;

[Collection("Api")]
public class CliTests : IDisposable
{
    private readonly ApiFixture _api = new();
    public void Dispose() => _api.Dispose();

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args) => await RunWith(null, args);

    private static async Task<(int Code, string Out, string Err)> RunWith(IDictionary<string, string?>? config, params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = await CommandLine.Run(args, stdout, stderr, ApiFixture.CliConfiguration(config));
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Commands_are_recognised_and_server_options_are_not()
    {
        Assert.True(CommandLine.IsCommand(["users", "list"]));
        Assert.True(CommandLine.IsCommand(["noosphere", "migrate"]));
        Assert.True(CommandLine.IsCommand(["--help"]));
        Assert.False(CommandLine.IsCommand([]));
        Assert.False(CommandLine.IsCommand(["--urls", "http://+:8080"]));
    }

    [Fact]
    public async Task Help_and_usage_errors()
    {
        var help = await Run("help");
        Assert.Equal(0, help.Code);
        Assert.Contains("users create <username>", help.Out, StringComparison.Ordinal);
        Assert.Equal(CommandLine.ExitCodes.Usage, (await Run("frobnicate")).Code);
        Assert.Equal(CommandLine.ExitCodes.Usage, (await Run("users", "list", "--bogus")).Code);
        Assert.Equal(CommandLine.ExitCodes.Usage, (await Run("export", "--format")).Code);
        var json = await Run("users", "--json");
        Assert.Equal(CommandLine.ExitCodes.Usage, json.Code);
        Assert.Equal(2, JsonDocument.Parse(json.Err).RootElement.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Migrate_reports_an_up_to_date_database()
    {
        var result = await Run("migrate", "--json");
        Assert.Equal(0, result.Code);
        Assert.Equal(0, JsonDocument.Parse(result.Out).RootElement.GetProperty("result").GetProperty("applied").GetInt32());
    }

    [Fact]
    public async Task Users_are_listed_created_and_reset()
    {
        var list = await Run("users", "list", "--json");
        Assert.Equal(["alice", "bob", "claire"], JsonDocument.Parse(list.Out).RootElement.EnumerateArray().Select(u => u.GetProperty("username").GetString()));
        Assert.Contains("alice", (await Run("users", "list")).Out, StringComparison.Ordinal);

        var created = await Run("users", "create", "Dan", "--role", "admin", "--display-name", "Dan Roux", "--json");
        Assert.Equal(0, created.Code);
        var password = JsonDocument.Parse(created.Out).RootElement.GetProperty("result").GetProperty("password").GetString()!;
        Assert.Equal(19, password.Length);
        Assert.Equal("admin", (await (await _api.SignedIn("dan", password)).GetJson("/api/auth/me")).Str("role"));
        Assert.Equal(CommandLine.ExitCodes.Failure, (await Run("users", "create", "dan")).Code);
        Assert.Equal(CommandLine.ExitCodes.Usage, (await Run("users", "create", "eve", "--role", "boss")).Code);
        Assert.Equal(CommandLine.ExitCodes.Failure, (await Run("users", "create", "eve", "--password", "short")).Code);

        var reset = await Run("users", "reset-password", "bob", "--password", "bob-new-password");
        Assert.Equal(0, reset.Code);
        Assert.DoesNotContain("bob-new-password", reset.Out, StringComparison.Ordinal);
        await _api.SignedIn("bob", "bob-new-password");
        Assert.Equal(CommandLine.ExitCodes.NotFound, (await Run("users", "reset-password", "nobody")).Code);
    }

    [Fact]
    public async Task Export_then_import_round_trips()
    {
        var yaml = await Run("export", "--format", "yaml");
        Assert.Equal(0, yaml.Code);
        Assert.StartsWith("formatVersion: noosphere/v1", yaml.Out, StringComparison.Ordinal);
        var json = await Run("export");
        ExchangeTests.AssertValid(json.Out);
        Assert.Contains("Fraud scoring", json.Out, StringComparison.Ordinal);

        var file = Path.Combine(_api.Directory, "landscape.yaml");
        Directory.CreateDirectory(_api.Directory);
        Assert.Equal(0, (await Run("export", "--output", file)).Code);
        Assert.StartsWith("formatVersion:", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
        var dry = await Run("import", file, "--dry-run", "--json");
        Assert.Equal(0, dry.Code);
        var result = JsonDocument.Parse(dry.Out).RootElement.GetProperty("result");
        Assert.Equal((78, true), (result.GetProperty("unchanged").GetInt32(), result.GetProperty("dryRun").GetBoolean()));

        var bad = Path.Combine(_api.Directory, "bad.json");
        await File.WriteAllTextAsync(bad, """{"formatVersion":"noosphere/v1","mode":"full","items":[{"kind":"Team","id":"x","metadata":{"name":""},"spec":{}}]}""");
        var failed = await Run("import", bad);
        Assert.Equal(CommandLine.ExitCodes.Failure, failed.Code);
        Assert.Contains("items[0].metadata.name", failed.Err, StringComparison.Ordinal);
        Assert.Equal(CommandLine.ExitCodes.NotFound, (await Run("import", Path.Combine(_api.Directory, "missing.json"))).Code);
    }

    [Fact]
    public async Task Demo_reset_needs_confirmation_and_restores_the_demo()
    {
        var alice = await _api.SignedIn("alice");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, await alice.Delete("/api/flows/acct-legacy"));
        await alice.PostJson("/api/domains", new { name = "Extra" }, System.Net.HttpStatusCode.Created);
        await Run("users", "reset-password", "bob", "--password", "changed-password");

        Assert.Equal(CommandLine.ExitCodes.Confirmation, (await Run("demo", "reset")).Code);
        Assert.Equal(0, (await Run("demo", "reset", "--yes")).Code);

        var landscape = await (await _api.SignedIn("bob", "demo")).GetJson("/api/landscape");
        Assert.Equal(36, landscape.GetProperty("stats").GetProperty("flows").GetInt32());
        Assert.Equal(6, landscape.GetProperty("domains").GetArrayLength());

        var off = await RunWith(new Dictionary<string, string?> { ["DEMO"] = "false" }, "demo", "reset", "--yes");
        Assert.Equal(CommandLine.ExitCodes.Failure, off.Code);
    }

    [Fact]
    public async Task Health_probe_fails_when_no_server_answers() =>
        Assert.Equal(CommandLine.ExitCodes.Environment, (await Run("health", "--url", "http://127.0.0.1:1/health/ready")).Code);

    [Fact]
    public async Task Unreachable_database_or_missing_configuration_exit_with_3()
    {
        var down = await RunWith(new Dictionary<string, string?> { ["DATABASE"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2" }, "users", "list");
        Assert.Equal(CommandLine.ExitCodes.Environment, down.Code);
        Assert.Contains("Database unreachable", down.Err, StringComparison.Ordinal);
        var missing = await RunWith(new Dictionary<string, string?> { ["DATABASE"] = "" }, "migrate");
        Assert.Equal(CommandLine.ExitCodes.Environment, missing.Code);
    }
}
