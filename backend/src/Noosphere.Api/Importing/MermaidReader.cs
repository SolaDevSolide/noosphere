using System.Text;
using System.Text.RegularExpressions;
using Noosphere.Api.Contracts;
using Noosphere.Domain;

namespace Noosphere.Api.Importing;

/// <summary>
/// Mermaid diagrams (<c>.mmd</c>, or <c>```mermaid</c> blocks of a Markdown file): <c>flowchart</c>/<c>graph</c>, and
/// C4 (<c>C4Context</c>, <c>C4Container</c>, <c>C4Component</c>). Nodes, systems and containers become applications
/// (databases of kind data, queues of kind bus, <c>_Ext</c> elements external); edges and <c>Rel</c> become flows,
/// whose type is guessed from the label and the technology. A subgraph or a boundary whose title names an existing domain
/// puts its applications in it. People are left out. Applications that already exist are only filled in where empty.
/// </summary>
public static partial class MermaidReader
{
    private sealed record Node(string Id, string Label, string Kind, string? Tech, string? Description, string? Domain, int Line);

    private sealed record Edge(string From, string To, string? Label, string? Tech, int Line);

    private static readonly string[] LabelGroups = ["label", "t1", "t2", "t3"];

    [GeneratedRegex(@"```mermaid\s*\n(?<code>[\s\S]*?)```", RegexOptions.CultureInvariant)]
    private static partial Regex Fence();

    [GeneratedRegex(@"\G\s*(?<id>[A-Za-z0-9_]+)(?<shape>\[\[.*?\]\]|\[\(.*?\)\]|\(\[.*?\]\)|\(\(.*?\)\)|\[/.*?/\]|\[\\.*?\\\]|\[/.*?\\\]|\[\\.*?/\]|\[.*?\]|\(.*?\)|\{\{.*?\}\}|\{.*?\}|>.*?\])?", RegexOptions.CultureInvariant)]
    private static partial Regex FlowNode();

    [GeneratedRegex(@"\G\s*(?<back><)?(?:--\s*(?<t1>[^\-|>][^|>]*?)\s*-{2,}|==\s*(?<t2>[^=|>][^|>]*?)\s*={2,}|-\.\s*(?<t3>[^.|>][^|>]*?)\s*\.-+|-{2,}|={2,}|-\.+-)(?<head>[>ox])?\s*(?:\|(?<label>[^|]*)\|)?", RegexOptions.CultureInvariant)]
    private static partial Regex FlowEdge();

    [GeneratedRegex(@"^(?<fn>[A-Za-z_]+)\s*\((?<args>.*)\)\s*(?<open>\{)?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex C4Call();

    public static void Read(string file, string text, ReadResult result)
    {
        var blocks = new List<(string Code, int Line)>();
        var fences = Fence().Matches(text);
        if (fences.Count > 0)
            foreach (Match m in fences) blocks.Add((m.Groups["code"].Value, text[..m.Groups["code"].Index].Count(c => c == '\n') + 1));
        else blocks.Add((text, 1));

        foreach (var (code, firstLine) in blocks)
        {
            var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
            var edges = new List<Edge>();
            var people = new HashSet<string>(StringComparer.Ordinal);
            var lines = code.ReplaceLineEndings("\n").Split('\n');
            var header = Array.FindIndex(lines, l => Clean(l).Length > 0);
            if (header < 0) continue;
            var type = Clean(lines[header]).Split(' ', 2)[0];
            var source = new ImportSourceDto(file, firstLine + header, null);
            if (type is "C4Context" or "C4Container" or "C4Component")
                C4(lines, header + 1, firstLine, file, nodes, edges, people, result);
            else if (type is "flowchart" or "graph")
                Flowchart(lines, header + 1, firstLine, nodes, edges);
            else
            {
                result.Fail(source, $"Diagram '{type}' is not read: expected flowchart, graph, C4Context, C4Container or C4Component.");
                continue;
            }

            foreach (var node in nodes.Values)
            {
                var app = result.Add(new Candidate(RecordKind.Application, node.Id, node.Label, new ImportSourceDto(file, node.Line, null)) { NameIsDefault = true })
                    .Default("kind", node.Kind)
                    .Default("tech", node.Tech)
                    .Default("description", node.Description);
                if (node.Domain is not null) app.Ref("domainId", RecordKind.Domain, node.Domain, soft: true);
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var skippedPeople = 0;
            foreach (var edge in edges)
            {
                var at = new ImportSourceDto(file, edge.Line, null);
                if (people.Contains(edge.From) || people.Contains(edge.To))
                {
                    skippedPeople++;
                    continue;
                }
                if (!nodes.TryGetValue(edge.From, out var from) || !nodes.TryGetValue(edge.To, out var to))
                {
                    result.Warn(at, $"Relation {edge.From} → {edge.To} skipped: {(nodes.ContainsKey(edge.From) ? edge.To : edge.From)} is not declared.");
                    continue;
                }
                if (from.Id == to.Id) continue;
                var key = Normalize.Key($"{from.Id}-{to.Id}");
                var suffix = "";
                if (!keys.Add(key))
                {
                    suffix = edge.Label ?? keys.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    keys.Add(key = Normalize.Key($"{from.Id}-{to.Id}-{suffix}"));
                }
                result.Add(new Candidate(RecordKind.Flow, key, edge.Label is { Length: > 0 } label ? label : $"{from.Label} to {to.Label}", at) { NameIsDefault = true, DerivedKeySuffix = suffix })
                    .Default("type", from.Kind == "bus" || to.Kind == "bus" ? "event" : ReaderKit.FlowType(edge.Label, edge.Tech))
                    .Default("description", edge.Tech is null ? null : $"Technology: {edge.Tech}.")
                    .Ref("fromId", RecordKind.Application, from.Id)
                    .Ref("toId", RecordKind.Application, to.Id);
            }
            if (people.Count > 0)
                result.Warn(source, $"{people.Count} person(s) and {skippedPeople} relation(s) with them left out: people are not applications.");
        }
    }

    private static string Clean(string line)
    {
        var comment = line.IndexOf("%%", StringComparison.Ordinal);
        return (comment >= 0 ? line[..comment] : line).Trim();
    }

    private static void Flowchart(string[] lines, int start, int firstLine, Dictionary<string, Node> nodes, List<Edge> edges)
    {
        var domains = new Stack<string>();
        for (var i = start; i < lines.Length; i++)
        {
            var lineNo = firstLine + i;
            foreach (var raw in Clean(lines[i]).Split(';'))
            {
                var statement = raw.Trim();
                if (statement.Length == 0) continue;
                var word = statement.Split(' ', 2)[0];
                if (word == "subgraph")
                {
                    var rest = statement.Length > 9 ? statement[9..].Trim() : "";
                    var bracket = rest.IndexOf('[', StringComparison.Ordinal);
                    domains.Push(Unquote(bracket >= 0 && rest.EndsWith(']') ? rest[(bracket + 1)..^1] : rest));
                    continue;
                }
                if (word == "end")
                {
                    if (domains.Count > 0) domains.Pop();
                    continue;
                }
                if (word is "classDef" or "class" or "style" or "linkStyle" or "click" or "direction" or "accTitle" or "accDescr" or "title") continue;
                ParseChain(statement, lineNo, domains.Count > 0 ? domains.Peek() : null, nodes, edges);
            }
        }
    }

    /// <summary><c>A[Billing] --&gt;|invoices| B[(Ledger)] &amp; C --&gt; D</c>: nodes joined by edges, each side a group of nodes.</summary>
    private static void ParseChain(string statement, int line, string? domain, Dictionary<string, Node> nodes, List<Edge> edges)
    {
        var at = 0;
        List<string>? previous = null;
        (string? Label, bool Both)? pending = null;
        while (at < statement.Length)
        {
            var group = new List<string>();
            while (true)
            {
                var m = FlowNode().Match(statement, at);
                if (!m.Success || m.Length == 0) return;
                at = m.Index + m.Length;
                var id = m.Groups["id"].Value;
                var shape = m.Groups["shape"].Value;
                if (!nodes.ContainsKey(id) || shape.Length > 0)
                {
                    var existing = nodes.GetValueOrDefault(id);
                    nodes[id] = new Node(id, shape.Length > 0 ? ShapeLabel(shape) : existing?.Label ?? id, shape.StartsWith("[(", StringComparison.Ordinal) ? "data" : existing?.Kind ?? "app",
                        null, null, existing?.Domain ?? domain, existing?.Line ?? line);
                }
                group.Add(id);
                var amp = Regex.Match(statement[at..], @"^\s*&");
                if (!amp.Success) break;
                at += amp.Length;
            }
            if (previous is not null && pending is { } edge)
                foreach (var from in previous)
                    foreach (var to in group)
                    {
                        edges.Add(new Edge(from, to, edge.Label, null, line));
                        if (edge.Both) edges.Add(new Edge(to, from, edge.Label, null, line));
                    }
            previous = group;
            var e = FlowEdge().Match(statement, at);
            if (!e.Success || e.Length == 0) return;
            at = e.Index + e.Length;
            var label = LabelGroups.Select(g => e.Groups[g].Value.Trim()).FirstOrDefault(v => v.Length > 0);
            pending = (label is null ? null : Unquote(label), e.Groups["back"].Success && e.Groups["head"].Value == ">");
        }
    }

    private static string ShapeLabel(string shape)
    {
        var inner = shape.Trim('[', ']', '(', ')', '{', '}', '/', '\\', '>');
        return Unquote(inner.Trim());
    }

    private static string Unquote(string text)
    {
        var t = text.Trim();
        return t.Length >= 2 && t[0] == '"' && t[^1] == '"' ? t[1..^1] : t;
    }

    private static void C4(string[] lines, int start, int firstLine, string file, Dictionary<string, Node> nodes, List<Edge> edges, HashSet<string> people, ReadResult result)
    {
        var domains = new Stack<string?>();
        for (var i = start; i < lines.Length; i++)
        {
            var lineNo = firstLine + i;
            var statement = Clean(lines[i]);
            if (statement.Length == 0) continue;
            while (statement.StartsWith('}'))
            {
                if (domains.Count > 0) domains.Pop();
                statement = statement[1..].Trim();
            }
            if (statement.Length == 0 || statement.StartsWith("title ", StringComparison.Ordinal)) continue;
            var m = C4Call().Match(statement);
            if (!m.Success)
            {
                result.Warn(new ImportSourceDto(file, lineNo, null), $"Line not understood, skipped: {Short(statement)}");
                continue;
            }
            var fn = m.Groups["fn"].Value;
            var args = Arguments(m.Groups["args"].Value);
            string Arg(int n) => n < args.Positional.Count ? args.Positional[n] : "";
            string? Opt(int n, string named) => args.Named.GetValueOrDefault(named) ?? (n < args.Positional.Count && args.Positional[n].Length > 0 ? args.Positional[n] : null);
            var domain = domains.FirstOrDefault(d => d is not null);
            if (fn.EndsWith("Boundary", StringComparison.Ordinal))
            {
                domains.Push(Arg(1).Length > 0 ? Arg(1) : null);
                continue;
            }
            if (m.Groups["open"].Success) domains.Push(null);
            if (fn.StartsWith("Person", StringComparison.Ordinal))
            {
                people.Add(Arg(0));
                continue;
            }
            var kind = fn.Contains("Db", StringComparison.Ordinal) ? "data" : fn.Contains("Queue", StringComparison.Ordinal) ? "bus" : fn.EndsWith("_Ext", StringComparison.Ordinal) ? "external" : "app";
            if (fn.StartsWith("System", StringComparison.Ordinal))
            {
                if (Arg(0).Length > 0) nodes[Arg(0)] = new Node(Arg(0), Arg(1).Length > 0 ? Arg(1) : Arg(0), kind, null, Opt(2, "descr"), domain, lineNo);
                continue;
            }
            if (fn.StartsWith("Container", StringComparison.Ordinal) || fn.StartsWith("Component", StringComparison.Ordinal))
            {
                if (Arg(0).Length > 0) nodes[Arg(0)] = new Node(Arg(0), Arg(1).Length > 0 ? Arg(1) : Arg(0), kind, Opt(2, "techn"), Opt(3, "descr"), domain, lineNo);
                continue;
            }
            if (fn.StartsWith("Rel", StringComparison.Ordinal) || fn.StartsWith("BiRel", StringComparison.Ordinal))
            {
                var (from, to) = fn == "Rel_Back" ? (Arg(1), Arg(0)) : (Arg(0), Arg(1));
                var label = Opt(2, "label");
                var tech = Opt(3, "techn");
                edges.Add(new Edge(from, to, label, tech, lineNo));
                if (fn.StartsWith("BiRel", StringComparison.Ordinal)) edges.Add(new Edge(to, from, label, tech, lineNo));
                continue;
            }
            if (fn.StartsWith("Update", StringComparison.Ordinal) || fn.StartsWith("Add", StringComparison.Ordinal) || fn == "Lay" || fn.StartsWith("Lay_", StringComparison.Ordinal)) continue;
            result.Warn(new ImportSourceDto(file, lineNo, null), $"{fn} is not read, skipped.");
        }
    }

    /// <summary>Arguments split on commas outside quotes; <c>$name="value"</c> arguments are named.</summary>
    private static (List<string> Positional, Dictionary<string, string> Named) Arguments(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '"') quoted = !quoted;
            if (c == ',' && !quoted)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else current.Append(c);
        }
        parts.Add(current.ToString());
        var positional = new List<string>();
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in parts.Select(p => p.Trim()))
        {
            if (raw.StartsWith('$') && raw.IndexOf('=', StringComparison.Ordinal) is var eq and > 0) named[raw[1..eq].Trim()] = Unquote(raw[(eq + 1)..]);
            else positional.Add(Unquote(raw));
        }
        return (positional, named);
    }

    private static string Short(string text) => text.Length <= 80 ? text : text[..80] + "...";
}
