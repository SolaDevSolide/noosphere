using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Noosphere.Api.Exchange;

/// <summary>YAML form of the exchange format: the same tree as the JSON form.</summary>
public static partial class Yaml
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithQuotingNecessaryStrings()
        .DisableAliases()
        .Build();

    [GeneratedRegex(@"^[-+]?\d+(\.\d+)?([eE][-+]?\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    public static string FromJson(JsonNode? node) => Serializer.Serialize(ToPlain(node));

    private static object? ToPlain(JsonNode? node) => node switch
    {
        null => null,
        JsonObject o => o.Aggregate(new OrderedDictionary<string, object?>(), (d, p) => { d.Add(p.Key, ToPlain(p.Value)); return d; }),
        JsonArray a => a.Select(ToPlain).ToList(),
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when v.TryGetValue<long>(out var l) => l,
            JsonValueKind.Number => v.GetValue<double>(),
            _ => null,
        },
        _ => null,
    };

    /// <summary>
    /// Reads the first YAML document as a JSON tree. Unquoted scalars follow the YAML core schema (null, booleans, numbers);
    /// quoted scalars are always strings.
    /// </summary>
    public static JsonNode? ToJson(string text)
    {
        var stream = new YamlStream();
        using (var reader = new StringReader(text)) stream.Load(reader);
        return stream.Documents.Count == 0 ? null : ToNode(stream.Documents[0].RootNode);
    }

    private static JsonNode? ToNode(YamlNode node) => node switch
    {
        YamlMappingNode map => map.Children.Aggregate(new JsonObject(), (o, p) =>
        {
            var key = p.Key is YamlScalarNode k ? k.Value ?? "" : throw new YamlException(p.Key.Start, p.Key.End, "Keys must be plain text.");
            o[key] = ToNode(p.Value);
            return o;
        }),
        YamlSequenceNode seq => new JsonArray([.. seq.Children.Select(ToNode)]),
        YamlScalarNode scalar => Scalar(scalar),
        _ => null,
    };

    private static JsonValue? Scalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "";
        if (scalar.Style is not ScalarStyle.Plain) return JsonValue.Create(value);
        if (value is "" or "~" or "null" or "Null" or "NULL") return null;
        if (value is "true" or "True" or "TRUE") return JsonValue.Create(true);
        if (value is "false" or "False" or "FALSE") return JsonValue.Create(false);
        if (NumberPattern().IsMatch(value))
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return JsonValue.Create(l);
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return JsonValue.Create(d);
        }
        return JsonValue.Create(value);
    }
}
