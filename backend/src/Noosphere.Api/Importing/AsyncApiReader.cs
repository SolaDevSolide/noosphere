using System.Globalization;
using System.Text.Json.Nodes;
using Noosphere.Api.Contracts;
using Noosphere.Domain;
using static Noosphere.Api.Importing.ReaderKit;

namespace Noosphere.Api.Importing;

/// <summary>
/// AsyncAPI 2.x and 3.x (YAML or JSON). Messages become data objects; each operation becomes an event flow between the
/// application and the broker: from the application to the broker when it sends (2.x <c>subscribe</c>, 3.x
/// <c>send</c>), from the broker when it receives (2.x <c>publish</c>, 3.x <c>receive</c>). The application is found as
/// for OpenAPI; the broker is <c>x-noosphere-bus</c>, or the first server (an application of kind bus). Both are only
/// filled in where empty when they exist.
/// </summary>
public static class AsyncApiReader
{
    private sealed record Operation(bool Sends, string Channel, List<(string Key, JsonObject Message)> Messages, string? Summary, ImportSourceDto Source);

    public static void Read(string file, string text, ImportOptions options, ReadResult result)
    {
        var (documents, yaml) = ParseYaml(text);
        var source = new ImportSourceDto(file, 1, null);
        if (documents.FirstOrDefault()?.Root is not JsonObject root)
        {
            result.Fail(source, "Expected an AsyncAPI document (an object with asyncapi, info and channels).");
            return;
        }
        var version = Str(root["asyncapi"]) ?? "";
        var v3 = version.StartsWith("3.", StringComparison.Ordinal);
        if (!v3 && !version.StartsWith("2.", StringComparison.Ordinal))
        {
            result.Fail(source, $"Expected AsyncAPI 2.x or 3.x (got asyncapi: '{version}').");
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

        var operations = v3 ? Operations3(root, file, yaml) : Operations2(root, file, yaml);
        Candidate? bus = null;
        var server = (root["servers"] as JsonObject)?.FirstOrDefault();
        var busToken = Str(root["x-noosphere-bus"]);
        if (busToken is not null || server is { Value: JsonObject })
        {
            var protocol = Str(server?.Value, "protocol");
            var busName = Str(server?.Value, "title") ?? (protocol is null ? null : $"{Capitalize(protocol)} broker");
            var tech = protocol is null ? null : $"{Capitalize(protocol)}{(Str(server?.Value, "protocolVersion") is { } pv ? " " + pv : "")}";
            bus = result.Add(new Candidate(RecordKind.Application, busToken ?? busName ?? server!.Value.Key, busName ?? busToken ?? server!.Value.Key,
                new ImportSourceDto(file, yaml.LineOf(server?.Value) ?? 1, server is null ? "x-noosphere-bus" : $"servers.{server.Value.Key}")) { NameIsDefault = true })
                .Default("kind", "bus")
                .Default("tech", tech);
            if (busToken is null && root["servers"] is JsonObject { Count: > 1 })
                result.Warn(source, $"Several servers: the first one ('{server!.Value.Key}') is taken as the broker. Set x-noosphere-bus to choose it.");
        }
        else if (operations.Count > 0)
            result.Warn(source, "No server nor x-noosphere-bus: the broker is unknown, so no flow is created (data objects only).");

        var objects = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var op in operations)
        {
            var keys = new List<string>();
            foreach (var (key, message) in op.Messages)
            {
                var name = Str(message["name"]) ?? key;
                var candidate = objects.GetValueOrDefault(Normalize.Key(name));
                if (candidate is null)
                {
                    candidate = result.Add(new Candidate(RecordKind.DataObject, name, Str(message["title"]) ?? name,
                        new ImportSourceDto(op.Source.File, yaml.LineOf(message) ?? op.Source.Line, op.Source.Place)) { NameIsDefault = true })
                        .Default("description", Str(message["summary"]) ?? Str(message["description"]))
                        .Default("sensitivity", Str(message["x-noosphere-sensitivity"]));
                    objects[candidate.Key!] = candidate;
                }
                if (op.Sends) candidate.Ref("sourceApplicationId", RecordKind.Application, app.Key, soft: true);
                keys.Add(candidate.Key!);
            }
            if (bus is null) continue;
            var (from, to) = op.Sends ? (app.Key!, bus.Key!) : (bus.Key!, app.Key!);
            var flowName = op.Messages.Count == 1 ? Str(op.Messages[0].Message["name"]) ?? op.Messages[0].Key : op.Channel;
            var flow = result.Add(new Candidate(RecordKind.Flow, $"{from}-{to}-{op.Channel}", flowName, op.Source) { DerivedKeySuffix = op.Channel })
                .Set("type", "event")
                .Default("description", op.Summary)
                .Ref("fromId", RecordKind.Application, from)
                .Ref("toId", RecordKind.Application, to);
            if (keys.Count > 0)
            {
                flow.Refs["dataObjectIds"] = new CandidateRef(RecordKind.DataObject, keys.Distinct().ToList());
                flow.Additive.Add("dataObjectIds");
            }
        }
    }

    /// <summary>2.x: <c>subscribe</c> describes what the application sends, <c>publish</c> what it receives.</summary>
    private static List<Operation> Operations2(JsonObject root, string file, Exchange.SafeYaml yaml)
    {
        var list = new List<Operation>();
        if (root["channels"] is not JsonObject channels) return list;
        foreach (var (name, channel) in channels)
            foreach (var (verb, sends) in new[] { ("subscribe", true), ("publish", false) })
            {
                if (Deref(root, channel?[verb]) is not JsonObject op) continue;
                var messages = new List<(string, JsonObject)>();
                var message = op["message"];
                var all = Deref(root, message) is JsonObject m && m["oneOf"] is JsonArray oneOf ? oneOf.ToList() : [message];
                foreach (var item in all)
                    if (Deref(root, item) is JsonObject resolved) messages.Add((RefName(item) ?? $"{name} message", resolved));
                list.Add(new Operation(sends, name, messages, Str(op["summary"]) ?? Str(op["description"]) ?? Str(channel?["description"]),
                    new ImportSourceDto(file, yaml.LineOf(op), $"channels.{name}.{verb}")));
            }
        return list;
    }

    /// <summary>3.x: operations with <c>action</c> send or receive, on a channel, with its messages (all those of the channel by default).</summary>
    private static List<Operation> Operations3(JsonObject root, string file, Exchange.SafeYaml yaml)
    {
        var list = new List<Operation>();
        if (root["operations"] is not JsonObject operations) return list;
        foreach (var (key, raw) in operations)
        {
            if (Deref(root, raw) is not JsonObject op) continue;
            var action = Str(op["action"]);
            if (action is not ("send" or "receive")) continue;
            var channel = Deref(root, op["channel"]) as JsonObject;
            var channelName = Str(channel?["address"]) ?? RefName(op["channel"]) ?? key;
            var messages = new List<(string, JsonObject)>();
            var refs = op["messages"] is JsonArray listed ? listed.ToList()
                : (channel?["messages"] as JsonObject)?.Select(p => p.Value).ToList() ?? [];
            foreach (var item in refs)
                if (Deref(root, item) is JsonObject resolved) messages.Add((RefName(item) ?? key, resolved));
            list.Add(new Operation(action == "send", channelName, messages, Str(op["summary"]) ?? Str(op["description"]) ?? Str(channel?["description"]),
                new ImportSourceDto(file, yaml.LineOf(op), $"operations.{key}")));
        }
        return list;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
