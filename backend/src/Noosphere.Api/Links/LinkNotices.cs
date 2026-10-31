using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Links;

/// <summary>
/// A saved change of an application, flow, domain or team that may concern linked peers, with what is needed to know
/// whether it was shared before (the original visibility of an application, the original ends of a flow).
/// </summary>
public sealed record ChangeCandidate(RecordKind Kind, string Id, bool Added, DateTimeOffset At,
    Visibility? OriginalVisibility = null, bool VisibilityChanged = false, string? OriginalFromId = null, string? OriginalToId = null);

/// <summary>Saved changes waiting to be turned into change notices (singleton, filled by <see cref="LinkChangeInterceptor"/>).</summary>
public sealed class LinkChangeFeed
{
    private readonly ConcurrentQueue<ChangeCandidate> _candidates = new();

    public void Enqueue(IEnumerable<ChangeCandidate> candidates)
    {
        foreach (var c in candidates) _candidates.Enqueue(c);
    }

    public List<ChangeCandidate> Drain()
    {
        var list = new List<ChangeCandidate>();
        while (_candidates.TryDequeue(out var c)) list.Add(c);
        return list;
    }
}

/// <summary>
/// Watches every save of the catalogue's records (whatever the path: API, proposals, restore, import, demo) and queues
/// the changes of the fields the catalogue carries once the save succeeded.
/// </summary>
public sealed class LinkChangeInterceptor(LinkChangeFeed queue, TimeProvider clock) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<ChangeCandidate>> _pending = [];

    private static readonly string[] AppFields = [nameof(Application.Name), nameof(Application.Description), nameof(Application.DomainId),
        nameof(Application.TeamId), nameof(Application.Kind), nameof(Application.Lifecycle), nameof(Application.Tech), nameof(Application.Visibility)];
    private static readonly string[] FlowFields = [nameof(Flow.Name), nameof(Flow.FromId), nameof(Flow.ToId), nameof(Flow.Type), nameof(Flow.Status), nameof(Flow.Schedule)];
    private static readonly string[] DomainFields = [nameof(BusinessDomain.Name)];
    private static readonly string[] TeamFields = [nameof(Team.Name), nameof(Team.Contact)];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Release(eventData.Context, saved: true);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Release(eventData.Context, saved: true);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Release(eventData.Context, saved: false);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Release(eventData.Context, saved: false);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is null) return;
        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        var found = new List<ChangeCandidate>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            var added = entry.State == EntityState.Added;
            switch (entry.Entity)
            {
                case Application a when Relevant(entry, AppFields):
                    var visibility = (Visibility)entry.Property(nameof(Application.Visibility)).OriginalValue!;
                    found.Add(new ChangeCandidate(RecordKind.Application, a.Id, added, now, added ? null : visibility,
                        entry.State == EntityState.Deleted || (entry.State == EntityState.Modified && visibility != a.Visibility)));
                    break;
                case Flow f when Relevant(entry, FlowFields):
                    found.Add(new ChangeCandidate(RecordKind.Flow, f.Id, added, now, null, false,
                        added ? null : (string?)entry.Property(nameof(Flow.FromId)).OriginalValue, added ? null : (string?)entry.Property(nameof(Flow.ToId)).OriginalValue));
                    break;
                case BusinessDomain d when Relevant(entry, DomainFields):
                    found.Add(new ChangeCandidate(RecordKind.Domain, d.Id, added, now));
                    break;
                case Team t when Relevant(entry, TeamFields):
                    found.Add(new ChangeCandidate(RecordKind.Team, t.Id, added, now));
                    break;
            }
        }
        if (found.Count == 0) return;
        lock (_pending)
        {
            if (_pending.TryGetValue(context, out var list)) list.AddRange(found);
            else _pending.Add(context, found);
        }
    }

    private static bool Relevant(EntityEntry entry, string[] fields) =>
        entry.State != EntityState.Modified || fields.Any(f => entry.Property(f).IsModified);

    private void Release(DbContext? context, bool saved)
    {
        if (context is null) return;
        List<ChangeCandidate>? list;
        lock (_pending)
        {
            if (!_pending.TryGetValue(context, out list)) return;
            _pending.Remove(context);
        }
        if (saved) queue.Enqueue(list);
    }
}

/// <summary>
/// Pending change notices per link: changes merged by record (the latest wins), at most one post per link every
/// 30 seconds, and a failed post retried three times with a growing delay (30 s, 60 s, 120 s) before it is given up.
/// </summary>
public sealed class NoticeBuckets
{
    public const int MaxAttempts = 4;

    private sealed class Bucket
    {
        public Dictionary<(string Kind, string Id), NoticeChange> Changes { get; } = [];
        public DateTimeOffset? LastPostAt { get; set; }
        public DateTimeOffset? RetryAt { get; set; }
        public int Attempts { get; set; }
    }

    private readonly Dictionary<Guid, Bucket> _buckets = [];

    public void Add(Guid linkId, IEnumerable<NoticeChange> changes)
    {
        lock (_buckets)
        {
            if (!_buckets.TryGetValue(linkId, out var bucket)) _buckets[linkId] = bucket = new Bucket();
            Merge(bucket, changes);
        }
    }

    private static void Merge(Bucket bucket, IEnumerable<NoticeChange> changes)
    {
        foreach (var c in changes)
            if (!bucket.Changes.TryGetValue((c.Kind, c.Id), out var known) || known.At <= c.At) bucket.Changes[(c.Kind, c.Id)] = c;
    }

    /// <summary>The changes to post now, per link (taken out of the buckets), with the attempt number (1 for the first post).</summary>
    public List<(Guid LinkId, List<NoticeChange> Changes, int Attempt)> TakeDue(DateTimeOffset now)
    {
        var due = new List<(Guid, List<NoticeChange>, int)>();
        lock (_buckets)
        {
            foreach (var (id, bucket) in _buckets)
            {
                if (bucket.Changes.Count == 0) continue;
                if (bucket.LastPostAt is { } last && now - last < LinkProtocol.NoticeInterval) continue;
                if (bucket.RetryAt is { } retry && now < retry) continue;
                bucket.LastPostAt = now;
                bucket.Attempts++;
                due.Add((id, bucket.Changes.Values.OrderBy(c => c.At).ThenBy(c => c.Kind, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal).ToList(), bucket.Attempts));
                bucket.Changes.Clear();
            }
        }
        return due;
    }

    public void Succeeded(Guid linkId)
    {
        lock (_buckets)
            if (_buckets.TryGetValue(linkId, out var bucket)) (bucket.Attempts, bucket.RetryAt) = (0, null);
    }

    /// <summary>Puts the changes back for a retry; false when the attempts are used up (the changes are dropped).</summary>
    public bool Failed(Guid linkId, IEnumerable<NoticeChange> changes, DateTimeOffset now)
    {
        lock (_buckets)
        {
            if (!_buckets.TryGetValue(linkId, out var bucket)) return false;
            if (bucket.Attempts >= MaxAttempts)
            {
                (bucket.Attempts, bucket.RetryAt) = (0, null);
                return false;
            }
            bucket.RetryAt = now + LinkProtocol.NoticeInterval * Math.Pow(2, bucket.Attempts - 1);
            Merge(bucket, changes.Where(c => !bucket.Changes.ContainsKey((c.Kind, c.Id))));
            return true;
        }
    }

    /// <summary>Forgets a link (cut, or no longer sending notices).</summary>
    public void Drop(Guid linkId)
    {
        lock (_buckets) _buckets.Remove(linkId);
    }

    public int Pending(Guid linkId)
    {
        lock (_buckets) return _buckets.TryGetValue(linkId, out var b) ? b.Changes.Count : 0;
    }
}

/// <summary>Turns saved changes into notices: shared now → upsert; shared before but not any more (deleted, restricted) → delete.</summary>
public static class NoticeResolver
{
    public static async Task<List<NoticeChange>> Resolve(NoosphereDbContext db, IReadOnlyList<ChangeCandidate> candidates, CancellationToken cancel = default)
    {
        if (candidates.Count == 0) return [];
        var visibility = await db.Applications.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Visibility, cancel);
        // Before this batch: the original visibility of the applications it changed, the current one of the others.
        var before = new Dictionary<string, Visibility?>(StringComparer.Ordinal);
        foreach (var c in candidates.Where(c => c.Kind == RecordKind.Application))
            before.TryAdd(c.Id, c.Added ? null : c.OriginalVisibility);
        bool SharedBefore(string? app)
        {
            if (app is null) return false;
            if (before.TryGetValue(app, out var v)) return v == Visibility.Everyone;
            return visibility.TryGetValue(app, out var current) && current == Visibility.Everyone;
        }
        bool SharedNow(string? app) => app is not null && visibility.TryGetValue(app, out var v) && v == Visibility.Everyone;

        var flows = await db.Flows.AsNoTracking().Select(f => new { f.Id, f.FromId, f.ToId }).ToListAsync(cancel);
        var flowEnds = flows.ToDictionary(f => f.Id, f => (f.FromId, f.ToId));
        var domains = (await db.Domains.AsNoTracking().Select(d => d.Id).ToListAsync(cancel)).ToHashSet(StringComparer.Ordinal);
        var teams = (await db.Teams.AsNoTracking().Select(t => t.Id).ToListAsync(cancel)).ToHashSet(StringComparer.Ordinal);

        var changes = new Dictionary<(string, string), NoticeChange>();
        void Emit(string kind, string id, bool was, bool now, DateTimeOffset at)
        {
            if (now) changes[(kind, id)] = new NoticeChange(kind, id, "upsert", at);
            else if (was) changes[(kind, id)] = new NoticeChange(kind, id, "delete", at);
        }

        foreach (var c in candidates)
        {
            switch (c.Kind)
            {
                case RecordKind.Application:
                    Emit("application", c.Id, !c.Added && c.OriginalVisibility == Visibility.Everyone, SharedNow(c.Id), c.At);
                    if (c.VisibilityChanged)
                        foreach (var f in flows.Where(f => f.FromId == c.Id || f.ToId == c.Id))
                            Emit("flow", f.Id, SharedBefore(f.FromId) && SharedBefore(f.ToId), SharedNow(f.FromId) && SharedNow(f.ToId), c.At);
                    break;
                case RecordKind.Flow:
                    var exists = flowEnds.TryGetValue(c.Id, out var ends);
                    Emit("flow", c.Id, !c.Added && SharedBefore(c.OriginalFromId) && SharedBefore(c.OriginalToId),
                        exists && SharedNow(ends.FromId) && SharedNow(ends.ToId), c.At);
                    break;
                case RecordKind.Domain:
                    Emit("domain", c.Id, !c.Added, domains.Contains(c.Id), c.At);
                    break;
                case RecordKind.Team:
                    Emit("team", c.Id, !c.Added, teams.Contains(c.Id), c.At);
                    break;
            }
        }
        return [.. changes.Values];
    }
}

/// <summary>
/// Posts change notices to the linked peers that receive them (active, confirmed here, with <c>notices:send</c>): every
/// second, it resolves the saved changes and posts what is due.
/// </summary>
public sealed class LinkNoticeSender(IServiceScopeFactory scopes, LinkChangeFeed queue, NoticeBuckets buckets, TimeProvider clock, ILogger<LinkNoticeSender> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await Flush(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Change notices: a round failed; the next one runs in a second.");
            }
        }
    }

    /// <summary>One round: resolve the queued changes, then post the notices that are due.</summary>
    public async Task Flush(CancellationToken cancel = default)
    {
        var candidates = queue.Drain();
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NoosphereDbContext>();
        if (candidates.Count > 0)
        {
            var changes = await NoticeResolver.Resolve(db, candidates, cancel);
            if (changes.Count > 0)
                foreach (var id in await db.Links.AsNoTracking().Where(Receives).Select(l => l.Id).ToListAsync(cancel))
                    buckets.Add(id, changes);
        }
        var now = clock.GetUtcNow();
        var due = buckets.TakeDue(now);
        if (due.Count == 0) return;
        var client = scope.ServiceProvider.GetRequiredService<LinkClient>();
        foreach (var (linkId, changes, attempt) in due)
        {
            var link = await db.Links.FirstOrDefaultAsync(l => l.Id == linkId, cancel);
            if (link is null || !ReceivesNow(link))
            {
                buckets.Drop(linkId);
                continue;
            }
            var payload = new JsonObject { ["changes"] = LinkProtocol.ToNode(changes) };
            var result = await client.Send(db, link, "notices", HttpMethod.Post, payload, cancel);
            if (result.Ok)
            {
                buckets.Succeeded(linkId);
                continue;
            }
            if (result.Reason == LinkProtocol.Reasons.NotConfirmed)
            {
                // The peer re-reads the whole catalogue once its administrator confirms: nothing to retry.
                link.PeerConfirmed = false;
                await db.SaveChangesAsync(cancel);
                buckets.Succeeded(linkId);
                continue;
            }
            if (!buckets.Failed(linkId, changes, clock.GetUtcNow()))
            {
                LinkLog.Add(db, link, LinkDirection.Out, "notices", LinkExchangeResult.Failed, result.Status, clock.GetUtcNow(),
                    $"Given up after {attempt} attempts: {changes.Count} change(s) not notified. The peer re-reads the catalogue every 15 minutes.");
                await db.SaveChangesAsync(cancel);
            }
        }
    }

    private static readonly System.Linq.Expressions.Expression<Func<Link, bool>> Receives = l =>
        l.State == LinkState.Active && l.ConfirmedHere && l.Capabilities.Contains(LinkProtocol.NoticesSend);

    private static readonly Func<Link, bool> ReceivesNow = Receives.Compile();
}
