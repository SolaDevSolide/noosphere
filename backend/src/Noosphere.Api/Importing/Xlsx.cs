using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;

namespace Noosphere.Api.Importing;

/// <summary>
/// The little of the Office Open XML spreadsheet format the import needs: reading the cell texts of each sheet, and
/// writing a workbook of text cells (the template). The workbook is a zip archive: it is opened with the same limits as
/// the uploaded archives, and its XML is read without DTDs nor external entities.
/// </summary>
public static class Xlsx
{
    public sealed record Sheet(string Name, List<(int Row, Dictionary<int, string> Cells)> Rows);

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static List<Sheet> Read(byte[] content)
    {
        using var archive = OpenArchive(content);
        var shared = new List<string>();
        if (archive.GetEntry("xl/sharedStrings.xml") is { } sst)
            using (var reader = Xml(Entry(sst)))
                while (reader.ReadToFollowing("si", Main))
                {
                    if (shared.Count >= 1_000_000) throw new ImportReadException("The workbook holds too many texts.");
                    shared.Add(Texts(reader.ReadSubtree()));
                }

        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (archive.GetEntry("xl/_rels/workbook.xml.rels") is { } rels)
            using (var reader = Xml(Entry(rels)))
                while (reader.ReadToFollowing("Relationship"))
                    if (reader.GetAttribute("Id") is { } id && reader.GetAttribute("Target") is { } target)
                        targets[id] = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

        var sheets = new List<Sheet>();
        var workbook = archive.GetEntry("xl/workbook.xml") ?? throw new ImportReadException("Not an Excel workbook: xl/workbook.xml is missing.");
        var declared = new List<(string Name, string Path)>();
        using (var reader = Xml(Entry(workbook)))
            while (reader.ReadToFollowing("sheet", Main))
                if (reader.GetAttribute("name") is { } name && reader.GetAttribute("id", RelNs) is { } rid && targets.TryGetValue(rid, out var path))
                    declared.Add((name, path));
        foreach (var (name, path) in declared)
        {
            if (archive.GetEntry(path) is not { } entry) continue;
            var rows = new List<(int, Dictionary<int, string>)>();
            using var reader = Xml(Entry(entry));
            var rowNumber = 0;
            while (reader.ReadToFollowing("row", Main))
            {
                rowNumber = int.TryParse(reader.GetAttribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : rowNumber + 1;
                if (rows.Count >= ImportLimits.MaxRows) throw new ImportReadException($"Sheet '{name}' has more than {ImportLimits.MaxRows} rows.");
                var cells = new Dictionary<int, string>();
                using var row = reader.ReadSubtree();
                var column = 0;
                while (row.ReadToFollowing("c", Main))
                {
                    var reference = row.GetAttribute("r");
                    column = reference is null ? column + 1 : Column(reference);
                    var type = row.GetAttribute("t");
                    var value = CellValue(row.ReadSubtree(), type, shared);
                    if (!string.IsNullOrWhiteSpace(value)) cells[column] = value.Trim();
                }
                if (cells.Count > 0) rows.Add((rowNumber, cells));
            }
            sheets.Add(new Sheet(name, rows));
        }
        return sheets;
    }

    private static ZipArchive OpenArchive(byte[] content)
    {
        try
        {
            return new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new ImportReadException("Not an Excel workbook (.xlsx): the archive cannot be opened.");
        }
    }

    private static MemoryStream Entry(ZipArchiveEntry entry) =>
        new MemoryStream(Uploads.ReadEntry(entry, ImportLimits.MaxFileBytes * 4, out var problem) ?? throw new ImportReadException($"{entry.FullName}: {problem}"));

    private static XmlReader Xml(Stream stream) => XmlReader.Create(stream, new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        MaxCharactersInDocument = ImportLimits.MaxFileBytes * 8,
        CloseInput = true,
    });

    /// <summary>All <c>t</c> texts of a shared string or an inline string (rich text runs joined).</summary>
    private static string Texts(XmlReader reader)
    {
        var text = new StringBuilder();
        using (reader)
            while (reader.Read())
                if (reader is { NodeType: XmlNodeType.Element, LocalName: "t" }) text.Append(reader.ReadElementContentAsString());
                else if (reader is { NodeType: XmlNodeType.Element, LocalName: "rPh" }) reader.Skip();
        return text.ToString();
    }

    private static string? CellValue(XmlReader cell, string? type, List<string> shared)
    {
        using (cell)
        {
            if (type == "inlineStr") return Texts(cell);
            while (cell.Read())
                if (cell is { NodeType: XmlNodeType.Element, LocalName: "v" })
                {
                    var raw = cell.ReadElementContentAsString();
                    return type switch
                    {
                        "s" => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < shared.Count ? shared[i] : null,
                        "b" => raw == "1" ? "true" : "false",
                        _ => raw,
                    };
                }
            return null;
        }
    }

    /// <summary>Column number (1-based) of a cell reference: <c>C7</c> is 3.</summary>
    public static int Column(string reference)
    {
        var n = 0;
        foreach (var c in reference)
        {
            if (c is < 'A' or > 'Z') break;
            n = n * 26 + (c - 'A' + 1);
        }
        return n;
    }

    public static string ColumnName(int column)
    {
        var name = "";
        for (var n = column; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    // ---------- Writing ----------

    /// <summary>A sheet to write: its rows of texts; the first row is bold and frozen, with the column widths given.</summary>
    public sealed record SheetToWrite(string Name, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<int> Widths, bool Header = true);

    public static byte[] Write(IReadOnlyList<SheetToWrite> sheets)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                // A fixed date: the template is the same file every time.
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
            var overrides = string.Concat(sheets.Select((_, i) =>
                $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                + overrides + "</Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>");
            Add("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + $"<workbook xmlns=\"{Main}\" xmlns:r=\"{RelNs}\"><sheets>"
                + string.Concat(sheets.Select((s, i) => $"<sheet name=\"{Escape(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"))
                + "</sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + string.Concat(sheets.Select((_, i) =>
                    $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))
                + $"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
                + "</Relationships>");
            Add("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + $"<styleSheet xmlns=\"{Main}\">"
                + "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
                + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
                + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
                + "<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
                + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
                + "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment wrapText=\"1\" vertical=\"top\"/></xf></cellXfs>"
                + "</styleSheet>");
            for (var i = 0; i < sheets.Count; i++) Add($"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
        }
        return output.ToArray();
    }

    private static string SheetXml(SheetToWrite sheet)
    {
        var xml = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"{Main}\">");
        if (sheet.Header)
            xml.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        if (sheet.Widths.Count > 0)
        {
            xml.Append("<cols>");
            for (var c = 0; c < sheet.Widths.Count; c++) xml.Append(CultureInfo.InvariantCulture, $"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{sheet.Widths[c]}\" customWidth=\"1\"/>");
            xml.Append("</cols>");
        }
        xml.Append("<sheetData>");
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{r + 1}\">");
            for (var c = 0; c < sheet.Rows[r].Count; c++)
            {
                var style = r == 0 && sheet.Header ? 1 : 2;
                xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{ColumnName(c + 1)}{r + 1}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{Escape(sheet.Rows[r][c])}</t></is></c>");
            }
            xml.Append("</row>");
        }
        xml.Append("</sheetData></worksheet>");
        return xml.ToString();
    }

    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";
}
