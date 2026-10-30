using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Data;
using Noosphere.Api.Exchange;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Cli;

/// <summary>
/// The <c>noosphere</c> command line, in the same executable as the server. It reads the same configuration
/// (NOOSPHERE_* environment variables) and works on the database directly. Results go to standard output, messages
/// and errors to standard error; <c>--json</c> makes both machine-readable. Exit codes: <see cref="ExitCodes"/>.
/// </summary>
public static class CommandLine
{
    public static class ExitCodes
    {
        public const int Success = 0;
        /// <summary>The command ran and failed: invalid file, refused value...</summary>
        public const int Failure = 1;
        /// <summary>Unknown command or option, missing argument.</summary>
        public const int Usage = 2;
        /// <summary>Database unreachable or configuration missing.</summary>
        public const int Environment = 3;
        /// <summary>A destructive command was run without <c>--yes</c>.</summary>
        public const int Confirmation = 4;
        /// <summary>The named user (or file) does not exist.</summary>
        public const int NotFound = 5;
    }

    private static readonly string[] Commands = ["migrate", "users", "export", "import", "demo", "health", "help", "version"];
    private static readonly HashSet<string> Flags = ["--json", "--yes", "--dry-run", "--help", "-h"];
    private static readonly HashSet<string> Valued = ["--format", "--output", "--role", "--display-name", "--language", "--password", "--url"];

    public const string Usage = """
        Usage: noosphere <command> [options]

        Commands:
          migrate                                  Apply pending database migrations
          users list                               List the accounts
          users create <username> [--role admin|member|guest] [--display-name <name>] [--language en|fr] [--password <p>]
                                                   Create an account (a password is generated when none is given)
          users reset-password <username> [--password <p>]
                                                   Set a new password (generated when none is given) and close the user's sessions
          export [--format json|yaml] [--output <file>]
                                                   Write the landscape in the exchange format (standard output by default)
          import <file|-> [--dry-run] [--format json|yaml]
                                                   Validate a file, then create or update its items (--dry-run: validate only)
          demo reset --yes                         Replace the landscape with the Alizé demo and reset the demo accounts (demo mode only)
          health [--url <url>]                     Exit 0 when the running server is ready (default http://127.0.0.1:8080/health/ready)
          version                                  Print the version
          help                                     Print this help

        Global options:
          --json                                   Machine-readable output
          --help, -h                               Print this help

        Exit codes: 0 success, 1 failure, 2 usage error, 3 database or configuration unavailable, 4 confirmation missing, 5 not found.
        """;

    private static readonly JsonSerializerOptions JsonOutput = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed class UsageException(string message) : Exception(message);

    private sealed class CommandException(int code, string message, IReadOnlyList<string>? details = null) : Exception(message)
    {
        public int Code { get; } = code;
        public IReadOnlyList<string>? Details { get; } = details;
    }

    private sealed record Options(List<string> Positionals, Dictionary<string, string> Values, HashSet<string> Flags)
    {
        public bool Json => Flags.Contains("--json");
        public string? this[string name] => Values.GetValueOrDefault(name);
    }

    private sealed record Output(TextWriter Out, TextWriter Error, bool Json);

    /// <summary>True when the arguments name a command (otherwise the web server starts, with ASP.NET options such as --urls).</summary>
    public static bool IsCommand(string[] args) =>
        args.Length > 0 && (args[0] is "noosphere" or "--help" or "-h" || Commands.Contains(args[0]) || !args[0].StartsWith('-'));

    public static async Task<int> Run(string[] args, TextWriter? stdout = null, TextWriter? stderr = null, IConfiguration? configuration = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;
        if (args is ["noosphere", .. var rest]) args = rest;
        var output = new Output(stdout, stderr, args.Contains("--json"));
        try
        {
            var options = Parse(args);
            if (options.Positionals.Count == 0 || options.Positionals[0] == "help" || options.Flags.Contains("--help") || options.Flags.Contains("-h"))
            {
                await stdout.WriteLineAsync(Usage);
                return ExitCodes.Success;
            }
            if (options.Positionals[0] == "version")
            {
                var version = typeof(CommandLine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
                await Done(output, version.Split('+')[0], new { version });
                return ExitCodes.Success;
            }
            if (options.Positionals[0] == "health") return await Health(options, output);
            configuration ??= DefaultConfiguration();
            var settings = Settings.From(configuration);
            var services = new ServiceCollection()
                .AddLogging(l => l.AddSimpleConsole(o => o.SingleLine = true)
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace))
                .AddNoosphereCore(settings)
                .BuildServiceProvider();
            await using (services)
            {
                using var scope = services.CreateScope();
                return await Dispatch(options, scope.ServiceProvider, output);
            }
        }
        catch (UsageException e)
        {
            await Fail(output, ExitCodes.Usage, e.Message, ["Run 'noosphere help' for the list of commands."]);
            return ExitCodes.Usage;
        }
        catch (CommandException e)
        {
            await Fail(output, e.Code, e.Message, e.Details ?? []);
            return e.Code;
        }
        catch (MissingConfigurationException e)
        {
            await Fail(output, ExitCodes.Environment, e.Message, []);
            return ExitCodes.Environment;
        }
        catch (Exception e) when (Unreachable(e) is { } cause)
        {
            await Fail(output, ExitCodes.Environment, $"Database unreachable: {cause.Message}", ["Check NOOSPHERE_DATABASE and that PostgreSQL is running."]);
            return ExitCodes.Environment;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Fail(output, ExitCodes.Failure, $"File error: {e.Message}", []);
            return ExitCodes.Failure;
        }
    }

    /// <summary>Probe used by the Docker HEALTHCHECK (the runtime image has neither curl nor wget).</summary>
    private static async Task<int> Health(Options o, Output output)
    {
        Expect(o.Positionals, 1);
        var url = o["--url"] ?? "http://127.0.0.1:8080/health/ready";
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await client.GetAsync(new Uri(url));
            if (response.IsSuccessStatusCode)
            {
                await Done(output, "Ready.", null);
                return ExitCodes.Success;
            }
            throw new CommandException(ExitCodes.Failure, $"Not ready: HTTP {(int)response.StatusCode}.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            throw new CommandException(ExitCodes.Environment, $"Server unreachable at {url}: {e.Message}");
        }
    }

    private static IConfiguration DefaultConfiguration()
    {
        var environment = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables();
        Settings.AddSources(builder);
        return builder.Build();
    }

    private static Exception? Unreachable(Exception e)
    {
        for (var x = (Exception?)e; x is not null; x = x.InnerException)
            if (x is Npgsql.NpgsqlException or System.Net.Sockets.SocketException) return x;
        return null;
    }

    private static Options Parse(string[] args)
    {
        var options = new Options([], [], []);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (Flags.Contains(a)) options.Flags.Add(a);
            else if (Valued.Contains(a))
            {
                if (i + 1 >= args.Length) throw new UsageException($"Missing value after {a}.");
                options.Values[a] = args[++i];
            }
            else if (a.StartsWith("--", StringComparison.Ordinal)) throw new UsageException($"Unknown option {a}.");
            else options.Positionals.Add(a);
        }
        return options;
    }

    private static async Task<int> Dispatch(Options o, IServiceProvider sp, Output output)
    {
        var db = sp.GetRequiredService<NoosphereDbContext>();
        var p = o.Positionals;
        switch (p[0])
        {
            case "migrate":
                Expect(p, 1);
                var settings = sp.GetRequiredService<Settings>();
                var count = await DatabaseMigrator.Migrate(db, settings.BackupsDirectory, sp.GetRequiredService<ILoggerFactory>().CreateLogger("Noosphere.Migrate"));
                await Done(output, count == 0 ? "The database is up to date." : $"{count} migration(s) applied.", new { applied = count });
                return ExitCodes.Success;
            case "users" when p.Count >= 2 && p[1] == "list":
                Expect(p, 2);
                var users = await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync();
                if (output.Json) await output.Out.WriteLineAsync(JsonSerializer.Serialize(await Accounts.Dtos(db, users), JsonOutput));
                else
                {
                    await output.Out.WriteLineAsync($"{"USERNAME",-20} {"ROLE",-8} {"LANGUAGE",-8} DISPLAY NAME");
                    foreach (var u in users)
                        await output.Out.WriteLineAsync($"{u.Username,-20} {u.Role.ToString().ToLowerInvariant(),-8} {u.Language,-8} {u.DisplayName}");
                }
                return ExitCodes.Success;
            case "users" when p.Count >= 2 && p[1] == "create":
                return await CreateUser(o, sp, output);
            case "users" when p.Count >= 2 && p[1] == "reset-password":
                return await ResetPassword(o, sp, output);
            case "users":
                throw new UsageException("Expected 'users list', 'users create <username>' or 'users reset-password <username>'.");
            case "export":
                return await Export(o, db, sp, output);
            case "import":
                return await Import(o, db, sp, output);
            case "demo" when p.Count >= 2 && p[1] == "reset":
                Expect(p, 2);
                if (!sp.GetRequiredService<Settings>().Demo)
                    throw new CommandException(ExitCodes.Failure, "Demo mode is off: set NOOSPHERE_DEMO=true to use 'demo reset'.");
                if (!o.Flags.Contains("--yes"))
                    throw new CommandException(ExitCodes.Confirmation, "This replaces the whole landscape with the demo. Run again with --yes to confirm.");
                var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
                await using (var transaction = await db.Database.BeginTransactionAsync())
                {
                    await Bootstrap.ClearLandscape(db);
                    await Bootstrap.EnsureDemoUsers(db, sp.GetRequiredService<IPasswordHasher<User>>(), now, resetPasswords: true);
                    await Bootstrap.LoadDemo(db, now);
                    await transaction.CommitAsync();
                }
                await Done(output, "Demo landscape and demo accounts reset.", null);
                return ExitCodes.Success;
            case "demo":
                throw new UsageException("Expected 'demo reset --yes'.");
            default:
                throw new UsageException($"Unknown command '{p[0]}'.");
        }
    }

    private static void Expect(List<string> positionals, int count)
    {
        if (positionals.Count > count) throw new UsageException($"Unexpected argument '{positionals[count]}'.");
        if (positionals.Count < count) throw new UsageException("Missing argument.");
    }

    private static async Task<int> CreateUser(Options o, IServiceProvider sp, Output output)
    {
        Expect(o.Positionals, 3);
        var db = sp.GetRequiredService<NoosphereDbContext>();
        var username = Accounts.NormalizeUsername(o.Positionals[2]);
        if (Accounts.UsernameProblem(username) is { } problem) throw new CommandException(ExitCodes.Failure, $"Invalid username: {problem}");
        var role = o["--role"] is { } r
            ? Enum.TryParse<Role>(r, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : throw new UsageException("--role expects admin, member or guest.")
            : Role.Member;
        var language = o["--language"]?.ToLowerInvariant() ?? "en";
        if (!Rules.Languages.Contains(language)) throw new UsageException("--language expects en or fr.");
        if (await db.Users.AnyAsync(u => u.Username == username)) throw new CommandException(ExitCodes.Failure, $"The username '{username}' is already used.");
        var (password, generated) = Password(o);
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
        var user = new User
        {
            Username = username, DisplayName = o["--display-name"]?.Trim() is { Length: > 0 } name ? name : username,
            Role = role, Language = language, CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        Accounts.SetPassword(user, password, sp.GetRequiredService<IPasswordHasher<User>>());
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await Done(output, $"User {username} created ({role.ToString().ToLowerInvariant()})." + (generated ? $" Password: {password}" : ""),
            new { user = Accounts.Dto(user, []), password = generated ? password : null });
        return ExitCodes.Success;
    }

    private static async Task<int> ResetPassword(Options o, IServiceProvider sp, Output output)
    {
        Expect(o.Positionals, 3);
        var db = sp.GetRequiredService<NoosphereDbContext>();
        var username = Accounts.NormalizeUsername(o.Positionals[2]);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username)
            ?? throw new CommandException(ExitCodes.NotFound, $"No user named '{username}'.");
        var (password, generated) = Password(o);
        Accounts.SetPassword(user, password, sp.GetRequiredService<IPasswordHasher<User>>());
        user.UpdatedAt = sp.GetRequiredService<TimeProvider>().GetUtcNow();
        user.Version++;
        await db.SaveChangesAsync();
        await Done(output, $"Password of {username} changed; their sessions are closed." + (generated ? $" New password: {password}" : ""),
            new { username, password = generated ? password : null });
        return ExitCodes.Success;
    }

    private static (string Password, bool Generated) Password(Options o)
    {
        if (o["--password"] is not { } given) return (Accounts.RandomPassword(), true);
        if (Accounts.PasswordProblem(given) is { } problem) throw new CommandException(ExitCodes.Failure, $"Password refused: {problem}");
        return (given, false);
    }

    private static async Task<int> Export(Options o, NoosphereDbContext db, IServiceProvider sp, Output output)
    {
        Expect(o.Positionals, 1);
        var format = (o["--format"] ?? (o["--output"] is { } f && (f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)) ? "yaml" : "json")).ToLowerInvariant();
        if (format is not ("json" or "yaml")) throw new UsageException("--format expects json or yaml.");
        var settings = sp.GetRequiredService<Settings>();
        var envelope = ExchangeFormat.Export(await Snapshot.Load(db), await db.Pages.AsNoTracking().ToListAsync(), Viewer.System,
            new Contracts.ExchangeSourceDto((await Endpoints.InstanceSettingsStore.Get(db, settings)).InstanceName, settings.PublicUrl, sp.GetRequiredService<TimeProvider>().GetUtcNow()));
        var text = format == "yaml" ? ExchangeFormat.ToYaml(envelope) : ExchangeFormat.ToJson(envelope);
        if (o["--output"] is { } file)
        {
            await File.WriteAllTextAsync(file, text);
            await Done(output, $"{envelope.Items.Count} item(s) written to {file}.", new { items = envelope.Items.Count, file });
        }
        else await output.Out.WriteAsync(text);
        return ExitCodes.Success;
    }

    private static async Task<int> Import(Options o, NoosphereDbContext db, IServiceProvider sp, Output output)
    {
        Expect(o.Positionals, 2);
        var file = o.Positionals[1];
        string text;
        if (file == "-") text = await Console.In.ReadToEndAsync();
        else if (!File.Exists(file)) throw new CommandException(ExitCodes.NotFound, $"File not found: {file}");
        else text = await File.ReadAllTextAsync(file);
        var format = o["--format"]?.ToLowerInvariant()
            ?? (file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ? "yaml"
                : file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "json" : null);
        if (format is not (null or "json" or "yaml")) throw new UsageException("--format expects json or yaml.");
        var dryRun = o.Flags.Contains("--dry-run");
        System.Text.Json.Nodes.JsonNode? root;
        try
        {
            root = ExchangeFormat.Parse(text, format);
        }
        catch (ApiProblem e)
        {
            throw new CommandException(ExitCodes.Failure, e.Message);
        }
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await ExchangeFormat.Import(db, root, dryRun, sp.GetRequiredService<TimeProvider>().GetUtcNow());
        await transaction.CommitAsync();
        if (result.Errors.Count > 0)
        {
            if (output.Json) await output.Out.WriteLineAsync(JsonSerializer.Serialize(new { code = ExitCodes.Failure, result }, JsonOutput));
            throw new CommandException(ExitCodes.Failure, $"The file has {result.Errors.Count} error(s); nothing was imported.",
                [.. result.Errors.Select(e => $"{e.Path}: {e.Message}")]);
        }
        var summary = $"{result.Created} created, {result.Updated} updated, {result.Unchanged} unchanged.";
        await Done(output, dryRun ? $"Dry run, nothing written: {summary}" : $"Imported: {summary}", result);
        return ExitCodes.Success;
    }

    private static async Task Done(Output output, string message, object? result)
    {
        if (output.Json) await output.Out.WriteLineAsync(JsonSerializer.Serialize(new { code = ExitCodes.Success, message, result }, JsonOutput));
        else await output.Out.WriteLineAsync(message);
    }

    private static async Task Fail(Output output, int code, string message, IReadOnlyList<string> details)
    {
        if (output.Json)
        {
            await output.Error.WriteLineAsync(JsonSerializer.Serialize(new { code, error = message, details }, JsonOutput));
            return;
        }
        await output.Error.WriteLineAsync($"Error: {message}");
        foreach (var d in details) await output.Error.WriteLineAsync($"  {d}");
    }
}
