using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Exchange;
using Noosphere.Api.Services;

namespace Noosphere.Api.Endpoints;

/// <summary>Landscape read model, search, export and import.</summary>
public static class ExplorerEndpoints
{
    public const long ImportMaxBytes = 20 * 1024 * 1024;
    /// <summary>Longest reason given for an export (kept in the access log).</summary>
    public const int MaxReasonLength = 200;

    public static void MapExplorer(this RouteGroupBuilder api, Settings settings)
    {
        api.MapGet("/landscape", async ([AsParameters] FilterQuery query, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
        {
            var filter = query.ToFilter();
            return LandscapeQueries.Landscape(await Snapshot.Load(db), await Access.Load(db, user), filter, clock.GetUtcNow());
        }).WithTags("Explorer");

        api.MapGet("/search", async ([FromQuery] string? q, ClaimsPrincipal user, NoosphereDbContext db) =>
            LandscapeQueries.Search(await Snapshot.Load(db), await Access.Load(db, user), q, await db.Pages.AsNoTracking().ToListAsync()))
            .WithTags("Explorer");

        api.MapGet("/export", async ([FromQuery] string? format, [FromQuery] string? scope, [FromQuery] string? reason, HttpContext ctx, NoosphereDbContext db, TimeProvider clock) =>
        {
            var errors = new FieldErrors();
            var yaml = (format ?? "json").Trim().ToLowerInvariant() switch
            {
                "json" => false,
                "yaml" or "yml" => true,
                _ => Error(errors, "format", "Expected json or yaml."),
            };
            var full = (scope ?? ExchangeFormat.VisibleScope).Trim().ToLowerInvariant() switch
            {
                ExchangeFormat.VisibleScope => false,
                ExchangeFormat.FullScope => true,
                _ => Error(errors, "scope", "Expected visible or full."),
            };
            var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (why is { Length: > MaxReasonLength }) errors.Add("reason", $"At most {MaxReasonLength} characters.");
            else errors.CheckSecret("reason", why);
            errors.ThrowIfAny();
            var access = await Access.Load(db, ctx.User);
            if (full && !access.IsAdmin) throw ApiProblem.Forbidden("Only administrators export the full landscape.");
            var now = clock.GetUtcNow();
            var url = settings.PublicUrl ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
            var name = (await InstanceSettingsStore.Get(db, settings)).InstanceName;
            var reads = new List<(Domain.RecordKind Kind, string Id)>();
            var envelope = ExchangeFormat.Export(await Snapshot.Load(db), await db.Pages.AsNoTracking().ToListAsync(), access,
                new ExchangeSourceDto(name, url, now), full ? ExchangeFormat.FullScope : ExchangeFormat.VisibleScope, reads);
            // A full export is always logged; a visible one when it holds restricted records or protected notes of other teams.
            if (full || reads.Count > 0)
            {
                // The reason, when given, is the detail of the line; otherwise what was exported.
                AccessLogs.Add(db, access, now, Domain.AccessLogWhat.Export, null, null, why
                    ?? $"scope {(full ? ExchangeFormat.FullScope : ExchangeFormat.VisibleScope)}, {(yaml ? "yaml" : "json")}, {envelope.Items.Count} items"
                    + (reads.Count > 0 ? $", {reads.Count} restricted or protected" : ""));
                await db.SaveChangesAsync();
            }
            var file = $"noosphere-{now:yyyyMMdd-HHmmss}.{(yaml ? "yaml" : "json")}";
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{file}\"";
            return yaml
                ? Results.Text(ExchangeFormat.ToYaml(envelope), "application/yaml; charset=utf-8")
                : Results.Text(ExchangeFormat.ToJson(envelope), "application/json; charset=utf-8");
        })
            .WithTags("Exchange")
            .Produces<ExchangeEnvelopeDto>(contentType: "application/json", additionalContentTypes: ["application/yaml"])
            .Produces<ProblemDto>(StatusCodes.Status403Forbidden);

        api.MapPost("/import", async ([FromQuery] bool? dryRun, [FromQuery] string? format, HttpRequest request, NoosphereDbContext db, TimeProvider clock) =>
        {
            using var reader = new StreamReader(request.Body);
            var text = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
            var kind = format?.Trim().ToLowerInvariant() switch
            {
                "json" => "json",
                "yaml" or "yml" => "yaml",
                _ => request.ContentType switch
                {
                    { } t when t.Contains("json", StringComparison.OrdinalIgnoreCase) => "json",
                    { } t when t.Contains("yaml", StringComparison.OrdinalIgnoreCase) => "yaml",
                    _ => null,
                },
            };
            var root = ExchangeFormat.Parse(text, kind);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await ExchangeFormat.Import(db, root, dryRun ?? false, clock.GetUtcNow(), Author.Of(Viewer.From(request.HttpContext.User)));
            await transaction.CommitAsync();
            return result.Errors.Count > 0 && !result.DryRun
                ? Results.Json(result, statusCode: StatusCodes.Status422UnprocessableEntity)
                : Results.Ok(result);
        })
            .RequireAdmin()
            .WithTags("Exchange")
            .WithMetadata(new RequestSizeLimitAttribute(ImportMaxBytes))
            // Any content type is accepted (the format is guessed when it is neither JSON nor YAML): described here only.
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                var envelope = new Microsoft.OpenApi.OpenApiSchemaReference(nameof(ExchangeEnvelopeDto));
                operation.RequestBody = new Microsoft.OpenApi.OpenApiRequestBody
                {
                    Required = true,
                    Description = "An exchange file, in JSON or YAML.",
                    Content = new Dictionary<string, Microsoft.OpenApi.OpenApiMediaType>
                    {
                        ["application/json"] = new() { Schema = envelope },
                        ["application/yaml"] = new() { Schema = envelope },
                    },
                };
                return Task.CompletedTask;
            })
            .Produces<ImportResultDto>()
            .Produces<ImportResultDto>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ProblemDto>(StatusCodes.Status400BadRequest);
    }

    private static bool Error(FieldErrors errors, string field, string message)
    {
        errors.Add(field, message);
        return false;
    }

    public static void MapHealth(this WebApplication app)
    {
        var health = app.MapGroup("/health").WithTags("Health").AllowAnonymous();
        health.MapGet("/live", () => new HealthDto("live"));
        health.MapGet("/ready", async (NoosphereDbContext db, CancellationToken cancel) =>
        {
            string database;
            try
            {
                database = !await db.Database.CanConnectAsync(cancel) ? "unreachable"
                    : (await db.Database.GetPendingMigrationsAsync(cancel)).Any() ? "migrations-pending"
                    : "ok";
            }
            catch (Exception e) when (e is Npgsql.NpgsqlException or InvalidOperationException or TimeoutException)
            {
                database = "unreachable";
            }
            return database == "ok"
                ? Results.Ok(new HealthDto("ready", database))
                : Results.Json(new HealthDto("unavailable", database), statusCode: StatusCodes.Status503ServiceUnavailable);
        }).Produces<HealthDto>().Produces<HealthDto>(StatusCodes.Status503ServiceUnavailable);
    }
}
