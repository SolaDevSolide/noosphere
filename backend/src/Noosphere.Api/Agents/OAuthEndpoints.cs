using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Endpoints;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Agents;

/// <summary>
/// Noosphere as the OAuth 2.1 authorization server of its own MCP endpoint: discovery (RFC 8414, RFC 9728), the
/// authorization code flow with PKCE S256 only, refresh token rotation, revocation (RFC 7009), Client ID Metadata
/// Documents and, when allowed, dynamic client registration (RFC 7591). Clients are public (no secret).
/// The consent page is the frontend route <c>/oauth/consent?request=…</c>; it reads and decides through
/// <c>/api/oauth/requests/{id}</c> with the session cookie.
/// </summary>
public static class OAuthEndpoints
{
    public const string CorsPolicy = "agents";
    public const string RegistrationRateLimit = "oauth-registration";
    public const string ConsentPath = "/oauth/consent";

    private static readonly string[] GrantTypes = ["authorization_code", "refresh_token"];
    private static readonly string[] ResponseTypes = ["code"];
    private static readonly string[] BearerMethods = ["header"];

    private static readonly JsonSerializerOptions Snake = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static void MapOAuth(this WebApplication app, Settings settings)
    {
        var meta = app.MapGroup("/.well-known").AllowAnonymous().RequireCors(CorsPolicy).ExcludeFromDescription();
        meta.MapGet("/oauth-protected-resource", ProtectedResource);
        meta.MapGet("/oauth-protected-resource/mcp", ProtectedResource);
        meta.MapGet("/oauth-authorization-server", AuthorizationServer);
        meta.MapGet("/oauth-authorization-server/mcp", AuthorizationServer);

        var oauth = app.MapGroup("/oauth").AllowAnonymous().ExcludeFromDescription();
        oauth.MapGet("/authorize", Authorize);
        oauth.MapPost("/token", Token).RequireCors(CorsPolicy);
        oauth.MapPost("/revoke", Revoke).RequireCors(CorsPolicy);
        oauth.MapPost("/register", Register).RequireCors(CorsPolicy).RequireRateLimiting(RegistrationRateLimit);
    }

    // ---------- Discovery ----------

    private static async Task<IResult> ProtectedResource(HttpRequest request, Settings settings, NoosphereDbContext db)
    {
        var baseUrl = PublicAddress.Of(request, settings);
        var instance = await InstanceSettingsStore.Get(db, settings);
        return Json(new Dictionary<string, object>
        {
            ["resource"] = AgentSettingsStore.McpUrl(baseUrl),
            ["authorization_servers"] = new[] { baseUrl },
            ["scopes_supported"] = Scopes.All,
            ["bearer_methods_supported"] = BearerMethods,
            ["resource_name"] = instance.InstanceName,
            ["resource_documentation"] = baseUrl + "/llms.txt",
        });
    }

    private static async Task<IResult> AuthorizationServer(HttpRequest request, Settings settings, NoosphereDbContext db)
    {
        var baseUrl = PublicAddress.Of(request, settings);
        var agents = await AgentSettingsStore.Get(db);
        var doc = new Dictionary<string, object>
        {
            ["issuer"] = baseUrl,
            ["authorization_endpoint"] = baseUrl + "/oauth/authorize",
            ["token_endpoint"] = baseUrl + "/oauth/token",
            ["revocation_endpoint"] = baseUrl + "/oauth/revoke",
            ["response_types_supported"] = new[] { "code" },
            ["response_modes_supported"] = new[] { "query" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "none" },
            ["revocation_endpoint_auth_methods_supported"] = new[] { "none" },
            ["scopes_supported"] = Scopes.All,
            ["client_id_metadata_document_supported"] = true,
            ["authorization_response_iss_parameter_supported"] = true,
        };
        if (agents.DynamicRegistration) doc["registration_endpoint"] = baseUrl + "/oauth/register";
        return Json(doc);
    }

    // ---------- Authorization ----------

    /// <summary>
    /// Checks the request, then sends the browser to the consent page. Errors about the client or its redirect URI are
    /// shown on the consent page (never sent to an unchecked URI); the others go back to the client.
    /// </summary>
    private static async Task<IResult> Authorize(HttpRequest request, NoosphereDbContext db, Settings settings, TimeProvider clock,
        IClientMetadataFetcher fetcher, ILoggerFactory logs)
    {
        var q = request.Query;
        string? Param(string name) => q[name].Count == 1 ? q[name][0] : null;
        IResult Local(string error, string description) =>
            Results.Redirect(QueryHelpers.AddQueryString(ConsentPath, new Dictionary<string, string?> { ["error"] = error, ["error_description"] = description }));

        var clientId = Param("client_id");
        if (string.IsNullOrEmpty(clientId)) return Local("invalid_request", "The client_id parameter is missing.");
        OAuthClientInfo client;
        try
        {
            client = await ResolveClient(db, fetcher, clientId, request.HttpContext.RequestAborted);
        }
        catch (ClientMetadataException e)
        {
            logs.CreateLogger("Noosphere.OAuth").LogInformation("Client {ClientId} refused: {Reason}", clientId, e.Message);
            return Local("invalid_client", e.Message);
        }
        var redirect = Param("redirect_uri");
        if (redirect is null)
        {
            if (client.RedirectUris.Count != 1) return Local("invalid_request", "The redirect_uri parameter is missing.");
            redirect = client.RedirectUris[0];
        }
        if (!ClientMetadata.RedirectMatches(redirect, client.RedirectUris))
            return Local("invalid_request", "The redirect_uri is not one of the client's registered redirect URIs.");

        var baseUrl = PublicAddress.Of(request, settings);
        var state = Param("state");
        IResult Back(string error, string description) => Results.Redirect(ErrorRedirect(redirect, error, description, state, baseUrl));

        if (Param("response_type") != "code") return Back("unsupported_response_type", "Only the authorization code flow (response_type=code) is supported.");
        var agents = await AgentSettingsStore.Get(db);
        if (!agents.Enabled) return Back("access_denied", "AI agents are turned off on this instance.");
        if (Param("code_challenge_method") != "S256") return Back("invalid_request", "PKCE is required, with code_challenge_method=S256.");
        var challenge = Param("code_challenge");
        if (!OpaqueTokens.IsChallenge(challenge)) return Back("invalid_request", "The code_challenge must be a base64url SHA-256 hash (43 characters).");
        if (Param("resource") is { } resource && resource.TrimEnd('/') != AgentSettingsStore.McpUrl(baseUrl))
            return Back("invalid_target", $"The only resource is {AgentSettingsStore.McpUrl(baseUrl)}.");
        var scopes = Scopes.Parse(Param("scope"));
        if (scopes is null) return Back("invalid_scope", $"Known scopes: {string.Join(' ', Scopes.All)}.");
        if (state is { Length: > ClientMetadata.MaxUriLength }) return Back("invalid_request", "The state is too long.");

        var now = clock.GetUtcNow();
        var pending = new OAuthRequest
        {
            ClientId = client.ClientId, ClientName = client.ClientName, ClientUri = client.ClientUri, RedirectUri = redirect, State = state,
            CodeChallenge = challenge!, Scopes = scopes, CreatedAt = now, ExpiresAt = now + OpaqueTokens.RequestLifetime,
        };
        db.OAuthRequests.Add(pending);
        await db.SaveChangesAsync();
        return Results.Redirect($"{ConsentPath}?request={pending.Id}");
    }

    public static async Task<OAuthClientInfo> ResolveClient(NoosphereDbContext db, IClientMetadataFetcher fetcher, string clientId, CancellationToken cancel)
    {
        if (ClientMetadata.IsMetadataUrl(clientId))
        {
            var url = ClientMetadata.CheckUrl(clientId);
            return ClientMetadata.Parse(clientId, await fetcher.Fetch(url, cancel));
        }
        var registered = await db.OAuthClients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == clientId, cancel)
            ?? throw new ClientMetadataException("Unknown client: register it first, or use a Client ID Metadata Document URL as client_id.");
        return new OAuthClientInfo(registered.ClientId, registered.ClientName, registered.ClientUri, registered.RedirectUris, MetadataDocument: false);
    }

    public static string ErrorRedirect(string redirect, string error, string description, string? state, string issuer) =>
        QueryHelpers.AddQueryString(redirect, new Dictionary<string, string?>
        {
            ["error"] = error, ["error_description"] = description, ["state"] = state, ["iss"] = issuer,
        }.Where(p => p.Value is not null));

    // ---------- Token ----------

    private static async Task<IResult> Token(HttpRequest request, NoosphereDbContext db, TimeProvider clock, ILoggerFactory logs)
    {
        if (!request.HasFormContentType) return Error("invalid_request", "Send the parameters as application/x-www-form-urlencoded.");
        var form = await request.ReadFormAsync();
        string? Param(string name) => form[name].Count == 1 ? form[name][0] : null;
        var now = clock.GetUtcNow();
        var clientId = Param("client_id");
        if (!(await AgentSettingsStore.Get(db)).Enabled) return Error("access_denied", "AI agents are turned off on this instance.");

        switch (Param("grant_type"))
        {
            case "authorization_code":
            {
                var code = Param("code");
                if (code is null || clientId is null) return Error("invalid_request", "code and client_id are required.");
                var hash = OpaqueTokens.Hash(code);
                var pending = await db.OAuthRequests.FirstOrDefaultAsync(r => r.CodeHash == hash);
                if (pending is null || pending.ClientId != clientId) return Error("invalid_grant", "The code is unknown.");
                if (pending.CodeUsedAt is not null)
                {
                    // A code used twice may have been stolen: the tokens it gave are revoked (OAuth 2.1).
                    var revoked = await db.AgentTokens.Where(t => t.RequestId == pending.Id && t.RevokedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
                    logs.CreateLogger("Noosphere.OAuth").LogWarning("Authorization code reused for client {ClientId}: {Count} token(s) revoked.", clientId, revoked);
                    return Error("invalid_grant", "The code was already used.");
                }
                if (pending.ExpiresAt <= now) return Error("invalid_grant", "The code expired.");
                if (Param("redirect_uri") is { } redirect && redirect != pending.RedirectUri) return Error("invalid_grant", "The redirect_uri differs from the authorization request.");
                if (!OpaqueTokens.VerifyPkce(Param("code_verifier"), pending.CodeChallenge)) return Error("invalid_grant", "The code_verifier does not match the code_challenge.");
                pending.CodeUsedAt = now;
                var connection = await db.AgentConnections.FirstOrDefaultAsync(c => c.Id == pending.ConnectionId);
                if (connection is null || connection.RevokedAt is not null)
                {
                    await db.SaveChangesAsync();
                    return Error("invalid_grant", "The connection was revoked.");
                }
                var issued = Issue(db, connection, connection.Scopes, pending.Id, now);
                await db.SaveChangesAsync();
                return issued;
            }
            case "refresh_token":
            {
                var token = Param("refresh_token");
                if (token is null || clientId is null) return Error("invalid_request", "refresh_token and client_id are required.");
                var hash = OpaqueTokens.Hash(token);
                var refresh = await db.AgentTokens.FirstOrDefaultAsync(t => t.Hash == hash && t.Kind == AgentTokenKind.Refresh);
                var connection = refresh is null ? null : await db.AgentConnections.FirstOrDefaultAsync(c => c.Id == refresh.ConnectionId);
                if (refresh is null || connection is null || connection.ClientId != clientId) return Error("invalid_grant", "The refresh token is unknown.");
                if (refresh.UsedAt is not null)
                {
                    // Rotation: a refresh token is used once. A second use means it leaked; every token of the connection goes.
                    var revoked = await RevokeTokens(db, connection.Id, now);
                    db.AgentActivities.Add(new AgentActivity
                    {
                        At = now, ConnectionId = connection.Id, Tool = "token", Kind = AgentActivityKind.Revoked,
                        Summary = $"A refresh token was used twice: {revoked} token(s) revoked. The agent must be authorised again.",
                    });
                    await db.SaveChangesAsync();
                    logs.CreateLogger("Noosphere.OAuth").LogWarning("Refresh token reused for connection {Connection}: tokens revoked.", connection.Id);
                    return Error("invalid_grant", "The refresh token was already used.");
                }
                if (refresh.RevokedAt is not null || connection.RevokedAt is not null) return Error("invalid_grant", "The refresh token was revoked.");
                if (refresh.ExpiresAt <= now) return Error("invalid_grant", "The refresh token expired.");
                refresh.UsedAt = now;
                var scopes = connection.Scopes.Where(refresh.Scopes.Contains).ToList();
                var issued = Issue(db, connection, scopes, refresh.RequestId, now);
                await db.SaveChangesAsync();
                return issued;
            }
            default:
                return Error("unsupported_grant_type", "Supported: authorization_code and refresh_token.");
        }
    }

    private static NoStoreResult Issue(NoosphereDbContext db, AgentConnection connection, List<string> scopes, Guid? requestId, DateTimeOffset now)
    {
        var access = OpaqueTokens.New(OpaqueTokens.AccessPrefix);
        var refresh = OpaqueTokens.New(OpaqueTokens.RefreshPrefix);
        db.AgentTokens.Add(new AgentToken
        {
            ConnectionId = connection.Id, Hash = OpaqueTokens.Hash(access), Kind = AgentTokenKind.Access, Scopes = scopes,
            CreatedAt = now, ExpiresAt = now + OpaqueTokens.AccessLifetime, RequestId = requestId,
        });
        db.AgentTokens.Add(new AgentToken
        {
            ConnectionId = connection.Id, Hash = OpaqueTokens.Hash(refresh), Kind = AgentTokenKind.Refresh, Scopes = scopes,
            CreatedAt = now, ExpiresAt = now + OpaqueTokens.RefreshLifetime, RequestId = requestId,
        });
        return NoStore(Results.Json(new Dictionary<string, object>
        {
            ["access_token"] = access,
            ["token_type"] = "Bearer",
            ["expires_in"] = (int)OpaqueTokens.AccessLifetime.TotalSeconds,
            ["refresh_token"] = refresh,
            ["scope"] = string.Join(' ', scopes),
        }));
    }

    /// <summary>Revokes every live token of a connection; returns how many.</summary>
    public static Task<int> RevokeTokens(NoosphereDbContext db, Guid connectionId, DateTimeOffset now) =>
        db.AgentTokens.Where(t => t.ConnectionId == connectionId && t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));

    // ---------- Revocation ----------

    /// <summary>RFC 7009: always 200. Revoking a refresh token revokes every token of the connection.</summary>
    private static async Task<IResult> Revoke(HttpRequest request, NoosphereDbContext db, TimeProvider clock)
    {
        if (!request.HasFormContentType) return Error("invalid_request", "Send the parameters as application/x-www-form-urlencoded.");
        var form = await request.ReadFormAsync();
        var token = form["token"].Count == 1 ? form["token"][0] : null;
        if (string.IsNullOrEmpty(token)) return Error("invalid_request", "The token parameter is missing.");
        var hash = OpaqueTokens.Hash(token);
        var found = await db.AgentTokens.FirstOrDefaultAsync(t => t.Hash == hash);
        if (found is not null && found.RevokedAt is null)
        {
            var clientId = form["client_id"].Count == 1 ? form["client_id"][0] : null;
            var connection = await db.AgentConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == found.ConnectionId);
            if (clientId is null || connection?.ClientId == clientId)
            {
                var now = clock.GetUtcNow();
                if (found.Kind == AgentTokenKind.Refresh) await RevokeTokens(db, found.ConnectionId, now);
                else
                {
                    found.RevokedAt = now;
                    await db.SaveChangesAsync();
                }
            }
        }
        return NoStore(Results.Ok());
    }

    // ---------- Dynamic client registration ----------

    private sealed record RegistrationInput(string? ClientName, string? ClientUri, JsonElement RedirectUris, List<string>? GrantTypes,
        List<string>? ResponseTypes, string? TokenEndpointAuthMethod, string? Scope);

    private static async Task<IResult> Register(HttpRequest request, NoosphereDbContext db, TimeProvider clock)
    {
        var agents = await AgentSettingsStore.Get(db);
        if (!agents.Enabled || !agents.DynamicRegistration)
            return Results.Json(new { error = "access_denied", error_description = "Dynamic client registration is turned off: use a Client ID Metadata Document URL as client_id." }, statusCode: 403);
        RegistrationInput? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<RegistrationInput>(request.Body, Snake, request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            input = null;
        }
        if (input is null) return Results.Json(new { error = "invalid_client_metadata", error_description = "Expected a JSON object." }, statusCode: 400);
        List<string> redirects;
        try
        {
            redirects = ClientMetadata.RedirectUris(input.RedirectUris);
        }
        catch (ClientMetadataException e)
        {
            return Results.Json(new { error = "invalid_redirect_uri", error_description = e.Message }, statusCode: 400);
        }
        if (input.GrantTypes is { } grants && grants.Any(g => g is not ("authorization_code" or "refresh_token")))
            return Results.Json(new { error = "invalid_client_metadata", error_description = "Supported grant types: authorization_code and refresh_token." }, statusCode: 400);
        if (input.ResponseTypes is { } responses && responses.Any(r => r != "code"))
            return Results.Json(new { error = "invalid_client_metadata", error_description = "Supported response type: code." }, statusCode: 400);
        var name = input.ClientName?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, Rules.MaxNameLength)] : "Unnamed agent";
        var now = clock.GetUtcNow();
        var client = new OAuthClient
        {
            ClientId = "nsclient_" + OpaqueTokens.Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
            ClientName = name, ClientUri = ClientMetadata.ClientUri(input.ClientUri), RedirectUris = redirects, CreatedAt = now,
        };
        db.OAuthClients.Add(client);
        await db.SaveChangesAsync();
        // Public clients only: whatever was asked, no secret is issued.
        return NoStore(Results.Json(new Dictionary<string, object?>
        {
            ["client_id"] = client.ClientId,
            ["client_id_issued_at"] = now.ToUnixTimeSeconds(),
            ["client_name"] = client.ClientName,
            ["client_uri"] = client.ClientUri,
            ["redirect_uris"] = client.RedirectUris,
            ["grant_types"] = GrantTypes,
            ["response_types"] = ResponseTypes,
            ["token_endpoint_auth_method"] = "none",
            ["scope"] = string.Join(' ', Scopes.All),
        }, statusCode: 201));
    }

    // ---------- Consent page API (session cookie + X-Requested-With) ----------

    public static void MapConsent(this RouteGroupBuilder api)
    {
        var requests = api.MapGroup("/oauth/requests").WithTags("AI agents");

        requests.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var (pending, person, agents) = await Open(db, id, user, clock.GetUtcNow());
            var existing = await db.AgentConnections.AsNoTracking()
                .AnyAsync(c => c.UserId == person.Id && c.ClientId == pending.ClientId && c.RevokedAt == null);
            var access = await AgentAccess.For(db, person, pending.Scopes.Where(s => Scopes.Grantable(s, person.Role, agents)).ToList(), agents);
            var redirectHost = Uri.TryCreate(pending.RedirectUri, UriKind.Absolute, out var u) ? (u.IsDefaultPort ? u.Host : $"{u.Host}:{u.Port}") : pending.RedirectUri;
            return new ConsentRequestDto(pending.Id, pending.ClientId,
                ClientMetadata.IsMetadataUrl(pending.ClientId) ? OAuthClientKind.MetadataDocument : OAuthClientKind.Registered,
                pending.ClientName, pending.ClientUri, redirectHost, new UserRefDto(person.Username, person.DisplayName),
                [.. pending.Scopes.Select(s => new ConsentScopeDto(s, Scopes.Grantable(s, person.Role, agents), Scopes.Describe(s)))],
                Capabilities.Of(access, agents, [.. pending.Scopes.Where(s => Scopes.Grantable(s, person.Role, agents))], person.DisplayName),
                agents.Ceiling, agents.WhoConnects == AgentWhoConnects.ApprovalRequired && person.Role != Role.Admin, existing, pending.ExpiresAt);
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound).Produces<ProblemDto>(StatusCodes.Status409Conflict);

        requests.MapPost("/{id:guid}/approve", async (Guid id, ConsentApproveInput? input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock, HttpRequest request, Settings settings) =>
        {
            var now = clock.GetUtcNow();
            var (pending, person, agents) = await Open(db, id, user, now, tracked: true);
            var scopes = pending.Scopes.Where(s => Scopes.Grantable(s, person.Role, agents)).ToList();
            if (input?.Scopes is { } chosen)
            {
                if (chosen.Any(s => !pending.Scopes.Contains(s)))
                    throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["scopes"] = ["Choose among the scopes the agent asked for."] });
                scopes = [.. scopes.Where(chosen.Contains)];
            }
            if (scopes.Count == 0)
                throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["scopes"] = ["Grant at least one scope."] });
            var connection = await db.AgentConnections.FirstOrDefaultAsync(c => c.UserId == person.Id && c.ClientId == pending.ClientId && c.RevokedAt == null);
            if (connection is null)
            {
                var approval = agents.WhoConnects == AgentWhoConnects.ApprovalRequired && person.Role != Role.Admin;
                connection = new AgentConnection
                {
                    ClientId = pending.ClientId, UserId = person.Id, CreatedAt = now, PendingApproval = approval,
                    ApprovedAt = approval ? null : now, ApprovedBy = approval ? null : person.Username,
                };
                db.AgentConnections.Add(connection);
            }
            (connection.ClientName, connection.ClientUri, connection.Scopes) = (pending.ClientName, pending.ClientUri, scopes);
            var code = OpaqueTokens.New(OpaqueTokens.CodePrefix);
            (pending.UserId, pending.ConnectionId, pending.CodeHash) = (person.Id, connection.Id, OpaqueTokens.Hash(code));
            pending.ExpiresAt = now + OpaqueTokens.RequestLifetime;
            await db.SaveChangesAsync();
            var redirect = QueryHelpers.AddQueryString(pending.RedirectUri, new Dictionary<string, string?>
            {
                ["code"] = code, ["state"] = pending.State, ["iss"] = PublicAddress.Of(request, settings),
            }.Where(p => p.Value is not null));
            return new ConsentResultDto(redirect, connection.PendingApproval);
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound).Produces<ProblemDto>(StatusCodes.Status409Conflict);

        requests.MapPost("/{id:guid}/deny", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock, HttpRequest request, Settings settings) =>
        {
            var now = clock.GetUtcNow();
            var (pending, _, _) = await Open(db, id, user, now, tracked: true);
            pending.DeniedAt = now;
            await db.SaveChangesAsync();
            return new ConsentResultDto(ErrorRedirect(pending.RedirectUri, "access_denied", "The person refused to connect this agent.", pending.State,
                PublicAddress.Of(request, settings)), false);
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound).Produces<ProblemDto>(StatusCodes.Status409Conflict);
    }

    /// <summary>An authorization request still waiting for a decision: 404 when unknown or expired, 409 when already decided.</summary>
    private static async Task<(OAuthRequest Request, User Person, AgentSettings Agents)> Open(NoosphereDbContext db, Guid id, ClaimsPrincipal user, DateTimeOffset now, bool tracked = false)
    {
        var pending = await (tracked ? db.OAuthRequests : db.OAuthRequests.AsNoTracking()).FirstOrDefaultAsync(r => r.Id == id);
        if (pending is null || pending.ExpiresAt <= now) throw ApiProblem.NotFound("This authorization request is unknown or expired. Start the connection again from the agent.");
        if (pending.CodeHash is not null || pending.DeniedAt is not null) throw ApiProblem.Conflict("This authorization request was already decided.");
        var agents = await AgentSettingsStore.Get(db);
        if (!agents.Enabled) throw ApiProblem.Forbidden("AI agents are turned off on this instance.");
        var viewer = Viewer.From(user);
        var person = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == viewer.Id) ?? throw new ApiProblem(401, "Sign in first.");
        return (pending, person, agents);
    }

    // ---------- Helpers ----------

    private static IResult Json(object value) => Results.Json(value, Snake);

    private static NoStoreResult Error(string error, string description, int status = 400) =>
        NoStore(Results.Json(new Dictionary<string, string> { ["error"] = error, ["error_description"] = description }, statusCode: status));

    private static NoStoreResult NoStore(IResult result) => new(result);

    private sealed class NoStoreResult(IResult inner) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            return inner.ExecuteAsync(httpContext);
        }
    }
}
