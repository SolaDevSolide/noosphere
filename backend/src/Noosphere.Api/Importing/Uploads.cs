using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Noosphere.Api.Contracts;

namespace Noosphere.Api.Importing;

/// <summary>Uploaded files: zip archives opened with limits against zip bombs, and the format of each file recognised.</summary>
public static partial class Uploads
{
    /// <summary>
    /// Opens the zip archives (one level: an archive inside an archive is skipped) and returns every file to read. A file or
    /// an archive entry over the limits is refused with an error.
    /// </summary>
    public static List<ImportFile> Expand(IReadOnlyList<ImportFile> uploaded, ReadResult messages)
    {
        var files = new List<ImportFile>();
        long extracted = 0;
        foreach (var file in uploaded)
        {
            var source = new ImportSourceDto(file.Name, null, null);
            if (!IsZip(file.Content) || IsWorkbookName(file.Name))
            {
                if (file.Content.LongLength > ImportLimits.MaxFileBytes) messages.Fail(source, $"The file is larger than {Mb(ImportLimits.MaxFileBytes)}.");
                else files.Add(file);
                continue;
            }
            try
            {
                using var archive = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (name.EndsWith('/') || Ignored(name)) continue;
                    var at = new ImportSourceDto($"{file.Name}/{name}", null, null);
                    if (files.Count >= ImportLimits.MaxFiles)
                    {
                        messages.Fail(source, $"At most {ImportLimits.MaxFiles} files in one import.");
                        break;
                    }
                    var content = ReadEntry(entry, ImportLimits.MaxFileBytes, out var problem);
                    if (content is null)
                    {
                        messages.Fail(at, problem!);
                        continue;
                    }
                    extracted += content.LongLength;
                    if (extracted > ImportLimits.MaxExtractedBytes)
                    {
                        messages.Fail(source, $"The archives hold more than {Mb(ImportLimits.MaxExtractedBytes)} once extracted.");
                        return files;
                    }
                    if (IsZip(content) && !IsWorkbookName(name))
                    {
                        messages.Warn(at, "An archive inside an archive is not opened.");
                        continue;
                    }
                    files.Add(new ImportFile(at.File, content));
                }
            }
            catch (InvalidDataException)
            {
                messages.Fail(source, "The zip archive is damaged or uses an unsupported compression.");
            }
        }
        if (files.Count > ImportLimits.MaxFiles)
        {
            messages.Fail(new ImportSourceDto("", null, null), $"At most {ImportLimits.MaxFiles} files in one import.");
            return [];
        }
        return files;
    }

    /// <summary>
    /// Reads one archive entry, never more than <paramref name="max"/> bytes whatever its header says, and refuses an entry
    /// compressed beyond <see cref="ImportLimits.MaxCompressionRatio"/>.
    /// </summary>
    public static byte[]? ReadEntry(ZipArchiveEntry entry, long max, out string? problem)
    {
        problem = null;
        if (entry.Length > max)
        {
            problem = $"The file is larger than {Mb(max)} once extracted.";
            return null;
        }
        if (entry.Length > 64 * 1024 && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > ImportLimits.MaxCompressionRatio)
        {
            problem = "The file is compressed too much to be genuine (possible zip bomb).";
            return null;
        }
        using var input = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > max)
            {
                problem = $"The file is larger than {Mb(max)} once extracted.";
                return null;
            }
        }
        if (output.Length > 64 * 1024 && entry.CompressedLength > 0 && output.Length / entry.CompressedLength > ImportLimits.MaxCompressionRatio)
        {
            problem = "The file is compressed too much to be genuine (possible zip bomb).";
            return null;
        }
        return output.ToArray();
    }

    public static bool IsZip(byte[] content) => content.Length >= 4 && content[0] == 'P' && content[1] == 'K' && content[2] is 3 or 5 && content[3] is 4 or 6;

    private static bool IsWorkbookName(string name) => name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    /// <summary>System files that archives made on macOS or Windows carry.</summary>
    private static bool Ignored(string path) =>
        path.StartsWith("__MACOSX/", StringComparison.Ordinal) || path.Split('/').Any(p => p.StartsWith('.')) || path.EndsWith("Thumbs.db", StringComparison.OrdinalIgnoreCase);

    private static string Mb(long bytes) => $"{bytes / (1024 * 1024)} MB";

    [GeneratedRegex(@"(?:^|[{,])\s*[""']?openapi[""']?\s*:\s*[""']?3\.", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex OpenApiPattern();

    [GeneratedRegex(@"(?:^|[{,])\s*[""']?swagger[""']?\s*:", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SwaggerPattern();

    [GeneratedRegex(@"(?:^|[{,])\s*[""']?asyncapi[""']?\s*:", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AsyncApiPattern();

    [GeneratedRegex(@"(?:^|[{,])\s*[""']?apiVersion[""']?\s*:\s*[""']?backstage\.io/", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BackstagePattern();

    [GeneratedRegex(@"(?:^|[{,])\s*[""']?formatVersion[""']?\s*:\s*[""']?noosphere/", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ExchangePattern();

    [GeneratedRegex(@"^\s*(C4Context|C4Container|C4Component|flowchart|graph)\b", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MermaidPattern();

    /// <summary>
    /// Format of a file: the one asked for, or recognised from the extension and the content. Null, with the reason, when
    /// the file is not one the import reads.
    /// </summary>
    public static string? Detect(ImportFile file, string? asked, out string? problem)
    {
        problem = null;
        var name = file.Name.ToLowerInvariant();
        var ext = Path.GetExtension(name);
        if (ext == ".xlsx" || (asked == ImportFormats.Excel && IsZip(file.Content)))
        {
            if (asked is not (null or ImportFormats.Excel)) problem = $"A workbook is read as excel, not {asked}.";
            return problem is null ? ImportFormats.Excel : null;
        }
        if (ext is ".xls" or ".ods" or ".csv")
        {
            problem = "Only .xlsx workbooks are read: save the file as an Excel workbook (.xlsx) from the template.";
            return null;
        }
        if (Text(file.Content) is not { } text)
        {
            problem = "The file is not a text file (UTF-8) nor an .xlsx workbook.";
            return null;
        }
        if (asked is not null) return asked;
        if (ext is ".md" or ".markdown") return ImportFormats.Adr;
        if (ext is ".mmd" or ".mermaid") return ImportFormats.Mermaid;
        if (ExchangePattern().IsMatch(text))
        {
            problem = "This is a Noosphere exchange file: import it with the exchange import (POST /api/import or 'noosphere import').";
            return null;
        }
        if (SwaggerPattern().IsMatch(text))
        {
            problem = "Swagger 2.0 is not read: convert the file to OpenAPI 3.";
            return null;
        }
        if (OpenApiPattern().IsMatch(text)) return ImportFormats.OpenApi;
        if (AsyncApiPattern().IsMatch(text)) return ImportFormats.AsyncApi;
        if (BackstagePattern().IsMatch(text)) return ImportFormats.Backstage;
        if (ext is not (".yaml" or ".yml" or ".json") && MermaidPattern().IsMatch(text)) return ImportFormats.Mermaid;
        problem = "Format not recognised: expected a Backstage catalog file, OpenAPI 3, AsyncAPI, an ADR in Markdown, a Mermaid diagram or an .xlsx workbook.";
        return null;
    }

    /// <summary>The content as UTF-8 text (without a byte order mark); null when it is not text.</summary>
    public static string? Text(byte[] content)
    {
        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(content);
            if (text.Length > 0 && text[0] == '﻿') text = text[1..];
            return text.Contains('\0', StringComparison.Ordinal) ? null : text;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
