using Noosphere.Api.Links;

namespace Noosphere.Tests;

/// <summary>Change notices: one post per link every 30 s at most, changes merged, failed posts retried three times.</summary>
public class NoticeBucketsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static NoticeChange Change(string id, string operation, int seconds, string kind = "application") => new(kind, id, operation, T0.AddSeconds(seconds));

    [Fact]
    public void Notices_are_debounced_and_merged_per_link()
    {
        var buckets = new NoticeBuckets();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        Assert.Empty(buckets.TakeDue(T0));

        buckets.Add(a, [Change("billing", "upsert", 0)]);
        var first = Assert.Single(buckets.TakeDue(T0));
        Assert.Equal((a, 1), (first.LinkId, first.Attempt));
        buckets.Succeeded(a);

        // Within 30 s, changes wait and merge: the latest change of a record wins.
        buckets.Add(a, [Change("billing", "upsert", 5), Change("crm", "upsert", 6)]);
        buckets.Add(a, [Change("billing", "delete", 10), Change("crm", "upsert", 3)]);
        buckets.Add(b, [Change("finance", "upsert", 10, "domain")]);
        var early = buckets.TakeDue(T0.AddSeconds(20));
        Assert.Equal(b, Assert.Single(early).LinkId);
        Assert.Equal(2, buckets.Pending(a));
        Assert.Empty(buckets.TakeDue(T0.AddSeconds(29)));

        var second = Assert.Single(buckets.TakeDue(T0.AddSeconds(30)));
        Assert.Equal(a, second.LinkId);
        Assert.Equal([("crm", "upsert", 6), ("billing", "delete", 10)],
            second.Changes.Select(c => (c.Id, c.Operation, (int)(c.At - T0).TotalSeconds)));
        Assert.Equal(0, buckets.Pending(a));
    }

    [Fact]
    public void Failed_posts_are_retried_three_times_with_backoff_then_given_up()
    {
        var buckets = new NoticeBuckets();
        var link = Guid.NewGuid();
        buckets.Add(link, [Change("billing", "upsert", 0)]);
        var now = T0;
        var waits = new List<double>();
        while (true)
        {
            var due = buckets.TakeDue(now);
            if (due.Count == 0)
            {
                now = now.AddSeconds(5);
                continue;
            }
            var (_, changes, attempt) = Assert.Single(due);
            if (attempt > 1) waits.Add((now - T0).TotalSeconds);
            if (!buckets.Failed(link, changes, now)) break;
        }
        // First post at 0 s, retries 30 s, 60 s and 120 s after each failure: at 30, 90 and 210 s; then given up.
        Assert.Equal([30, 90, 210], waits);
        Assert.Equal(0, buckets.Pending(link));

        // A new change starts afresh.
        buckets.Add(link, [Change("crm", "upsert", 300)]);
        Assert.Equal(1, Assert.Single(buckets.TakeDue(now.AddSeconds(30))).Attempt);
    }
}
