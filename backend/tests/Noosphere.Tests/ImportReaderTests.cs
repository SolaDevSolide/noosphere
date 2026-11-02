using System.Text;
using Noosphere.Api.Contracts;
using Noosphere.Api.Exchange;
using Noosphere.Api.Importing;
using Noosphere.Domain;

namespace Noosphere.Tests;

/// <summary>Readers and matching rules, without a database.</summary>
public class ImportReaderTests
{
    [Theory]
    [InlineData("Réseau Auto+", "reseau auto")]
    [InlineData("  Customer   portal ", "customer portal")]
    [InlineData("CRM_v2", "crm v2")]
    public void Names_are_normalized_before_matching(string name, string normalized) => Assert.Equal(normalized, Normalize.Name(name));

    [Theory]
    [InlineData("customer portal", "customer portl", true)]
    [InlineData("billing", "billing service", true)]
    [InlineData("invoice issued", "invoice", true)]
    [InlineData("billing", "payments", false)]
    [InlineData("crm", "cms", false)]
    [InlineData("billing", "billing", false)]
    public void Close_names_are_possible_matches_only(string a, string b, bool close) => Assert.Equal(close, Normalize.Close(a, b));

    [Fact]
    public void Safe_yaml_reads_every_document_with_its_lines_and_caps_depth_and_nodes()
    {
        var yaml = new SafeYaml();
        var documents = yaml.Load("a: 1\n---\nb:\n  c: [x, 'y']\n");
        Assert.Equal(2, documents.Count);
        Assert.Equal(3, yaml.LineOf(documents[1].Root));
        Assert.Equal("y", documents[1].Root!["b"]!["c"]![1]!.GetValue<string>());
        Assert.Throws<YamlDotNet.Core.YamlException>(() => new SafeYaml { MaxDepth = 3 }.Load("a: { b: { c: { d: { e: 1 } } } }"));
        Assert.Throws<YamlDotNet.Core.YamlException>(() => new SafeYaml { MaxNodes = 10 }.Load("[" + string.Join(", ", Enumerable.Range(0, 20)) + "]"));
        var alias = Assert.Throws<YamlDotNet.Core.YamlException>(() => new SafeYaml().Load("a: &x 1\nb: *x\n"));
        Assert.Contains("aliases", alias.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Formats_are_recognised_from_the_extension_and_the_content()
    {
        string? Detect(string name, string text, string? asked = null) => Uploads.Detect(new ImportFile(name, Encoding.UTF8.GetBytes(text)), asked, out _);
        Assert.Equal(ImportFormats.Backstage, Detect("catalog-info.yaml", "apiVersion: backstage.io/v1alpha1\nkind: Component\n"));
        Assert.Equal(ImportFormats.OpenApi, Detect("api.json", "{\"openapi\": \"3.1.0\"}"));
        Assert.Equal(ImportFormats.AsyncApi, Detect("events.yml", "asyncapi: 3.0.0\n"));
        Assert.Equal(ImportFormats.Adr, Detect("0001-x.md", "# X"));
        Assert.Equal(ImportFormats.Mermaid, Detect("c4.mmd", "C4Context\n"));
        Assert.Equal(ImportFormats.Mermaid, Detect("diagram.txt", "flowchart LR\n a --> b\n"));
        Assert.Equal(ImportFormats.Mermaid, Detect("notes.md", "```mermaid\nflowchart LR\n```", ImportFormats.Mermaid));
        Assert.Null(Detect("old.csv", "a,b"));
    }

    [Fact]
    public void Mermaid_flowcharts_read_chains_groups_and_both_ways_edges()
    {
        var result = new ReadResult();
        MermaidReader.Read("x.md", """
            Some text.

            ```mermaid
            graph TD
              a[Quotes] & b(Policies) --> c[(Contracts store)]
              c <-->|sync| d{{Bus}}
              d -- PolicyIssued --> e>Billing]
            ```
            """, result);
        Assert.Empty(result.Errors);
        var apps = result.Candidates.Where(c => c.Kind == RecordKind.Application).ToDictionary(c => c.Key!, c => c);
        Assert.Equal(["a", "b", "c", "d", "e"], apps.Keys.Order());
        Assert.Equal("Contracts store", apps["c"].Name);
        Assert.Equal("data", apps["c"].Defaults["kind"]!.GetValue<string>());
        var flows = result.Candidates.Where(c => c.Kind == RecordKind.Flow).Select(c => (c.Refs["fromId"].Tokens[0], c.Refs["toId"].Tokens[0], c.Name)).ToList();
        Assert.Equal([("a", "c", "Quotes to Contracts store"), ("b", "c", "Policies to Contracts store"), ("c", "d", "sync"), ("d", "c", "sync"), ("d", "e", "PolicyIssued")], flows);
        Assert.Equal(5, result.Candidates.First(c => c.Kind == RecordKind.Application).Source.Line);
    }

    [Fact]
    public void Nygard_and_madr_statuses_and_dates_are_found()
    {
        var result = new ReadResult();
        AdrReader.Read("docs/adr/0012-use-events.md", "# 12. Use events\n\nDate: 2026-01-05\n\n## Status\n\nSuperseded by [13. Use files](0013-use-files.md)\n", new ImportOptions(), result);
        var page = result.Candidates.Single(c => c.Kind == RecordKind.Page && c.Key == "adr-0012-use-events");
        Assert.Equal("ADR 0012: Use events", page.Name);
        Assert.StartsWith("**Status:** Superseded by [13. Use files](0013-use-files.md) · **Date:** 2026-01-05", page.Fields["body"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(["adr", "superseded"], page.Fields["tags"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal(12, page.Fields["order"]!.GetValue<int>());

        var missing = new ReadResult();
        AdrReader.Read("notes.md", "No heading here.", new ImportOptions(), missing);
        Assert.Contains("No title", missing.Errors.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Backstage_entities_of_other_kinds_or_versions_are_reported()
    {
        var result = new ReadResult();
        BackstageReader.Read("catalog.yaml", """
            apiVersion: backstage.io/v1alpha1
            kind: Location
            metadata: { name: everything }
            spec: { targets: [./a.yaml] }
            ---
            apiVersion: example.com/v1
            kind: Component
            metadata: { name: other }
            ---
            apiVersion: backstage.io/v1alpha1
            kind: Component
            metadata: { title: No name }
            """, result);
        Assert.Empty(result.Candidates);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Equal(new ImportSourceDto("catalog.yaml", 10, "documents[2]"), result.Errors.Single().Source);
    }
}
