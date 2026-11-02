using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Links;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Integrations;

/// <summary>
/// Outgoing webhooks: events queued as deliveries, posted as JSON signed with HMAC-SHA256, retried with a growing delay.
/// <para>
/// <c>X-Noosphere-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex&gt;</c>, where v1 = HMAC-SHA256(secret, "&lt;t&gt;.&lt;body&gt;").
/// <c>t</c> is the time of the attempt: receivers refuse a signature older than 5 minutes (replay), and drop a
/// second delivery with the same <c>X-Noosphere-Delivery</c>.
/// </para>
/// </summary>
public static class Webhooks
{
    public const string CatalogChanged = "catalog.changed";
    public const string DeploymentUpdated = "deployment.updated";
    /// <summary>Sent by the test button only.</summary>
    public const string Ping = "ping";
    public static readonly IReadOnlyList<string> EventTypes = [CatalogChanged, DeploymentUpdated];

    public const string SignatureHeader = "X-Noosphere-Signature";
    public const string EventHeader = "X-Noosphere-Event";
    public const string DeliveryHeader = "X-Noosphere-Delivery";
    public const string HttpClientName = "noosphere-webhooks";
    public const string SecretPrefix = "whsec_";
    /// <summary>A receiver should refuse a signature whose <c>t</c> is further than this from its clock.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    /// <summary>Delay before attempt n+1 after n failed attempts: 30 s, 2 min, 10 min, 1 h, 6 h; then the delivery fails.</summary>
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), TimeSpan.FromHours(6)];
    public static int MaxAttempts => Backoff.Length + 1;

    public static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("Noosphere.Webhooks.v1");

    public static string NewSecret() => SecretPrefix + RandomNumberGenerator.GetString(ApiKeys.Alphabet, 40);

    /// <summary>The value of <c>X-Noosphere-Signature</c> for a body sent at <paramref name="timestamp"/>.</summary>
    public static string Signature(string secret, long timestamp, byte[] body)
    {
        var signed = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".").Concat(body).ToArray();
        return $"t={timestamp.ToString(CultureInfo.InvariantCulture)},v1={Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed))}";
    }

    /// <summary>Event types from user input: known values, deduplicated, in order; at least one.</summary>
    public static List<string> Events(IEnumerable<string>? values, FieldErrors errors, string field = "events")
    {
        var wanted = (values ?? []).Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (var unknown in wanted.Where(v => !EventTypes.Contains(v)))
            errors.Add(field, $"Unknown event type '{unknown}'. Expected {string.Join(", ", EventTypes)}.");
        if (wanted.Count == 0) errors.Add(field, $"At least one event type: {string.Join(", ", EventTypes)}.");
        return [.. EventTypes.Where(wanted.Contains)];
    }

    /// <summary>The JSON body of an event: <c>{id, type, createdAt, data}</c>.</summary>
    public static string Body(Guid eventId, string type, DateTimeOffset at, JsonNode data) =>
        new JsonObject { ["id"] = eventId.ToString("D"), ["type"] = type, ["createdAt"] = LinkProtocol.Time(at), ["data"] = data.DeepClone() }.ToJsonString(LinkProtocol.Json);

    /// <summary>Queues an event for every enabled webhook that receives its type (saved with the caller's next save).</summary>
    public static async Task<int> Enqueue(NoosphereDbContext db, string type, JsonNode data, DateTimeOffset now, CancellationToken cancel = default)
    {
        var hooks = await db.Webhooks.AsNoTracking().Where(w => w.Enabled && w.Events.Contains(type)).Select(w => w.Id).ToListAsync(cancel);
        foreach (var id in hooks)
        {
            var eventId = Guid.NewGuid();
            db.WebhookDeliveries.Add(new WebhookDelivery
            {
                WebhookId = id, EventId = eventId, EventType = type, Payload = Body(eventId, type, now, data), CreatedAt = now,
                State = WebhookDeliveryState.Pending, NextAttemptAt = now,
            });
        }
        return hooks.Count;
    }

    public static WebhookDto Dto(Webhook w, int pending) =>
        new(w.Id, w.Name, w.Url, w.Events, w.Enabled, w.CreatedAt, w.CreatedBy, w.LastDeliveryAt, pending, w.Version);

    public static WebhookDeliveryDto Dto(WebhookDelivery d) =>
        new(d.Id, d.EventId, d.EventType, d.State, d.Attempts, d.CreatedAt, d.LastAttemptAt, d.State == WebhookDeliveryState.Pending ? d.NextAttemptAt : null,
            d.ResponseStatus, d.Error, d.Test);

    /// <summary>Deletes deliveries older than 90 days (with the exchange log) and idempotency records older than 24 hours.</summary>
    public static async Task<int> Purge(NoosphereDbContext db, DateTimeOffset now, CancellationToken cancel = default)
    {
        var old = now.AddDays(-LinkProtocol.ExchangeDays);
        var deliveries = await db.WebhookDeliveries.Where(d => d.CreatedAt < old && d.State != WebhookDeliveryState.Pending).ExecuteDeleteAsync(cancel);
        var stale = now - IntegrationEndpoints.IdempotencyWindow;
        await db.IdempotentRequests.Where(r => r.CreatedAt < stale).ExecuteDeleteAsync(cancel);
        return deliveries;
    }
}

/// <summary>Posts webhook deliveries: one attempt at a time, each written to the delivery and to the exchange log.</summary>
public sealed class WebhookClient(IHttpClientFactory http, IDataProtectionProvider protection, IWebhookResolver resolver, TimeProvider clock, ILogger<WebhookClient> logger)
{
    /// <summary>One attempt (the delivery and the webhook are tracked by <paramref name="db"/>; the caller saves).</summary>
    public async Task Attempt(NoosphereDbContext db, Webhook hook, WebhookDelivery delivery, bool allowPrivate, CancellationToken cancel = default)
    {
        var started = clock.GetUtcNow();
        delivery.Attempts++;
        delivery.LastAttemptAt = started;
        int? status = null;
        string? error = null;
        try
        {
            if (!allowPrivate && Uri.TryCreate(hook.Url, UriKind.Absolute, out var uri) && await WebhookAddresses.CheckHost(uri.IdnHost, resolver, cancel) is { } refused)
                error = refused;
            else
            {
                var body = Encoding.UTF8.GetBytes(delivery.Payload);
                using var request = new HttpRequestMessage(HttpMethod.Post, hook.Url) { Content = new ByteArrayContent(body) };
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                var secret = Webhooks.Protector(protection).Unprotect(hook.ProtectedSecret);
                request.Headers.TryAddWithoutValidation(Webhooks.SignatureHeader, Webhooks.Signature(secret, started.ToUnixTimeSeconds(), body));
                request.Headers.TryAddWithoutValidation(Webhooks.EventHeader, delivery.EventType);
                request.Headers.TryAddWithoutValidation(Webhooks.DeliveryHeader, delivery.EventId.ToString("D"));
                request.Headers.UserAgent.ParseAdd("Noosphere-Webhooks/" + LinkAdmin.Version);
                request.Options.Set(WebhookAddresses.AllowPrivateOption, allowPrivate);
                using var response = await http.CreateClient(Webhooks.HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
                status = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    error = (int)response.StatusCode is >= 300 and < 400 ? $"Redirect ({status}) not followed: point the webhook at the final URL." : $"The receiver answered {status}.";
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or System.Net.Sockets.SocketException && !cancel.IsCancellationRequested)
        {
            error = e is TaskCanceledException ? "No answer within 10 seconds." : $"Unreachable: {e.Message}";
        }
        var now = clock.GetUtcNow();
        delivery.ResponseStatus = status;
        delivery.Error = error is { Length: > Rules.MaxShortTextLength } ? error[..Rules.MaxShortTextLength] : error;
        if (error is null)
        {
            (delivery.State, delivery.NextAttemptAt) = (WebhookDeliveryState.Delivered, null);
            hook.LastDeliveryAt = now;
        }
        else if (delivery.Test || delivery.Attempts >= Webhooks.MaxAttempts)
            (delivery.State, delivery.NextAttemptAt) = (WebhookDeliveryState.Failed, null);
        else
            delivery.NextAttemptAt = now + Webhooks.Backoff[delivery.Attempts - 1];
        var detail = $"{hook.Name}: {delivery.EventType} {(error is null ? "delivered" : error)} (attempt {delivery.Attempts}"
            + (delivery.State == WebhookDeliveryState.Pending ? $", next at {LinkProtocol.Time(delivery.NextAttemptAt!.Value)})" : delivery.State == WebhookDeliveryState.Failed && !delivery.Test ? ", given up)" : ")");
        var line = LinkLog.Add(db, null, LinkDirection.Out, "webhook", error is null ? LinkExchangeResult.Ok : LinkExchangeResult.Failed, status, now, detail);
        line.WebhookId = hook.Id;
        if (error is not null) logger.LogInformation("Webhook {Name}: {Detail}", hook.Name, detail);
    }
}

/// <summary>Every 2 seconds, posts the webhook deliveries that are due (at most 20 per round).</summary>
public sealed class WebhookSender(IServiceScopeFactory scopes, TimeProvider clock, ILogger<WebhookSender> logger) : BackgroundService
{
    public const int PerRound = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await Flush(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Webhooks: a round failed; the next one runs in 2 seconds.");
            }
        }
    }

    /// <summary>One round; returns the number of attempts made.</summary>
    public async Task<int> Flush(CancellationToken cancel = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
        var now = clock.GetUtcNow();
        var due = await db.WebhookDeliveries.Where(d => d.State == WebhookDeliveryState.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id).Take(PerRound).ToListAsync(cancel);
        if (due.Count == 0) return 0;
        var client = scope.ServiceProvider.GetRequiredService<WebhookClient>();
        var allowPrivate = (await IntegrationEndpoints.Settings(db, cancel)).AllowPrivateWebhookAddresses;
        var hooks = await db.Webhooks.Where(w => due.Select(d => d.WebhookId).Contains(w.Id)).ToDictionaryAsync(w => w.Id, cancel);
        var watch = Stopwatch.StartNew();
        foreach (var delivery in due)
        {
            if (!hooks.TryGetValue(delivery.WebhookId, out var hook)) continue;
            if (!hook.Enabled)
            {
                (delivery.State, delivery.NextAttemptAt, delivery.Error) = (WebhookDeliveryState.Failed, null, "The webhook was disabled before delivery.");
                continue;
            }
            await client.Attempt(db, hook, delivery, allowPrivate, cancel);
            await db.SaveChangesAsync(cancel);
        }
        await db.SaveChangesAsync(cancel);
        logger.LogDebug("Webhooks: {Count} attempt(s) in {Elapsed} ms.", due.Count, watch.ElapsedMilliseconds);
        return due.Count;
    }
}
