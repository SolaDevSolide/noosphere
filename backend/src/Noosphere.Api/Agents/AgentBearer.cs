using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Noosphere.Api.Auth;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Agents;

/// <summary>
/// Bearer tokens of AI agents (MCP server and Markdown views). The token is opaque: its hash is looked up, with its
/// connection and person. The principal holds the person's claims, the connection id and the granted scopes.
/// </summary>
public sealed class AgentBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    NoosphereDbContext db, TimeProvider clock, Settings settings) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AgentBearer";
    public const string ConnectionClaim = "noosphere:agent";
    public const string ScopeClaim = "scope";
    public const string Policy = "agent";

    private const string ErrorItem = "noosphere:bearer-error";

    /// <summary>The token of the request, or null.</summary>
    public static string? TokenOf(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && header.Length > 7 ? header[7..].Trim() : null;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = TokenOf(Request);
        if (token is null) return AuthenticateResult.NoResult();
        var hash = OpaqueTokens.Hash(token);
        var now = clock.GetUtcNow();
        var found = await (from t in db.AgentTokens.AsNoTracking()
                           join c in db.AgentConnections.AsNoTracking() on t.ConnectionId equals c.Id
                           join u in db.Users.AsNoTracking() on c.UserId equals u.Id
                           where t.Hash == hash && t.Kind == AgentTokenKind.Access
                           select new { Token = t, Connection = c, User = u }).FirstOrDefaultAsync(Context.RequestAborted);
        string? problem = found switch
        {
            null => "The access token is unknown.",
            { Token.RevokedAt: not null } or { Connection.RevokedAt: not null } => "The access token was revoked.",
            _ when found.Token.ExpiresAt <= now => "The access token expired.",
            _ => null,
        };
        if (problem is null && !(await AgentSettingsStore.Get(db, Context.RequestAborted)).Enabled) problem = "AI agents are turned off on this instance.";
        if (problem is not null)
        {
            Context.Items[ErrorItem] = problem;
            return AuthenticateResult.Fail(problem);
        }
        var person = Accounts.Principal(found!.User);
        var identity = new ClaimsIdentity(person.Claims, SchemeName);
        identity.AddClaim(new Claim(ConnectionClaim, found.Connection.Id.ToString()));
        foreach (var scope in found.Token.Scopes) identity.AddClaim(new Claim(ScopeClaim, scope));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>401 with the address of the protected resource metadata (RFC 9728), so that MCP clients start the OAuth flow.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Challenge(Context, settings, Context.Items[ErrorItem] as string);

    public static Task Challenge(HttpContext ctx, Settings settings, string? error)
    {
        var metadata = ResourceMetadataUrl(PublicAddress.Of(ctx.Request, settings));
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.Headers.WWWAuthenticate = error is null
            ? $"Bearer resource_metadata=\"{metadata}\""
            : $"Bearer resource_metadata=\"{metadata}\", error=\"invalid_token\", error_description=\"{error.Replace("\"", "'", StringComparison.Ordinal)}\"";
        return Problems.Write(ctx, 401, error ?? "An access token is required: connect this agent through OAuth (see the resource metadata).");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", scope=\"{Scopes.Read}\"";
        return Problems.Write(Context, 403, "The access token does not allow this.");
    }

    /// <summary>Where an MCP client finds the protected resource metadata of <c>/mcp</c>.</summary>
    public static string ResourceMetadataUrl(string baseUrl) => baseUrl + "/.well-known/oauth-protected-resource/mcp";

    public static Guid? ConnectionOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(ConnectionClaim), out var id) ? id : null;

    public static IReadOnlyList<string> ScopesOf(ClaimsPrincipal? user) =>
        [.. user?.FindAll(ScopeClaim).Select(c => c.Value) ?? []];

    public static void AddAgentAuthentication(IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AgentBearerHandler>(SchemeName, null);
        services.AddAuthorizationBuilder().AddPolicy(Policy, p => p.AddAuthenticationSchemes(SchemeName).RequireAuthenticatedUser().RequireClaim(ConnectionClaim));
    }
}

/// <summary>Builds the person's rights for an agent call.</summary>
public static class AgentAccess
{
    /// <summary>min(person, ceiling, scopes): the person's rights under the ceiling of the settings and the scopes of the token.</summary>
    public static async Task<Access> For(NoosphereDbContext db, User person, IReadOnlyCollection<string> scopes, AgentSettings settings, CancellationToken cancel = default)
    {
        var viewer = new Viewer(person.Id, person.Username, person.Role);
        var access = new Access(viewer, await Memberships.TeamIds(db, person.Id, cancel), await RecordFacts.Load(db, cancel));
        return access.WithLimits(new AgentLimits(settings.Ceiling, scopes.Contains(Scopes.ReadRestricted)));
    }

    /// <summary>An anonymous reader of the Markdown views: records visible to everyone, without protected notes.</summary>
    public static async Task<Access> Anonymous(NoosphereDbContext db, CancellationToken cancel = default) =>
        new Access(new Viewer(Guid.Empty, "anonymous", Role.Member), new HashSet<string>(), await RecordFacts.Load(db, cancel)).WithLimits(AgentLimits.Anonymous);
}
