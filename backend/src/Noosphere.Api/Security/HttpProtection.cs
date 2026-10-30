using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Noosphere.Api.Services;

namespace Noosphere.Api.Security;

/// <summary>Reverse proxy headers, login rate limit, anti-CSRF header and browser security headers.</summary>
public static class HttpProtection
{
    public const string LoginPolicy = "login";
    /// <summary>Required on every state-changing /api request. A form or an image on another site cannot add it.</summary>
    public const string CsrfHeader = "X-Requested-With";

    public static IServiceCollection AddHttpProtection(this IServiceCollection services, Settings settings)
    {
        // Client address and scheme sent by a reverse proxy, trusted only from the listed networks.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var entry in settings.TrustedProxies.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                if (System.Net.IPNetwork.TryParse(entry, out var network)) o.KnownIPNetworks.Add(network);
                else if (IPAddress.TryParse(entry, out var address)) o.KnownProxies.Add(address);
            }
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, cancel) => new ValueTask(Problems.Write(ctx.HttpContext, 429,
                ctx.HttpContext.Request.Path.StartsWithSegments("/oauth") ? "Too many client registrations. Try again in a minute."
                    : "Too many sign-in attempts. Try again in a minute.", cancel: cancel));
            o.AddPolicy(LoginPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = settings.LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(Agents.OAuthEndpoints.RegistrationRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = settings.RegistrationsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            var method = ctx.Request.Method;
            if (ctx.Request.Path.StartsWithSegments("/api")
                && !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method)
                && !ctx.Request.Headers.ContainsKey(CsrfHeader))
            {
                await Problems.Write(ctx, 403, $"Missing {CsrfHeader} header.");
                return;
            }
            await next();
        });

    /// <summary>Browser hardening on every response. The SPA is served from the same origin and loads nothing from elsewhere.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((ctx, next) =>
        {
            var headers = ctx.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; "
                + "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
            return next(ctx);
        });
}
