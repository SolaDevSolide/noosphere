using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Importing;
using Noosphere.Api.Services;

namespace Noosphere.Api.Endpoints;

/// <summary>Options of an import, in the query string.</summary>
public sealed record ImportQuery(
    [FromQuery] string? Format, [FromQuery] string? DomainId, [FromQuery] string? TeamId, [FromQuery] string? ApplicationId, [FromQuery] string? ParentPageId)
{
    public ImportOptions ToOptions()
    {
        var format = Blank(Format)?.ToLowerInvariant();
        if (format is not null && !ImportFormats.All.Contains(format))
        {
            var errors = new FieldErrors();
            errors.Add("format", $"Expected one of: {string.Join(", ", ImportFormats.All)}.");
            errors.ThrowIfAny();
        }
        return new ImportOptions(format, Blank(DomainId), Blank(TeamId), Blank(ApplicationId), Blank(ParentPageId));
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>
/// Imports from other formats (Backstage, OpenAPI, AsyncAPI, ADR, Mermaid, Excel): a dry run stores a plan and returns
/// its preview, then the same plan is applied by id. Administrators only, as for the exchange import; the Excel template
/// is open to everyone signed in.
/// </summary>
public static class ImportEndpoints
{
    public const string TemplateFile = "noosphere-import-template.xlsx";

    public static void MapImports(this RouteGroupBuilder api)
    {
        var imports = api.MapGroup("/imports").WithTags("Imports");

        imports.MapPost("", async ([AsParameters] ImportQuery query, HttpRequest request, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock,
            ILoggerFactory loggers) =>
        {
            var options = query.ToOptions();
            if (!request.HasFormContentType) throw ApiProblem.BadRequest("Send the files as multipart/form-data (one or more files, or a zip archive).");
            var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
            var files = new List<ImportFile>();
            foreach (var file in form.Files)
            {
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream, request.HttpContext.RequestAborted);
                files.Add(new ImportFile(FileName(file.FileName, files.Count), stream.ToArray()));
            }
            var access = await Access.Load(db, user);
            var plan = await ImportService.Plan(db, access, files, options, clock.GetUtcNow(), loggers.CreateLogger("Noosphere.Imports"), request.HttpContext.RequestAborted);
            return Results.Created($"/api/imports/{plan.Id}", plan);
        })
            .RequireAdmin()
            .WithMetadata(new RequestSizeLimitAttribute(ImportLimits.MaxUploadBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = ImportLimits.MaxUploadBytes, ValueCountLimit = ImportLimits.MaxFiles + 10 })
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.Summary = "Dry run of an import: reads the files and stores a plan to apply";
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Description = "One or more files (Backstage YAML, OpenAPI, AsyncAPI, ADR Markdown, Mermaid, .xlsx) or zip archives of them.",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["multipart/form-data"] = new()
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Object,
                                Required = new HashSet<string> { "files" },
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["files"] = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } },
                                },
                            },
                        },
                    },
                };
                return Task.CompletedTask;
            })
            .Produces<ImportPlanDto>(StatusCodes.Status201Created)
            .Produces<ProblemDto>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDto>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDto>(StatusCodes.Status413PayloadTooLarge);

        imports.MapGet("", async ([FromQuery] int? limit, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            await ImportService.List(db, await Access.Load(db, user), clock.GetUtcNow(), limit ?? 50))
            .RequireAdmin()
            .WithSummary("Recent imports: one's own, everyone's for administrators");

        imports.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock) =>
            await ImportService.Get(db, await Access.Load(db, user), id, clock.GetUtcNow()))
            .RequireAdmin()
            .Produces<ProblemDto>(StatusCodes.Status404NotFound);

        // The body is optional (no body applies the whole plan), so it is read by hand: a declared body would make the
        // route refuse requests without a JSON content type.
        imports.MapPost("/{id:guid}/apply", async (Guid id, HttpRequest request, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock,
            ILoggerFactory loggers) =>
        {
            ImportApplyInput? input = null;
            if (request.HasJsonContentType())
            {
                try
                {
                    input = await request.ReadFromJsonAsync<ImportApplyInput>(Records.Json, request.HttpContext.RequestAborted);
                }
                catch (System.Text.Json.JsonException)
                {
                    throw ApiProblem.BadRequest("Send { \"skip\": [entry indexes] }, or no body to apply the whole plan.");
                }
            }
            return await ImportService.Apply(db, await Access.Load(db, user), id, clock.GetUtcNow(), loggers.CreateLogger("Noosphere.Imports"), input?.Skip);
        })
            .RequireAdmin()
            .AddOpenApiOperationTransformer(async (operation, context, cancel) =>
            {
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = false,
                    Description = "Entries to leave out; no body applies the whole plan.",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new() { Schema = await context.GetOrCreateSchemaAsync(typeof(ImportApplyInput), null, cancel) },
                    },
                };
            })
            .Produces<ImportPlanDto>()
            .WithSummary("Applies the plan of a dry run (same person, before it expires, catalogue unchanged), leaving out the entries in skip")
            .Produces<ProblemDto>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDto>(StatusCodes.Status404NotFound)
            .Produces<ProblemDto>(StatusCodes.Status409Conflict)
            .Produces<ProblemDto>(StatusCodes.Status410Gone)
            .Produces<ProblemDto>(StatusCodes.Status422UnprocessableEntity);

        imports.MapGet("/template.xlsx", (HttpContext ctx) =>
        {
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{TemplateFile}\"";
            return Results.Bytes(ExcelReader.Template(), ExcelReader.ContentType);
        })
            .WithSummary("Excel template of the import (one sheet per kind of record)")
            .Produces(StatusCodes.Status200OK, contentType: ExcelReader.ContentType);
    }

    /// <summary>The name of an uploaded file, without the client's folders.</summary>
    private static string FileName(string? name, int index)
    {
        var clean = (name ?? "").Replace('\\', '/').Split('/')[^1].Trim();
        if (clean.Length == 0) clean = $"file-{index + 1}";
        return clean.Length > 200 ? clean[^200..] : clean;
    }
}
