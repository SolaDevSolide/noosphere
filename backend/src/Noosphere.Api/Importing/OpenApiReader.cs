using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Noosphere.Api.Contracts;
using Noosphere.Domain;
using static Noosphere.Api.Importing.ReaderKit;

namespace Noosphere.Api.Importing;

/// <summary>
/// OpenAPI 3.x (YAML or JSON). The API becomes a page attached to its application, holding a summary of the spec
/// (servers, operations, schemas), and a flow from each declared consumer to the application. The application is the
/// <c>applicationId</c> option, <c>x-noosphere-application</c>, or the API title; it is created when it does not exist,
/// and otherwise left as it is. Consumers: <c>x-noosphere-consumers</c> (ids or names).
/// </summary>
public static class OpenApiReader
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "patch", "head", "options", "trace"];

    public static void Read(string file, string text, ImportOptions options, ReadResult result)
    {
        var (documents, yaml) = ParseYaml(text);
        var source = new ImportSourceDto(file, 1, null);
        if (documents.FirstOrDefault()?.Root is not JsonObject root)
        {
            result.Fail(source, "Expected an OpenAPI document (an object with openapi, info and paths).");
            return;
        }
        var version = Str(root["openapi"]) ?? "";
        if (!version.StartsWith("3.", StringComparison.Ordinal))
        {
            result.Fail(source, $"Expected OpenAPI 3.x (got openapi: '{version}').");
            return;
        }
        var info = Obj(root, "info");
        var title = Str(info, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            result.Fail(new ImportSourceDto(file, yaml.LineOf(info) ?? 1, "info.title"), "info.title is required.");
            return;
        }
        var provider = options.ApplicationId ?? Str(root["x-noosphere-application"]) ?? title;
        var app = result.Add(new Candidate(RecordKind.Application, provider, provider == title ? title : provider, source) { NameIsDefault = true })
            .Default("description", Str(info, "description"))
            .Default("kind", "app");

        var page = result.Add(new Candidate(RecordKind.Page, $"{app.Key}-api",
            title.EndsWith("API", StringComparison.OrdinalIgnoreCase) ? title : $"{title} API", source));
        page.Fields["body"] = Summary(root, info, version, title);
        page.Fields["tags"] = new JsonArray("api", "openapi");
        page.Additive.Add("tags");
        page.Ref("attachedTo", RecordKind.Application, app.Key);

        foreach (var consumer in Strings(root["x-noosphere-consumers"]))
            result.Add(new Candidate(RecordKind.Flow, $"{consumer}-{app.Key}-api", title, new ImportSourceDto(file, yaml.LineOf(root["x-noosphere-consumers"]), "x-noosphere-consumers")) { DerivedKeySuffix = "api" })
                .Set("type", "api")
                .Default("description", $"Calls the {title} (OpenAPI).")
                .Ref("fromId", RecordKind.Application, consumer)
                .Ref("toId", RecordKind.Application, app.Key);
        if (root["x-noosphere-consumers"] is null)
            result.Warn(source, "No x-noosphere-consumers: only the API page is imported (no flow).");
    }

    private static string Summary(JsonObject root, JsonObject? info, string version, string title)
    {
        var body = new StringBuilder();
        body.Append(CultureInfo.InvariantCulture, $"**OpenAPI {version}**");
        if (Str(info, "version") is { } v) body.Append(CultureInfo.InvariantCulture, $" · version {v}");
        body.Append("\n\n");
        if (Str(info, "description") is { Length: > 0 } description) body.Append(description.Trim()).Append("\n\n");

        if (root["servers"] is JsonArray { Count: > 0 } servers)
        {
            body.Append("## Servers\n\n| URL | Description |\n| --- | --- |\n");
            foreach (var server in servers) body.Append(CultureInfo.InvariantCulture, $"| {Cell(Str(server, "url"))} | {Cell(Str(server, "description"))} |\n");
            body.Append('\n');
        }

        var operations = new List<(string Method, string Path, string? Summary, string? Id)>();
        if (root["paths"] is JsonObject paths)
            foreach (var (path, item) in paths)
                foreach (var method in Methods)
                    if (item?[method] is JsonObject op)
                        operations.Add((method.ToUpperInvariant(), path, Str(op["summary"]) ?? FirstLine(Str(op["description"])), Str(op["operationId"])));
        body.Append(CultureInfo.InvariantCulture, $"## Operations\n\n{operations.Count} operation(s) in {title}.\n\n");
        if (operations.Count > 0)
        {
            body.Append("| Method | Path | Summary | Operation id |\n| --- | --- | --- | --- |\n");
            foreach (var (method, path, summary, id) in operations)
                body.Append(CultureInfo.InvariantCulture, $"| {method} | `{Cell(path)}` | {Cell(summary)} | {(id is null ? "" : $"`{Cell(id)}`")} |\n");
            body.Append('\n');
        }

        if (Obj(Obj(root, "components"), "schemas") is { Count: > 0 } schemas)
        {
            body.Append("## Schemas\n\n");
            body.Append(string.Join(", ", schemas.Select(s => $"`{Cell(s.Key)}`")));
            body.Append("\n\nSchemas are listed here, not imported as data objects: document the business data they carry as data objects.\n");
        }
        return body.ToString().TrimEnd() + "\n";
    }

    private static string? FirstLine(string? text) => text?.Trim().Split('\n')[0];
}
