using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Services;

/// <summary>
/// The access log: reads of restricted records and of protected notes by people outside the owning team (administrators
/// included, flagged), and full exports. Lines older than <see cref="Settings.AccessLogDays"/> days are purged at startup
/// and daily.
/// </summary>
public static class AccessLogs
{
    /// <summary>Adds a line (saved with the next <c>SaveChanges</c>).</summary>
    public static void Add(NoosphereDbContext db, Access access, DateTimeOffset now, AccessLogWhat what, RecordKind? kind, string? entityId, string? detail = null)
    {
        var (why, team) = Why(access, kind, entityId);
        db.AccessLogs.Add(new AccessLog
        {
            At = now,
            UserId = access.Viewer.Id == Guid.Empty ? null : access.Viewer.Id,
            Username = access.Viewer.Username,
            Kind = kind,
            EntityId = entityId,
            What = what,
            Detail = detail is { Length: > Rules.MaxShortTextLength } ? detail[..Rules.MaxShortTextLength] : detail,
            Admin = access.IsAdmin,
            Why = why,
            WhyTeamId = team,
        });
    }

    /// <summary>Why the reader may read the record: administrator, owning team, a listed team, or named on the access list.</summary>
    public static (AccessLogWhy? Why, string? TeamId) Why(Access access, RecordKind? kind, string? entityId)
    {
        if (access.IsAdmin) return (AccessLogWhy.Admin, null);
        if (kind is not { } k || entityId is null) return (null, null);
        if (access.InTeam(access.Facts.Owner(k, entityId))) return (AccessLogWhy.OwningTeam, null);
        IEnumerable<AccessList> lists = k switch
        {
            RecordKind.Application => [access.Facts.AppAccess(entityId)],
            RecordKind.Page => [access.Facts.PageAccess(entityId)],
            RecordKind.Flow when access.Facts.Ends(entityId) is { } ends => [access.Facts.AppAccess(ends.FromId), access.Facts.AppAccess(ends.ToId)],
            _ => [],
        };
        var all = lists.ToList();
        if (all.SelectMany(l => l.TeamIds).FirstOrDefault(access.InTeam) is { } team) return (AccessLogWhy.ListedTeam, team);
        if (all.Any(l => l.Usernames.Contains(access.Viewer.Username, StringComparer.Ordinal))) return (AccessLogWhy.ListedUser, null);
        return (null, null);
    }

    /// <summary>
    /// Logs the read of a record in full: a line when it is restricted, another when its protected notes were shown, unless
    /// the reader is in its owning team. Saves at once.
    /// </summary>
    public static Task RecordRead(NoosphereDbContext db, Access access, RecordKind kind, VersionedEntity e, bool notesShown,
        DateTimeOffset now, string? detail = null, CancellationToken cancel = default) =>
        RecordRead(db, access, kind, e.Id, access.Facts.Owner(kind, e), access.Facts.Restricted(kind, e), notesShown, now, detail, cancel);

    /// <summary>Same, for a record known by its owner and whether it is restricted (a revision of it, for instance).</summary>
    public static async Task RecordRead(NoosphereDbContext db, Access access, RecordKind kind, string id, string? owner, bool restricted,
        bool notesShown, DateTimeOffset now, string? detail = null, CancellationToken cancel = default)
    {
        if (access.InTeam(owner)) return;
        if (restricted) Add(db, access, now, AccessLogWhat.Record, kind, id, detail);
        if (notesShown) Add(db, access, now, AccessLogWhat.ProtectedNotes, kind, id, detail);
        if (restricted || notesShown) await db.SaveChangesAsync(cancel);
    }

    /// <summary>Deletes the lines older than <paramref name="days"/> days; returns how many.</summary>
    public static Task<int> Purge(NoosphereDbContext db, int days, DateTimeOffset now, CancellationToken cancel = default)
    {
        var limit = now.AddDays(-days);
        return db.AccessLogs.Where(l => l.At < limit).ExecuteDeleteAsync(cancel);
    }
}

/// <summary>Purges the access log, the agent activity (with expired tokens and requests), the link exchange log, nonces and old deployments at startup, then once a day.</summary>
public sealed class AccessLogPurge(IServiceScopeFactory scopes, Settings settings, TimeProvider clock, ILogger<AccessLogPurge> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
                var purged = await AccessLogs.Purge(db, settings.AccessLogDays, clock.GetUtcNow(), stoppingToken);
                if (purged > 0) logger.LogInformation("Access log: {Count} line(s) older than {Days} days purged.", purged, settings.AccessLogDays);
                var activity = await Agents.AgentRuntime.Purge(db, settings.AgentActivityDays, clock.GetUtcNow(), stoppingToken);
                if (activity > 0) logger.LogInformation("Agent activity: {Count} line(s) older than {Days} days purged.", activity, settings.AgentActivityDays);
                var (exchanges, deployments) = await Links.LinkLog.Purge(db, clock.GetUtcNow(), stoppingToken);
                if (exchanges + deployments > 0)
                    logger.LogInformation("Links: {Exchanges} exchange line(s) older than 90 days and {Deployments} deployment(s) ended over 365 days ago purged.", exchanges, deployments);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Access log purge failed; it runs again tomorrow.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
