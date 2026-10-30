using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Agents;

/// <summary>A call refused, with the reason sent back to the agent: written to the activity as <c>refused</c> (or the given kind).</summary>
/// <param name="reason"><c>ceiling</c>, <c>protectedNotes</c>, <c>visibility</c>, <c>secret</c>, <c>scope</c>, <c>role</c>, <c>approval</c>, <c>notFound</c> or <c>invalid</c>.</param>
public sealed class AgentRefusal(string message, string reason, AgentActivityKind kind = AgentActivityKind.Refused,
    RecordKind? recordKind = null, string? recordId = null, string? summary = null) : Exception(message)
{
    public string Reason { get; } = reason;
    public AgentActivityKind Kind { get; } = kind;
    public RecordKind? RecordKind { get; } = recordKind;
    public string? RecordId { get; } = recordId;
    /// <summary>The activity line; the message when null.</summary>
    public string? Summary { get; } = summary;
}

/// <summary>The call cannot run at all (quota reached, agents turned off): a JSON-RPC error for MCP clients.</summary>
public sealed class AgentUnavailable(string message) : Exception(message);

/// <summary>One call of an agent: its connection, person, rights (min of person, ceiling and scopes) and what to log.</summary>
public sealed class AgentCall
{
    public required NoosphereDbContext Db { get; init; }
    public required AgentConnection Connection { get; init; }
    public required User Person { get; init; }
    public required AgentSettings Settings { get; init; }
    public required Access Access { get; init; }
    public required IReadOnlyCollection<string> Scopes { get; init; }
    public required DateTimeOffset Now { get; init; }
    public required string Tool { get; init; }
    public required string BaseUrl { get; init; }

    public AgentActivityKind Kind { get; private set; } = AgentActivityKind.Read;
    public string Summary { get; private set; } = "";
    public RecordKind? RecordKind { get; private set; }
    public string? RecordId { get; private set; }

    /// <summary>The person's own rights, without the agent's limits: tells a ceiling refusal from a visibility one.</summary>
    public Access PersonAccess => new(Access.Viewer, Access.Teams, Access.Facts);

    public void Log(AgentActivityKind kind, string summary, RecordKind? recordKind = null, string? recordId = null) =>
        (Kind, Summary, RecordKind, RecordId) = (kind, summary, recordKind, recordId);

    public void Require(string scope)
    {
        if (!Scopes.Contains(scope))
            throw new AgentRefusal($"Refused (scope): this connection was not granted {scope}. Ask {Person.DisplayName} to connect the agent again and allow it.", "scope");
    }
}

/// <summary>
/// Runs agent calls: checks the connection (revoked, waiting for approval, paused), counts the hourly quota, builds the
/// limited rights, and writes one activity line per call.
/// </summary>
public static class AgentRuntime
{
    /// <summary>JSON-RPC error code of a call refused by the quota (implementation-defined server error range).</summary>
    public const int QuotaErrorCode = -32029;

    public static async Task<AgentCall> Begin(NoosphereDbContext db, Guid connectionId, IReadOnlyCollection<string> scopes, string tool,
        DateTimeOffset now, string baseUrl, int? quotaOverride = null, CancellationToken cancel = default)
    {
        var connection = await db.AgentConnections.FirstOrDefaultAsync(c => c.Id == connectionId, cancel)
            ?? throw new AgentUnavailable("This connection no longer exists.");
        if (connection.RevokedAt is not null) throw new AgentUnavailable("This connection was revoked: authorise the agent again.");
        var settings = await AgentSettingsStore.Get(db, cancel);
        if (!settings.Enabled) throw new AgentUnavailable("AI agents are turned off on this instance.");
        var person = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == connection.UserId, cancel)
            ?? throw new AgentUnavailable("The person this agent acts for no longer exists.");
        if (connection.PendingApproval)
        {
            await Write(db, connection.Id, tool, now, AgentActivityKind.Refused, "Refused: the connection waits for an administrator's approval.", cancel: cancel);
            throw new AgentRefusal("Refused (approval): this connection waits for an administrator's approval. Try again once it is approved.", "approval");
        }
        if (connection.PausedUntil is { } paused && paused > now)
            throw new AgentUnavailable($"Paused: this agent reached its quota of {quotaOverride ?? settings.QuotaPerHour} calls in an hour. Calls resume at {paused.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)} UTC.");

        var quota = quotaOverride ?? settings.QuotaPerHour;
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        var used = await CallsSince(db, connection.Id, hour, cancel);
        if (used >= quota)
        {
            var until = hour.AddHours(1);
            connection.PausedUntil = until;
            var time = until.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            await Write(db, connection.Id, tool, now, AgentActivityKind.Quota, $"Reached {quota} calls in an hour. Paused until {time} UTC.", cancel: cancel);
            throw new AgentUnavailable($"Quota reached: this agent made {quota} calls this hour, the most this instance allows. It is paused until {time} UTC; try again then.");
        }
        connection.LastUsedAt = now;
        return new AgentCall
        {
            Db = db, Connection = connection, Person = person, Settings = settings, Scopes = scopes, Now = now, Tool = tool, BaseUrl = baseUrl,
            Access = await AgentAccess.For(db, person, scopes, settings, cancel),
        };
    }

    /// <summary>Calls counted by the quota since <paramref name="from"/> (quota and revocation lines excluded).</summary>
    public static Task<int> CallsSince(NoosphereDbContext db, Guid connectionId, DateTimeOffset from, CancellationToken cancel = default) =>
        db.AgentActivities.CountAsync(a => a.ConnectionId == connectionId && a.At >= from
            && a.Kind != AgentActivityKind.Quota && a.Kind != AgentActivityKind.Revoked, cancel);

    /// <summary>Runs a call from start to end: one activity line whatever happens.</summary>
    public static async Task<T> Run<T>(NoosphereDbContext db, Guid connectionId, IReadOnlyCollection<string> scopes, string tool, DateTimeOffset now,
        string baseUrl, Func<AgentCall, Task<T>> body, int? quotaOverride = null, CancellationToken cancel = default)
    {
        var call = await Begin(db, connectionId, scopes, tool, now, baseUrl, quotaOverride, cancel);
        try
        {
            var result = await body(call);
            db.AgentActivities.Add(Line(call.Connection.Id, tool, now, call.Kind, call.Summary, call.RecordKind, call.RecordId));
            await db.SaveChangesAsync(cancel);
            return result;
        }
        catch (AgentRefusal r)
        {
            await Fail(call, r.Kind, r.Summary ?? r.Message, r.RecordKind, r.RecordId, cancel);
            throw;
        }
        catch (ApiProblem p)
        {
            var reason = p.Status switch { 403 => "visibility", 404 => "notFound", 409 => "invalid", 422 => "secret", _ => "invalid" };
            await Fail(call, AgentActivityKind.Refused, $"Refused ({reason}): {p.Message}", cancel: cancel);
            throw new AgentRefusal($"Refused ({reason}): {p.Message}", reason);
        }
    }

    /// <summary>Forgets what the call changed, then writes its line.</summary>
    private static async Task Fail(AgentCall call, AgentActivityKind kind, string summary, RecordKind? recordKind = null, string? recordId = null, CancellationToken cancel = default)
    {
        var db = call.Db;
        db.ChangeTracker.Clear();
        await db.AgentConnections.Where(c => c.Id == call.Connection.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, call.Now), cancel);
        await Write(db, call.Connection.Id, call.Tool, call.Now, kind, summary, recordKind, recordId, cancel);
    }

    public static async Task Write(NoosphereDbContext db, Guid connectionId, string tool, DateTimeOffset at, AgentActivityKind kind, string summary,
        RecordKind? recordKind = null, string? recordId = null, CancellationToken cancel = default)
    {
        db.AgentActivities.Add(Line(connectionId, tool, at, kind, summary, recordKind, recordId));
        await db.SaveChangesAsync(cancel);
    }

    private static AgentActivity Line(Guid connectionId, string tool, DateTimeOffset at, AgentActivityKind kind, string summary, RecordKind? recordKind, string? recordId) => new()
    {
        At = at, ConnectionId = connectionId, Tool = tool, Kind = kind,
        Summary = summary.Length > Rules.MaxShortTextLength ? summary[..(Rules.MaxShortTextLength - 1)] + "…" : summary,
        RecordKind = recordKind, RecordId = recordId,
    };

    /// <summary>Deletes activity lines older than <paramref name="days"/> days, expired tokens and requests, and unused registered clients.</summary>
    public static async Task<int> Purge(NoosphereDbContext db, int days, DateTimeOffset now, CancellationToken cancel = default)
    {
        var limit = now.AddDays(-days);
        var purged = await db.AgentActivities.Where(a => a.At < limit).ExecuteDeleteAsync(cancel);
        await db.AgentTokens.Where(t => t.ExpiresAt < now.AddDays(-1)).ExecuteDeleteAsync(cancel);
        await db.OAuthRequests.Where(r => r.ExpiresAt < now.AddDays(-1)).ExecuteDeleteAsync(cancel);
        var stale = now.AddDays(-30);
        await db.OAuthClients.Where(c => c.CreatedAt < stale && !db.AgentConnections.Any(a => a.ClientId == c.ClientId)).ExecuteDeleteAsync(cancel);
        return purged;
    }
}
