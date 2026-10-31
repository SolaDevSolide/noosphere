using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api.Links;

/// <summary>The exchange log of links: every request sent or received, refusals included. Kept 90 days.</summary>
public static class LinkLog
{
    /// <summary>Adds a line (saved with the next <c>SaveChanges</c>); a successful exchange also dates the link's last exchange.</summary>
    public static LinkExchange Add(NoosphereDbContext db, Link? link, LinkDirection direction, string operation, LinkExchangeResult result,
        int? status, DateTimeOffset now, string? detail = null, string? reason = null)
    {
        if (link is not null && result == LinkExchangeResult.Ok) link.LastExchangeAt = now;
        var line = new LinkExchange
        {
            At = now, LinkId = link?.Id, Direction = direction, Operation = operation, Result = result, Status = status, Reason = reason,
            Detail = detail is { Length: > Rules.MaxShortTextLength } ? detail[..Rules.MaxShortTextLength] : detail,
        };
        db.LinkExchanges.Add(line);
        return line;
    }

    /// <summary>Deletes exchange lines older than 90 days, nonces older than 10 minutes and deployments ended over 365 days ago.</summary>
    public static async Task<(int Exchanges, int Deployments)> Purge(NoosphereDbContext db, DateTimeOffset now, CancellationToken cancel = default)
    {
        var exchanges = now.AddDays(-LinkProtocol.ExchangeDays);
        var nonces = now - LinkProtocol.NonceWindow;
        var ended = now.AddDays(-LinkProtocol.DeploymentDays);
        var e = await db.LinkExchanges.Where(x => x.At < exchanges).ExecuteDeleteAsync(cancel);
        await db.LinkNonces.Where(n => n.SeenAt < nonces).ExecuteDeleteAsync(cancel);
        var d = await db.Deployments.Where(x => x.EndedAt != null && x.EndedAt < ended).ExecuteDeleteAsync(cancel);
        return (e, d);
    }
}
