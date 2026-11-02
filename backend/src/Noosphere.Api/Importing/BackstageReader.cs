using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Noosphere.Api.Contracts;
using Noosphere.Domain;
using static Noosphere.Api.Importing.ReaderKit;

namespace Noosphere.Api.Importing;

/// <summary>
/// Backstage catalog files (<c>catalog-info.yaml</c>, one or several documents). Domain → domain, Group → team,
/// System, Component and Resource → applications (a component's domain is that of its system), API → one flow from each
/// consumer to each provider (from the provider to each consumer for an AsyncAPI API), <c>dependsOn</c> → flows. Owners
/// (groups), lifecycles and links are mapped. The annotation <c>noosphere.io/id</c> gives the Noosphere id of an entity.
/// </summary>
public static class BackstageReader
{
    public const string IdAnnotation = "noosphere.io/id";

    private static readonly string[] BusWords = ["queue", "topic", "kafka", "broker", "bus", "event"];
    private static readonly string[] FileWords = ["bucket", "s3", "sftp", "ftp", "file", "storage"];

    private sealed record Entity(string Kind, string Name, string Key, string Title, JsonObject Root, JsonObject Spec, ImportSourceDto Source)
    {
        public string? Description => Str(Root["metadata"], "description");
        public string Ref => $"{Kind.ToLowerInvariant()}:{Name}";
    }

    public static void Read(string file, string text, ReadResult result)
    {
        var (documents, yaml) = ParseYaml(text);
        var entities = new List<Entity>();
        for (var i = 0; i < documents.Count; i++)
        {
            var source = new ImportSourceDto(file, yaml.LineOf(documents[i].Root) ?? documents[i].Line, $"documents[{i}]");
            if (documents[i].Root is null) continue;
            if (documents[i].Root is not JsonObject root)
            {
                result.Fail(source, "Expected a Backstage entity (an object with apiVersion, kind, metadata and spec).");
                continue;
            }
            var apiVersion = Str(root["apiVersion"]) ?? "";
            if (!apiVersion.StartsWith("backstage.io/", StringComparison.Ordinal))
            {
                result.Warn(source, $"Skipped: apiVersion '{apiVersion}' is not a Backstage entity.");
                continue;
            }
            var kind = Str(root["kind"]) ?? "";
            var name = Str(root["metadata"], "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                result.Fail(source, "metadata.name is required.");
                continue;
            }
            var key = Str(Obj(root["metadata"], "annotations"), IdAnnotation) ?? name;
            var title = Str(root["metadata"], "title") ?? (kind == "Group" ? Str(Obj(Obj(root, "spec"), "profile"), "displayName") : null) ?? name;
            entities.Add(new Entity(kind, name.Trim(), key, title, root, Obj(root, "spec") ?? [], source));
        }

        var byRef = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entities) byRef.TryAdd(e.Ref, e);
        Entity? Find(string reference, string defaultKind)
        {
            var (kind, name) = ParseRef(reference, defaultKind);
            return byRef.GetValueOrDefault($"{kind}:{name}");
        }

        foreach (var e in entities)
        {
            switch (e.Kind)
            {
                case "Domain":
                    result.Add(new Candidate(RecordKind.Domain, e.Key, e.Title, e.Source)).Set("description", Description(e));
                    break;
                case "Group":
                    result.Add(new Candidate(RecordKind.Team, e.Key, e.Title, e.Source))
                        .Set("description", Description(e))
                        .Set("contact", Str(Obj(e.Spec, "profile"), "email"));
                    break;
                case "System" or "Component" or "Resource":
                    Application(e, result, Find);
                    break;
                case "API":
                    break;
                case "User":
                    result.Warn(e.Source, $"User '{e.Name}' skipped: accounts are managed in Noosphere, and teams own the records.");
                    break;
                default:
                    result.Warn(e.Source, $"{e.Kind} '{e.Name}' skipped: this kind of entity is not imported.");
                    break;
            }
        }

        // Relations: APIs between their consumers and providers, then dependencies.
        foreach (var api in entities.Where(e => e.Kind == "API"))
        {
            var providers = entities.Where(e => e.Kind is "Component" or "System" && Strings(e.Spec["providesApis"]).Any(r => Same(r, "api", api))).ToList();
            var consumers = entities.Where(e => e.Kind is "Component" or "System" && Strings(e.Spec["consumesApis"]).Any(r => Same(r, "api", api))).ToList();
            if (providers.Count == 0 || consumers.Count == 0)
            {
                result.Warn(api.Source, $"API '{api.Name}' has no {(providers.Count == 0 ? "provider" : "consumer")} in the file: no flow created.");
                continue;
            }
            var type = (Str(api.Spec["type"]) ?? "").ToLowerInvariant();
            var events = type == "asyncapi";
            foreach (var provider in providers)
                foreach (var consumer in consumers.Where(c => c != provider))
                {
                    var (from, to) = events ? (provider, consumer) : (consumer, provider);
                    var flow = result.Add(new Candidate(RecordKind.Flow, $"{from.Key}-{to.Key}-{api.Key}", api.Title, api.Source) { DerivedKeySuffix = api.Key })
                        .Set("description", Description(api))
                        .Set("type", events ? "event" : "api")
                        .Set("status", FlowStatus(Str(api.Spec["lifecycle"])))
                        .Ref("fromId", RecordKind.Application, from.Key)
                        .Ref("toId", RecordKind.Application, to.Key);
                    Owner(api, flow, "ownerTeamId", result, Find);
                }
        }
        foreach (var e in entities.Where(e => e.Kind is "Component" or "Resource" or "System"))
        {
            foreach (var reference in Strings(e.Spec["dependsOn"]))
                Dependency(e, Find(reference, "component"), reference, result, reversed: false);
            foreach (var reference in Strings(e.Spec["dependencyOf"]))
                Dependency(e, Find(reference, "component"), reference, result, reversed: true);
        }
    }

    private static void Application(Entity e, ReadResult result, Func<string, string, Entity?> find)
    {
        var app = result.Add(new Candidate(RecordKind.Application, e.Key, e.Title, e.Source)).Set("description", Description(e));
        var type = (Str(e.Spec["type"]) ?? "").ToLowerInvariant();
        app.Default("kind", e.Kind == "Resource" ? ResourceKind(type) : "app");
        if (e.Kind == "Component" && Str(e.Spec["lifecycle"]) is { } lifecycle)
        {
            if (Lifecycle(lifecycle) is { } mapped) app.Set("lifecycle", mapped);
            else result.Warn(e.Source, $"Lifecycle '{lifecycle}' of '{e.Name}' is not known: left as it is (expected experimental, production or deprecated).");
        }
        // The domain: given for a system, that of its system for a component or a resource.
        var domain = e.Kind == "System" ? Str(e.Spec["domain"])
            : Str(e.Spec["system"]) is { } system && find(system, "system") is { } parent ? Str(parent.Spec["domain"]) : null;
        if (domain is not null) app.Ref("domainId", RecordKind.Domain, Key(find(domain, "domain"), ParseRef(domain, "domain").Name));
        Owner(e, app, "teamId", result, find);
    }

    private static void Dependency(Entity e, Entity? target, string reference, ReadResult result, bool reversed)
    {
        var (kind, name) = ParseRef(reference, "component");
        if (kind is not ("component" or "resource" or "system"))
        {
            result.Warn(e.Source, $"Dependency '{reference}' of '{e.Name}' skipped: only components, resources and systems become flows.");
            return;
        }
        var targetKey = Key(target, name);
        var (from, to) = reversed ? (targetKey, e.Key) : (e.Key, targetKey);
        var toTitle = reversed ? e.Title : target?.Title ?? name;
        result.Add(new Candidate(RecordKind.Flow, $"{from}-{to}", $"Depends on {toTitle}", e.Source) { DerivedKeySuffix = "" })
            .Default("type", FlowType(Str((reversed ? e : target)?.Spec["type"])))
            .Ref("fromId", RecordKind.Application, from)
            .Ref("toId", RecordKind.Application, to);
    }

    private static void Owner(Entity e, Candidate c, string field, ReadResult result, Func<string, string, Entity?>? find = null)
    {
        if (Str(e.Spec["owner"]) is not { } owner) return;
        var (kind, name) = ParseRef(owner, "group");
        if (kind != "group")
        {
            result.Warn(e.Source, $"Owner '{owner}' of '{e.Name}' is not a group: not mapped (teams own the records).");
            return;
        }
        c.Ref(field, RecordKind.Team, Key(find?.Invoke(owner, "group"), name));
    }

    private static string Key(Entity? e, string name) => e?.Key ?? name;

    private static bool Same(string reference, string defaultKind, Entity e)
    {
        var (kind, name) = ParseRef(reference, defaultKind);
        return string.Equals(kind, e.Kind, StringComparison.OrdinalIgnoreCase) && string.Equals(name, e.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An entity reference <c>[kind:][namespace/]name</c>, without its namespace.</summary>
    private static (string Kind, string Name) ParseRef(string reference, string defaultKind)
    {
        var text = reference.Trim();
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var kind = colon > 0 ? text[..colon].ToLowerInvariant() : defaultKind;
        var rest = colon > 0 ? text[(colon + 1)..] : text;
        var slash = rest.LastIndexOf('/');
        return (kind, slash >= 0 ? rest[(slash + 1)..] : rest);
    }

    /// <summary>Description, followed by the entity's links as a list.</summary>
    private static string? Description(Entity e)
    {
        var builder = new StringBuilder(e.Description?.Trim() ?? "");
        if (e.Root["metadata"]?["links"] is JsonArray links && links.Count > 0)
        {
            if (builder.Length > 0) builder.Append("\n\n");
            builder.Append("Links:\n");
            foreach (var link in links)
                if (Str(link, "url") is { } url)
                    builder.Append(CultureInfo.InvariantCulture, $"\n- [{Str(link, "title") ?? url}]({url})");
        }
        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string? Lifecycle(string lifecycle) => lifecycle.Trim().ToLowerInvariant() switch
    {
        "experimental" or "planned" => "planned",
        "production" or "live" => "live",
        "deprecated" => "retiring",
        "retired" => "retired",
        _ => null,
    };

    private static string? FlowStatus(string? lifecycle) => lifecycle?.Trim().ToLowerInvariant() switch
    {
        "experimental" => "planned",
        "production" => "live",
        "deprecated" => "deprecated",
        _ => null,
    };

    private static string ResourceKind(string type) => type switch
    {
        _ when BusWords.Any(type.Contains) => "bus",
        _ when FileWords.Any(type.Contains) => "file",
        _ => "data",
    };
}
