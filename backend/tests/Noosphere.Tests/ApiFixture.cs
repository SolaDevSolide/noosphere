using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Noosphere.Tests;

/// <summary>API test classes share one database: they run one after the other.</summary>
[CollectionDefinition("Api", DisableParallelization = true)]
public class ApiTestGroup;

/// <summary>
/// Starts the application against the PostgreSQL database <c>noosphere_tests</c>, created afresh for every instance
/// (migrations and demo data loaded by the application at startup). Server: TEST_PG_SERVER, by default
/// localhost:5432 with postgres/postgres.
/// </summary>
public sealed class ApiFixture : IDisposable
{
    public const string Database = "noosphere_tests";

    public static string Server =>
        Environment.GetEnvironmentVariable("TEST_PG_SERVER") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    public static string ConnectionString => $"{Server};Database={Database}";

    private readonly WebApplicationFactory<Program> _root;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Dictionary<string, string?> _previous = [];

    /// <summary>Temporary directory (keys, backups, web root), deleted at the end.</summary>
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "noosphere-tests-" + Guid.NewGuid().ToString("N"));

    /// <param name="demo">false: an empty instance outside demo mode.</param>
    /// <param name="settings">Extra NOOSPHERE_* variables (without the prefix).</param>
    public ApiFixture(bool demo = true, IDictionary<string, string>? settings = null, Action<IWebHostBuilder>? host = null)
    {
        RecreateDatabase();
        var values = new Dictionary<string, string>
        {
            ["DATABASE"] = ConnectionString,
            ["DEMO"] = demo ? "true" : "false",
            ["LOGIN_RATE_LIMIT"] = "1000",
            ["KEYS_DIR"] = Path.Combine(Directory, "keys"),
            ["BACKUPS_DIR"] = Path.Combine(Directory, "backups"),
            ["ADMIN_PASSWORD"] = "",
        };
        foreach (var (k, v) in settings ?? new Dictionary<string, string>()) values[k] = v;
        foreach (var (k, v) in values)
        {
            _previous[k] = Environment.GetEnvironmentVariable("NOOSPHERE_" + k);
            Environment.SetEnvironmentVariable("NOOSPHERE_" + k, v);
        }
        _factory = _root = new WebApplicationFactory<Program>();
        if (host is not null) _factory = _root.WithWebHostBuilder(host);
        _ = _factory.Server; // starts the host: migrations and demo data
    }

    public static void RecreateDatabase()
    {
        using var cn = new NpgsqlConnection(Server + ";Database=postgres");
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"drop database if exists {Database} with (force)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = $"create database {Database}";
        cmd.ExecuteNonQuery();
    }

    public IServiceProvider Services => _factory.Services;

    /// <summary>Configuration as the command line reads it (the same NOOSPHERE_* variables), plus overrides.</summary>
    public static IConfiguration CliConfiguration(IDictionary<string, string?>? extra = null)
    {
        var builder = new ConfigurationBuilder().AddEnvironmentVariables("NOOSPHERE_");
        if (extra is not null) builder.AddInMemoryCollection(extra);
        return builder.Build();
    }

    /// <summary>HTTP client sending the anti-CSRF header, like the frontend.</summary>
    public HttpClient Client()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Requested-With", "fetch");
        return client;
    }

    /// <summary>HTTP client without the anti-CSRF header (a request forged by another site).</summary>
    public HttpClient ClientWithoutHeader() => _factory.CreateClient();

    public async Task<HttpClient> SignedIn(string username, string password = "demo")
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public void Dispose()
    {
        _root.Dispose();
        foreach (var (k, v) in _previous) Environment.SetEnvironmentVariable("NOOSPHERE_" + k, v);
        NpgsqlConnection.ClearAllPools();
        if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
    }
}
