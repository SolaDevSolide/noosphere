using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Routing;
using Noosphere.Api;
using Noosphere.Api.Agents;
using Noosphere.Api.Auth;
using Noosphere.Api.Cli;
using Noosphere.Api.Data;
using Noosphere.Api.Endpoints;
using Noosphere.Api.Links;
using Noosphere.Api.Security;
using Noosphere.Api.Services;

// Command line ("noosphere help"): same executable, same configuration, no web server.
if (CommandLine.IsCommand(args))
{
    Environment.ExitCode = await CommandLine.Run(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);
Settings.AddSources(builder.Configuration);
var settings = Settings.From(builder.Configuration);

builder.Services.AddNoosphereCore(settings);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    // Enums as camelCase text only: an integer is refused instead of being stored.
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
});
// An invalid JSON body raises BadHttpRequestException, turned into a 400 problem.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1024 * 1024);
builder.Services.AddAccounts();
builder.Services.AddNoosphereAgents();
builder.Services.AddHttpProtection(settings);
builder.Services.AddNoosphereOpenApi();
builder.Services.AddHostedService<AccessLogPurge>();
builder.Services.AddSingleton<LinkNoticeSender>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LinkNoticeSender>());

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSecurityHeaders();
app.UseProblems();
app.UseDefaultFiles();
// index.html is never cached, so that a new version is picked up at once; hashed assets can be.
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    },
};
app.UseStaticFiles(staticFiles);
app.UseCsrfHeaderCheck();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi(OpenApiSetup.Path).AllowAnonymous();
app.MapHealth();
app.MapOAuth(settings);
app.MapMcp("/mcp").RequireAuthorization(AgentBearerHandler.Policy).RequireCors(OAuthEndpoints.CorsPolicy);
app.MapLlms();
app.MapLinks(settings);

var api = app.MapGroup("/api").RequireAuthorization();
api.MapAccounts();
api.MapCatalog();
api.MapPages();
api.MapProposals();
api.MapHistory();
api.MapAccess();
api.MapExplorer(settings);
api.MapConsent();
api.MapAgents();
if (settings.Demo) api.MapDemo();
api.MapLinkAdmin(settings);
// An unknown /api route never falls back to index.html.
api.Map("{**rest}", () => Problems.Result(404, "Unknown API route.")).AllowAnonymous().ExcludeFromDescription();

app.MapFallbackToFile("index.html", staticFiles);

await Bootstrap.Run(app.Services);
await app.RunAsync();

/// <summary>Exposed to WebApplicationFactory (integration tests).</summary>
public partial class Program;
