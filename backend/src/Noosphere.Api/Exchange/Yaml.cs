using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
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
    /// quoted scalars are always strings. Aliases are refused (see <see cref="SafeYaml"/>).
    /// </summary>
    public static JsonNode? ToJson(string text)
    {
        var documents = new SafeYaml().Load(text);
        return documents.Count == 0 ? null : documents[0].Root;
    }

    /// <summary>A scalar in the YAML core schema: plain scalars may be null, booleans or numbers; quoted ones are strings.</summary>
    internal static JsonValue? Scalar(string? text, ScalarStyle style)
    {
        var value = text ?? "";
        if (style is not ScalarStyle.Plain) return JsonValue.Create(value);
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
