using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Exchange;
using Noosphere.Domain;

namespace Noosphere.Api.Data;

/// <summary>Startup: migrations, first administrator, and the demo landscape and accounts in demo mode.</summary>
public static class Bootstrap
{
    public const string DemoPassword = "demo";

    /// <summary>Demo accounts: alice (admin), bob (member), claire (guest), all with the password <see cref="DemoPassword"/>.</summary>
    public static readonly IReadOnlyList<(string Username, string DisplayName, Role Role)> DemoUsers =
    [
        ("alice", "Alice Martin", Role.Admin),
        ("bob", "Bob Durand", Role.Member),
        ("claire", "Claire Petit", Role.Guest),
    ];

    public static async Task Run(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<NoosphereDbContext>();
        var settings = sp.GetRequiredService<Settings>();
        var hasher = sp.GetRequiredService<IPasswordHasher<User>>();
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Noosphere.Startup");

        await DatabaseMigrator.Migrate(db, settings.BackupsDirectory, logger);

        if (settings.Demo)
        {
            logger.LogWarning("Demo mode is on (NOOSPHERE_DEMO=true): accounts alice, bob and claire have the password \"demo\". Never use it on a shared server.");
            if (await EnsureDemoUsers(db, hasher, now) is > 0 and var created)
                logger.LogInformation("{Count} demo account(s) created.", created);
            if (!await db.Applications.AnyAsync() && !await db.Domains.AnyAsync())
            {
                await LoadDemoLandscape(db, now);
                logger.LogInformation("Demo landscape \"Alizé\" loaded.");
            }
            return;
        }

        if (await db.Users.AnyAsync()) return;
        var admin = new User
        {
            Username = settings.AdminUsername, DisplayName = "Administrator", Role = Role.Admin,
            CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        if (settings.AdminPassword is { } password)
        {
            if (Accounts.PasswordProblem(password) is { } problem)
            {
                logger.LogError("NOOSPHERE_ADMIN_PASSWORD is refused ({Problem}): no account created. Fix it and restart.", problem);
                return;
            }
            Accounts.SetPassword(admin, password, hasher);
            logger.LogInformation("Administrator account {Username} created with the password from NOOSPHERE_ADMIN_PASSWORD.", admin.Username);
        }
        else
        {
            var generated = Accounts.RandomPassword();
            Accounts.SetPassword(admin, generated, hasher);
            // Printed once: the password is not stored anywhere else.
            logger.LogWarning("Administrator account {Username} created. Its password, shown only this once, is: {Password}", admin.Username, generated);
        }
        db.Users.Add(admin);
        await db.SaveChangesAsync();
    }

    /// <summary>Creates the missing demo accounts; returns how many were created.</summary>
    public static async Task<int> EnsureDemoUsers(NoosphereDbContext db, IPasswordHasher<User> hasher, DateTimeOffset now, bool resetPasswords = false)
    {
        var created = 0;
        foreach (var (username, displayName, role) in DemoUsers)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user is null)
            {
                user = new User { Username = username, DisplayName = displayName, Role = role, CreatedAt = now, UpdatedAt = now, Version = 1 };
                db.Users.Add(user);
                created++;
            }
            else if (!resetPasswords) continue;
            else
            {
                (user.DisplayName, user.Role, user.UpdatedAt) = (displayName, role, now);
                user.Version++;
            }
            Accounts.SetPassword(user, DemoPassword, hasher);
        }
        await db.SaveChangesAsync();
        return created;
    }

    /// <summary>The "Alizé" demo landscape, in the exchange format.</summary>
    public static string DemoLandscape()
    {
        using var stream = typeof(Bootstrap).Assembly.GetManifestResourceStream("alize.noosphere.json")
            ?? throw new InvalidOperationException("Embedded demo landscape missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static async Task LoadDemoLandscape(NoosphereDbContext db, DateTimeOffset now)
    {
        var result = await ExchangeFormat.Import(db, ExchangeFormat.Parse(DemoLandscape(), "json"), dryRun: false, now);
        if (result.Errors.Count > 0)
            throw new InvalidOperationException("Invalid demo landscape: " + string.Join("; ", result.Errors.Select(e => $"{e.Path}: {e.Message}")));
    }

    /// <summary>Deletes the whole landscape (not the accounts).</summary>
    public static async Task ClearLandscape(NoosphereDbContext db)
    {
        await db.Flows.ExecuteDeleteAsync();
        await db.DataObjects.ExecuteDeleteAsync();
        await db.Applications.ExecuteDeleteAsync();
        await db.Teams.ExecuteDeleteAsync();
        await db.Domains.ExecuteDeleteAsync();
    }
}
