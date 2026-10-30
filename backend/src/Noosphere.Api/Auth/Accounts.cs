using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Auth;

/// <summary>Who is calling: read from the session cookie, refreshed from the database on every request.</summary>
public sealed record Viewer(Guid Id, string Username, Role Role)
{
    public bool IsAdmin => Role == Role.Admin;
    public bool CanEdit => Role is Role.Admin or Role.Member;

    /// <summary>Used by the command line, which works on the database directly.</summary>
    public static readonly Viewer System = new(Guid.Empty, "system", Role.Admin);

    public static Viewer From(ClaimsPrincipal principal) => new(
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
        principal.Identity?.Name ?? "",
        Enum.TryParse<Role>(principal.FindFirstValue(ClaimTypes.Role), ignoreCase: true, out var role) ? role : Role.Guest);
}

/// <summary>Local accounts, cookie sessions and roles.</summary>
public static class Accounts
{
    public const string CookieName = "noosphere.auth";
    public const string EditorPolicy = "editor";
    public const string AdminPolicy = "admin";
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 1024;
    public const int MaxUsernameLength = 100;
    private const string StampClaim = "noosphere:stamp";

    public static IServiceCollection AddAccounts(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                // Secure whenever the request is HTTPS (directly, or through a trusted reverse proxy: TRUSTED_PROXIES).
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Cookie.IsEssential = true;
                o.ExpireTimeSpan = TimeSpan.FromHours(12);
                o.SlidingExpiration = true;
                // An API never redirects to a login page.
                o.Events.OnRedirectToLogin = ctx => Problems.Write(ctx.HttpContext, 401, "Sign in first.");
                o.Events.OnRedirectToAccessDenied = ctx => Problems.Write(ctx.HttpContext, 403, "Your role does not allow this action.");
                o.Events.OnValidatePrincipal = ValidateSession;
            });
        services.AddAuthorizationBuilder()
            .AddPolicy(EditorPolicy, p => p.RequireRole(nameof(Role.Admin), nameof(Role.Member)))
            .AddPolicy(AdminPolicy, p => p.RequireRole(nameof(Role.Admin)));
        return services;
    }

    public static TBuilder RequireEditor<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(EditorPolicy);

    public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(AdminPolicy);

    /// <summary>
    /// On every request, the account must still exist and carry the same security stamp as the cookie; its role is read
    /// again, so that a role change applies at once.
    /// </summary>
    private static async Task ValidateSession(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal;
        var stamp = principal?.FindFirstValue(StampClaim);
        var db = ctx.HttpContext.RequestServices.GetRequiredService<NoosphereDbContext>();
        var user = Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id)
            : null;
        if (user is null || stamp != user.SecurityStamp.ToString())
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        ctx.ReplacePrincipal(Principal(user));
    }

    public static ClaimsPrincipal Principal(User user) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role.ToString()),
        new Claim(StampClaim, user.SecurityStamp.ToString()),
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    public static Task SignIn(HttpContext ctx, User user) =>
        ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Principal(user));

    public static Task SignOut(HttpContext ctx) => ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    public static string NormalizeUsername(string? username) => (username ?? "").Trim().ToLowerInvariant();

    /// <summary>Checks a username and password; returns the account or null. Unknown usernames cost as much as wrong passwords.</summary>
    public static async Task<User?> Verify(NoosphereDbContext db, IPasswordHasher<User> hasher, string? username, string? password)
    {
        var name = NormalizeUsername(username);
        password ??= "";
        if (name.Length is 0 or > MaxUsernameLength || password.Length > MaxPasswordLength) return null;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == name);
        if (user is null)
        {
            hasher.HashPassword(new User(), password);
            return null;
        }
        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await db.SaveChangesAsync();
        }
        return user;
    }

    /// <summary>Sets a new password and closes the account's open sessions.</summary>
    public static void SetPassword(User user, string password, IPasswordHasher<User> hasher)
    {
        user.PasswordHash = hasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid();
    }

    /// <summary>Reason why a password is refused, or null.</summary>
    public static string? PasswordProblem(string? password) =>
        (password ?? "").Length < MinPasswordLength ? $"At least {MinPasswordLength} characters."
        : password!.Length > MaxPasswordLength ? $"At most {MaxPasswordLength} characters."
        : null;

    /// <summary>Reason why a username is refused, or null.</summary>
    public static string? UsernameProblem(string username) =>
        username.Length == 0 ? "This field is required."
        : username.Length > MaxUsernameLength ? $"At most {MaxUsernameLength} characters."
        : username.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ? "No spaces." : null;

    /// <summary>Random readable password (no 0/O, 1/l/I): four groups of four characters.</summary>
    public static string RandomPassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var raw = RandomNumberGenerator.GetString(alphabet, 16);
        return string.Join('-', Enumerable.Range(0, 4).Select(i => raw.Substring(i * 4, 4)));
    }

    public static async Task<MeDto> Me(NoosphereDbContext db, User u) => new(u.Username, u.DisplayName, u.Role, u.Language,
        await db.TeamMembers.Where(m => m.UserId == u.Id).Join(db.Teams, m => m.TeamId, t => t.Id, (_, t) => t)
            .OrderBy(t => t.Name).Select(t => new TeamRefDto(t.Id, t.Name)).ToListAsync());

    public static UserDto Dto(User u, IReadOnlyList<string> teamIds) => new(u.Username, u.DisplayName, u.Role, u.Language, teamIds, u.Version, u.CreatedAt, u.UpdatedAt);

    /// <summary>Accounts with their teams.</summary>
    public static async Task<List<UserDto>> Dtos(NoosphereDbContext db, IEnumerable<User> users)
    {
        var teams = (await db.TeamMembers.AsNoTracking().ToListAsync()).ToLookup(m => m.UserId, m => m.TeamId);
        return [.. users.Select(u => Dto(u, [.. teams[u.Id].Order(StringComparer.Ordinal)]))];
    }

    public static async Task<UserDto> Dto(NoosphereDbContext db, User u) => (await Dtos(db, [u]))[0];
}
