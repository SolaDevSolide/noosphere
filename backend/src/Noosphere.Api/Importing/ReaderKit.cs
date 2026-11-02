using System.Text.Json;
using System.Text.Json.Nodes;
using Noosphere.Api.Exchange;

namespace Noosphere.Api.Importing;

/// <summary>Helpers shared by the readers.</summary>
internal static class ReaderKit
{
    /// <summary>Text of a scalar (numbers and booleans as written); null otherwise.</summary>
    public static string? Str(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.GetValueKind() is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToJsonString(),
        _ => null,
    };

    public static string? Str(JsonNode? node, string property) => Str((node as JsonObject)?[property]);

    public static JsonObject? Obj(JsonNode? node, string property) => (node as JsonObject)?[property] as JsonObject;

    /// <summary>Texts of a sequence (a single text counts as a sequence of one).</summary>
    public static List<string> Strings(JsonNode? node) => node switch
    {
        JsonArray a => [.. a.Select(Str).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())],
        JsonValue => Str(node) is { Length: > 0 } s ? [s.Trim()] : [],
        _ => [],
    };

    /// <summary>
    /// Parses a YAML or JSON file (JSON is YAML) with <see cref="SafeYaml"/>; throws <see cref="ImportReadException"/>
    /// with the line on error.
    /// </summary>
    public static (List<YamlDocument> Documents, SafeYaml Reader) ParseYaml(string text)
    {
        var reader = new SafeYaml { MaxNodes = ImportLimits.MaxNodes, MaxDepth = ImportLimits.MaxDepth, MaxDocuments = ImportLimits.MaxDocuments };
        try
        {
            return (reader.Load(text), reader);
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            var message = e.InnerException?.Message ?? e.Message;
            // YamlDotNet prefixes its messages with the position: keep the reason only.
            var cut = message.IndexOf("): ", StringComparison.Ordinal);
            if (message.StartsWith('(') && cut > 0) message = message[(cut + 3)..];
            throw new ImportReadException($"Invalid YAML or JSON: {message}", (int)e.Start.Line);
        }
    }

    /// <summary>A local <c>$ref</c> (<c>#/components/messages/Invoice</c>) followed, at most a few times; null when it does not resolve.</summary>
    public static JsonNode? Deref(JsonNode? root, JsonNode? node)
    {
        for (var hops = 0; hops < 8 && node is JsonObject o && Str(o["$ref"]) is { } reference; hops++)
        {
            if (!reference.StartsWith("#/", StringComparison.Ordinal)) return null;
            node = root;
            foreach (var raw in reference[2..].Split('/'))
            {
                var part = Uri.UnescapeDataString(raw).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
                node = node switch
                {
                    JsonObject obj => obj[part],
                    JsonArray arr when int.TryParse(part, out var i) && i >= 0 && i < arr.Count => arr[i],
                    _ => null,
                };
                if (node is null) return null;
            }
        }
        return node;
    }

    /// <summary>Last part of a local <c>$ref</c>: the name of what it points to.</summary>
    public static string? RefName(JsonNode? node) =>
        Str((node as JsonObject)?["$ref"]) is { } r && r.StartsWith("#/", StringComparison.Ordinal) ? Uri.UnescapeDataString(r[(r.LastIndexOf('/') + 1)..]) : null;

    /// <summary>
    /// Flow type guessed from a protocol, a technology or a label: events for brokers and topics, batch for files,
    /// API otherwise.
    /// </summary>
    public static string FlowType(params string?[] hints)
    {
        var text = string.Join(' ', hints.Where(h => h is not null)).ToLowerInvariant();
        string[] events = ["kafka", "amqp", "mqtt", "event", "topic", "queue", "publish", "subscribe", "jms", "nats", "pubsub", "sns", "sqs", "stomp", "webhook"];
        string[] batches = ["sftp", "ftp", "batch", "file", "csv", "nightly", "etl", "export", "extract"];
        if (events.Any(text.Contains)) return "event";
        if (batches.Any(text.Contains)) return "batch";
        return "api";
    }

    /// <summary>A Markdown table cell: pipes escaped, one line.</summary>
    public static string Cell(string? text) => (text ?? "").Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ").Trim();
}
