using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Security;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccounts(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth");
        auth.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(HttpProtection.LoginPolicy)
            .Produces<ProblemDto>(StatusCodes.Status401Unauthorized).Produces<ProblemDto>(StatusCodes.Status429TooManyRequests);
        auth.MapPost("/logout", async (HttpContext ctx) =>
        {
            await Accounts.SignOut(ctx);
            return Results.NoContent();
        }).AllowAnonymous();
        auth.MapGet("/me", async (ClaimsPrincipal user, NoosphereDbContext db) => Accounts.Me(await Current(db, user)))
            .Produces<ProblemDto>(StatusCodes.Status401Unauthorized);
        auth.MapPut("/me/language", async (LanguageInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var language = CheckLanguage(input.Language, required: true)!;
            var me = await Current(db, user, tracked: true);
            me.Language = language;
            me.UpdatedAt = clock.GetUtcNow();
            me.Version++;
            await db.SaveChangesAsync();
            return Accounts.Me(me);
        });
        auth.MapPut("/me/password", async (PasswordChangeInput input, HttpContext ctx, NoosphereDbContext db, IPasswordHasher<User> hasher, TimeProvider clock) =>
        {
            var me = await Current(db, ctx.User, tracked: true);
            var errors = new FieldErrors();
            if (hasher.VerifyHashedPassword(me, me.PasswordHash, input.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
                errors.Add("currentPassword", "Wrong password.");
            if (Accounts.PasswordProblem(input.NewPassword) is { } problem) errors.Add("newPassword", problem);
            errors.ThrowIfAny();
            Accounts.SetPassword(me, input.NewPassword!, hasher);
            me.UpdatedAt = clock.GetUtcNow();
            me.Version++;
            await db.SaveChangesAsync();
            // The security stamp changed: sign in again so that this session stays open (the others are closed).
            await Accounts.SignIn(ctx, me);
            return Results.NoContent();
        });

        var users = api.MapGroup("/users").WithTags("Users").RequireAdmin();
        users.MapGet("", async (NoosphereDbContext db) =>
            (await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync()).Select(Accounts.Dto).ToList());
        users.MapGet("/{username}", async (string username, NoosphereDbContext db) => Accounts.Dto(await Find(db, username)));
        users.MapPost("", CreateUser).Produces<UserDto>(StatusCodes.Status201Created);
        users.MapPut("/{username}", UpdateUser);
        users.MapDelete("/{username}", DeleteUser);
    }

    private static async Task<IResult> Login(LoginInput input, HttpContext ctx, NoosphereDbContext db, IPasswordHasher<User> hasher)
    {
        var user = await Accounts.Verify(db, hasher, input.Username, input.Password);
        if (user is null) return Problems.Result(401, "Wrong username or password.");
        await Accounts.SignIn(ctx, user);
        return Results.Ok(Accounts.Me(user));
    }

    private static async Task<IResult> CreateUser(UserCreateInput input, NoosphereDbContext db, IPasswordHasher<User> hasher, TimeProvider clock)
    {
        var errors = new FieldErrors();
        var username = Accounts.NormalizeUsername(input.Username);
        if (Accounts.UsernameProblem(username) is { } p) errors.Add("username", p);
        var user = new User
        {
            Username = username,
            DisplayName = errors.Required("displayName", string.IsNullOrWhiteSpace(input.DisplayName) ? input.Username : input.DisplayName),
            Role = input.Role ?? Role.Member,
            Language = CheckLanguage(input.Language, errors) ?? "en",
        };
        if (Accounts.PasswordProblem(input.Password) is { } pw) errors.Add("password", pw);
        errors.ThrowIfAny();
        if (await db.Users.AnyAsync(u => u.Username == username)) throw ApiProblem.Conflict($"The username '{username}' is already used.");
        Accounts.SetPassword(user, input.Password!, hasher);
        user.CreatedAt = user.UpdatedAt = clock.GetUtcNow();
        user.Version = 1;
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Created($"/api/users/{user.Username}", Accounts.Dto(user));
    }

    private static async Task<UserDto> UpdateUser(string username, UserUpdateInput input, NoosphereDbContext db, IPasswordHasher<User> hasher, TimeProvider clock)
    {
        var user = await Find(db, username, tracked: true);
        var errors = new FieldErrors();
        if (input.Version is null) errors.Add("version", "This field is required.");
        var displayName = errors.Required("displayName", input.DisplayName);
        var language = CheckLanguage(input.Language, errors) ?? user.Language;
        if (!string.IsNullOrEmpty(input.Password) && Accounts.PasswordProblem(input.Password) is { } pw) errors.Add("password", pw);
        errors.ThrowIfAny();
        if (input.Version != user.Version) throw ApiProblem.Stale();
        var role = input.Role ?? user.Role;
        if (user.Role == Role.Admin && role != Role.Admin) await KeepOneAdmin(db, user);
        if (role != user.Role) user.SecurityStamp = Guid.NewGuid();
        user.DisplayName = displayName;
        user.Role = role;
        user.Language = language;
        if (!string.IsNullOrEmpty(input.Password)) Accounts.SetPassword(user, input.Password, hasher);
        user.UpdatedAt = clock.GetUtcNow();
        user.Version++;
        await db.SaveChangesAsync();
        return Accounts.Dto(user);
    }

    private static async Task<IResult> DeleteUser(string username, ClaimsPrincipal caller, NoosphereDbContext db)
    {
        var user = await Find(db, username, tracked: true);
        if (user.Id == Viewer.From(caller).Id) throw ApiProblem.Conflict("You cannot delete your own account.");
        if (user.Role == Role.Admin) await KeepOneAdmin(db, user);
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task KeepOneAdmin(NoosphereDbContext db, User user)
    {
        if (!await db.Users.AnyAsync(u => u.Role == Role.Admin && u.Id != user.Id))
            throw ApiProblem.Conflict("This is the last administrator: give the admin role to someone else first.");
    }

    private static async Task<User> Find(NoosphereDbContext db, string username, bool tracked = false)
    {
        var name = Accounts.NormalizeUsername(username);
        return await (tracked ? db.Users : db.Users.AsNoTracking()).FirstOrDefaultAsync(u => u.Username == name)
            ?? throw ApiProblem.NotFound($"No user named '{name}'.");
    }

    private static async Task<User> Current(NoosphereDbContext db, ClaimsPrincipal user, bool tracked = false)
    {
        var id = Viewer.From(user).Id;
        return await (tracked ? db.Users : db.Users.AsNoTracking()).FirstOrDefaultAsync(u => u.Id == id)
            ?? throw new ApiProblem(401, "Sign in first.");
    }

    private static string? CheckLanguage(string? language, bool required)
    {
        var errors = new FieldErrors();
        var value = CheckLanguage(language, errors);
        if (required && value is null && !errors.Any) errors.Add("language", "This field is required.");
        errors.ThrowIfAny();
        return value;
    }

    private static string? CheckLanguage(string? language, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var value = language.Trim().ToLowerInvariant();
        if (!Rules.Languages.Contains(value)) errors.Add("language", $"Expected one of: {string.Join(", ", Rules.Languages)}.");
        return value;
    }
}
