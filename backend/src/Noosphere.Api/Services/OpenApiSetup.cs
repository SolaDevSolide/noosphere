using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Noosphere.Api.Contracts;

namespace Noosphere.Api.Services;

/// <summary>
/// OpenAPI description served at <c>/openapi/v1.json</c>. A copy is committed as <c>docs/openapi.json</c> (the frontend
/// generates its types from it) and a test fails when it is stale.
/// </summary>
public static class OpenApiSetup
{
    public const string Path = "/openapi/{documentName}.json";

    /// <summary>Response properties and operations that hold a full or a restricted record, discriminated by <c>restricted</c>.</summary>
    private static readonly (string Schema, string Property, string Full, string Restricted)[] Unions =
    [
        (nameof(LandscapeDto), "applications", nameof(ApplicationDto), nameof(RestrictedApplicationDto)),
        (nameof(LandscapeDto), "flows", nameof(FlowDto), nameof(RestrictedFlowDto)),
    ];

    private static readonly (string Path, bool List, string Full, string Restricted)[] UnionOperations =
    [
        ("/api/applications", true, nameof(ApplicationDto), nameof(RestrictedApplicationDto)),
        ("/api/applications/{id}", false, nameof(ApplicationDto), nameof(RestrictedApplicationDto)),
        ("/api/flows", true, nameof(FlowDto), nameof(RestrictedFlowDto)),
        ("/api/flows/{id}", false, nameof(FlowDto), nameof(RestrictedFlowDto)),
    ];

    public static IServiceCollection AddNoosphereOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(o =>
        {
            o.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Noosphere",
                    Version = "v1",
                    Description = "Application landscape API: applications, flows, data objects, teams and domains. Guide: docs/en/api.md.",
                };
                document.Servers = [];
                return Task.CompletedTask;
            });
            o.AddDocumentTransformer(AddContracts);
            o.AddDocumentTransformer(DescribeUnions);
            o.AddSchemaTransformer(StrictNumbers);
            o.AddSchemaTransformer(NullableObjects);
        });

    /// <summary>The "Web" JSON defaults also read numbers written as strings; clients always send real numbers.</summary>
    private static Task StrictNumbers(OpenApiSchema schema, OpenApiSchemaTransformerContext _, CancellationToken __)
    {
        if (schema.Type is { } type && (type & (JsonSchemaType.Integer | JsonSchemaType.Number)) != 0 && type.HasFlag(JsonSchemaType.String))
        {
            schema.Type = type & ~JsonSchemaType.String;
            schema.Pattern = null;
        }
        return Task.CompletedTask;
    }

    /// <summary>A nullable object property (<c>PlannedChangeDto? PlannedChange</c>) is described as "null or the reference".</summary>
    private static Task NullableObjects(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken _)
    {
        if (schema.Properties is null) return Task.CompletedTask;
        foreach (var property in context.JsonTypeInfo.Properties)
        {
            if (!property.IsGetNullable || !schema.Properties.TryGetValue(property.Name, out var current)) continue;
            if (current is OpenApiSchemaReference reference)
                schema.Properties[property.Name] = new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, reference] };
        }
        return Task.CompletedTask;
    }

    /// <summary>Every input and output contract is listed in the components, even those no route declares.</summary>
    private static async Task AddContracts(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancel)
    {
        var contracts = typeof(LandscapeDto).Assembly.GetTypes()
            .Where(t => (t.Namespace == typeof(LandscapeDto).Namespace || t == typeof(ProblemDto)) && t.IsPublic && !t.IsEnum)
            .OrderBy(t => t.Name, StringComparer.Ordinal);
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        foreach (var type in contracts)
        {
            if (document.Components.Schemas.ContainsKey(type.Name)) continue;
            document.Components.Schemas[type.Name] = await context.GetOrCreateSchemaAsync(type, null, cancel);
        }
    }

    /// <summary>
    /// Full and restricted records share a list: "oneOf" the two schemas, and <c>restricted</c> is the literal false or
    /// true, so that generated clients get a discriminated union.
    /// </summary>
    private static Task DescribeUnions(OpenApiDocument document, OpenApiDocumentTransformerContext _, CancellationToken __)
    {
        var schemas = document.Components?.Schemas;
        if (schemas is null) return Task.CompletedTask;
        IOpenApiSchema Union(string full, string restricted) =>
            new OpenApiSchema { OneOf = [new OpenApiSchemaReference(full, document), new OpenApiSchemaReference(restricted, document)] };

        foreach (var (name, flag) in new[] { (nameof(ApplicationDto), false), (nameof(FlowDto), false), (nameof(RestrictedApplicationDto), true), (nameof(RestrictedFlowDto), true) })
            if (schemas.TryGetValue(name, out var s) && s is OpenApiSchema schema && schema.Properties?.TryGetValue("restricted", out var r) == true && r is OpenApiSchema restricted)
                restricted.Enum = [JsonValue.Create(flag)];

        foreach (var (schemaName, property, full, restricted) in Unions)
            if (schemas.TryGetValue(schemaName, out var s) && s is OpenApiSchema schema && schema.Properties is { } props)
                props[property] = new OpenApiSchema { Type = JsonSchemaType.Array, Items = Union(full, restricted) };

        foreach (var (path, list, full, restricted) in UnionOperations)
        {
            if (document.Paths?.TryGetValue(path, out var item) != true || item!.Operations is null) continue;
            if (!item.Operations.TryGetValue(HttpMethod.Get, out var operation) || operation.Responses?.TryGetValue("200", out var response) != true) continue;
            if (response!.Content?.TryGetValue("application/json", out var media) != true || media is not OpenApiMediaType m) continue;
            m.Schema = list ? new OpenApiSchema { Type = JsonSchemaType.Array, Items = Union(full, restricted) } : Union(full, restricted);
        }
        return Task.CompletedTask;
    }
}
