using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Noosphere.Api.Exchange;

/// <summary>One YAML document read as a JSON tree, with the line where it starts.</summary>
public sealed record YamlDocument(JsonNode? Root, int Line);

/// <summary>
/// YAML reader that is safe on files from elsewhere: aliases are refused (no "billion laughs" expansion), and the number
/// of documents and nodes and the nesting depth are capped. It keeps the line of every mapping and sequence, so that an
/// import can point to the place of an entry.
/// </summary>
public sealed class SafeYaml
{
    private readonly Dictionary<JsonNode, int> _lines = new(ReferenceEqualityComparer.Instance);

    public int MaxNodes { get; init; } = 200_000;
    public int MaxDepth { get; init; } = 64;
    public int MaxDocuments { get; init; } = 1000;

    /// <summary>Line (1-based) where a mapping or sequence starts; null for other nodes.</summary>
    public int? LineOf(JsonNode? node) => node is not null && _lines.TryGetValue(node, out var line) ? line : null;

    /// <summary>Reads every document of the stream. Throws <see cref="YamlException"/> on invalid or refused YAML.</summary>
    public List<YamlDocument> Load(string text)
    {
        var parser = new Parser(new StringReader(text));
        var documents = new List<YamlDocument>();
        var nodes = 0;
        parser.Consume<StreamStart>();
        while (parser.TryConsume<DocumentStart>(out var start))
        {
            if (documents.Count >= MaxDocuments) throw new YamlException(start.Start, start.End, $"At most {MaxDocuments} documents in one file.");
            JsonNode? root = null;
            if (!parser.Accept<DocumentEnd>(out _)) root = Node(parser, 0, ref nodes);
            parser.Consume<DocumentEnd>();
            documents.Add(new YamlDocument(root, (int)start.Start.Line));
        }
        parser.Consume<StreamEnd>();
        return documents;
    }

    private JsonNode? Node(IParser parser, int depth, ref int nodes)
    {
        if (++nodes > MaxNodes) throw new YamlException(parser.Current!.Start, parser.Current.End, $"The document is too large (more than {MaxNodes} nodes).");
        if (depth > MaxDepth) throw new YamlException(parser.Current!.Start, parser.Current.End, $"The document is nested too deeply (more than {MaxDepth} levels).");
        if (parser.TryConsume<AnchorAlias>(out var alias))
            throw new YamlException(alias.Start, alias.End, "YAML aliases (*name) are not accepted: write the values out.");
        if (parser.TryConsume<Scalar>(out var scalar)) return Yaml.Scalar(scalar.Value, scalar.Style);
        if (parser.TryConsume<SequenceStart>(out var sequence))
        {
            var array = new JsonArray();
            _lines[array] = (int)sequence.Start.Line;
            while (!parser.TryConsume<SequenceEnd>(out _)) array.Add(Node(parser, depth + 1, ref nodes));
            return array;
        }
        if (parser.TryConsume<MappingStart>(out var mapping))
        {
            var obj = new JsonObject();
            _lines[obj] = (int)mapping.Start.Line;
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                if (!parser.TryConsume<Scalar>(out var key))
                {
                    var at = parser.Current!;
                    throw new YamlException(at.Start, at.End, at is AnchorAlias
                        ? "YAML aliases (*name) are not accepted: write the values out."
                        : "Keys must be plain text.");
                }
                obj[key.Value] = Node(parser, depth + 1, ref nodes);
            }
            return obj;
        }
        var other = parser.Current!;
        throw new YamlException(other.Start, other.End, "Unexpected YAML content.");
    }
}
