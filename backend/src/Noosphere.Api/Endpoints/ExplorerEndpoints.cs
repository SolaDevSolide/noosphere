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

    public static void MapExplorer(this RouteGroupBuilder api, Settings settings)
    {
        api.MapGet("/landscape", async ([AsParameters] FilterQuery query, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            LandscapeQueries.Landscape(await Snapshot.Load(db), Viewer.From(user), query.ToFilter(), clock.GetUtcNow()))
            .WithTags("Explorer");

        api.MapGet("/search", async ([FromQuery] string? q, ClaimsPrincipal user, NoosphereDbContext db) =>
            LandscapeQueries.Search(await Snapshot.Load(db), Viewer.From(user), q))
            .WithTags("Explorer");

        api.MapGet("/export", async ([FromQuery] string? format, HttpContext ctx, NoosphereDbContext db, TimeProvider clock) =>
        {
            var yaml = (format ?? "json").Trim().ToLowerInvariant() switch
            {
                "json" => false,
                "yaml" or "yml" => true,
                _ => throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["format"] = ["Expected json or yaml."] }),
            };
            var now = clock.GetUtcNow();
            var url = settings.PublicUrl ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
            var envelope = ExchangeFormat.Export(await Snapshot.Load(db), Viewer.From(ctx.User), new ExchangeSourceDto(settings.InstanceName, url, now));
            var file = $"noosphere-{now:yyyyMMdd-HHmmss}.{(yaml ? "yaml" : "json")}";
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{file}\"";
            return yaml
                ? Results.Text(ExchangeFormat.ToYaml(envelope), "application/yaml; charset=utf-8")
                : Results.Text(ExchangeFormat.ToJson(envelope), "application/json; charset=utf-8");
        })
            .WithTags("Exchange")
            .Produces<ExchangeEnvelopeDto>(contentType: "application/json", additionalContentTypes: ["application/yaml"]);

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
            var result = await ExchangeFormat.Import(db, root, dryRun ?? false, clock.GetUtcNow());
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
