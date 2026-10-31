using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Data;
using Noosphere.Api.Links;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Cli;

/// <summary><c>noosphere link ...</c>: pairing codes and links with gestion-mep instances.</summary>
public static partial class CommandLine
{
    private static async Task<int> Link(Options o, IServiceProvider sp, Output output)
    {
        var p = o.Positionals;
        var admin = sp.GetRequiredService<LinkAdmin>();
        var db = sp.GetRequiredService<NoosphereDbContext>();
        try
        {
            switch (p.Count >= 2 ? p[1] : "")
            {
                case "code":
                    Expect(p, 2);
                    return await LinkCode(o, sp, admin, output);
                case "list":
                    Expect(p, 2);
                    var links = await db.Links.AsNoTracking().OrderBy(l => l.State == LinkState.Cut).ThenByDescending(l => l.PairedAt).ToListAsync();
                    if (output.Json) await output.Out.WriteLineAsync(JsonSerializer.Serialize(links.Select(LinkAdmin.Dto), JsonOutput));
                    else
                    {
                        await output.Out.WriteLineAsync($"{"ID",-36} {"STATE",-8} {"PEER",-6} {"INSTANCE",-24} FINGERPRINT");
                        foreach (var l in links)
                            await output.Out.WriteLineAsync($"{l.Id,-36} {Lower(l.State),-8} {(l.PeerConfirmed switch { true => "yes", false => "no", _ => "?" }),-6} {Clip(l.InstanceName, 24),-24} {l.Fingerprint}");
                    }
                    return ExitCodes.Success;
                case "confirm":
                    Expect(p, 3);
                    var fingerprint = o["--fingerprint"] ?? throw new UsageException("Give the fingerprint shown on gestion-mep: --fingerprint \"<four words>\".");
                    var confirmed = await admin.Confirm(await FindLink(db, p[2]), fingerprint, "system");
                    await Done(output, $"Link with {confirmed.InstanceName} confirmed: it is active."
                        + (confirmed.PeerConfirmed == true ? "" : " Waiting for confirmation on the gestion-mep side."), LinkAdmin.Dto(confirmed));
                    return ExitCodes.Success;
                case "renew":
                    Expect(p, 3);
                    var renewed = await admin.RenewKeys(await FindLink(db, p[2]));
                    await Done(output, $"Keys renewed. New fingerprint: {renewed.Fingerprint}", LinkAdmin.Dto(renewed));
                    return ExitCodes.Success;
                case "cut":
                    Expect(p, 3);
                    var link = await FindLink(db, p[2]);
                    if (!o.Flags.Contains("--yes"))
                        throw new CommandException(ExitCodes.Confirmation, $"Cutting the link with {link.InstanceName} cannot be undone. Run again with --yes to confirm.");
                    var (cut, notified) = await admin.Cut(link, "system");
                    await Done(output, $"Link with {cut.InstanceName} cut; its keys are erased."
                        + (notified is { Ok: false } ? $" The peer could not be told ({notified.Detail})." : ""), LinkAdmin.Dto(cut));
                    return ExitCodes.Success;
                default:
                    throw new UsageException("Expected 'link code', 'link list', 'link confirm <id> --fingerprint \"...\"', 'link renew <id>' or 'link cut <id> --yes'.");
            }
        }
        catch (ApiProblem e)
        {
            throw new CommandException(e.Status == 404 ? ExitCodes.NotFound : ExitCodes.Failure, e.Message);
        }
    }

    private static async Task<int> LinkCode(Options o, IServiceProvider sp, LinkAdmin admin, Output output)
    {
        var lifetime = LinkProtocol.CodeLifetime;
        if (o["--expires"] is { } expires)
        {
            var text = expires.Trim().ToLowerInvariant();
            var (number, unit) = text.EndsWith('h') ? (text[..^1], 60) : text.EndsWith('m') ? (text[..^1], 1) : (text, 1);
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n * unit is < 1 or > 1440)
                throw new UsageException("--expires expects a duration from 1m to 24h, e.g. 15m or 2h.");
            lifetime = TimeSpan.FromMinutes(n * unit);
        }
        var errors = new FieldErrors();
        var capabilities = LinkAdmin.Capabilities(o["--capabilities"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), errors);
        if (errors.Any) throw new UsageException(string.Join(" ", errors.All.Select(e => e.Message)));
        var (code, value) = await admin.CreateCode("system", lifetime, capabilities);
        var url = sp.GetRequiredService<Settings>().PublicUrl;
        await Done(output, $"Pairing code: {value}\nValid until {LinkProtocol.Time(code.ExpiresAt)}, single use. Capabilities: {string.Join(", ", code.Capabilities)}.\n"
            + (url is null ? "In gestion-mep, enter this instance's URL and the code." : $"In gestion-mep, enter the URL {url} and the code."),
            new { id = code.Id, code = value, codeId = code.CodeId, expiresAt = code.ExpiresAt, capabilities = code.Capabilities, pairUrl = url });
        return ExitCodes.Success;
    }

    /// <summary>A link by its id, or by the beginning of it (at least 4 characters, unique).</summary>
    private static async Task<Domain.Link> FindLink(NoosphereDbContext db, string text)
    {
        if (Guid.TryParse(text, out var id))
            return await db.Links.FirstOrDefaultAsync(l => l.Id == id) ?? throw new CommandException(ExitCodes.NotFound, $"No link with the id '{text}'.");
        if (text.Length < 4) throw new UsageException("Give the link id (or at least its first 4 characters): see 'noosphere link list'.");
        var prefix = text.ToLowerInvariant();
        var matches = (await db.Links.ToListAsync()).Where(l => l.Id.ToString().StartsWith(prefix, StringComparison.Ordinal)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CommandException(ExitCodes.NotFound, $"No link with an id starting with '{text}'."),
            _ => throw new UsageException($"Several links start with '{text}': give more characters."),
        };
    }

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
