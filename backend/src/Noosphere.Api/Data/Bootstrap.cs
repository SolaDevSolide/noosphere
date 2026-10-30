using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using System.Text.Json.Nodes;
using Noosphere.Api.Contracts;
using Noosphere.Api.Exchange;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Data;

/// <summary>Startup: migrations, first administrator, and the demo landscape and accounts in demo mode.</summary>
public static class Bootstrap
{
    public const string DemoPassword = "demo";

    /// <summary>
    /// Demo accounts, all with the password <see cref="DemoPassword"/>: alice (admin), bob (member of Team Finance and
    /// Team Payments), claire (guest) and david (member of Team CRM).
    /// </summary>
    public static readonly IReadOnlyList<(string Username, string DisplayName, Role Role)> DemoUsers =
    [
        ("alice", "Alice Martin", Role.Admin),
        ("bob", "Bob Durand", Role.Member),
        ("claire", "Claire Petit", Role.Guest),
        ("david", "David Moreau", Role.Member),
    ];

    /// <summary>Team memberships of the demo accounts.</summary>
    public static readonly IReadOnlyList<(string Username, string TeamId)> DemoMemberships =
    [
        ("bob", "team-finance"),
        ("bob", "team-payments"),
        ("david", "team-crm"),
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
            logger.LogWarning("Demo mode is on (NOOSPHERE_DEMO=true): accounts alice, bob, claire and david have the password \"demo\". Never use it on a shared server.");
            if (await EnsureDemoUsers(db, hasher, now) is > 0 and var created)
                logger.LogInformation("{Count} demo account(s) created.", created);
            if (!await db.Applications.AnyAsync() && !await db.Domains.AnyAsync())
            {
                await LoadDemo(db, now);
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

    /// <summary>
    /// The demo landscape (loaded as of 30 days ago), team memberships, a few revisions of Billing, the customer portal
    /// shared with claire (guest), and proposals: two waiting for Team Finance and one refused. The demo accounts must exist.
    /// </summary>
    public static async Task LoadDemo(NoosphereDbContext db, DateTimeOffset now)
    {
        await LoadDemoLandscape(db, now.AddDays(-30));
        await SeedDemoActivity(db, now);
    }

    public static async Task LoadDemoLandscape(NoosphereDbContext db, DateTimeOffset now)
    {
        var result = await ExchangeFormat.Import(db, ExchangeFormat.Parse(DemoLandscape(), "json"), dryRun: false, now);
        if (result.Errors.Count > 0)
            throw new InvalidOperationException("Invalid demo landscape: " + string.Join("; ", result.Errors.Select(e => $"{e.Path}: {e.Message}")));
    }

    private static async Task SeedDemoActivity(NoosphereDbContext db, DateTimeOffset now)
    {
        var users = await db.Users.ToDictionaryAsync(u => u.Username);
        if (DemoUsers.Any(u => !users.ContainsKey(u.Username))) return;
        foreach (var (username, team) in DemoMemberships)
            if (!await db.TeamMembers.AnyAsync(m => m.UserId == users[username].Id && m.TeamId == team))
                db.TeamMembers.Add(new TeamMember { UserId = users[username].Id, TeamId = team });
        await db.SaveChangesAsync();

        Author As(string username) => new(users[username].Id, username);
        async Task Edit(string id, DateTimeOffset at, string username, string summary, Action<JsonObject> change)
        {
            var e = await db.Applications.FirstAsync(a => a.Id == id);
            var body = Records.Application.Snapshot(e);
            change(body);
            new Publisher(db, at).Update(Records.Application, e, body, As(username), await References.Load(db), summary: summary);
            await db.SaveChangesAsync();
        }

        // History of Billing.
        await Edit("billing", now.AddDays(-21), "bob", "Tech stack after the .NET upgrade", b => b["tech"] = ".NET 8, PostgreSQL 16");
        await Edit("billing", now.AddDays(-9), "alice", "Mention the invoice run", b => b["description"] =
            "Invoices premiums and sends them by email and post. The invoice run starts every night at 05:00.");
        await Edit("billing", now.AddDays(-2), "bob", "Credit notes are issued by Billing too", b => b["description"] =
            "Invoices premiums, issues credit notes and sends them by email and post. The invoice run starts every night at 05:00.");

        // The customer portal is shared with claire (guest): she sees it, and its flows, in full.
        await Edit("portal", now.AddDays(-12), "alice", "Shared with Claire for the portal redesign audit", b =>
            b["access"] = new JsonObject { ["teamIds"] = new JsonArray(), ["usernames"] = new JsonArray("claire") });

        // Proposals from david (Team CRM) on records owned by Team Finance.
        var david = new Access(new Viewer(users["david"].Id, "david", Role.Member), new HashSet<string> { "team-crm" }, await RecordFacts.Load(db));
        async Task<Proposal> Propose(string title, string? comment, DateTimeOffset at, params ProposalChangeInput[] changes)
        {
            var p = new Proposal
            {
                Title = title, Comment = comment, AuthorId = users["david"].Id, AuthorName = "david", AuthorKind = AuthorKind.User,
                Status = ProposalStatus.Submitted, CreatedAt = at, UpdatedAt = at, SubmittedAt = at, Version = 1,
            };
            p.Changes = await Proposals.BuildChanges(db, p.Id, [.. changes], david);
            db.Proposals.Add(p);
            await db.SaveChangesAsync();
            return p;
        }
        var acct = await db.Applications.AsNoTracking().FirstAsync(a => a.Id == "acct");
        var renamed = Records.Application.Snapshot(acct);
        renamed["name"] = "General ledger";
        var refused = await Propose("Rename Accounting to General ledger", "\"Accounting\" is ambiguous for the CRM team: it reads like a department.",
            now.AddDays(-6), new ProposalChangeInput(RecordKind.Application, "acct", ChangeOperation.Update, acct.Version, renamed));
        refused.Status = ProposalStatus.Refused;
        refused.RefusalReason = "Accounting stays: it is the name of the SAP module that auditors and the finance team use every day.";
        (refused.DecidedById, refused.DecidedByName, refused.DecidedAt, refused.UpdatedAt) = (users["bob"].Id, "bob", now.AddDays(-5), now.AddDays(-5));
        refused.Version++;
        await db.SaveChangesAsync();

        var billing = await db.Applications.AsNoTracking().FirstAsync(a => a.Id == "billing");
        var described = Records.Application.Snapshot(billing);
        described["description"] = "Invoices premiums, issues credit notes and sends them by email and post. The invoice run starts every night "
            + "at 05:00, once the nightly customer import from the CRM is loaded: a late import delays every invoice of the day.";
        await Propose("Clarify Billing's description", "We get questions about late invoices: this explains that the invoice run waits for our nightly extract.",
            now.AddDays(-1), new ProposalChangeInput(RecordKind.Application, "billing", ChangeOperation.Update, billing.Version, described));

        await Propose("Document the invoice status events sent to the CRM",
            "Billing 4.3 publishes invoice status changes; the CRM will show them to advisors. Planned for the November release.",
            now.AddHours(-3), new ProposalChangeInput(RecordKind.Flow, null, ChangeOperation.Create, null, new JsonObject
            {
                ["id"] = "billing-to-crm-invoice-status",
                ["name"] = "Invoice status",
                ["fromId"] = "billing",
                ["toId"] = "crm",
                ["type"] = "event",
                ["schedule"] = "On every status change",
                ["dataObjectIds"] = new JsonArray("invoice"),
                ["status"] = "planned",
                ["description"] = "Issued, sent, paid or cancelled: Billing publishes each status change of an invoice, and the CRM shows it on the customer's timeline.",
            }));
    }

    /// <summary>Deletes the whole landscape, its pages, history and proposals (not the accounts).</summary>
    public static async Task ClearLandscape(NoosphereDbContext db)
    {
        await db.Proposals.ExecuteDeleteAsync();
        await db.Revisions.ExecuteDeleteAsync();
        await db.Pages.ExecuteDeleteAsync();
        await db.Flows.ExecuteDeleteAsync();
        await db.DataObjects.ExecuteDeleteAsync();
        await db.Applications.ExecuteDeleteAsync();
        await db.Teams.ExecuteDeleteAsync();
        await db.Domains.ExecuteDeleteAsync();
    }
}
