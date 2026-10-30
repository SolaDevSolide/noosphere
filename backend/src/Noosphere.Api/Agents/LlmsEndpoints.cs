using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Noosphere.Api.Data;
using Noosphere.Api.Endpoints;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Agents;

/// <summary>
/// <c>/llms.txt</c>, <c>/llms-full.txt</c> and <c>/{kind}/{id}.md</c>. Readers: a session (the person's rights), a bearer
/// token with <c>docs:read</c> (the agent's limited rights; the call counts in its quota and activity), or anyone when
/// <c>anonymousLlms</c> is on (records visible to everyone, without protected notes).
/// </summary>
public static class LlmsEndpoints
{
    private const string Markdown = "text/markdown; charset=utf-8";
    private const string Kinds = "^(application|flow|data-object|team|domain|page)$";

    public static void MapLlms(this WebApplication app)
    {
        var group = app.MapGroup("").AllowAnonymous().ExcludeFromDescription();
        group.MapGet("/llms.txt", (HttpContext ctx) => Serve(ctx, "llms.txt", null, null, full: false));
        group.MapGet("/llms-full.txt", (HttpContext ctx) => Serve(ctx, "llms-full.txt", null, null, full: true));
        group.MapGet($"/{{kind:regex({Kinds})}}/{{id}}.md", (string kind, string id, HttpContext ctx) => Serve(ctx, "markdown", kind, id, full: false));
    }

    private static async Task Serve(HttpContext ctx, string tool, string? kindName, string? id, bool full)
    {
        var sp = ctx.RequestServices;
        var db = sp.GetRequiredService<NoosphereDbContext>();
        var settings = sp.GetRequiredService<Settings>();
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
        var baseUrl = PublicAddress.Of(ctx.Request, settings);
        ctx.Response.Headers.CacheControl = "private, no-store";

        if (AgentBearerHandler.TokenOf(ctx.Request) is not null)
        {
            var result = await ctx.AuthenticateAsync(AgentBearerHandler.SchemeName);
            if (!result.Succeeded)
            {
                await AgentBearerHandler.Challenge(ctx, settings, result.Failure?.Message ?? "The access token is not valid.");
                return;
            }
            var connection = AgentBearerHandler.ConnectionOf(result.Principal)!.Value;
            var scopes = AgentBearerHandler.ScopesOf(result.Principal);
            if (!scopes.Contains(Scopes.Read))
            {
                ctx.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", scope=\"{Scopes.Read}\"";
                await Problems.Write(ctx, 403, $"The access token does not allow {Scopes.Read}.");
                return;
            }
            try
            {
                var text = await AgentRuntime.Run(db, connection, scopes, tool, now, baseUrl, async call =>
                {
                    var views = await MarkdownViews.Load(db, call.Access, baseUrl);
                    if (kindName is null)
                    {
                        call.Log(AgentActivityKind.Read, full ? "Read llms-full.txt (every record it may see)." : "Read the llms.txt index.");
                        return await Text(db, settings, views, full);
                    }
                    var kind = MarkdownViews.ParseKind(kindName)!.Value;
                    AgentTools.CheckReadable(call, views, kind, id!);
                    await AgentTools.LogAccess(call, kind, id!, views.NotesShown(kind, id!));
                    call.Log(AgentActivityKind.Read, $"Read the Markdown view of {call.Access.Facts.Title(kind, id!) ?? id} ({AgentTools.Noun(kind)}).", kind, id);
                    return views.Record(kind, id!)!;
                }, cancel: ctx.RequestAborted);
                await Write(ctx, text, plain: kindName is null);
            }
            catch (AgentRefusal r)
            {
                await Problems.Write(ctx, r.Reason == "notFound" ? 404 : 403, r.Message);
            }
            catch (AgentUnavailable e)
            {
                await Problems.Write(ctx, 429, e.Message);
            }
            return;
        }

        Access access;
        var cookie = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (cookie.Succeeded) access = await Access.Load(db, cookie.Principal);
        else if ((await AgentSettingsStore.Get(db)).AnonymousLlms) access = await AgentAccess.Anonymous(db);
        else
        {
            await AgentBearerHandler.Challenge(ctx, settings, null);
            return;
        }
        var markdown = await MarkdownViews.Load(db, access, baseUrl);
        if (kindName is null)
        {
            await Write(ctx, await Text(db, settings, markdown, full), plain: true);
            return;
        }
        var k = MarkdownViews.ParseKind(kindName)!.Value;
        if (!markdown.Exists(k, id!))
        {
            await Problems.Write(ctx, 404, $"No {AgentTools.Noun(k)} with the id '{id}'.");
            return;
        }
        switch (access.Sight(k, id!))
        {
            case Sight.Hidden:
                await Problems.Write(ctx, 403, $"This {AgentTools.Noun(k)} is restricted.");
                return;
            case Sight.Outline:
                await Problems.Write(ctx, 403, $"This {AgentTools.Noun(k)} is not shared with you.");
                return;
        }
        if (cookie.Succeeded)
            await AccessLogs.RecordRead(db, access, k, id!, access.Facts.Owner(k, id!), access.Facts.Restricted(k, id!), markdown.NotesShown(k, id!), now, "markdown");
        await Write(ctx, markdown.Record(k, id!)!, plain: false);
    }

    private static async Task<string> Text(NoosphereDbContext db, Settings settings, MarkdownViews views, bool full)
    {
        var instance = await InstanceSettingsStore.Get(db, settings);
        return full ? views.Full(instance.InstanceName, instance.OrganisationName) : views.Index(instance.InstanceName, instance.OrganisationName);
    }

    private static Task Write(HttpContext ctx, string text, bool plain)
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = plain ? "text/plain; charset=utf-8" : Markdown;
        return ctx.Response.WriteAsync(text);
    }
}
