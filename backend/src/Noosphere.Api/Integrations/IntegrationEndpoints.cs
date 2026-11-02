using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Agents;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Endpoints;
using Noosphere.Api.Links;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Integrations;

/// <summary>
/// The simple connection for third-party tools. With an API key (<c>Authorization: Bearer</c>, no cookie, no CSRF
/// header): <c>GET /api/integrations/catalog</c> and <c>POST /api/integrations/deployments</c>. For administrators:
/// API keys, webhooks, their deliveries and the integration settings.
/// </summary>
public static class IntegrationEndpoints
{
    public const int DefaultCatalogPage = 100;
    public const int MaxCatalogPage = 500;
    public const int MaxIdempotencyKey = 200;
    public static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);
    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>Paths called with an API key: no session, so no CSRF header either.</summary>
    public static readonly string[] BearerPaths = ["/api/integrations/catalog", "/api/integrations/deployments"];

    public static IServiceCollection AddNoosphereIntegrations(this IServiceCollection services)
    {
        services.AddSingleton<IWebhookResolver, DnsWebhookResolver>();
        services.AddScoped<WebhookClient>();
        services.AddHttpClient(Webhooks.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(sp => WebhookAddresses.Handler(sp.GetRequiredService<IWebhookResolver>()));
        services.AddSingleton<WebhookSender>();
        services.AddHostedService(sp => sp.GetRequiredService<WebhookSender>());
        return services;
    }

    public static async Task<IntegrationSettings> Settings(NoosphereDbContext db, CancellationToken cancel = default) =>
        await db.IntegrationSettings.FirstOrDefaultAsync(cancel) ?? new IntegrationSettings();

    // ---------- Called with an API key ----------

    public static void MapIntegrations(this WebApplication app, Settings settings)
    {
        var keyed = app.MapGroup("/api/integrations").AllowAnonymous().RequireRateLimiting(ApiKeys.RateLimit).WithTags("Integrations");

        keyed.MapGet("/catalog", async (HttpContext ctx, [FromQuery] string? cursor, [FromQuery] int? limit, NoosphereDbContext db, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow();
            var (key, refusal) = await ApiKeys.Authenticate(ctx, db, ApiKeys.CatalogRead, "catalog", now);
            if (key is null) return refusal!;
            var size = limit ?? DefaultCatalogPage;
            if (size is < 1 or > MaxCatalogPage)
            {
                ApiKeys.Log(db, key, "catalog", LinkExchangeResult.Refused, 400, now, $"limit: between 1 and {MaxCatalogPage}.", LinkProtocol.Reasons.BadBody);
                await db.SaveChangesAsync();
                throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["limit"] = [$"Between 1 and {MaxCatalogPage}."] });
            }
            var catalog = await LinkCatalog.Build(db, (await InstanceSettingsStore.Get(db, settings)).InstanceName, PublicAddress.Of(ctx.Request, settings), now, ctx.RequestAborted);
            var page = catalog.Applications.Where(a => cursor is null || string.CompareOrdinal(a.Id, cursor) > 0).Take(size + 1).ToList();
            var apps = page.Take(size).ToList();
            var ids = apps.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            var result = new IntegrationCatalogPageDto(catalog.GeneratedAt, catalog.InstanceName, catalog.BaseUrl,
                [.. catalog.Domains.Select(d => new IntegrationDomainDto(d.Id, d.Name))],
                [.. catalog.Teams.Select(t => new IntegrationTeamDto(t.Id, t.Name, t.Contact))],
                [.. apps.Select(a => new IntegrationApplicationDto(a.Id, a.Name, a.Description, a.DomainId, a.TeamId, a.Kind, a.Lifecycle, a.Tech, a.Url))],
                [.. catalog.Flows.Where(f => ids.Contains(f.FromId)).Select(f => new IntegrationFlowDto(f.Id, f.Name, f.FromId, f.ToId, f.Type, f.Status, f.Schedule, f.Url))],
                page.Count > size ? apps[^1].Id : null);
            ApiKeys.Log(db, key, "catalog", LinkExchangeResult.Ok, 200, now, $"{key.Name}: {result.Applications.Count} application(s), {result.Flows.Count} flow(s)"
                + (result.NextCursor is null ? "." : ", more pages."));
            await db.SaveChangesAsync();
            return Results.Ok(result);
        })
            .WithName("IntegrationCatalog")
            .WithSummary("The shared catalogue, page by page (API key with catalog:read)")
            .WithDescription("Authorization: Bearer <API key>. Applications visible to everyone, by id, with the flows starting from them; domains and teams on every page.")
            .Produces<IntegrationCatalogPageDto>()
            .Produces<ProblemDto>(StatusCodes.Status400BadRequest).Produces<ProblemDto>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDto>(StatusCodes.Status403Forbidden).Produces<ProblemDto>(StatusCodes.Status429TooManyRequests);

        keyed.MapPost("/deployments", async (HttpContext ctx, NoosphereDbContext db, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow();
            var (key, refusal) = await ApiKeys.Authenticate(ctx, db, ApiKeys.DeploymentsWrite, "deployments", now);
            if (key is null) return refusal!;
            using var buffer = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(buffer, ctx.RequestAborted);
            var raw = buffer.ToArray();

            async Task<IResult> Refuse(int status, string reason, string detail, IReadOnlyDictionary<string, string[]>? errors = null)
            {
                ApiKeys.Log(db, key, "deployments", LinkExchangeResult.Refused, status, now, detail, reason);
                await db.SaveChangesAsync();
                return ApiKeys.Problem(status, reason, detail, errors);
            }

            var idempotency = ctx.Request.Headers[IdempotencyHeader].ToString().Trim();
            if (idempotency.Length > MaxIdempotencyKey)
                return await Refuse(400, "bad-idempotency-key", $"{IdempotencyHeader}: at most {MaxIdempotencyKey} characters.");
            JsonObject? body;
            try
            {
                body = raw.Length == 0 ? null : JsonNode.Parse(raw) as JsonObject;
            }
            catch (JsonException)
            {
                body = null;
            }
            if (body is null) return await Refuse(400, LinkProtocol.Reasons.BadBody, "A JSON object {events: [...]} is expected.");
            var errors = new FieldErrors();
            var events = Deployments.Parse(body, errors);
            if (errors.Any)
                return await Refuse(400, LinkProtocol.Reasons.BadBody, "Invalid events: " + string.Join("; ", errors.All.Take(5).Select(e => $"{e.Field}: {e.Message}")),
                    errors.All.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));

            var hash = Convert.ToHexStringLower(SHA256.HashData(raw));
            await using var transaction = await db.Database.BeginTransactionAsync(ctx.RequestAborted);
            IdempotentRequest? record = null;
            if (idempotency.Length > 0)
            {
                var known = await db.IdempotentRequests.FirstOrDefaultAsync(r => r.ApiKeyId == key.Id && r.Key == idempotency, ctx.RequestAborted);
                if (known is not null && known.CreatedAt > now - IdempotencyWindow && known.Status != 0)
                {
                    await transaction.RollbackAsync();
                    if (known.RequestHash != hash)
                        return await Refuse(422, "idempotency-key-reused", $"This {IdempotencyHeader} was used with another body in the last 24 hours: use a new key for a new request.");
                    ApiKeys.Log(db, key, "deployments", LinkExchangeResult.Ok, known.Status, now, $"{key.Name}: replayed the answer of {IdempotencyHeader} {idempotency}.");
                    await db.SaveChangesAsync();
                    ctx.Response.Headers["Idempotent-Replayed"] = "true";
                    return Results.Content(known.Response, "application/json", statusCode: known.Status);
                }
                if (known is not null) db.IdempotentRequests.Remove(known);
                record = new IdempotentRequest { ApiKeyId = key.Id, Key = idempotency, RequestHash = hash, CreatedAt = now, Response = "{}" };
                db.IdempotentRequests.Add(record);
                try
                {
                    await db.SaveChangesAsync(ctx.RequestAborted);
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync();
                    db.ChangeTracker.Clear();
                    key = await db.ApiKeys.FirstAsync(k => k.Id == key.Id);
                    return await Refuse(409, "idempotency-key-in-use", $"A request with this {IdempotencyHeader} is in progress: retry in a moment.");
                }
            }
            // A rotated key still in its grace period writes as the key that replaced it: the tool's deployments stay one list.
            var sender = key;
            for (var hops = 0; sender.ReplacedById is { } next && hops < 10; hops++)
                sender = await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Id == next, ctx.RequestAborted) ?? sender;
            var receipt = await Deployments.Upsert(db, DeploymentSender.Of(sender), events, now, ctx.RequestAborted);
            var answer = new DeploymentsReceiptDto(receipt.Accepted, receipt.IgnoredApplicationIds);
            if (record is not null)
                (record.Status, record.Response) = (200, JsonSerializer.Serialize(answer, JsonSerializerOptions.Web));
            ApiKeys.Log(db, key, "deployments", LinkExchangeResult.Ok, 200, now, $"{key.Name}: {receipt.Accepted} event(s) accepted"
                + (receipt.IgnoredApplicationIds.Count > 0 ? $", unknown applications ignored: {string.Join(", ", receipt.IgnoredApplicationIds)}." : "."));
            await db.SaveChangesAsync(ctx.RequestAborted);
            await transaction.CommitAsync(ctx.RequestAborted);
            return Results.Ok(answer);
        })
            .WithName("IntegrationDeployments")
            .WithSummary("Send deployment events (API key with deployments:write)")
            .WithDescription("Authorization: Bearer <API key>. The same events as the link protocol, upserted by deploymentId for this key. An optional Idempotency-Key header (at most 200 characters) replays the first answer for 24 hours.")
            .Accepts<DeploymentEventsInput>("application/json")
            .Produces<DeploymentsReceiptDto>()
            .Produces<ProblemDto>(StatusCodes.Status400BadRequest).Produces<ProblemDto>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDto>(StatusCodes.Status403Forbidden).Produces<ProblemDto>(StatusCodes.Status409Conflict)
            .Produces<ProblemDto>(StatusCodes.Status422UnprocessableEntity).Produces<ProblemDto>(StatusCodes.Status429TooManyRequests);
    }

    // ---------- Administrators ----------

    public static void MapIntegrationAdmin(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/integrations").WithTags("Integrations").RequireAdmin();

        admin.MapGet("/keys", async (NoosphereDbContext db, TimeProvider clock) =>
        {
            var now = clock.GetUtcNow();
            return (await db.ApiKeys.AsNoTracking().OrderBy(k => k.RevokedAt != null).ThenByDescending(k => k.CreatedAt).ToListAsync()).Select(k => ApiKeys.Dto(k, now)).ToList();
        });

        admin.MapPost("/keys", async (ApiKeyInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var errors = new FieldErrors();
            var name = errors.Required("name", input.Name);
            var scopes = ApiKeys.Scopes(input.Scopes, errors);
            if (input.ExpiresInDays is not { } days || !ApiKeys.Lifetimes.Contains(days))
                errors.Add("expiresInDays", $"Required: {string.Join(", ", ApiKeys.Lifetimes)} days.");
            errors.ThrowIfAny();
            var now = clock.GetUtcNow();
            var (key, value) = NewKey(name, scopes, now, input.ExpiresInDays!.Value, Viewer.From(user).Username);
            db.ApiKeys.Add(key);
            await db.SaveChangesAsync();
            return Results.Created($"/api/integrations/keys/{key.Id}", new ApiKeyCreatedDto(ApiKeys.Dto(key, now), value));
        }).Produces<ApiKeyCreatedDto>(StatusCodes.Status201Created);

        admin.MapPost("/keys/{id:guid}/rotate", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var old = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id) ?? throw ApiProblem.NotFound($"No API key with the id '{id}'.");
            var now = clock.GetUtcNow();
            if (ApiKeys.State(old, now) != ApiKeyState.Active) throw ApiProblem.Conflict("Only an active key can be rotated: create a new one instead.");
            if (old.ReplacedById is not null) throw ApiProblem.Conflict("This key was already rotated: rotate the new one.");
            // The new key lives as long as the old one was meant to (rounded to a lifetime offered), from now.
            var lifetime = old.ExpiresAt is { } until ? (until - old.CreatedAt).TotalDays : ApiKeys.Lifetimes[^1];
            var days = ApiKeys.Lifetimes.OrderBy(l => Math.Abs(l - lifetime)).First();
            var (key, value) = NewKey(old.Name, old.Scopes, now, days, Viewer.From(user).Username);
            db.ApiKeys.Add(key);
            old.ReplacedById = key.Id;
            var grace = now + ApiKeys.RotationGrace;
            if (old.ExpiresAt is null || old.ExpiresAt > grace) old.ExpiresAt = grace;
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.SaveChangesAsync();
            // The deployments the tool sent follow its new key.
            await db.Deployments.Where(d => d.ApiKeyId == old.Id).ExecuteUpdateAsync(u => u.SetProperty(d => d.ApiKeyId, key.Id));
            await transaction.CommitAsync();
            return Results.Created($"/api/integrations/keys/{key.Id}", new ApiKeyCreatedDto(ApiKeys.Dto(key, now), value));
        })
            .WithSummary("Rotate an API key: a new key, shown once; the old one keeps working for 24 hours")
            .Produces<ApiKeyCreatedDto>(StatusCodes.Status201Created).Produces<ProblemDto>(StatusCodes.Status404NotFound).Produces<ProblemDto>(StatusCodes.Status409Conflict);

        admin.MapPost("/keys/{id:guid}/revoke", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id) ?? throw ApiProblem.NotFound($"No API key with the id '{id}'.");
            var now = clock.GetUtcNow();
            if (key.RevokedAt is null) (key.RevokedAt, key.RevokedBy) = (now, Viewer.From(user).Username);
            await db.SaveChangesAsync();
            return ApiKeys.Dto(key, now);
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound);

        admin.MapGet("/keys/{id:guid}/exchanges", async (Guid id, [FromQuery] string? cursor, [FromQuery] int? limit, NoosphereDbContext db) =>
        {
            if (!await db.ApiKeys.AnyAsync(k => k.Id == id)) throw ApiProblem.NotFound($"No API key with the id '{id}'.");
            return await LinkAdminEndpoints.Exchanges(db.LinkExchanges.AsNoTracking().Where(x => x.ApiKeyId == id), cursor, limit);
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound);

        admin.MapGet("/exchanges", async ([FromQuery(Name = "apiKeyId")] Guid[]? keyIds, [FromQuery(Name = "webhookId")] Guid[]? hookIds, [FromQuery] string? cursor,
            [FromQuery] int? limit, NoosphereDbContext db) =>
        {
            var keys = keyIds ?? [];
            var hooks = hookIds ?? [];
            if (keys.Length + hooks.Length is 0 or > 20)
                throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["apiKeyId"] = ["Between 1 and 20 apiKeyId and webhookId values."] });
            return await LinkAdminEndpoints.Exchanges(db.LinkExchanges.AsNoTracking()
                .Where(x => (x.ApiKeyId != null && keys.Contains(x.ApiKeyId.Value)) || (x.WebhookId != null && hooks.Contains(x.WebhookId.Value))), cursor, limit);
        })
            .WithSummary("The exchange log of some API keys and webhooks (a third-party tool), newest first")
            .Produces<ProblemDto>(StatusCodes.Status400BadRequest);

        admin.MapGet("/webhooks", async (NoosphereDbContext db) =>
        {
            var hooks = await db.Webhooks.AsNoTracking().OrderBy(w => w.Name).ToListAsync();
            var pending = await Pending(db);
            return hooks.Select(w => Webhooks.Dto(w, pending.GetValueOrDefault(w.Id))).ToList();
        });

        admin.MapGet("/webhooks/{id:guid}", async (Guid id, NoosphereDbContext db) =>
            Webhooks.Dto(await FindHook(db, id), (await Pending(db)).GetValueOrDefault(id)))
            .Produces<ProblemDto>(StatusCodes.Status404NotFound);

        admin.MapPost("/webhooks", async (WebhookInput input, ClaimsPrincipal user, NoosphereDbContext db, IDataProtectionProvider protection, IWebhookResolver resolver, TimeProvider clock) =>
        {
            var (name, url, events) = await Validate(input, db, resolver);
            var secret = Webhooks.NewSecret();
            var hook = new Webhook
            {
                Name = name, Url = url, Events = events, Enabled = input.Enabled ?? true, ProtectedSecret = Webhooks.Protector(protection).Protect(secret),
                CreatedAt = clock.GetUtcNow(), CreatedBy = Viewer.From(user).Username, Version = 1,
            };
            db.Webhooks.Add(hook);
            await db.SaveChangesAsync();
            return Results.Created($"/api/integrations/webhooks/{hook.Id}", new WebhookCreatedDto(Webhooks.Dto(hook, 0), secret));
        }).Produces<WebhookCreatedDto>(StatusCodes.Status201Created);

        admin.MapPut("/webhooks/{id:guid}", async (Guid id, WebhookInput input, NoosphereDbContext db, IWebhookResolver resolver) =>
        {
            var hook = await FindHook(db, id);
            if (input.Version is { } version && version != hook.Version) throw ApiProblem.Stale();
            var (name, url, events) = await Validate(input, db, resolver);
            (hook.Name, hook.Url, hook.Events) = (name, url, events);
            if (input.Enabled is { } enabled) hook.Enabled = enabled;
            // Switched on again: a paused webhook resumes, with a fresh count of failures.
            if (input.Enabled == true) (hook.PausedAt, hook.FailingSince) = (null, null);
            hook.Version++;
            await db.SaveChangesAsync();
            return Webhooks.Dto(hook, (await Pending(db)).GetValueOrDefault(id));
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound).Produces<ProblemDto>(StatusCodes.Status409Conflict);

        admin.MapDelete("/webhooks/{id:guid}", async (Guid id, NoosphereDbContext db) =>
        {
            db.Webhooks.Remove(await FindHook(db, id));
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces<ProblemDto>(StatusCodes.Status404NotFound);

        admin.MapPost("/webhooks/{id:guid}/secret", async (Guid id, NoosphereDbContext db, IDataProtectionProvider protection) =>
        {
            var hook = await FindHook(db, id);
            var secret = Webhooks.NewSecret();
            hook.ProtectedSecret = Webhooks.Protector(protection).Protect(secret);
            hook.Version++;
            await db.SaveChangesAsync();
            return new WebhookCreatedDto(Webhooks.Dto(hook, (await Pending(db)).GetValueOrDefault(id)), secret);
        })
            .WithSummary("Regenerate a webhook's signing secret, shown once; the old one stops signing at once")
            .Produces<ProblemDto>(StatusCodes.Status404NotFound);

        admin.MapPost("/webhooks/{id:guid}/deliveries/{deliveryId:long}/retry", async (Guid id, long deliveryId, NoosphereDbContext db, WebhookClient client) =>
        {
            var hook = await FindHook(db, id);
            var delivery = await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId && d.WebhookId == id)
                ?? throw ApiProblem.NotFound($"No delivery {deliveryId} for this webhook.");
            if (delivery.State == WebhookDeliveryState.Delivered) throw ApiProblem.Conflict("This delivery was already delivered.");
            if (!hook.Enabled || hook.PausedAt is not null) throw ApiProblem.Conflict("Switch the webhook on (or resume it) before retrying.");
            // One more attempt now; a failed delivery gets this attempt only.
            delivery.State = WebhookDeliveryState.Pending;
            await client.Attempt(db, hook, delivery, (await Settings(db)).AllowPrivateWebhookAddresses);
            await db.SaveChangesAsync();
            return Webhooks.Dto(delivery);
        })
            .WithSummary("Try a waiting or failed delivery again, now")
            .Produces<ProblemDto>(StatusCodes.Status404NotFound).Produces<ProblemDto>(StatusCodes.Status409Conflict);

        admin.MapPost("/webhooks/{id:guid}/test", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db, WebhookClient client, TimeProvider clock) =>
        {
            var hook = await FindHook(db, id);
            var now = clock.GetUtcNow();
            var eventId = Guid.NewGuid();
            var delivery = new WebhookDelivery
            {
                WebhookId = hook.Id, EventId = eventId, EventType = Webhooks.Ping, CreatedAt = now, State = WebhookDeliveryState.Pending, Test = true,
                Payload = Webhooks.Body(eventId, Webhooks.Ping, now, new JsonObject
                {
                    ["message"] = $"Test delivery from Noosphere, sent by {Viewer.From(user).Username}.", ["webhookId"] = hook.Id.ToString("D"),
                }),
            };
            db.WebhookDeliveries.Add(delivery);
            await client.Attempt(db, hook, delivery, (await Settings(db)).AllowPrivateWebhookAddresses);
            await db.SaveChangesAsync();
            return Webhooks.Dto(delivery);
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound);

        admin.MapGet("/webhooks/{id:guid}/deliveries", async (Guid id, [FromQuery] string? cursor, [FromQuery] int? limit, NoosphereDbContext db) =>
        {
            await FindHook(db, id);
            var errors = new FieldErrors();
            long? before = null;
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                if (long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var c)) before = c;
                else errors.Add("cursor", "Pass the nextCursor of the previous page.");
            }
            var size = limit ?? LinkAdminEndpoints.DefaultPageSize;
            if (size is < 1 or > LinkAdminEndpoints.MaxPageSize) errors.Add("limit", $"Between 1 and {LinkAdminEndpoints.MaxPageSize}.");
            errors.ThrowIfAny();
            var query = db.WebhookDeliveries.AsNoTracking().Where(d => d.WebhookId == id);
            if (before is { } until) query = query.Where(d => d.Id < until);
            var rows = await query.OrderByDescending(d => d.Id).Take(size + 1).ToListAsync();
            var items = rows.Take(size).Select(Webhooks.Dto).ToList();
            return new WebhookDeliveryPageDto(items, rows.Count > size ? items[^1].Id.ToString(CultureInfo.InvariantCulture) : null);
        }).Produces<ProblemDto>(StatusCodes.Status404NotFound);

        admin.MapGet("/settings", async (NoosphereDbContext db) => SettingsDto(await Settings(db)));

        admin.MapPut("/settings", async (IntegrationSettingsInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            if (input.AllowPrivateAddresses is not { } allow)
                throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["allowPrivateAddresses"] = ["This field is required."] });
            var row = await db.IntegrationSettings.FirstOrDefaultAsync();
            if (row is null) db.IntegrationSettings.Add(row = new IntegrationSettings());
            (row.AllowPrivateWebhookAddresses, row.UpdatedAt, row.UpdatedBy) = (allow, clock.GetUtcNow(), Viewer.From(user).Username);
            await db.SaveChangesAsync();
            return SettingsDto(row);
        });
    }

    private static (ApiKey Key, string Value) NewKey(string name, List<string> scopes, DateTimeOffset now, int days, string by)
    {
        var (value, prefix, hash) = ApiKeys.New();
        return (new ApiKey
        {
            Name = name, Prefix = prefix, SecretHash = hash, Scopes = [.. scopes], CreatedAt = now, CreatedBy = by, ExpiresAt = now.AddDays(days),
        }, value);
    }

    private static IntegrationSettingsDto SettingsDto(IntegrationSettings s) =>
        new(s.AllowPrivateWebhookAddresses, s.UpdatedBy is null ? null : s.UpdatedAt, s.UpdatedBy);

    private static async Task<Webhook> FindHook(NoosphereDbContext db, Guid id) =>
        await db.Webhooks.FirstOrDefaultAsync(w => w.Id == id) ?? throw ApiProblem.NotFound($"No webhook with the id '{id}'.");

    private static async Task<Dictionary<Guid, int>> Pending(NoosphereDbContext db) =>
        await db.WebhookDeliveries.Where(d => d.State == WebhookDeliveryState.Pending).GroupBy(d => d.WebhookId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);

    private static async Task<(string Name, string Url, List<string> Events)> Validate(WebhookInput input, NoosphereDbContext db, IWebhookResolver resolver)
    {
        var errors = new FieldErrors();
        var name = errors.Required("name", input.Name);
        var url = input.Url?.Trim() ?? "";
        if (url.Length == 0) errors.Add("url", "This field is required.");
        else if (await WebhookAddresses.Check(url, (await Settings(db)).AllowPrivateWebhookAddresses, resolver) is { } problem) errors.Add("url", problem);
        var events = Webhooks.Events(input.Events, errors);
        errors.ThrowIfAny();
        return (name, url, events);
    }
}
